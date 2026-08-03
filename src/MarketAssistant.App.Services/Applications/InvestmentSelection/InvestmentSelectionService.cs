using MarketAssistant.Agents.InvestmentSelection;
using MarketAssistant.Applications.InvestmentSelection.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.InvestmentSelection;

/// <summary>
/// AI投资选择服务 - 业务逻辑层，负责对外API和业务规则
/// 使用 Agent Framework Workflows 实现确定性投资选择流程
/// 支持股票和虚拟币市场
/// </summary>
public class InvestmentSelectionService : IDisposable
{
    private readonly InvestmentSelectionWorkflow _selectionWorkflow;
    private readonly ILogger<InvestmentSelectionService> _logger;
    private bool _disposed = false;

    public InvestmentSelectionService(
        InvestmentSelectionWorkflow selectionWorkflow,
        ILogger<InvestmentSelectionService> logger)
    {
        _selectionWorkflow = selectionWorkflow ?? throw new ArgumentNullException(nameof(selectionWorkflow));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region 业务API接口

    /// <summary>
    /// 功能1: 根据用户需求推荐投资标的
    /// </summary>
    public async Task<InvestmentSelectionResult> RecommendByUserRequirementAsync(
        InvestmentRecommendationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserRequirements))
        {
            throw new ArgumentException("用户需求不能为空", nameof(request));
        }

        try
        {
            _logger.LogInformation("开始基于用户需求的AI投资选择，市场: {Market}，需求: {Requirements}",
                request.MarketType, request.UserRequirements);

            var validatedRequest = ValidateAndNormalizeUserRequest(request);

            var result = await _selectionWorkflow.AnalyzeUserRequirementAsync(validatedRequest, cancellationToken);

            var optimizedResult = OptimizeUserBasedResult(result, validatedRequest);

            _logger.LogInformation("用户需求投资选择完成，市场: {Market}，推荐数量: {Count}，置信度: {Confidence:F1}%",
                request.MarketType, optimizedResult.Recommendations.Count, optimizedResult.ConfidenceScore);

            return optimizedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "基于用户需求的投资选择过程中发生错误");
            throw;
        }
    }

    /// <summary>
    /// 功能2: 根据新闻推荐投资标的
    /// </summary>
    public async Task<InvestmentSelectionResult> RecommendByNewsAsync(
        NewsBasedInvestmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            request = new NewsBasedInvestmentRequest();
        }

        try
        {
            _logger.LogInformation("开始基于热点新闻的AI投资选择，市场: {Market}，推荐数: {Max}",
                request.MarketType, request.MaxRecommendations);

            var validatedRequest = ValidateAndNormalizeNewsRequest(request);

            var result = await _selectionWorkflow.AnalyzeNewsHotspotAsync(validatedRequest, cancellationToken);

            var optimizedResult = OptimizeNewsBasedResult(result, validatedRequest);

            _logger.LogInformation("热点新闻投资选择完成，市场: {Market}，推荐数量: {Count}，置信度: {Confidence:F1}%",
                request.MarketType, optimizedResult.Recommendations.Count, optimizedResult.ConfidenceScore);

            return optimizedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "基于热点新闻的投资选择过程中发生错误");
            throw;
        }
    }

    /// <summary>
    /// 功能4: 快速选择（预设策略）
    /// </summary>
    public async Task<InvestmentSelectionResult> QuickSelectAsync(
        QuickSelectionStrategy strategy,
        MarketType marketType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始执行快速选择，策略: {Strategy}, 市场: {Market}", strategy, marketType);

            var request = ConvertStrategyToUserRequest(strategy, marketType);

            var result = await RecommendByUserRequirementAsync(request, cancellationToken);

            _logger.LogInformation("快速选择完成，策略: {Strategy}，结果数量: {Count}",
                strategy, result.Recommendations.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行快速选择时发生错误，策略: {Strategy}", strategy);
            throw;
        }
    }

    /// <summary>
    /// 功能5: 获取快速选择策略列表（根据市场类型返回不同策略）
    /// </summary>
    public List<QuickSelectionStrategyInfo> GetQuickSelectionStrategies(MarketType marketType)
    {
        return marketType switch
        {
            MarketType.AShare => GetStockStrategies(),
            MarketType.Crypto => GetCryptoStrategies(),
            _ => throw new NotSupportedException($"不支持的市场类型: {marketType}")
        };
    }

    /// <summary>
    /// 获取股票市场预设策略
    /// </summary>
    private List<QuickSelectionStrategyInfo> GetStockStrategies()
    {
        return new List<QuickSelectionStrategyInfo>
        {
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.ValueInvestment,
                Name = "价值投资",
                Icon = "💎",
                Description = "筛选PE低、PB低、ROE高的优质价值标的",
                Scenario = "适合稳健型投资者，追求长期价值投资",
                RiskLevel = "低风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.GrowthInvestment,
                Name = "成长投资",
                Icon = "🚀",
                Description = "筛选营收和利润高增长的成长型标的",
                Scenario = "适合积极型投资者，追求高成长收益",
                RiskLevel = "中高风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.ActiveTrading,
                Name = "活跃标的",
                Icon = "🔥",
                Description = "筛选换手率高、成交活跃的热门标的",
                Scenario = "适合短线交易者，追求市场热点",
                RiskLevel = "高风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.LargeCap,
                Name = "大盘标的",
                Icon = "🏢",
                Description = "筛选市值大、业绩稳定的蓝筹标的",
                Scenario = "适合保守型投资者，追求稳定收益",
                RiskLevel = "低风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.SmallCap,
                Name = "小盘标的",
                Icon = "🌱",
                Description = "筛选市值较小、具有成长潜力的标的",
                Scenario = "适合风险偏好较高的投资者",
                RiskLevel = "高风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.HighYield,
                Name = "高股息",
                Icon = "💰",
                Description = "筛选股息率高、分红稳定的高股息标的",
                Scenario = "适合追求稳定现金流的投资者",
                RiskLevel = "低风险"
            }
        };
    }

    /// <summary>
    /// 获取虚拟币市场预设策略
    /// </summary>
    private List<QuickSelectionStrategyInfo> GetCryptoStrategies()
    {
        return new List<QuickSelectionStrategyInfo>
        {
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.ValueInvestment,
                Name = "价值币种",
                Icon = "💎",
                Description = "筛选市值大、技术成熟、社区活跃的主流币种",
                Scenario = "适合稳健型投资者，追求长期持有价值币",
                RiskLevel = "低风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.GrowthInvestment,
                Name = "高成长币",
                Icon = "🚀",
                Description = "筛选7日/30日涨幅较高、交易量增长的高成长币种",
                Scenario = "适合积极型投资者，追求高收益潜力币",
                RiskLevel = "中高风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.ActiveTrading,
                Name = "热门币种",
                Icon = "🔥",
                Description = "筛选24h交易量大、价格波动活跃的热门币种",
                Scenario = "适合短线交易者，追捧市场热点币",
                RiskLevel = "高风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.LargeCap,
                Name = "主流大币",
                Icon = "🏢",
                Description = "筛选市值排名前50、流动性充足的蓝筹主流币",
                Scenario = "适合保守型投资者，追求稳定的主流币",
                RiskLevel = "低风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.SmallCap,
                Name = "潜力小币",
                Icon = "🌱",
                Description = "筛选市值排名100-500、具有创新性的潜力币种",
                Scenario = "适合风险偏好较高的投资者，寻找黑马币",
                RiskLevel = "高风险"
            },
            new QuickSelectionStrategyInfo
            {
                Strategy = QuickSelectionStrategy.HighYield,
                Name = "高波动币",
                Icon = "⚡",
                Description = "筛选24h价格波动大于±5%的高波动币种",
                Scenario = "适合激进型交易者，追求短期高波动收益",
                RiskLevel = "高风险"
            }
        };
    }

    #endregion

    #region 业务逻辑处理

    private InvestmentRecommendationRequest ValidateAndNormalizeUserRequest(InvestmentRecommendationRequest request)
    {
        var normalized = new InvestmentRecommendationRequest
        {
            MarketType = request.MarketType,
            UserRequirements = request.UserRequirements?.Trim() ?? "",
            InvestmentAmount = request.InvestmentAmount,
            RiskPreference = NormalizeRiskPreference(request.RiskPreference),
            InvestmentHorizon = request.InvestmentHorizon,
            PreferredSectors = request.PreferredSectors,
            ExcludedSectors = request.ExcludedSectors,
            MaxRecommendations = Math.Clamp(request.MaxRecommendations, 1, 10)
        };

        if (string.IsNullOrWhiteSpace(normalized.RiskPreference))
        {
            normalized.RiskPreference = "moderate";
        }

        return normalized;
    }

    private NewsBasedInvestmentRequest ValidateAndNormalizeNewsRequest(NewsBasedInvestmentRequest request)
    {
        var normalized = new NewsBasedInvestmentRequest
        {
            MarketType = request.MarketType,
            NewsContent = request.NewsContent?.Trim() ?? "",
            MaxRecommendations = Math.Max(1, Math.Min(10, request.MaxRecommendations))
        };

        return normalized;
    }

    private InvestmentSelectionResult OptimizeUserBasedResult(InvestmentSelectionResult result, InvestmentRecommendationRequest request)
    {
        if (request.RiskPreference == "conservative")
        {
            result.Recommendations = result.Recommendations
                .Where(r => r.RiskLevel != RiskLevel.High)
                .ToList();
        }
        else if (request.RiskPreference == "aggressive")
        {
            result.Recommendations = result.Recommendations
                .OrderByDescending(r => r.ExpectedReturn ?? 0)
                .ToList();
        }

        return result;
    }

    private InvestmentSelectionResult OptimizeNewsBasedResult(InvestmentSelectionResult result, NewsBasedInvestmentRequest request)
    {
        if (result.Recommendations.Count > request.MaxRecommendations)
        {
            result.Recommendations = result.Recommendations
                .Take(request.MaxRecommendations)
                .ToList();
        }

        foreach (var recommendation in result.Recommendations)
        {
            recommendation.Reason = $"[新闻热点] {recommendation.Reason}";
        }

        return result;
    }

    private InvestmentRecommendationRequest ConvertStrategyToUserRequest(QuickSelectionStrategy strategy, MarketType marketType)
    {
        var (requirements, riskPreference) = (marketType, strategy) switch
        {
            // 股票市场策略
            (MarketType.AShare, QuickSelectionStrategy.ValueInvestment) =>
                ("请筛选价值型标的：PE低于20，PB低于3，ROE大于10%的优质价值标的", "conservative"),
            (MarketType.AShare, QuickSelectionStrategy.GrowthInvestment) =>
                ("请筛选成长型标的：营收增长率大于20%，净利润增长率大于15%的高成长标的", "aggressive"),
            (MarketType.AShare, QuickSelectionStrategy.ActiveTrading) =>
                ("请筛选活跃标的：换手率大于2%，成交额大，量比大于1.5的活跃标的", "moderate"),
            (MarketType.AShare, QuickSelectionStrategy.LargeCap) =>
                ("请筛选大盘标的：市值大，流动性好，业绩稳定的大盘蓝筹标的", "conservative"),
            (MarketType.AShare, QuickSelectionStrategy.SmallCap) =>
                ("请筛选小盘标的：市值较小，具有成长潜力的优质小盘标的", "aggressive"),
            (MarketType.AShare, QuickSelectionStrategy.HighYield) =>
                ("请筛选高收益标的：股息率大于3%，分红稳定的高股息标的", "conservative"),

            // 虚拟币市场策略
            (MarketType.Crypto, QuickSelectionStrategy.ValueInvestment) =>
                ("请筛选主流价值币种：市值排名前30，存在时间超过3年，社区活跃度高，技术成熟的价值币", "conservative"),
            (MarketType.Crypto, QuickSelectionStrategy.GrowthInvestment) =>
                ("请筛选高成长币种：7日涨幅大于10%，30日涨幅大于20%，24h交易量增长大于50%的高成长币", "aggressive"),
            (MarketType.Crypto, QuickSelectionStrategy.ActiveTrading) =>
                ("请筛选热门活跃币种：24h交易量大于1亿美元，价格波动大于5%，社交媒体讨论度高的热门币", "moderate"),
            (MarketType.Crypto, QuickSelectionStrategy.LargeCap) =>
                ("请筛选主流大币：市值排名前50，市值大于100亿美元，流动性充足，风险相对较低的主流币", "conservative"),
            (MarketType.Crypto, QuickSelectionStrategy.SmallCap) =>
                ("请筛选潜力小币：市值排名100-500，上市时间1-3年，技术创新性强，具有成长潜力的小市值币", "aggressive"),
            (MarketType.Crypto, QuickSelectionStrategy.HighYield) =>
                ("请筛选高波动币种：24小时价格变化绝对值大于5%，波动活跃，适合短线交易的币种", "aggressive"),

            _ => throw new ArgumentException($"不支持的策略或市场类型: {strategy}, {marketType}")
        };

        return new InvestmentRecommendationRequest
        {
            MarketType = marketType,
            UserRequirements = requirements,
            RiskPreference = riskPreference
        };
    }

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
            _selectionWorkflow?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}

