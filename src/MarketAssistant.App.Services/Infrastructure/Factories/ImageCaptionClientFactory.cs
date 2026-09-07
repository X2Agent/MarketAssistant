using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Rag.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 基于 IChatClientFactory 的图像描述客户端工厂：
/// 统一 AI 客户端由 ChatClientFactory 创建（M.E.AI IChatClient），此处适配为 SK IChatCompletionService；
/// AI 未配置（FriendlyException）时返回 null，Caption 降级为占位符。
/// </summary>
public sealed class ImageCaptionClientFactory : IImageCaptionClientFactory
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<ImageCaptionClientFactory> _logger;

    public ImageCaptionClientFactory(IChatClientFactory chatClientFactory, ILogger<ImageCaptionClientFactory> logger)
    {
        _chatClientFactory = chatClientFactory;
        _logger = logger;
    }

    public IChatCompletionService? Create()
    {
        try
        {
            return _chatClientFactory.CreateClient().AsChatCompletionService();
        }
        catch (FriendlyException ex)
        {
            _logger.LogDebug(ex, "AI 未配置，图像描述功能降级为占位符");
            return null;
        }
    }
}
