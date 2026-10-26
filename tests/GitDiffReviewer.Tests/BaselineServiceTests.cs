using System.Diagnostics;
using System.IO;
using GitDiffReviewer.Services;
using Xunit;

namespace GitDiffReviewer.Tests;

/// <summary>BaselineService 状态机 + 状态文件落盘（FR-2 / §7.6）。</summary>
public class BaselineServiceTests : IDisposable
{
    private readonly string _repo;
    private readonly string _appData;

    public BaselineServiceTests()
    {
        _appData = Path.Combine(Path.GetTempPath(), "gdr-b3cfg-" + Guid.NewGuid().ToString("N"));
        _repo = Path.Combine(Path.GetTempPath(), "gdr-b3repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repo);
        Git("init", "-q");
        Git("config", "user.email", "t@t.com");
        Git("config", "user.name", "t");
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "v1\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "c1");
        C1 = GitOut("rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "v2\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "c2");
        C2 = GitOut("rev-parse", "HEAD").Trim();
    }

    private string C1 { get; }
    private string C2 { get; }

    private BaselineService NewService() =>
        new(_repo, new GitCommands(new GitRunner(_repo)), _appData);

    [Fact]
    public async Task 首次初始化_基线等于当前HEAD_source为initial()
    {
        var svc = NewService();
        var st = await svc.InitializeAsync();

        Assert.Equal(C2, st.BaselineHead);
        Assert.Equal("initial", st.BaselineSource);
        // 状态文件写到 APPDATA/state/
        Assert.True(Directory.Exists(Path.Combine(_appData, "state")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_appData, "state"), "*.json"));
    }

    [Fact]
    public async Task 标记已审阅_基线推进到HEAD_source为manual()
    {
        var svc = NewService();
        await svc.InitializeAsync();
        var st = await svc.MarkReviewedAsync();
        Assert.Equal(C2, st.BaselineHead);
        Assert.Equal("manual", st.BaselineSource);
    }

    [Fact]
    public async Task 手选基线_回到C1_source为picked()
    {
        var svc = NewService();
        await svc.InitializeAsync();
        var st = await svc.PickBaselineAsync(C1[..7]);
        Assert.Equal(C1, st.BaselineHead);
        Assert.Equal("picked", st.BaselineSource);
    }

    [Fact]
    public async Task 重启后_基线不自动推进()
    {
        // 先选 C1 为基线
        var svc1 = NewService();
        await svc1.InitializeAsync();
        await svc1.PickBaselineAsync(C1[..7]);

        // 新实例（模拟重启）再初始化：基线仍应是 C1，不自动跑到 C2
        var svc2 = NewService();
        var st = await svc2.InitializeAsync();
        Assert.Equal(C1, st.BaselineHead);
        Assert.Equal(C2, st.LastSeenHead);
    }

    private void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        Assert.True(p.WaitForExit(15000));
        if (p.ExitCode != 0) throw new Exception($"git {string.Join(' ', args)} 失败: {p.StandardError.ReadToEnd()}");
    }

    private string GitOut(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var s = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15000);
        return s;
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, true); } catch { }
        try { Directory.Delete(_appData, true); } catch { }
    }
}
