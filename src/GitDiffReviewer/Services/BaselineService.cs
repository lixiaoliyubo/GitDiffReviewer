using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitDiffReviewer.Services;

/// <summary>state/&lt;repoKey&gt;.json 的内容（§7.6）。</summary>
public sealed record BaselineState(
    string BaselineHead,
    string LastSeenHead,
    string BaselineAt,
    string BaselineSource);

/// <summary>
/// 基线状态机（FR-2 / §7.5）：
///  - 无 baselineHead → 初始化为当前 HEAD（source=initial）；
///  - HEAD 变化绝不自动推进 baselineHead，只更 lastSeenHead；
///  - 只有「标记已审阅」「手选提交」才推进 baselineHead。
/// </summary>
public sealed class BaselineService
{
    private readonly string _repoRoot;
    private readonly GitCommands _cmds;
    private readonly string _stateDir;
    private readonly string _stateFile;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public BaselineState? State { get; private set; }

    public BaselineService(string repoRoot, GitCommands cmds, string appDataDir)
    {
        _repoRoot = repoRoot;
        _cmds = cmds;
        _stateDir = Path.Combine(appDataDir, "state");
        _stateFile = Path.Combine(_stateDir, ComputeRepoKey(repoRoot) + ".json");
    }

    /// <summary>启动/切换仓库时调用：加载或初始化基线，并刷新 lastSeenHead。</summary>
    public async Task<BaselineState> InitializeAsync()
    {
        Directory.CreateDirectory(_stateDir);
        var head = await _cmds.GetHeadShaAsync();

        var st = await ReadAsync();
        if (st is null || string.IsNullOrEmpty(st.BaselineHead))
        {
            st = new BaselineState(head, head, DateTime.UtcNow.ToString("o"), "initial");
        }
        else
        {
            // 基线 commit 已被 gc：重置为当前 HEAD（FR-2 第 4 条）
            try { await _cmds.VerifyCommitAsync(st.BaselineHead); }
            catch (GitException)
            {
                st = new BaselineState(head, head, DateTime.UtcNow.ToString("o"), "initial");
            }
        }

        State = st with { LastSeenHead = head };
        await SaveAsync(State);
        return State;
    }

    /// <summary>「标记已审阅」：baseline = HEAD（manual）。</summary>
    public async Task<BaselineState> MarkReviewedAsync()
    {
        var head = await _cmds.GetHeadShaAsync();
        State = new BaselineState(head, head, DateTime.UtcNow.ToString("o"), "manual");
        await SaveAsync(State);
        return State;
    }

    /// <summary>「选择基线」：校验 SHA 后写入（picked）。</summary>
    public async Task<BaselineState> PickBaselineAsync(string sha)
    {
        var verified = await _cmds.VerifyCommitAsync(sha);
        var head = await _cmds.GetHeadShaAsync();
        State = new BaselineState(verified, head, DateTime.UtcNow.ToString("o"), "picked");
        await SaveAsync(State);
        return State;
    }

    /// <summary>HeadWatcher 触发：只更新 lastSeenHead，绝不碰 baselineHead。</summary>
    public async Task<BaselineState> UpdateLastSeenAsync()
    {
        if (State is null) return await InitializeAsync();
        var head = await _cmds.GetHeadShaAsync();
        if (head == State.LastSeenHead) return State;
        State = State with { LastSeenHead = head };
        await SaveAsync(State);
        return State;
    }

    private async Task<BaselineState?> ReadAsync()
    {
        if (!File.Exists(_stateFile)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(_stateFile);
            return JsonSerializer.Deserialize<BaselineState>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveAsync(BaselineState st)
    {
        Directory.CreateDirectory(_stateDir);
        var json = JsonSerializer.Serialize(st, JsonOpts);
        var tmp = _stateFile + ".tmp";
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(false));
        // 原子替换，避免半截 JSON（§7.6）
        File.Move(tmp, _stateFile, overwrite: true);
    }

    /// <summary>repoKey = SHA256(规范化绝对路径小写) 前 16 位 hex（§7.6）。</summary>
    private static string ComputeRepoKey(string repoRoot)
    {
        var full = Path.GetFullPath(repoRoot).ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(full));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
