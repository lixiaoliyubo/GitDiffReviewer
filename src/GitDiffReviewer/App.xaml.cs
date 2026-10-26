using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace GitDiffReviewer;

/// <summary>WPF 应用入口：全局异常兜底，写日志（§10）。</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GitDiffReviewer", "logs");
            Directory.CreateDirectory(dir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [FATAL] {e.Exception}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, $"app-{DateTime.Now:yyyyMMdd}.log"), line, new UTF8Encoding(false));
        }
        catch { }
        MessageBox.Show("出错了：" + e.Exception.Message, "GitDiffReviewer",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
