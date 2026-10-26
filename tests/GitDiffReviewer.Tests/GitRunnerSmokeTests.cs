using System.Diagnostics;
using System.IO;
using GitDiffReviewer.Services;
using Xunit;

namespace GitDiffReviewer.Tests;

/// <summary>
/// 对一个临时真实 git 仓库冒烟：证明 GitRunner/GitCommands 能拿到 HEAD（B2 验收）。
/// </summary>
public class GitRunnerSmokeTests : IDisposable
{
    private readonly string _repoDir;

    public GitRunnerSmokeTests()
    {
        _repoDir = Path.Combine(Path.GetTempPath(), "gdr-b2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoDir);
        RunGit("init -q", _repoDir);
        RunGit("config user.email test@test.com", _repoDir);
        RunGit("config user.name test", _repoDir);
        File.WriteAllText(Path.Combine(_repoDir, "readme.txt"), "hello");
        RunGit("add readme.txt", _repoDir);
        RunGit("commit -q -m init", _repoDir);
    }

    [Fact]
    public async Task GetHeadSha_返回40位Hex()
    {
        var runner = new GitRunner(_repoDir);
        var cmds = new GitCommands(runner);

        var head = await cmds.GetHeadShaAsync();

        Assert.Equal(40, head.Length);
        Assert.Matches("^[0-9a-f]{40}$", head);
    }

    [Fact]
    public async Task VerifyCommit_head_能校验通过()
    {
        var runner = new GitRunner(_repoDir);
        var cmds = new GitCommands(runner);
        var head = await cmds.GetHeadShaAsync();

        var verified = await cmds.VerifyCommitAsync(head[..7]); // 短 SHA 也能验

        Assert.Equal(head, verified);
    }

    [Fact]
    public async Task IsAncestor_headIsAncestorOfItself()
    {
        var runner = new GitRunner(_repoDir);
        var cmds = new GitCommands(runner);
        var head = await cmds.GetHeadShaAsync();

        Assert.True(await cmds.IsAncestorAsync(head, head));
    }

    private static void RunGit(string args, string workDir)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args.Split(' ')) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        Assert.True(p.WaitForExit(10000), $"git {args} 超时");
        if (p.ExitCode != 0)
            throw new Exception($"git {args} 失败: {p.StandardError.ReadToEnd()}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_repoDir, recursive: true); } catch { /* 忽略 */ }
    }
}
