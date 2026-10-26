using GitDiffReviewer.Models;

namespace GitDiffReviewer.Services;

/// <summary>
/// 计算「基线 → HEAD」变更集（FR-3 + §7.3）。
/// 只解析 git 的 -z 输出，不做任何写操作。
/// </summary>
public sealed class ChangeSetService(GitCommands cmds)
{
    public async Task<ChangeSet> BuildAsync(string repoRoot, string baseSha, string headSha, int maxFiles = 3000)
    {
        bool nonFastForward;
        try
        {
            nonFastForward = !await cmds.IsAncestorAsync(baseSha, headSha);
        }
        catch
        {
            nonFastForward = false;
        }

        var ns = await cmds.DiffNameStatusAsync(baseSha, headSha);
        var entries = ParseNameStatus(ns.StdOut);

        var nst = await cmds.DiffNumStatAsync(baseSha, headSha);
        MergeNumStat(nst.StdOut, entries);

        var all = ChangeSorter.Sort(entries.Values);
        bool truncated = all.Count > maxFiles;
        var page = truncated ? all.Take(maxFiles).ToList() : all;

        return new ChangeSet(
            RepoRoot: repoRoot,
            BaseSha: baseSha,
            TargetSha: headSha,
            BaseShort: baseSha[..Math.Min(7, baseSha.Length)],
            TargetShort: headSha[..Math.Min(7, headSha.Length)],
            NonFastForward: nonFastForward,
            Truncated: truncated,
            Entries: page,
            TotalInsertions: all.Sum(e => e.Insertions),
            TotalDeletions: all.Sum(e => e.Deletions));
    }

    /// <summary>解析 name-status -z：状态、路径（R/C 为 状态、旧路径、新路径）。</summary>
    public static Dictionary<string, ChangeEntry> ParseNameStatus(byte[] buf)
    {
        var tokens = EncodingUtil.SplitNul(buf);
        var dict = new Dictionary<string, ChangeEntry>(StringComparer.Ordinal);
        int i = 0;
        while (i < tokens.Count)
        {
            var statusTok = EncodingUtil.Decode(tokens[i++]);
            if (statusTok.Length == 0) break;

            var statusChar = char.ToUpperInvariant(statusTok[0]);
            string? score = statusTok.Length > 1 ? statusTok[1..] : null;

            string? oldPath = null;
            string newPath;
            if (statusChar is 'R' or 'C')
            {
                if (i + 1 >= tokens.Count) break;
                oldPath = EncodingUtil.Decode(tokens[i++]);
                newPath = EncodingUtil.Decode(tokens[i++]);
            }
            else
            {
                newPath = EncodingUtil.Decode(tokens[i++]);
            }

            var status = statusChar switch
            {
                'M' => ChangeStatus.Modified,
                'A' => ChangeStatus.Added,
                'D' => ChangeStatus.Deleted,
                'R' => ChangeStatus.Renamed,
                'C' => ChangeStatus.Copied,
                'T' => ChangeStatus.TypeChanged,
                'U' => ChangeStatus.Broken,
                _ => ChangeStatus.Unknown,
            };

            dict[newPath] = new ChangeEntry(newPath, oldPath, status, score, 0, 0, false);
        }
        return dict;
    }

    /// <summary>合并 numstat -z：填充增删行数与 binary 标记。</summary>
    public static void MergeNumStat(byte[] buf, Dictionary<string, ChangeEntry> entries)
    {
        var tokens = EncodingUtil.SplitNul(buf);
        int i = 0;
        while (i < tokens.Count)
        {
            var head = EncodingUtil.Decode(tokens[i++]);
            if (head.Length == 0) break;

            var tab = head.Split('\t');
            if (tab.Length < 3) continue;

            var addTok = tab[0];
            var delTok = tab[1];
            var path = tab[2];
            bool binary = addTok == "-" && delTok == "-";
            int add = binary ? 0 : int.Parse(addTok);
            int del = binary ? 0 : int.Parse(delTok);

            string newPath;
            if (path == "")
            {
                // rename：本 token 后空路径，紧跟旧路径、新路径
                if (i + 1 >= tokens.Count) break;
                i++; // 跳过旧路径
                newPath = EncodingUtil.Decode(tokens[i++]);
            }
            else
            {
                newPath = path;
            }

            if (entries.TryGetValue(newPath, out var old))
            {
                entries[newPath] = old with { Insertions = add, Deletions = del, Binary = binary };
            }
        }
    }
}
