using System.Diagnostics;
using System.IO;
using System.Text;
using GitDiffReviewer.Models;
using GitDiffReviewer.Services;
using Xunit;

namespace GitDiffReviewer.Tests;

/// <summary>
/// 按 §15 造一个 7 条变更的 fixture，验证 ChangeSetService 解析结果（AC-3）。
/// </summary>
public class ChangeSetServiceFixtureTests : IDisposable
{
    private readonly string _repo;

    public ChangeSetServiceFixtureTests()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        _repo = Path.Combine(Path.GetTempPath(), "gdr-b3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repo);
        Git("init", "-q");
        Git("config", "user.email", "t@t.com");
        Git("config", "user.name", "t");

        // BASE：modify.txt(v1) / delete.txt / rename-old.txt
        File.WriteAllText(P("modify.txt"), "line1\nline2\n", new UTF8Encoding(false));
        File.WriteAllText(P("delete.txt"), "bye\n", new UTF8Encoding(false));
        File.WriteAllText(P("rename-old.txt"), "same\n", new UTF8Encoding(false));
        Git("add", "-A");
        Git("commit", "-q", "-m", "base");
        Base = GitOut("rev-parse", "HEAD").Trim();

        // HEAD：7 条变更
        File.WriteAllText(P("modify.txt"), "line1\nline2\nline3\nline4\n", new UTF8Encoding(false)); // M +2
        File.WriteAllText(P("added.txt"), "a\nb", new UTF8Encoding(false));                        // A +2
        Directory.CreateDirectory(P("sub"));
        File.WriteAllText(P("sub/中文新增.txt"), "x\n", new UTF8Encoding(false));                 // A 中文路径
        File.Delete(P("delete.txt"));                                                              // D
        Git("mv", P("rename-old.txt"), P("rename-new.txt"));                                       // R100
        File.WriteAllBytes(P("bin.dat"), new byte[] { 1, 2, 0, 3, 0, 4 });                        // 二进制
        var gbk = Encoding.GetEncoding("GB18030");
        File.WriteAllBytes(P("gbk.txt"), gbk.GetBytes("中文内容"));                                // GBK 文件

        Git("add", "-A");
        Git("commit", "-q", "-m", "head");
        Head = GitOut("rev-parse", "HEAD").Trim();
    }

    private string Base { get; }
    private string Head { get; }

    private string P(string rel) => Path.Combine(_repo, rel.Replace('/', Path.DirectorySeparatorChar));

    private void Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        Assert.True(p.WaitForExit(15000), $"git {string.Join(' ', args)} 超时");
        if (p.ExitCode != 0) throw new Exception($"git {string.Join(' ', args)} 失败: {p.StandardError.ReadToEnd()}");
    }

    private string GitOut(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var s = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15000);
        return s;
    }

    [Fact]
    public async Task Build_恰好7条_状态与行数正确()
    {
        var cmds = new GitCommands(new GitRunner(_repo));
        var svc = new ChangeSetService(cmds);

        var cs = await svc.BuildAsync(_repo, Base, Head);

        Assert.Equal(7, cs.Entries.Count);
        Assert.False(cs.NonFastForward);
        Assert.False(cs.Truncated);

        var byPath = cs.Entries.ToDictionary(e => e.Path, StringComparer.Ordinal);

        // 排序：M 在最前，R 在最后
        Assert.Equal(ChangeStatus.Modified, cs.Entries[0].Status);
        Assert.Equal("modify.txt", cs.Entries[0].Path);
        Assert.Equal(2, cs.Entries[0].Insertions);
        Assert.Equal(0, cs.Entries[0].Deletions);
        Assert.Equal(ChangeStatus.Renamed, cs.Entries[^1].Status);
        Assert.Equal("rename-new.txt", cs.Entries[^1].Path);

        // 各条目状态
        Assert.Equal(ChangeStatus.Added, byPath["added.txt"].Status);
        Assert.Equal(ChangeStatus.Added, byPath["sub/中文新增.txt"].Status);
        Assert.Equal(ChangeStatus.Deleted, byPath["delete.txt"].Status);
        Assert.Equal("rename-old.txt", byPath["rename-new.txt"].OldPath);

        // 二进制 / GBK
        Assert.True(byPath["bin.dat"].Binary);
        Assert.False(byPath["gbk.txt"].Binary);
        Assert.Equal(ChangeStatus.Added, byPath["gbk.txt"].Status);
    }

    [Fact]
    public async Task Build_基线等于HEAD_返回空集()
    {
        var cmds = new GitCommands(new GitRunner(_repo));
        var svc = new ChangeSetService(cmds);
        var cs = await svc.BuildAsync(_repo, Head, Head);
        Assert.Empty(cs.Entries);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { }
    }
}
