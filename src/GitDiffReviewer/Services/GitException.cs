namespace GitDiffReviewer.Services;

/// <summary>
/// 业务异常：带错误码（§10），由 MessageRouter 统一转成 ok:false 回包。
/// </summary>
public sealed class GitException : Exception
{
    public string Code { get; }

    public GitException(string code, string message) : base(message)
    {
        Code = code;
    }
}
