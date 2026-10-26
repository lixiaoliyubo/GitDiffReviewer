using GitDiffReviewer.Services;

namespace GitDiffReviewer.Bridge;

/// <summary>按活动仓库组装的服务会话（切换仓库时重建）。</summary>
public sealed class RepoSession
{
    public string RepoRoot { get; }
    public GitCommands Commands { get; }
    public BaselineService Baseline { get; }
    public ChangeSetService Changes { get; }

    public RepoSession(string repoRoot, string appDataDir)
    {
        RepoRoot = repoRoot;
        var runner = new GitRunner(repoRoot);
        Commands = new GitCommands(runner);
        Baseline = new BaselineService(repoRoot, Commands, appDataDir);
        Changes = new ChangeSetService(Commands);
    }
}
