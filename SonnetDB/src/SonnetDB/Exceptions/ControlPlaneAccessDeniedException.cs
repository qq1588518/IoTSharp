namespace SonnetDB.Exceptions;

/// <summary>
/// 表示当前请求无权执行目标控制面操作。
/// </summary>
internal sealed class ControlPlaneAccessDeniedException : Exception
{
    public ControlPlaneAccessDeniedException(string message)
        : base(message)
    {
    }
}
