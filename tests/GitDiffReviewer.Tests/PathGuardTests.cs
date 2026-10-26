using System.IO;
using GitDiffReviewer.Services;
using Xunit;

namespace GitDiffReviewer.Tests;

public class PathGuardTests
{
    private static readonly string Repo = @"C:\repo";

    [Fact]
    public void EnsureWithinRepo_正常相对路径_通过()
    {
        var full = PathGuard.EnsureWithinRepo(Repo, "src/Program.cs");
        Assert.Equal(Path.GetFullPath(Path.Combine(Repo, "src/Program.cs")), full);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../../windows.exe")]
    [InlineData("a/../../escape.txt")]
    public void EnsureWithinRepo_路径越界_抛错(string evil)
    {
        Assert.Throws<GitException>(() => PathGuard.EnsureWithinRepo(Repo, evil));
    }

    [Fact]
    public void EnsureWithinRepo_绝对路径_抛错()
    {
        Assert.Throws<GitException>(() => PathGuard.EnsureWithinRepo(Repo, @"C:\Windows\System32\hosts"));
    }

    [Theory]
    [InlineData("abc12345678901234567890123456789012345678", true)] // 40 位
    [InlineData("abc1234", true)]
    [InlineData("ABCDEF0", true)]
    [InlineData("xyz", false)]
    [InlineData("", false)]
    [InlineData("abc123!", false)]
    public void IsHexSha_判定(string s, bool expected)
    {
        Assert.Equal(expected, PathGuard.IsHexSha(s));
    }
}
