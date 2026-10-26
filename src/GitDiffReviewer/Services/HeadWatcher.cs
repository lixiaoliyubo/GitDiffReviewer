using System.IO;

namespace GitDiffReviewer.Services;

/// <summary>
/// 监听 .git/HEAD 与 .git/refs/heads/ 变化（FR-7）：FileSystemWatcher + 800ms 防抖 + 30s 轮询兜底。
/// HEAD 变化后回调（由调用方更新 lastSeenHead 并推 headChanged 给前端）。
/// </summary>
public sealed class HeadWatcher : IDisposable
{
    private readonly string _repoRoot;
    private readonly Func<Task> _onChanged;
    private readonly LogService? _log;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private Timer? _poll;
    private string _lastHead = "";
    private bool _disposed;

    public HeadWatcher(string repoRoot, Func<Task> onChanged, LogService? log = null)
    {
        _repoRoot = repoRoot;
        _onChanged = onChanged;
        _log = log;
    }

    public void Start(string currentHead)
    {
        _lastHead = currentHead;
        var gitDir = Path.Combine(_repoRoot, ".git");
        if (!Directory.Exists(gitDir)) return;

        try
        {
            _watcher = new FileSystemWatcher(gitDir)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };
            _watcher.Changed += (_, _) => Kick();
            _watcher.Created += (_, _) => Kick();
            _watcher.Renamed += (_, _) => Kick();
        }
        catch (Exception ex)
        {
            _log?.Warn($"FileSystemWatcher 启动失败，仅靠轮询：{ex.Message}");
        }

        // 30s 轮询兜底
        _poll = new Timer(async _ => await PollAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private void Kick()
    {
        // 800ms 防抖
        _debounce?.Dispose();
        _debounce = new Timer(async _ => await PollAsync(), null, 800, Timeout.Infinite);
    }

    private async Task PollAsync()
    {
        if (_disposed) return;
        try
        {
            var runner = new GitRunner(_repoRoot);
            var r = await runner.RunAsync(["rev-parse", "HEAD"], timeoutMs: 5000);
            if (r.ExitCode != 0) return;
            var head = EncodingUtil.Decode(r.StdOut).Trim();
            if (head != _lastHead)
            {
                _lastHead = head;
                _log?.Info($"HEAD 变化：{head[..7]}");
                await _onChanged();
            }
        }
        catch (Exception ex)
        {
            _log?.Warn($"HeadWatcher 轮询失败：{ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
        _debounce?.Dispose();
        _poll?.Dispose();
    }
}
