using System.Globalization;
using System.Text.RegularExpressions;
using GitDiffReviewer.Models;

namespace GitDiffReviewer.Services;

/// <summary>
/// 列出提交历史，并构建某个 commit 相对于其父提交的 ChangeSet。
/// </summary>
public sealed class CommitService(GitCommands cmds)
{
    public sealed record CommitInfo(string Sha, string Short, string Message, string Author, string DateIso);

    public async Task<List<CommitInfo>> ListAsync(int count = 50)
    {
        // %H 全量 sha，%h 短 sha，%an 作者，%aI  ISO8601 日期，%s  主题
        var r = await cmds.LogFormatAsync("%H%x1f%h%x1f%an%x1f%aI%x1f%s", count);
        if (r.ExitCode != 0) return new List<CommitInfo>();
        var text = EncodingUtil.Decode(r.StdOut);
        var list = new List<CommitInfo>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\x1f');
            if (parts.Length < 5) continue;
            list.Add(new CommitInfo(parts[0], parts[1], parts[4], parts[2], parts[3]));
        }
        return list;
    }

    public async Task<ChangeSet> BuildCommitDiffAsync(string repoRoot, string sha)
    {
        // parent..commit
        var range = $"{sha}~1..{sha}";
        var ns = await cmds.DiffNameStatusAsync($"{sha}~1", sha);
        var entries = ChangeSetService.ParseNameStatus(ns.StdOut);
        var nst = await cmds.DiffNumStatAsync($"{sha}~1", sha);
        ChangeSetService.MergeNumStat(nst.StdOut, entries);

        var all = ChangeSorter.Sort(entries.Values);
        return new ChangeSet(
            RepoRoot: repoRoot,
            BaseSha: $"{sha}~1",
            TargetSha: sha,
            BaseShort: "",
            TargetShort: sha[..Math.Min(7, sha.Length)],
            NonFastForward: false,
            Truncated: false,
            Entries: all,
            TotalInsertions: all.Sum(e => e.Insertions),
            TotalDeletions: all.Sum(e => e.Deletions));
    }
}
