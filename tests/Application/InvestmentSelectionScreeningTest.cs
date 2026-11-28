using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Agents.InvestmentSelection.Strategies;
using MarketAssistant.Applications.AssetScreener;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Applications.InvestmentSelection.Models;
using MarketAssistant.Infrastructure.Core;

namespace TestMarketAssistant.Application;

/// <summary>
/// AI 选股/选币筛选链路的纯逻辑单测：报告期、查询参数、条件文案、数据格式化与结果校验（无网络、无 LLM）。
/// </summary>
[TestClass]
public sealed class InvestmentSelectionScreeningTest
{
    private const string WideRange = "-99999999999999_99999999999999";

    #region 雪球报告期与查询参数

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("2026-11-28", "20251231", "20260930", DisplayName = "11–12 月：三季报")]
    [DataRow("2026-12-31", "20251231", "20260930", DisplayName = "年末仍是三季报")]
    [DataRow("2026-10-15", "20251231", "20260630", DisplayName = "9–10 月：半年报")]
    [DataRow("2026-09-01", "20251231", "20260630", DisplayName = "9 月 1 日起半年报已披露完")]
    [DataRow("2026-08-31", "20251231", "20260331", DisplayName = "5–8 月：一季报")]
    [DataRow("2026-05-01", "20251231", "20260331", DisplayName = "5 月 1 日起上年年报已披露完")]
    [DataRow("2026-04-30", "20241231", "20250930", DisplayName = "4 月 30 日披露截止前：仍取前年年报与上年三季报")]
    [DataRow("2026-01-15", "20241231", "20250930", DisplayName = "1–4 月：前年年报与上年三季报")]
    public void GetReportingPeriod_ShouldFollowDisclosureDeadlines(
        string today, string expectedCumulative, string expectedGrowth)
    {
        var date = DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture);

        Assert.AreEqual(expectedCumulative, StockScreenerService.GetReportingPeriod(date, isGrowthIndicator: false));
        Assert.AreEqual(expectedGrowth, StockScreenerService.GetReportingPeriod(date, isGrowthIndicator: true));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BuildQueryParams_WithoutConditions_ShouldSortByMarketCapAndFillAnalysisColumns()
    {
        var criteria = new StockCriteria { Limit = IScreeningCriteria.CandidatePoolSize };

        var query = StockScreenerService.BuildQueryParams(criteria, new DateOnly(2026, 11, 28));

        Assert.Contains("order_by=mc&order=desc", query);
        Assert.Contains("size=30", query);
        // 条件没涉及的关键指标用宽区间补列：累计类取最近年报，增速类取最近已披露完的季报，行情类原样
        foreach (var column in new[]
        {
            "mc", "pettm", "pb", "roediluted.20251231", "npay.20260930",
            "oiy.20260930", "amount", "tr", "pct20", "pct60"
        })
        {
            Assert.Contains($"&{column}={WideRange}", query);
        }

        Assert.DoesNotContain("dy_l", query);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BuildQueryParams_WithAmountCondition_ShouldSortByAmount()
    {
        var criteria = new StockCriteria
        {
            Limit = IScreeningCriteria.CandidatePoolSize,
            Criteria =
            [
                new() { Code = "tr", DisplayName = "换手率", MinValue = 2m },
                new() { Code = "amount", DisplayName = "成交额", MinValue = 500_000_000m }
            ]
        };

        var query = StockScreenerService.BuildQueryParams(criteria, new DateOnly(2026, 11, 28));

        Assert.Contains("order_by=amount&order=desc", query);
        Assert.Contains("&amount=500000000_", query);
        // 条件列已存在时不再补宽区间
        Assert.DoesNotContain($"&amount={WideRange}", query);
        Assert.DoesNotContain($"&tr={WideRange}", query);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BuildQueryParams_ShouldNotSortByFirstCondition()
    {
        var criteria = new StockCriteria
        {
            Limit = IScreeningCriteria.CandidatePoolSize,
            Criteria =
            [
                new() { Code = "pettm", DisplayName = "市盈率TTM", MaxValue = 20m },
                new() { Code = "pb", DisplayName = "市净率", MaxValue = 3m }
            ]
        };

        var query = StockScreenerService.BuildQueryParams(criteria, new DateOnly(2026, 11, 28));

        // 条件只决定入池资格；按第一个条件降序会让候选集中贴着条件上沿（如 PE 全在 19.8–20）
        Assert.Contains("order_by=mc&order=desc", query);
        Assert.DoesNotContain("order_by=pettm", query);
        Assert.Contains("&pettm=_20", query);
        Assert.Contains("&pb=_3", query);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BuildQueryParams_ShouldUseReportingPeriodByIndicatorKind()
    {
        var criteria = new StockCriteria
        {
            Limit = IScreeningCriteria.CandidatePoolSize,
            Criteria =
            [
                new() { Code = "roediluted", DisplayName = "ROE", MinValue = 10m },
                new() { Code = "npay", DisplayName = "净利润同比增长", MinValue = 15m },
                new() { Code = "oiy", DisplayName = "营收同比增长", MinValue = 20m }
            ]
        };

        var query = StockScreenerService.BuildQueryParams(criteria, new DateOnly(2026, 11, 28));

        Assert.Contains("&roediluted.20251231=10_", query);
        Assert.Contains("&npay.20260930=15_", query);
        Assert.Contains("&oiy.20260930=20_", query);
        Assert.DoesNotContain("roediluted.20251231=" + WideRange, query);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BuildQueryParams_ShouldAppendMarketAndIndustryFilters()
    {
        var criteria = new StockCriteria
        {
            Limit = IScreeningCriteria.CandidatePoolSize,
            Market = AShareType.ShanghaiAShares,
            Industry = IndustryType.Semiconductor
        };

        var query = StockScreenerService.BuildQueryParams(criteria, new DateOnly(2026, 11, 28));

        Assert.Contains("exchange=sha", query);
        Assert.Contains("indcode=S2701", query);
    }

    #endregion

    #region 条件文案

    [TestMethod]
    [TestCategory("Unit")]
    public void CriteriaDescriber_Stock_ShouldListMarketIndustryAndRanges()
    {
        var criteria = new StockCriteria
        {
            Market = AShareType.ShanghaiAShares,
            Industry = IndustryType.Semiconductor,
            Criteria =
            [
                new() { Code = "mc", DisplayName = "总市值", MinValue = 10_000_000_000m },
                new() { Code = "pettm", DisplayName = "市盈率TTM", MaxValue = 40m },
                new() { Code = "pct20", DisplayName = "近20日涨跌幅", MinValue = -5m, MaxValue = 10m },
                new() { Code = "tr", MinValue = 2.5m }
            ]
        };

        var text = CriteriaDescriber.Describe(criteria);

        Assert.AreEqual("沪市A股 · 行业 半导体 · 总市值 ≥ 100亿 · 市盈率TTM ≤ 40 · 近20日涨跌幅 -5~10 · tr ≥ 2.5", text);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CriteriaDescriber_Stock_ShouldSkipDefaultMarketAndIndustry()
    {
        var criteria = new StockCriteria
        {
            Criteria = [new() { Code = "pb", DisplayName = "市净率", MaxValue = 1.5m }]
        };

        Assert.AreEqual("市净率 ≤ 1.5", CriteriaDescriber.Describe(criteria));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CriteriaDescriber_Crypto_ShouldUseReadableLabels()
    {
        var criteria = new CryptoCriteria
        {
            Criteria =
            [
                new() { Code = "market_cap_rank", MaxValue = 50m },
                new() { Code = "volume_24h", MinValue = 100_000_000m },
                new() { Code = "price_change_7d", MinValue = 0m, MaxValue = 30m }
            ]
        };

        var text = CriteriaDescriber.Describe(criteria);

        Assert.AreEqual("市值排名 ≤ 50 · 24h成交额(美元) ≥ 1亿 · 7日涨跌幅(%) 0~30", text);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CriteriaDescriber_WithoutConditions_ShouldReturnEmpty()
    {
        Assert.AreEqual(string.Empty, CriteriaDescriber.Describe(new StockCriteria()));
        Assert.AreEqual(string.Empty, CriteriaDescriber.Describe(new CryptoCriteria()));
        Assert.AreEqual(string.Empty, CriteriaDescriber.Describe(null));
    }

    #endregion

    #region 数据格式化

    [TestMethod]
    [TestCategory("Unit")]
    public void CryptoDataFormatter_ShouldKeepLowPricesAndSymbolCode()
    {
        var assets = new List<ScreenerAssetInfo>
        {
            new ScreenerCryptoInfo
            {
                Name = "Bitcoin", Symbol = "BTC", Current = 65000m, Mc = 1_200_000_000_000m,
                Amount = 26_000_000_000m, Volume = 26_000_000_000m, MarketCapRank = 1, Pct = 1.2m
            },
            new ScreenerCryptoInfo
            {
                Name = "Pepe", Symbol = "PEPE", Current = 0.00000345m, Mc = 1_450_000_000m,
                Amount = 210_000_000m, Volume = 210_000_000m, MarketCapRank = 45, Pct = -3.2m
            }
        };

        var json = new CryptoDataFormatter().FormatAssetsForAnalysis(assets);

        // 低价币不能被舍入成 0 而丢失价格；代码使用 BTC 而非交易对
        Assert.Contains("\"当前价格_USDT\": 0.00000345", json);
        Assert.Contains("\"代码\": \"BTC\"", json);
        Assert.Contains("\"24h成交额_亿美元\"", json);
        // 加密币的 Volume 与 Amount 同为 24h 美元成交额；旧版再按“万”输出一次并标成“交易量”，既重复又误导
        Assert.DoesNotContain("24h交易量_万", json);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CryptoDataFormatter_Instructions_ShouldDefineSymbolContractAndForbidFabrication()
    {
        var instructions = new CryptoDataFormatter().GetAnalysisInstructions(isNewsAnalysis: false);

        Assert.Contains("原样使用候选数据中的“代码”字段", instructions);
        Assert.Contains("不得编造", instructions);
        // 不再要求模型输出数据里并不存在的维度
        Assert.DoesNotContain("BTC/USDT", instructions);
        Assert.DoesNotContain("新闻关联度", instructions);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CryptoDataFormatter_Instructions_ShouldMentionNewsOnlyForNewsAnalysis()
    {
        var instructions = new CryptoDataFormatter().GetAnalysisInstructions(isNewsAnalysis: true);

        Assert.Contains("新闻关联度", instructions);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void StockDataFormatter_Instructions_ShouldNotRequireMissingDimensions()
    {
        var instructions = new StockDataFormatter().GetAnalysisInstructions(isNewsAnalysis: false);

        Assert.Contains("不得编造", instructions);
        Assert.DoesNotContain("ROE、利润增长率、现金流", instructions);
        Assert.DoesNotContain("新闻关联度", instructions);
    }

    #endregion

    #region 第 3 步结果校验

    [TestMethod]
    [TestCategory("Unit")]
    public void ValidateResult_ShouldAcceptSymbolFromCandidatesIgnoringCase()
    {
        var result = CreateResult("btc");

        var errors = AnalyzeAssetsExecutor.ValidateResult(result, CreateScreeningResult(), CreateRequest());

        Assert.IsEmpty(errors, string.Join("; ", errors));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ValidateResult_ShouldRejectTradingPairSymbol()
    {
        var result = CreateResult("BTC/USDT");

        var errors = AnalyzeAssetsExecutor.ValidateResult(result, CreateScreeningResult(), CreateRequest());

        Assert.HasCount(1, errors);
        Assert.Contains("不在筛选结果中", errors[0]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ValidateResult_ShouldRejectMoreRecommendationsThanRequested()
    {
        var result = CreateResult("BTC");
        result.Recommendations.Add(CreateRecommendation("ETH"));
        var request = CreateRequest() with { MaxRecommendations = 1 };

        var errors = AnalyzeAssetsExecutor.ValidateResult(result, CreateScreeningResult(), request);

        Assert.IsTrue(errors.Any(error => error.Contains("超过请求上限")), string.Join("; ", errors));
    }

    private static InvestmentSelectionWorkflowRequest CreateRequest() => new()
    {
        MarketType = MarketType.Crypto,
        Content = "找主流币",
        MaxRecommendations = 5
    };

    private static AssetScreeningResult CreateScreeningResult() => new()
    {
        ScreenedAssets =
        [
            new ScreenerCryptoInfo { Name = "Bitcoin", Symbol = "BTC", Current = 65000m },
            new ScreenerCryptoInfo { Name = "Ethereum", Symbol = "ETH", Current = 3200m }
        ],
        OriginalRequest = CreateRequest()
    };

    private static InvestmentRecommendation CreateRecommendation(string symbol) => new()
    {
        Symbol = symbol,
        Name = symbol,
        RecommendationScore = 80,
        Reason = "24h 成交额 260 亿美元、市值排名第 1，30 天涨幅 8.5%，流动性充足；但虚拟币波动剧烈，仓位应严格控制。",
        RiskLevel = RiskLevel.Medium
    };

    private static InvestmentSelectionResult CreateResult(string symbol) => new()
    {
        SelectionType = SelectionType.UserRequest,
        AnalysisSummary = new string('析', 60),
        MarketEnvironmentAnalysis = new string('环', 60),
        Recommendations = [CreateRecommendation(symbol)],
        RiskWarnings = ["风险一", "风险二", "风险三"],
        InvestmentAdvice = new string('建', 60),
        ConfidenceScore = 70
    };

    #endregion
}
