using System.IO;
using GitDiffReviewer.Models;

namespace GitDiffReviewer.Services;

/// <summary>
/// 计算「工作区 vs HEAD」的未提交改动：已跟踪文件的修改/删除/重命名 + 未跟踪的新文件。
/// 只读，不写仓库。
/// </summary>
public sealed class WorkingTreeService(GitCommands cmds)
{
    public async Task<ChangeSet> BuildAsync(string repoRoot, string headSha, int maxFiles = 3000)
    {
        var ns = await cmds.WorkTreeNameStatusAsync();
        var entries = ChangeSetService.ParseNameStatus(ns.StdOut);

        var nst = await cmds.WorkTreeNumStatAsync();
        ChangeSetService.MergeNumStat(nst.StdOut, entries);

        // 未跟踪文件：当作 Added，行数读盘统计
        var ut = await cmds.ListUntrackedAsync();
        var untracked = EncodingUtil.SplitNul(ut.StdOut)
            .Select(EncodingUtil.Decode)
            .Where(p => p.Length > 0)
            .ToList();
        foreach (var p in untracked)
        {
            if (entries.ContainsKey(p)) continue;
            var full = PathGuard.EnsureWithinRepo(repoRoot, p);
            bool binary;
            int lines = CountLines(full, out binary);
            entries[p] = new ChangeEntry(p, null, ChangeStatus.Added, null, lines, 0, binary);
        }

        var all = ChangeSorter.Sort(entries.Values);
        bool truncated = all.Count > maxFiles;
        var page = truncated ? all.Take(maxFiles).ToList() : all;

        return new ChangeSet(
            RepoRoot: repoRoot,
            BaseSha: headSha,
            TargetSha: headSha,
            BaseShort: headSha[..Math.Min(7, headSha.Length)],
            TargetShort: "工作区",
            NonFastForward: false,
            Truncated: truncated,
            Entries: page,
            TotalInsertions: all.Sum(e => e.Insertions),
            TotalDeletions: all.Sum(e => e.Deletions));
    }

    /// <summary>对比任意 base（commit/HEAD）与工作区。</summary>
    public async Task<ChangeSet> BuildForBaseAsync(string repoRoot, string baseSha, int maxFiles = 3000)
    {
        var ns = await cmds.DiffNameStatusAsync(baseSha, "");
        var entries = ChangeSetService.ParseNameStatus(ns.StdOut);
        var nst = await cmds.DiffNumStatAsync(baseSha, "");
        ChangeSetService.MergeNumStat(nst.StdOut, entries);

        var ut = await cmds.ListUntrackedAsync();
        var untracked = EncodingUtil.SplitNul(ut.StdOut)
            .Select(EncodingUtil.Decode)
            .Where(p => p.Length > 0)
            .ToList();
        foreach (var p in untracked)
        {
            if (entries.ContainsKey(p)) continue;
            var full = PathGuard.EnsureWithinRepo(repoRoot, p);
            bool binary;
            int lines = CountLines(full, out binary);
            entries[p] = new ChangeEntry(p, null, ChangeStatus.Added, null, lines, 0, binary);
        }

        var all = ChangeSorter.Sort(entries.Values);
        bool truncated = all.Count > maxFiles;
        var page = truncated ? all.Take(maxFiles).ToList() : all;

        return new ChangeSet(
            RepoRoot: repoRoot,
            BaseSha: baseSha,
            TargetSha: "WORKTREE",
            BaseShort: baseSha[..Math.Min(7, baseSha.Length)],
            TargetShort: "工作区",
            NonFastForward: false,
            Truncated: truncated,
            Entries: page,
            TotalInsertions: all.Sum(e => e.Insertions),
            TotalDeletions: all.Sum(e => e.Deletions));
    }

    private static int CountLines(string fullPath, out bool binary)
    {
        binary = false;
        try
        {
            if (!File.Exists(fullPath)) return 0;
            var info = new FileInfo(fullPath);
            if (info.Length > 5 << 20) { binary = true; return 0; }
            var bytes = File.ReadAllBytes(fullPath);
            // 含 NUL 视为二进制
            for (int i = 0; i < Math.Min(bytes.Length, 8000); i++)
                if (bytes[i] == 0) { binary = true; return 0; }
            var text = EncodingUtil.Decode(bytes);
            return text.Count(c => c == '\n');
        }
        catch
        {
            return 0;
        }
    }
}
