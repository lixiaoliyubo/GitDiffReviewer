using GitDiffReviewer.Models;

namespace GitDiffReviewer.Services;

/// <summary>
/// 变更集排序（FR-3 第 4 步）：状态优先级 M → A → D → R → C → 其它，
/// 同状态按路径升序。纯函数，B3 的 ChangeSetService 直接调用。
/// </summary>
public static class ChangeSorter
{
    private static int Rank(ChangeStatus s) => s switch
    {
        ChangeStatus.Modified => 0,
        ChangeStatus.Added => 1,
        ChangeStatus.Deleted => 2,
        ChangeStatus.Renamed => 3,
        ChangeStatus.Copied => 4,
        _ => 5,
    };

    public static List<ChangeEntry> Sort(IEnumerable<ChangeEntry> entries) =>
        entries
            .OrderBy(e => Rank(e.Status))
            .ThenBy(e => e.Path, StringComparer.Ordinal)
            .ToList();
}
