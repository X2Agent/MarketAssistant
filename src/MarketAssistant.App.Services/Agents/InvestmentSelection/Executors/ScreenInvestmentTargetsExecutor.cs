using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.InvestmentSelection.Executors;

/// <summary>
/// 步骤2: 执行投资标的筛选的 Executor（共用，支持多市场）
/// 通过 IAssetScreenerService 接口抽象，根据市场类型动态选择筛选服务
/// </summary>
public sealed partial class ScreenInvestmentTargetsExecutor : Executor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScreenInvestmentTargetsExecutor> _logger;

    public ScreenInvestmentTargetsExecutor(
        IServiceProvider serviceProvider,
        ILogger<ScreenInvestmentTargetsExecutor> logger) : base("ScreenInvestmentTargets")
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [MessageHandler]
    private async ValueTask<AssetScreeningResult> HandleAsync(
        CriteriaGenerationResult input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤2/3] 执行投资标的筛选");

        try
        {
            if (input?.Criteria == null)
            {
                throw new ArgumentNullException(nameof(input), "筛选条件不能为空");
            }

            var originalRequest = input.OriginalRequest;
            if (originalRequest == null)
            {
                throw new InvalidOperationException("缺少原始请求信息");
            }

            // 根据市场类型获取对应的筛选服务（使用 Keyed Services）
            var screenerService = _serviceProvider.GetRequiredKeyedService<IAssetScreenerService>(originalRequest.MarketType);

            _logger.LogInformation("[步骤2/3] 使用市场类型: {MarketType}, 筛选服务: {ServiceType}",
                originalRequest.MarketType, screenerService.GetType().Name);

            // 调用筛选服务
            List<ScreenerAssetInfo> assets = await screenerService.ScreenAsync(input.Criteria);

            _logger.LogInformation("[步骤2/3] 筛选完成，获得 {Count} 个投资标的", assets.Count);

            // 返回筛选结果
            return new AssetScreeningResult
            {
                ScreenedAssets = assets,
                Criteria = input.Criteria,
                OriginalRequest = originalRequest
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤2/3] 投资标的筛选失败");
            throw new FriendlyException("投资标的筛选失败，请稍后重试", ex);
        }
    }
}

