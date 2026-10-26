using System.IO;
using GitDiffReviewer.Models;
using GitDiffReviewer.Services;
using Xunit;

namespace GitDiffReviewer.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "gdr-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task 文件不存在_返回默认值()
    {
        var s = await new SettingsStore(_dir).LoadAsync();
        Assert.True(s.InlineDiff);
        Assert.Equal(3000, s.MaxFiles);
        Assert.Empty(s.Repos);
    }

    [Fact]
    public async Task 保存后再读_字段一致()
    {
        var store = new SettingsStore(_dir);
        var s = new Settings
        {
            ActiveRepo = @"E:\demo",
            InlineDiff = false,
            MaxFiles = 123,
            OnboardingDone = true,
        };
        s.Repos.Add(new RepoInfo { Path = @"E:\demo", Name = "demo", AddedAt = "2026-01-01" });

        await store.SaveAsync(s);
        var back = await store.LoadAsync();

        Assert.Equal(@"E:\demo", back.ActiveRepo);
        Assert.False(back.InlineDiff);
        Assert.Equal(123, back.MaxFiles);
        Assert.True(back.OnboardingDone);
        Assert.Single(back.Repos);
        Assert.Equal("demo", back.Repos[0].Name);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
