using System.Text.RegularExpressions;

namespace GitDiffReviewer.Services;

/// <summary>
/// §7.2 git 命令白名单封装（只有这 7 条）。B2 阶段只暴露只读命令的薄包装，
/// name-status / numstat 的结构化解析放在 B3 的 ChangeSetService。
/// </summary>
public sealed class GitCommands(GitRunner runner)
{
    private static readonly Regex ShaHex = new("^[0-9a-fA-F]{7,64}$", RegexOptions.Compiled);

    /// <summary>当前 HEAD 的 40 位 SHA。</summary>
    public async Task<string> GetHeadShaAsync()
    {
        var r = await runner.RunAsync(["rev-parse", "HEAD"]);
        if (r.ExitCode != 0)
            throw new GitException("NOT_A_REPO", "这个文件夹不是 git 仓库");
        return EncodingUtil.Decode(r.StdOut).Trim();
    }

    /// <summary>校验 rev 是否指向一个 commit，返回 40 位 SHA。</summary>
    public async Task<string> VerifyCommitAsync(string rev)
    {
        if (!ShaHex.IsMatch(rev))
            throw new GitException("BAD_REV", "提交引用格式非法");
        var r = await runner.RunAsync(["rev-parse", "--verify", "--end-of-options", $"{rev}^{{commit}}"]);
        if (r.ExitCode != 0)
            throw new GitException("BASELINE_GONE", "基线已失效，已重置为当前 HEAD");
        return EncodingUtil.Decode(r.StdOut).Trim();
    }

    /// <summary>祖先判断：true=是祖先 / false=非祖先 / 抛错=其它。</summary>
    public async Task<bool> IsAncestorAsync(string a, string b)
    {
        var r = await runner.RunAsync(["merge-base", "--is-ancestor", a, b]);
        return r.ExitCode == 0; // 1 = 非祖先
    }

    /// <summary>变更清单（name-status，-z 原始字节，B3 解析）。</summary>
    public Task<GitResult> DiffNameStatusAsync(string a, string b) =>
        runner.RunAsync(["diff", "--no-ext-diff", "--no-textconv", "--find-renames=50%", "--name-status", "-z", a, b]);

    /// <summary>增删行数（numstat，-z 原始字节，B3 解析）。</summary>
    public Task<GitResult> DiffNumStatAsync(string a, string b) =>
        runner.RunAsync(["diff", "--no-ext-diff", "--no-textconv", "--find-renames=50%", "--numstat", "-z", a, b]);

    /// <summary>取 <paramref name="sha"/> 处 <paramref name="path"/> 的原始字节内容。</summary>
    public Task<GitResult> ShowFileAsync(string sha, string path) =>
        runner.RunAsync(["show", $"{sha}:{path}"]);

    /// <summary>最近 n 条提交（原始字节输出，B4 解析制表符分隔字段）。</summary>
    public Task<GitResult> LogRecentAsync(int n = 30) =>
        runner.RunAsync(["log", "-n", n.ToString(), "--format=%H%x09%h%x09%s", "HEAD"]);

    /// <summary>自定义 format 的 log。</summary>
    public Task<GitResult> LogFormatAsync(string format, int n = 50) =>
        runner.RunAsync(["log", "-n", n.ToString(), $"--format={format}", "HEAD"]);

    // ---- 工作区改动（未提交） ----

    /// <summary>工作区 vs HEAD 的 name-status（含已暂存+未暂存，不含未跟踪文件）。</summary>
    public Task<GitResult> WorkTreeNameStatusAsync() =>
        runner.RunAsync(["diff", "--no-ext-diff", "--no-textconv", "--find-renames=50%", "--name-status", "-z", "HEAD"]);

    /// <summary>工作区 vs HEAD 的 numstat（同上）。</summary>
    public Task<GitResult> WorkTreeNumStatAsync() =>
        runner.RunAsync(["diff", "--no-ext-diff", "--no-textconv", "--find-renames=50%", "--numstat", "-z", "HEAD"]);

    /// <summary>未跟踪文件列表（不含 .gitignore），-z 原始字节。</summary>
    public Task<GitResult> ListUntrackedAsync() =>
        runner.RunAsync(["ls-files", "--others", "--exclude-standard", "-z"]);

    /// <summary>所有已跟踪文件列表，-z 原始字节。</summary>
    public Task<GitResult> LsFilesAsync() =>
        runner.RunAsync(["ls-files", "-z"]);
}
