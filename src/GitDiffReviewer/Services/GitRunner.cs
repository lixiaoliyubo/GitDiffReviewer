using System.Diagnostics;
using System.IO;
using System.Text;

namespace GitDiffReviewer.Services;

public sealed record GitResult(int ExitCode, byte[] StdOut, string StdErr);

/// <summary>
/// git 进程唯一出口（§7.1）。所有 git 调用都走这里：
///  - 只允许 ArgumentList，禁止拼接命令行；
///  - 拿原始字节，解码在调用方按字段做；
///  - stdout 有上限，避免大仓库撑爆内存（§11）。
/// </summary>
public sealed class GitRunner(string repoRoot)
{
    private static readonly string[] Prefix =
    [
        "--no-pager", "-c", "core.quotepath=false"
    ];

    public async Task<GitResult> RunAsync(
        IReadOnlyList<string> args,
        int timeoutMs = 30_000,
        int maxBytes = 32 << 20,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
            // 不设置 StandardOutputEncoding：要拿原始字节
        };
        foreach (var a in Prefix) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["LC_ALL"] = "C.UTF-8";

        Process p;
        try
        {
            p = Process.Start(psi) ?? throw new GitException("GIT_NOT_FOUND", "未找到 git，请先安装 Git for Windows");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new GitException("GIT_NOT_FOUND", "未找到 git，请先安装 Git for Windows");
        }

        using (p)
        {
            using var ms = new MemoryStream();
            var readTask = ReadLimitedAsync(p.StandardOutput.BaseStream, ms, maxBytes, ct);
            var errTask = p.StandardError.ReadToEndAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            try
            {
                await Task.WhenAll(readTask, errTask).WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
                throw new GitException("GIT_TIMEOUT", $"git 执行超时（{timeoutMs}ms）");
            }

            await p.WaitForExitAsync(ct);
            return new GitResult(p.ExitCode, ms.ToArray(), await errTask);
        }
    }

    /// <summary>带上限读取：超过 maxBytes 就截断，不把整个 stdout 无上限地读进内存。</summary>
    private static async Task ReadLimitedAsync(Stream src, MemoryStream dst, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        int total = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            if (total + read > maxBytes)
            {
                var remain = maxBytes - total;
                if (remain > 0) dst.Write(buffer.AsSpan(0, remain));
                return;
            }
            dst.Write(buffer.AsSpan(0, read));
            total += read;
        }
    }
}
