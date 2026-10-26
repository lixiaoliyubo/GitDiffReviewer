using GitDiffReviewer.Models;
using GitDiffReviewer.Services;
using Xunit;

namespace GitDiffReviewer.Tests;

public class ChangeSorterTests
{
    private static ChangeEntry E(ChangeStatus s, string path) =>
        new(path, null, s, null, 0, 0, false);

    [Fact]
    public void Sort_状态按MADRC优先_同状态按路径升序()
    {
        var input = new[]
        {
            E(ChangeStatus.Added, "b.txt"),
            E(ChangeStatus.Modified, "a.txt"),
            E(ChangeStatus.Deleted, "d.txt"),
            E(ChangeStatus.Renamed, "r.txt"),
            E(ChangeStatus.Added, "a.txt"),
        };

        var sorted = ChangeSorter.Sort(input);

        Assert.Equal(
            new[] {
                ChangeStatus.Modified,   // M 最先
                ChangeStatus.Added,      // A
                ChangeStatus.Added,
                ChangeStatus.Deleted,    // D
                ChangeStatus.Renamed,    // R
            },
            sorted.Select(e => e.Status).ToArray());

        // 两个 Added：路径 a.txt 在 b.txt 前
        Assert.Equal("a.txt", sorted[1].Path);
        Assert.Equal("b.txt", sorted[2].Path);
    }

    [Fact]
    public void Sort_空输入_返回空列表()
    {
        Assert.Empty(ChangeSorter.Sort(Array.Empty<ChangeEntry>()));
    }
}
