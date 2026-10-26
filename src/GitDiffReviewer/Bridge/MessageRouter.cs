using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using GitDiffReviewer.Models;
using GitDiffReviewer.Services;
using Microsoft.Web.WebView2.Core;

namespace GitDiffReviewer.Bridge;

/// <summary>
/// 前后端消息路由（§6.2 / §7.8）：JS 发 {id,type,payload}，C# 回 {replyTo,ok,data|error}。
/// 所有异常统一转成 ok:false 回包，不抛到 UI 线程。
/// </summary>
public sealed class MessageRouter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly CoreWebView2 _web;
    private readonly string _appDataDir;
    private readonly SettingsStore _settings;
    private readonly Func<Task<string?>> _pickFolder;
    private RepoSession? _session;
    private ChangeSet? _lastChangeSet;
    private string _baseSha = "";
    private string? _targetSha;
    private WorkingTreeWatcher? _watcher;
    private readonly LogService _log;

    public MessageRouter(CoreWebView2 web, string appDataDir, Func<Task<string?>> pickFolder, LogService log)
    {
        _web = web;
        _appDataDir = appDataDir;
        _settings = new SettingsStore(appDataDir);
        _pickFolder = pickFolder;
        _log = log;
    }

    public async Task HandleAsync(string raw)
    {
        string? id = null;
        try
        {
            _log.Info("RX: " + raw.Substring(0, Math.Min(200, raw.Length)));
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var type = root.GetProperty("type").GetString() ?? "";
            var payload = root.TryGetProperty("payload", out var p) ? p : default;

            object? data = type switch
            {
                "bootstrap" => await BootstrapAsync(),
                "addRepo" => await AddRepoAsync(),
                "selectRepo" => await SelectRepoAsync(payload),
                "listChanges" => await ListChangesAsync(),
                "listAllFiles" => await ListAllFilesAsync(),
                "openInExplorer" => OpenInExplorer(payload),
                "deleteFile" => DeleteFile(payload),
                "saveFile" => SaveFile(payload),
                "markReviewed" => await MarkReviewedAsync(),
                "getFileContent" => await GetFileContentAsync(payload),
                "chooseBaseline" => await ChooseBaselineAsync(),
                "setBaseline" => await SetBaselineAsync(payload),
                "saveSettings" => await SaveSettingsAsync(payload),
                "listCommits" => await ListCommitsAsync(),
                "getCommitDiff" => await GetCommitDiffAsync(payload),
                _ => throw new GitException("UNKNOWN_TYPE", $"未知消息类型: {type}")
            };
            Post(new { replyTo = id, ok = true, data });
        }
        catch (Exception ex)
        {
            var code = ex is GitException ge ? ge.Code : "UNEXPECTED";
            Post(new { replyTo = id, ok = false, error = new { code, message = "出错了：" + ex.Message } });
        }
    }

    private void Post(object msg)
    {
        var json = JsonSerializer.Serialize(msg, JsonOpts);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            _web.PostWebMessageAsJson(json);
        else
            _ = dispatcher.InvokeAsync(() => _web.PostWebMessageAsJson(json));
    }

    private async Task<object> BootstrapAsync()
    {
        var s = await _settings.LoadAsync();
        if (!string.IsNullOrEmpty(s.ActiveRepo) && Directory.Exists(s.ActiveRepo))
        {
            _session = new RepoSession(s.ActiveRepo, _appDataDir);
            await StartWatcherAsync();
        }
        return new { repos = s.Repos, activeRepo = s.ActiveRepo, settings = s, hasWebView2 = true };
    }

    private async Task<object> AddRepoAsync()
    {
        var path = await _pickFolder();
        if (string.IsNullOrEmpty(path)) throw new GitException("NO_REPO", "未选择目录");
        if (!await IsGitRepoAsync(path)) throw new GitException("NOT_A_REPO", "这个文件夹不是 git 仓库");

        var s = await _settings.LoadAsync();
        if (!s.Repos.Any(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            s.Repos.Add(new RepoInfo
            {
                Path = path,
                Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)),
                AddedAt = DateTime.UtcNow.ToString("o"),
            });
        }
        s.ActiveRepo = path;
        await _settings.SaveAsync(s);
        _session = new RepoSession(path, _appDataDir);
        _lastChangeSet = null;
        await StartWatcherAsync();
        return new { repo = new { path, name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) } };
    }

    private async Task<object> SelectRepoAsync(JsonElement payload)
    {
        var path = payload.GetProperty("path").GetString() ?? "";
        var s = await _settings.LoadAsync();
        s.ActiveRepo = path;
        await _settings.SaveAsync(s);
        _session = new RepoSession(path, _appDataDir);
        _lastChangeSet = null;
        await StartWatcherAsync();
        return new { repo = path };
    }

    private async Task<object> ListChangesAsync()
    {
        var session = EnsureSession();
        var head = await session.Commands.GetHeadShaAsync();
        var svc = new WorkingTreeService(session.Commands);
        var cs = await svc.BuildAsync(session.RepoRoot, head);
        _lastChangeSet = cs;
        _baseSha = head;
        _targetSha = null;
        return new { changeSet = cs, headSha = head };
    }

    private async Task<object> ListAllFilesAsync()
    {
        var session = EnsureSession();
        var r = await session.Commands.LsFilesAsync();
        if (r.ExitCode != 0) throw new GitException("GIT_FAILED", "git ls-files 失败");
        var paths = EncodingUtil.Decode(r.StdOut).Split('\0', StringSplitOptions.RemoveEmptyEntries).OrderBy(p => p, StringComparer.Ordinal);
        return new { files = paths };
    }

    private object OpenInExplorer(JsonElement payload)
    {
        var session = EnsureSession();
        var path = payload.GetProperty("path").GetString() ?? "";
        var full = PathGuard.EnsureWithinRepo(session.RepoRoot, path);
        if (!File.Exists(full)) throw new GitException("NOT_FOUND", "文件不存在: " + path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{full}\"",
            UseShellExecute = true,
        });
        return new { ok = true };
    }

    private object DeleteFile(JsonElement payload)
    {
        var session = EnsureSession();
        var path = payload.GetProperty("path").GetString() ?? "";
        var full = PathGuard.EnsureWithinRepo(session.RepoRoot, path);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
        else if (Directory.Exists(full))
        {
            Directory.Delete(full, true);
        }
        else
        {
            throw new GitException("NOT_FOUND", "文件不存在: " + path);
        }
        return new { ok = true };
    }

    private object SaveFile(JsonElement payload)
    {
        var session = EnsureSession();
        var path = payload.GetProperty("path").GetString() ?? "";
        var content = payload.GetProperty("content").GetString() ?? "";
        var full = PathGuard.EnsureWithinRepo(session.RepoRoot, path);
        File.WriteAllText(full, content);
        return new { ok = true };
    }

    private async Task<object> GetFileContentAsync(JsonElement payload)
    {
        var session = EnsureSession();
        var path = payload.GetProperty("path").GetString() ?? "";
        // 先在变更集里找；找不到就按未改动文件处理（old=new=磁盘内容）
        var entry = _lastChangeSet?.Entries.FirstOrDefault(e => e.Path == path);
        if (entry is null)
        {
            // 验证文件存在且在仓库内
            var full = PathGuard.EnsureWithinRepo(session.RepoRoot, path);
            if (!File.Exists(full)) throw new GitException("NOT_FOUND", "文件不存在: " + path);
            entry = new ChangeEntry(path, null, ChangeStatus.Modified, null, 0, 0, false);
        }
        var svc = new FileContentService(session.Commands);
        var content = await svc.GetAsync(entry, _baseSha, session.RepoRoot, _targetSha);
        // 附加上下文元信息供详情 tab 使用
        var fi = new FileInfo(PathGuard.EnsureWithinRepo(session.RepoRoot, path));
        return new
        {
            oldText = content.OldText,
            newText = content.NewText,
            encoding = content.Encoding,
            binary = content.Binary,
            truncated = content.Truncated,
            language = content.Language,
            oldImage = content.OldImage,
            newImage = content.NewImage,
            size = fi.Exists ? fi.Length : 0,
            modified = fi.Exists ? fi.LastWriteTimeUtc : default,
        };
    }

    private Task<object> MarkReviewedAsync() =>
        Task.FromResult<object>(new { ok = true });

    private Task<object> ChooseBaselineAsync() =>
        Task.FromResult<object>(new { commits = Array.Empty<object>() });

    private Task<object> SetBaselineAsync(JsonElement payload) =>
        Task.FromResult<object>(new { ok = true });

    private async Task<object> SaveSettingsAsync(JsonElement payload)
    {
        var s = await _settings.LoadAsync();
        if (payload.TryGetProperty("inlineDiff", out var inline)) s.InlineDiff = inline.GetBoolean();
        if (payload.TryGetProperty("notifyOnChange", out var n)) s.NotifyOnChange = n.GetBoolean();
        if (payload.TryGetProperty("onboardingDone", out var ob)) s.OnboardingDone = ob.GetBoolean();
        await _settings.SaveAsync(s);
        return new { settings = s };
    }

    private async Task<object> ListCommitsAsync()
    {
        var session = EnsureSession();
        var svc = new CommitService(session.Commands);
        var commits = await svc.ListAsync(50);
        return new { commits = commits.Select(c => new {
            sha = c.Sha, @short = c.Short, message = c.Message, author = c.Author, date = c.DateIso
        }).ToList() };
    }

    private async Task<object> GetCommitDiffAsync(JsonElement payload)
    {
        var session = EnsureSession();
        var sha = payload.GetProperty("sha").GetString() ?? "";
        if (string.IsNullOrEmpty(sha)) throw new GitException("BAD_REV", "缺少 sha");
        // base = 选中的 commit，target = 工作区（磁盘文件）
        var svc = new WorkingTreeService(session.Commands);
        var cs = await svc.BuildForBaseAsync(session.RepoRoot, sha);
        _lastChangeSet = cs;
        _baseSha = sha;
        _targetSha = null; // new side 读磁盘
        return new { changeSet = cs };
    }

    private RepoSession EnsureSession()
    {
        if (_session is null) throw new GitException("NO_REPO", "尚未选择仓库");
        return _session;
    }

    private async Task StartWatcherAsync()
    {
        _watcher?.Dispose();
        var session = _session!;
        try
        {
            _watcher = new WorkingTreeWatcher(session.RepoRoot, OnTreeChangedAsync, _log);
            _watcher.Start();
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _log.Error("启动文件监听失败", ex);
        }
    }

    private async Task OnTreeChangedAsync()
    {
        try
        {
            var session = _session!;
            var head = await session.Commands.GetHeadShaAsync();
            var svc = new WorkingTreeService(session.Commands);
            var cs = await svc.BuildAsync(session.RepoRoot, head);
            _lastChangeSet = cs;
            _baseSha = head;
            Post(new { type = "treeChanged", data = new { count = cs.Entries.Count } });
        }
        catch (Exception ex)
        {
            _log.Error("工作区变化处理失败", ex);
        }
    }

    private static async Task<bool> IsGitRepoAsync(string path)
    {
        try
        {
            var r = await new GitRunner(path).RunAsync(["rev-parse", "--git-dir"]);
            return r.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
