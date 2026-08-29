using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 图像描述（Caption）客户端工厂：为 Rag 层提供可选的多模态聊天能力。
/// AI 未配置时返回 null，由调用方降级为占位符描述。
/// </summary>
public interface IImageCaptionClientFactory
{
    IChatCompletionService? Create();
}
