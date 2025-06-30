using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Filtering;

public sealed class PromptRenderLoggingFilter(ILogger<PromptRenderLoggingFilter> logger) : IPromptRenderFilter
{
    public Task OnPromptRenderAsync(PromptRenderContext context, Func<PromptRenderContext, Task> next)
    {
        // 记录提示词渲染的上下文信息
        logger.LogTrace("Rendered prompt: {Prompt}", context.RenderedPrompt);
        // 调用下一个过滤器
        return next(context);
    }
}
