using System.IO;

namespace GitDiffReviewer.Services;

/// <summary>
/// 监听工作区文件变化（修改/新增/删除）：FileSystemWatcher + 800ms 防抖 + 30s 轮询兜底。
/// 变化后回调，由调用方重算工作区改动并推消息给前端。
/// </summary>
public sealed class WorkingTreeWatcher : IDisposable
{
    private readonly string _repoRoot;
    private readonly Func<Task> _onChanged;
    private readonly LogService? _log;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private Timer? _poll;
    private bool _disposed;

    public WorkingTreeWatcher(string repoRoot, Func<Task> onChanged, LogService? log = null)
    {
        _repoRoot = repoRoot;
        _onChanged = onChanged;
        _log = log;
    }

    public void Start()
    {
        try
        {
            _watcher = new FileSystemWatcher(_repoRoot)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
            };
            _watcher.Changed += (_, e) => OnAny(e.FullPath);
            _watcher.Created += (_, e) => OnAny(e.FullPath);
            _watcher.Deleted += (_, e) => OnAny(e.FullPath);
            _watcher.Renamed += (_, e) => OnAny(e.FullPath);
        }
        catch (Exception ex)
        {
            _log?.Warn($"FileSystemWatcher 启动失败，仅靠轮询：{ex.Message}");
        }

        // 30s 轮询兜底（对比一次 porcelain 摘要）
        _poll = new Timer(async _ => await SafeTriggerAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private void OnAny(string fullPath)
    {
        // 忽略 .git 内部变化
        var rel = Path.GetRelativePath(_repoRoot, fullPath);
        if (rel.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            rel.Equals(".git", StringComparison.Ordinal))
            return;
        Kick();
    }

    private void Kick()
    {
        _debounce?.Dispose();
        _debounce = new Timer(async _ => await SafeTriggerAsync(), null, 800, Timeout.Infinite);
    }

    private async Task SafeTriggerAsync()
    {
        if (_disposed) return;
        try { await _onChanged(); }
        catch (Exception ex) { _log?.Warn($"工作区变化回调失败：{ex.Message}"); }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
        _debounce?.Dispose();
        _poll?.Dispose();
    }
}
