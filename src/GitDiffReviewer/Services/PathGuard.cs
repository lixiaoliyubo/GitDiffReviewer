using System.IO;
using System.Text.RegularExpressions;

namespace GitDiffReviewer.Services;

/// <summary>
/// 路径与引用校验（§7.7）：前端传来的 path 一律不信任。
/// </summary>
public static class PathGuard
{
    private static readonly Regex ShaHex = new("^[0-9a-fA-F]{7,64}$", RegexOptions.Compiled);

    /// <summary>
    /// 把相对路径拼到 repoRoot 下并校验不越界。返回安全的绝对路径。
    /// </summary>
    public static string EnsureWithinRepo(string repoRoot, string relativePath)
    {
        var full = Path.GetFullPath(relativePath, repoRoot);
        var rel = Path.GetRelativePath(repoRoot, full);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            throw new GitException("PATH_ESCAPE", "该路径不在仓库内");
        return full;
    }

    /// <summary>rev 必须是 7~64 位 hex。</summary>
    public static bool IsHexSha(string s) =>
        !string.IsNullOrEmpty(s) && ShaHex.IsMatch(s);
}
