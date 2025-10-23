using MarketAssistant.Agents.Plugins;
using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Services.Browser;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MarketAssistant.Agents.Workflows;

/// <summary>
/// 步骤2: 执行股票筛选的 Executor（确定性调用，无 AI 参与）
/// 直接调用 StockScreenerPlugin 进行股票筛选
/// </summary>
internal sealed class ScreenStocksExecutor :
    ReflectingExecutor<ScreenStocksExecutor>("ScreenStocks"),
    IMessageHandler<string, ScreeningResult>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScreenStocksExecutor> _logger;

    public ScreenStocksExecutor(
        IServiceProvider serviceProvider,
        ILogger<ScreenStocksExecutor> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<ScreeningResult> HandleAsync(
        string criteriaJson,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤2/3] 执行股票筛选");

        try
        {
            // 反序列化筛选条件
            var criteria = JsonSerializer.Deserialize<StockCriteria>(
                criteriaJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (criteria == null)
            {
                throw new InvalidOperationException("筛选条件 JSON 解析失败");
            }

            _logger.LogInformation("[步骤2/3] 筛选条件: 市场={Market}, 行业={Industry}, 条件数={Count}",
                criteria.Market, criteria.Industry, criteria.Criteria.Count);

            // 创建 StockScreenerPlugin 并执行筛选（100% 确定性，无 AI 参与）
            var playwrightService = _serviceProvider.GetRequiredService<PlaywrightService>();
            var pluginLogger = _serviceProvider.GetRequiredService<ILogger<StockScreenerPlugin>>();
            var plugin = new StockScreenerPlugin(playwrightService, pluginLogger);

            List<ScreenerStockInfo> stocks = await plugin.ScreenStocksAsync(criteria);

            _logger.LogInformation("[步骤2/3] 筛选完成，获得 {Count} 只股票", stocks.Count);

            return new ScreeningResult
            {
                CriteriaJson = criteriaJson,
                ScreenedStocks = stocks,
                Criteria = criteria
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤2/3] 股票筛选失败");
            throw new InvalidOperationException($"股票筛选失败: {ex.Message}", ex);
        }
    }
}

