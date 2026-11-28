using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.InvestmentSelection.Executors;

/// <summary>
/// 步骤2: 执行投资标的筛选（共用，支持多市场）
/// 通过 IAssetScreenerService 接口抽象，根据市场类型动态选择筛选服务
/// </summary>
public sealed partial class ScreenInvestmentTargetsExecutor(
    IServiceProvider serviceProvider,
    ILogger<ScreenInvestmentTargetsExecutor> logger) : Executor("ScreenInvestmentTargets")
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<ScreenInvestmentTargetsExecutor> _logger = logger;

    [MessageHandler]
    private async ValueTask<AssetScreeningResult> HandleAsync(
        CriteriaGenerationResult input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤2/3] 执行投资标的筛选");

        try
        {
            var originalRequest = input.OriginalRequest;

            // 根据市场类型获取对应的筛选服务（使用 Keyed Services）
            var screenerService = _serviceProvider.GetRequiredKeyedService<IAssetScreenerService>(originalRequest.MarketType);

            _logger.LogInformation("[步骤2/3] 使用市场类型: {MarketType}, 筛选服务: {ServiceType}",
                originalRequest.MarketType, screenerService.GetType().Name);

            // 透传取消令牌，用户中断时立即终止上游 HTTP 请求
            List<ScreenerAssetInfo> assets = await screenerService.ScreenAsync(input.Criteria, cancellationToken);

            _logger.LogInformation("[步骤2/3] 筛选完成，获得 {Count} 个投资标的", assets.Count);

            return new AssetScreeningResult
            {
                ScreenedAssets = assets,
                Criteria = input.Criteria,
                OriginalRequest = originalRequest
            };
        }
        // 取消与已带用户友好信息的 FriendlyException 原样上抛，不二次包装
        catch (Exception ex) when (ex is not (OperationCanceledException or FriendlyException))
        {
            _logger.LogError(ex, "[步骤2/3] 投资标的筛选失败");
            throw new FriendlyException($"投资标的筛选失败: {ex.Message}", ex);
        }
    }
}
