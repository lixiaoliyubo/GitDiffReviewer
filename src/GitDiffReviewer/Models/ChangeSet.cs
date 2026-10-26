namespace GitDiffReviewer.Models;

/// <summary>一次基线→HEAD 的变更集（§6.1）。</summary>
public sealed record ChangeSet(
    string RepoRoot,
    string BaseSha, string TargetSha,
    string BaseShort, string TargetShort,
    bool NonFastForward,
    bool Truncated,
    IReadOnlyList<ChangeEntry> Entries,
    int TotalInsertions, int TotalDeletions);
