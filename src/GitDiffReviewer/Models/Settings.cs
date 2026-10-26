namespace GitDiffReviewer.Models;

public sealed class RepoInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string AddedAt { get; set; } = "";
}

public sealed class WindowSettings
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>settings.json 结构（§9）。</summary>
public sealed class Settings
{
    public List<RepoInfo> Repos { get; set; } = new();
    public string ActiveRepo { get; set; } = "";
    public bool InlineDiff { get; set; } = true;
    public bool NotifyOnChange { get; set; } = true;
    public int MaxFiles { get; set; } = 3000;
    public int MaxFileLines { get; set; } = 20000;
    public bool ShowUncommitted { get; set; } = false; // 预留，不实现
    public bool OnboardingDone { get; set; } = false;
    public WindowSettings? Window { get; set; }
    public double Splitter { get; set; } = 320;
}
