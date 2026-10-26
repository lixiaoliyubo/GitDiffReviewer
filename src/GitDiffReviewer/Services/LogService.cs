using System.IO;
using System.Text;

namespace GitDiffReviewer.Services;

/// <summary>
/// 极简日志：%APPDATA%\GitDiffReviewer\logs\app-yyyyMMdd.log（§11）。
/// 按天滚动，启动时清理 7 天前的旧日志。
/// </summary>
public sealed class LogService
{
    private readonly string _logDir;
    private static readonly object Lock = new();

    public LogService(string appDataDir)
    {
        _logDir = Path.Combine(appDataDir, "logs");
        Directory.CreateDirectory(_logDir);
        CleanOld();
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message} :: {ex}");

    private void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (Lock)
            {
                File.AppendAllText(Path.Combine(_logDir, $"app-{DateTime.Now:yyyyMMdd}.log"), line,
                    new UTF8Encoding(false));
            }
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    private void CleanOld()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_logDir, "app-*.log"))
            {
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7))
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch { }
    }
}
