using System.IO;
using GitDiffReviewer.Models;

namespace GitDiffReviewer.Services;

/// <summary>
/// 取文件两侧内容给 Monaco（FR-5）：git show → 解码 → 截断/二进制/图片识别。
/// 入参 entry 必须来自当前变更集（由 MessageRouter 查表），不信任前端直接传的路径。
/// </summary>
public sealed class FileContentService(GitCommands cmds)
{
    public const int MaxLines = 20000;

    public async Task<FileContent> GetAsync(ChangeEntry entry, string baseSha, string repoRoot, string? targetSha = null)
    {
        var oldPath = entry.OldPath ?? entry.Path;
        var newPath = entry.Path;

        // old 侧：Added 为空；其余从 git show baseSha:oldPath
        var oldBytes = entry.Status == ChangeStatus.Added
            ? Array.Empty<byte>()
            : (await cmds.ShowFileAsync(baseSha, oldPath)).StdOut;

        // new 侧：Deleted 为空；指定 targetSha 时从 git show targetSha:newPath，否则读工作区文件
        byte[] newBytes;
        if (entry.Status == ChangeStatus.Deleted)
        {
            newBytes = Array.Empty<byte>();
        }
        else if (targetSha != null)
        {
            newBytes = (await cmds.ShowFileAsync(targetSha, newPath)).StdOut;
        }
        else
        {
            var fullNew = PathGuard.EnsureWithinRepo(repoRoot, newPath);
            newBytes = File.Exists(fullNew) ? await File.ReadAllBytesAsync(fullNew) : Array.Empty<byte>();
        }

        var ext = Path.GetExtension(newPath).ToLowerInvariant();
        bool binary = entry.Binary || HasNul(oldBytes) || HasNul(newBytes);

        if (binary && IsImageExt(ext))
        {
            return new FileContent(
                "", "", oldPath, newPath, "binary", true, false, "plaintext",
                oldBytes.Length == 0 ? null : Convert.ToBase64String(oldBytes),
                newBytes.Length == 0 ? null : Convert.ToBase64String(newBytes));
        }
        if (binary)
        {
            return new FileContent("", "", oldPath, newPath, "binary", true, false, "plaintext", null, null);
        }

        var oldText = EncodingUtil.Decode(oldBytes);
        var newText = EncodingUtil.Decode(newBytes);

        bool truncated = false;
        if (CountLines(oldText) > MaxLines) { oldText = TruncateLines(oldText, MaxLines); truncated = true; }
        if (CountLines(newText) > MaxLines) { newText = TruncateLines(newText, MaxLines); truncated = true; }

        return new FileContent(oldText, newText, oldPath, newPath, "utf-8", false, truncated,
            LanguageFor(ext), null, null);
    }

    private static bool HasNul(byte[] b)
    {
        for (int i = 0; i < b.Length; i++) if (b[i] == 0) return true;
        return false;
    }

    private static bool IsImageExt(string ext) => ext is
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg";

    private static int CountLines(string s)
    {
        if (s.Length == 0) return 0;
        int n = 1;
        foreach (var c in s) if (c == '\n') n++;
        return n;
    }

    private static string TruncateLines(string s, int max)
    {
        int count = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\n')
            {
                count++;
                if (count >= max) return s[..(i + 1)];
            }
        }
        return s;
    }

    private static string LanguageFor(string ext) => ext switch
    {
        ".cs" => "csharp",
        ".ts" or ".tsx" => "typescript",
        ".js" or ".jsx" => "javascript",
        ".vue" or ".html" or ".htm" => "html",
        ".json" => "json",
        ".md" or ".markdown" => "markdown",
        ".py" => "python",
        ".java" => "java",
        ".c" or ".h" => "c",
        ".cpp" or ".hpp" or ".cc" => "cpp",
        ".go" => "go",
        ".rs" => "rust",
        ".css" => "css",
        ".xml" => "xml",
        ".yml" or ".yaml" => "yaml",
        ".sql" => "sql",
        _ => "plaintext"
    };
}

public sealed record FileContent(
    string OldText, string NewText,
    string OldLabel, string NewLabel,
    string Encoding, bool Binary, bool Truncated,
    string Language,
    string? OldImage, string? NewImage);
