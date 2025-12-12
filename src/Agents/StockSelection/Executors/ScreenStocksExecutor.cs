using MarketAssistant.Agents.StockSelection.Models;
using MarketAssistant.Services.StockScreener;
using MarketAssistant.Services.StockScreener.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.StockSelection.Executors;

/// <summary>
/// 步骤2: 执行股票筛选的 Executor（基于 Executor<TInput, TOutput> 模式）
/// 直接调用 StockScreenerService 进行股票筛选
/// </summary>
public sealed class ScreenStocksExecutor : Executor<CriteriaGenerationResult, ScreeningResult>
{
    private readonly StockScreenerService _stockScreenerService;
    private readonly ILogger<ScreenStocksExecutor> _logger;

    public ScreenStocksExecutor(
        StockScreenerService stockScreenerService,
        ILogger<ScreenStocksExecutor> logger) : base("ScreenStocks")
    {
        _stockScreenerService = stockScreenerService ?? throw new ArgumentNullException(nameof(stockScreenerService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async ValueTask<ScreeningResult> HandleAsync(
        CriteriaGenerationResult input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤2/3] 执行股票筛选");

        try
        {
            if (input?.Criteria == null)
            {
                throw new ArgumentNullException(nameof(input), "筛选条件不能为空");
            }

            _logger.LogInformation("[步骤2/3] 筛选条件: 市场={Market}, 行业={Industry}, 条件数={Count}",
                input.Criteria.Market, input.Criteria.Industry, input.Criteria.Criteria.Count);

            // 调用 StockScreenerService 执行筛选
            List<ScreenerStockInfo> stocks = await _stockScreenerService.ScreenStocksAsync(input.Criteria);

            _logger.LogInformation("[步骤2/3] 筛选完成，获得 {Count} 只股票", stocks.Count);

            // 返回筛选结果
            return new ScreeningResult
            {
                ScreenedStocks = stocks,
                Criteria = input.Criteria,
                OriginalRequest = input.OriginalRequest
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤2/3] 股票筛选失败");
            throw new InvalidOperationException($"股票筛选失败: {ex.Message}", ex);
        }
    }
}

