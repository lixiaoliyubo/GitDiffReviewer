namespace GitDiffReviewer.Models;

/// <summary>变更状态（§6.1）。</summary>
public enum ChangeStatus
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Broken,
    Unknown
}

/// <summary>单个文件的变更记录（§6.1）。</summary>
public sealed record ChangeEntry(
    string Path,          // 新路径，相对仓库根，正斜杠
    string? OldPath,      // 重命名/复制的旧路径
    ChangeStatus Status,
    string? Score,        // 重命名相似度 "100"
    int Insertions,
    int Deletions,
    bool Binary);
