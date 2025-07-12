using MarketAssistant.Agents;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MarketAssistant.Infrastructure;

/// <summary>
/// AI选股服务 - 业务逻辑层，负责对外API和业务规则
/// </summary>
public class StockSelectionService : IDisposable
{
    private readonly StockSelectionManager _selectionManager;
    private readonly ILogger<StockSelectionService> _logger;
    private bool _disposed = false;

    public StockSelectionService(
        StockSelectionManager selectionManager,
        ILogger<StockSelectionService> logger)
    {
        _selectionManager = selectionManager ?? throw new ArgumentNullException(nameof(selectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region 业务API接口

    /// <summary>
    /// 功能1: 根据用户需求推荐股票
    /// </summary>
    public async Task<StockSelectionResult> RecommendStocksByUserRequirementAsync(
        StockRecommendationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserRequirements))
        {
            throw new ArgumentException("用户需求不能为空", nameof(request));
        }

        try
        {
            _logger.LogInformation("开始基于用户需求的AI选股，需求: {Requirements}", request.UserRequirements);

            // 业务逻辑：验证和预处理请求
            var validatedRequest = ValidateAndNormalizeUserRequest(request);

            // 调用AI Manager进行分析
            var result = await _selectionManager.AnalyzeUserRequirementAsync(validatedRequest, cancellationToken);

            // 业务逻辑：后处理和结果优化
            var optimizedResult = OptimizeUserBasedResult(result, validatedRequest);

            _logger.LogInformation("用户需求选股完成，推荐股票数量: {Count}, 置信度: {Confidence:F1}%",
                optimizedResult.Recommendations.Count, optimizedResult.ConfidenceScore);

            return optimizedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "基于用户需求的选股过程中发生错误");
            throw;
        }
    }

    /// <summary>
    /// 功能2: 根据热点新闻推荐股票
    /// </summary>
    public async Task<StockSelectionResult> RecommendStocksByNewsHotspotAsync(
        NewsBasedSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            request = new NewsBasedSelectionRequest(); // 使用默认设置
        }

        try
        {
            _logger.LogInformation("开始基于热点新闻的AI选股，推荐股票数: {Max}", request.MaxRecommendations);

            // 业务逻辑：验证和预处理请求
            var validatedRequest = ValidateAndNormalizeNewsRequest(request);

            // 调用AI Manager进行分析
            var result = await _selectionManager.AnalyzeNewsHotspotAsync(validatedRequest, cancellationToken);

            // 业务逻辑：后处理和结果优化
            var optimizedResult = OptimizeNewsBasedResult(result, validatedRequest);

            _logger.LogInformation("热点新闻选股完成，推荐股票数量: {Count}, 置信度: {Confidence:F1}%",
                optimizedResult.Recommendations.Count, optimizedResult.ConfidenceScore);

            return optimizedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "基于热点新闻的选股过程中发生错误");
            throw;
        }
    }

    /// <summary>
    /// 功能4: 快速选股（预设策略）
    /// </summary>
    public async Task<string> QuickSelectAsync(
        QuickSelectionStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始执行快速选股，策略: {Strategy}", strategy);

            // 业务逻辑：将策略转换为用户需求
            var request = ConvertStrategyToUserRequest(strategy);

            // 调用用户需求分析
            var result = await RecommendStocksByUserRequirementAsync(request, cancellationToken);

            // 业务逻辑：格式化输出
            var formattedResult = FormatQuickSelectionResult(result, strategy);

            _logger.LogInformation("快速选股完成，策略: {Strategy}，结果长度: {Length}",
                strategy, formattedResult.Length);

            return formattedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行快速选股时发生错误，策略: {Strategy}", strategy);
            throw;
        }
    }

    /// <summary>
    /// 功能5: 获取快速选股策略列表
    /// </summary>
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
                Name = "高股息筛选",
                Description = "筛选股息率高、分红稳定的股票",
                Scenario = "适合追求稳定现金流的投资者",
                RiskLevel = "低风险"
            }
        };
    }

    #endregion

    #region 业务逻辑处理

    /// <summary>
    /// 验证和规范化用户请求
    /// </summary>
    private StockRecommendationRequest ValidateAndNormalizeUserRequest(StockRecommendationRequest request)
    {
        var normalized = new StockRecommendationRequest
        {
            UserRequirements = request.UserRequirements?.Trim() ?? "",
            InvestmentAmount = request.InvestmentAmount,
            RiskPreference = NormalizeRiskPreference(request.RiskPreference),
            InvestmentHorizon = request.InvestmentHorizon
        };

        // 业务规则：设置默认值
        if (string.IsNullOrWhiteSpace(normalized.RiskPreference))
        {
            normalized.RiskPreference = "moderate";
        }

        return normalized;
    }

    /// <summary>
    /// 验证和规范化新闻请求
    /// </summary>
    private NewsBasedSelectionRequest ValidateAndNormalizeNewsRequest(NewsBasedSelectionRequest request)
    {
        var normalized = new NewsBasedSelectionRequest
        {
            NewsContent = request.NewsContent?.Trim() ?? "",
            MaxRecommendations = Math.Max(1, Math.Min(10, request.MaxRecommendations)) // 限制在1-10只之间
        };

        return normalized;
    }

    /// <summary>
    /// 优化用户需求分析结果
    /// </summary>
    private StockSelectionResult OptimizeUserBasedResult(StockSelectionResult result, StockRecommendationRequest request)
    {
        // 业务逻辑：根据用户风险偏好调整推荐
        if (request.RiskPreference == "conservative")
        {
            // 保守型投资者，过滤掉高风险股票
            result.Recommendations = result.Recommendations
                .Where(r => r.RiskLevel != "高风险")
                .ToList();
        }
        else if (request.RiskPreference == "aggressive")
        {
            // 激进型投资者，优先推荐高收益股票
            result.Recommendations = result.Recommendations
                .OrderByDescending(r => r.ExpectedReturn ?? 0)
                .ToList();
        }

        // 业务逻辑：限制推荐数量
        if (result.Recommendations.Count > 10)
        {
            result.Recommendations = result.Recommendations.Take(10).ToList();
        }

        return result;
    }

    /// <summary>
    /// 优化新闻分析结果
    /// </summary>
    private StockSelectionResult OptimizeNewsBasedResult(StockSelectionResult result, NewsBasedSelectionRequest request)
    {
        // 业务逻辑：根据请求的最大推荐数量限制结果
        if (result.Recommendations.Count > request.MaxRecommendations)
        {
            result.Recommendations = result.Recommendations
                .Take(request.MaxRecommendations)
                .ToList();
        }

        // 业务逻辑：添加新闻相关性标识
        foreach (var recommendation in result.Recommendations)
        {
            recommendation.Reason = $"[新闻热点] {recommendation.Reason}";
        }

        return result;
    }

    /// <summary>
    /// 将策略转换为用户请求
    /// </summary>
    private StockRecommendationRequest ConvertStrategyToUserRequest(QuickSelectionStrategy strategy)
    {
        var (requirements, riskPreference) = strategy switch
        {
            QuickSelectionStrategy.ValueStocks =>
                ("请筛选价值股：PE低于20，PB低于3，ROE大于10%，负债率低于60%的优质价值股", "conservative"),
            QuickSelectionStrategy.GrowthStocks =>
                ("请筛选成长股：营收增长率大于20%，净利润增长率大于15%，PEG小于1.5的高成长股", "aggressive"),
            QuickSelectionStrategy.ActiveStocks =>
                ("请筛选活跃股：换手率大于2%，成交额大于5亿，量比大于1.5的活跃股票", "moderate"),
            QuickSelectionStrategy.LargeCap =>
                ("请筛选大盘股：市值大于500亿，流动性好，业绩稳定的大盘蓝筹股", "conservative"),
            QuickSelectionStrategy.SmallCap =>
                ("请筛选小盘股：市值在50-200亿之间，具有成长潜力的优质小盘股", "aggressive"),
            QuickSelectionStrategy.Dividend =>
                ("请筛选高股息股：股息率大于3%，连续分红3年以上，现金流稳定的高股息股票", "conservative"),
            _ => throw new ArgumentException($"不支持的选股策略: {strategy}")
        };

        return new StockRecommendationRequest
        {
            UserRequirements = requirements,
            RiskPreference = riskPreference
        };
    }

    /// <summary>
    /// 格式化快速选股结果
    /// </summary>
    private string FormatQuickSelectionResult(StockSelectionResult result, QuickSelectionStrategy strategy)
    {
        var output = new StringBuilder();
        output.AppendLine($"=== {GetStrategyName(strategy)} 分析结果 ===\n");

        output.AppendLine($"📊 **分析摘要**");
        output.AppendLine($"   推荐股票数量: {result.Recommendations.Count}只");
        output.AppendLine($"   分析置信度: {result.ConfidenceScore:F1}%\n");

        if (result.Recommendations.Any())
        {
            output.AppendLine("📈 **推荐股票列表**");
            for (int i = 0; i < result.Recommendations.Count; i++)
            {
                var stock = result.Recommendations[i];
                output.AppendLine($"   {i + 1}. {stock.Name} ({stock.Symbol})");
                output.AppendLine($"      推荐理由: {stock.Reason}");
                output.AppendLine($"      风险等级: {stock.RiskLevel}");
                if (stock.ExpectedReturn.HasValue)
                {
                    output.AppendLine($"      预期收益: {stock.ExpectedReturn:F1}%");
                }
                output.AppendLine();
            }
        }

        output.AppendLine("⚠️ **风险提示**");
        output.AppendLine("   以上分析仅供参考，不构成投资建议。");
        output.AppendLine("   投资有风险，请根据个人风险承受能力谨慎决策。");

        return output.ToString();
    }

    /// <summary>
    /// 规范化风险偏好
    /// </summary>
    private string NormalizeRiskPreference(string riskPreference)
    {
        return riskPreference?.ToLower() switch
        {
            "conservative" or "保守" or "低风险" => "conservative",
            "aggressive" or "激进" or "高风险" => "aggressive",
            "moderate" or "稳健" or "中等风险" or "中风险" => "moderate",
            _ => "moderate"
        };
    }

    /// <summary>
    /// 获取策略名称
    /// </summary>
    private string GetStrategyName(QuickSelectionStrategy strategy)
    {
        return strategy switch
        {
            QuickSelectionStrategy.ValueStocks => "价值股筛选",
            QuickSelectionStrategy.GrowthStocks => "成长股筛选",
            QuickSelectionStrategy.ActiveStocks => "活跃股筛选",
            QuickSelectionStrategy.LargeCap => "大盘股筛选",
            QuickSelectionStrategy.SmallCap => "小盘股筛选",
            QuickSelectionStrategy.Dividend => "高股息筛选",
            _ => "未知策略"
        };
    }

    #endregion

    #region 资源管理

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _selectionManager?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}