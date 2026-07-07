using SonnetDB.Contracts;

namespace SonnetDB.Copilot;

/// <summary>
/// 对话 completion provider 抽象。
/// </summary>
public interface IChatProvider
{
    /// <summary>
    /// 基于消息列表生成单轮回复。
    /// </summary>
    /// <param name="messages">输入消息。</param>
    /// <param name="modelOverride">可选模型名覆盖；为空时使用服务端默认模型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>模型回复文本。</returns>
    ValueTask<string> CompleteAsync(IReadOnlyList<AiMessage> messages, string? modelOverride = null, CancellationToken cancellationToken = default);
}
