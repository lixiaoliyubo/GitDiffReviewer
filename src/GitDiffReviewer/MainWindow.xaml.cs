using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using GitDiffReviewer.Bridge;
using GitDiffReviewer.Models;
using GitDiffReviewer.Services;
using Microsoft.Web.WebView2.Core;

namespace GitDiffReviewer;

/// <summary>
/// 主窗口：承载 WebView2（§4.4），把消息交给 MessageRouter；窗口位置持久化（§9）。
/// </summary>
public partial class MainWindow : Window
{
    private MessageRouter? _router;
    private LogService? _log;
    private string _appDataDir = "";
    private Settings? _settings;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitWebViewAsync();
        Closed += OnClosed;
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            _appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GitDiffReviewer");
            Directory.CreateDirectory(_appDataDir);
            _log = new LogService(_appDataDir);
            _log.Info("启动");

            RestoreWindowPlacement();

            var userDataFolder = Path.Combine(_appDataDir, "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await WebView.EnsureCoreWebView2Async(env);

            var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.local", wwwroot, CoreWebView2HostResourceAccessKind.DenyCors);

            var pickFolder = new Func<Task<string?>>(() =>
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择 git 仓库目录" };
                var ok = dlg.ShowDialog(this) == true;
                return Task.FromResult(ok ? dlg.FolderName : null);
            });

            _router = new MessageRouter(WebView.CoreWebView2, _appDataDir, pickFolder, _log);
            WebView.CoreWebView2.WebMessageReceived += async (_, e) =>
                await _router.HandleAsync(e.WebMessageAsJson);

            WebView.CoreWebView2.Navigate("https://app.local/index.html");
        }
        catch (Exception ex)
        {
            _log?.Error("WebView2 初始化失败", ex);
            var choice = MessageBox.Show(
                this,
                "缺少 WebView2 运行时，本工具无法启动。\n\n是否前往微软官方下载页安装？\n\n详情：" + ex.Message,
                "GitDiffReviewer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);
            if (choice == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://developer.microsoft.com/microsoft-edge/webview2/",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
            Application.Current.Shutdown();
        }
    }

    private async void RestoreWindowPlacement()
    {
        try
        {
            _settings = await new SettingsStore(_appDataDir).LoadAsync();
            if (_settings.Window is { } w)
            {
                Left = w.X; Top = w.Y; Width = w.Width; Height = w.Height;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }
        catch { }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        try
        {
            if (_settings is null) _settings = await new SettingsStore(_appDataDir).LoadAsync();
            _settings.Window = new WindowSettings
            {
                X = Left, Y = Top, Width = Width, Height = Height
            };
            await new SettingsStore(_appDataDir).SaveAsync(_settings);
        }
        catch { }
    }
}
