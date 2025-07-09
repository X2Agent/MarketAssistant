using MarketAssistant.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Services;

/// <summary>
/// AI选股服务
/// </summary>
public class StockSelectionService : IDisposable
{
    private readonly StockSelectionManager _selectionManager;
    private readonly ILogger<StockSelectionService> _logger;

    public StockSelectionService(
        Kernel kernel,
        ILogger<StockSelectionService> logger)
    {
        _selectionManager = new StockSelectionManager(kernel);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 根据用户需求执行AI选股
    /// </summary>
    /// <param name="userRequirements">用户选股需求描述</param>
    /// <returns>选股分析结果</returns>
    public async Task<string> SelectStocksAsync(string userRequirements)
    {
        if (string.IsNullOrWhiteSpace(userRequirements))
        {
            throw new ArgumentException("用户选股需求不能为空", nameof(userRequirements));
        }

        try
        {
            _logger.LogInformation("开始执行AI选股，用户需求: {Requirements}", userRequirements);

            var result = await _selectionManager.ExecuteStockSelectionAsync(userRequirements);

            _logger.LogInformation("AI选股完成，结果长度: {Length}", result.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行AI选股时发生错误，用户需求: {Requirements}", userRequirements);
            throw;
        }
    }

    /// <summary>
    /// 执行快速选股（预设策略）
    /// </summary>
    /// <param name="strategy">选股策略</param>
    /// <returns>选股分析结果</returns>
    public async Task<string> QuickSelectAsync(QuickSelectionStrategy strategy)
    {
        try
        {
            _logger.LogInformation("开始执行快速选股，策略: {Strategy}", strategy);

            var result = await _selectionManager.ExecuteQuickSelectionAsync(strategy);

            _logger.LogInformation("快速选股完成，策略: {Strategy}，结果长度: {Length}", strategy, result.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行快速选股时发生错误，策略: {Strategy}", strategy);
            throw;
        }
    }

    /// <summary>
    /// 获取支持的快速选股策略列表
    /// </summary>
    /// <returns>策略列表</returns>
    public List<QuickSelectionStrategyInfo> GetQuickSelectionStrategies()
    {
        return new List<QuickSelectionStrategyInfo>
        {
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.ValueStocks,
                Name = "价值股筛选",
                Description = "筛选PE低、PB低、ROE高的优质价值股",
                Scenario = "适合稳健型投资者，追求长期价值投资",
                RiskLevel = "低风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.GrowthStocks,
                Name = "成长股筛选",
                Description = "筛选营收和利润高增长的成长型股票",
                Scenario = "适合积极型投资者，追求高成长收益",
                RiskLevel = "中高风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.ActiveStocks,
                Name = "活跃股筛选",
                Description = "筛选换手率高、成交活跃的热门股票",
                Scenario = "适合短线交易者，追求市场热点",
                RiskLevel = "高风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.LargeCap,
                Name = "大盘股筛选",
                Description = "筛选市值大、业绩稳定的蓝筹股",
                Scenario = "适合保守型投资者，追求稳定收益",
                RiskLevel = "低风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.SmallCap,
                Name = "小盘股筛选",
                Description = "筛选市值较小、具有成长潜力的股票",
                Scenario = "适合风险偏好较高的投资者",
                RiskLevel = "高风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.Dividend,
                Name = "高股息股筛选",
                Description = "筛选股息率高、分红稳定的股票",
                Scenario = "适合追求稳定现金流的投资者",
                RiskLevel = "低风险"
            }
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _selectionManager?.Dispose();
    }
}