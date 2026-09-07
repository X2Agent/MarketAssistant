using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure.Extensions;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 分析师结果卡片中的单条评分条
/// </summary>
public sealed class AnalystScoreBarItem
{
    public AnalystScoreBarItem(string label, double value)
    {
        Label = label;
        Value = value;
        NormalizedValue = Math.Clamp(value / 10.0 * 100.0, 0, 100);
        ValueText = value.ToString("0.#");
    }

    public string Label { get; }

    /// <summary>原始分值（1-10）</summary>
    public double Value { get; }

    /// <summary>归一化到 0-100 的进度条值</summary>
    public double NormalizedValue { get; }

    public string ValueText { get; }
}

/// <summary>
/// 分析师结果卡片中的单条"标签: 数值"关键数据
/// </summary>
public sealed class AnalystKeyValueItem
{
    public AnalystKeyValueItem(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }
}

/// <summary>
/// 分析师结构化结果卡片视图模型：将分析师产出的 JSON 产物解析为
/// 评级徽标 + 评分条 + 核心观点 + 风险提示。
/// </summary>
public sealed class AnalystCardViewModel
{
    private AnalystCardViewModel(
        string? ratingText,
        AnalystRatingTone ratingTone,
        IReadOnlyList<AnalystScoreBarItem> scoreBars,
        IReadOnlyList<string> keyPoints,
        IReadOnlyList<AnalystKeyValueItem> keyValues,
        IReadOnlyList<string> risks)
    {
        RatingText = ratingText;
        RatingTone = ratingTone;
        ScoreBars = scoreBars;
        KeyPoints = keyPoints;
        KeyValues = keyValues;
        Risks = risks;
    }

    /// <summary>评级徽标文本（如"买入"/"乐观"），无评级时为 null</summary>
    public string? RatingText { get; }

    public AnalystRatingTone RatingTone { get; }

    public bool RatingIsPositive => RatingTone == AnalystRatingTone.Positive;

    public bool RatingIsNeutral => RatingTone == AnalystRatingTone.Neutral;

    public bool RatingIsNegative => RatingTone == AnalystRatingTone.Negative;

    public bool HasRating => !string.IsNullOrEmpty(RatingText);

    public IReadOnlyList<AnalystScoreBarItem> ScoreBars { get; }

    public bool HasScoreBars => ScoreBars.Count > 0;

    public IReadOnlyList<string> KeyPoints { get; }

    public bool HasKeyPoints => KeyPoints.Count > 0;

    public IReadOnlyList<AnalystKeyValueItem> KeyValues { get; }

    public bool HasKeyValues => KeyValues.Count > 0;

    public IReadOnlyList<string> Risks { get; }

    public bool HasRisks => Risks.Count > 0;

    /// <summary>
    /// 按消息作者（ASCII Agent Name）尝试把内容解析为结构化卡片。
    /// 解析失败（非 JSON、字段不匹配等）返回 false，由调用方回退为富文本展示。
    /// </summary>
    public static bool TryCreate(string? authorName, string content, out AnalystCardViewModel? card)
    {
        card = null;
        if (string.IsNullOrWhiteSpace(content) || content.Length < 20)
            return false;

        var json = ExtractJsonCandidate(content);
        if (json is null)
            return false;

        card = authorName switch
        {
            "FundamentalAnalyst" => FromJson(json, FundamentalTypeInfo, BuildFrom),
            "FinancialAnalyst" => FromJson(json, FinancialTypeInfo, BuildFrom),
            "TechnicalAnalyst" => FromJson(json, TechnicalTypeInfo, BuildFrom),
            "MarketSentimentAnalyst" => FromJson(json, SentimentTypeInfo, BuildFrom),
            "NewsEventAnalyst" => FromJson(json, NewsTypeInfo, BuildFrom),
            "CryptoMetricsAnalyst" => FromJson(json, CryptoTypeInfo, BuildFrom),
            "CoordinatorAnalyst" or "Coordinator" => FromJson(json, CoordinatorTypeInfo, BuildFrom),
            _ => null
        };

        return card != null;
    }

    private static AnalystCardViewModel? FromJson<T>(
        string json,
        JsonTypeInfo<T> typeInfo,
        Func<T?, AnalystCardViewModel?> builder)
    {
        try
        {
            return builder(JsonSerializer.Deserialize(json, typeInfo));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // 卡片解析是展示层增强，解析失败（含反序列化配置异常）必须回退为富文本展示，
            // 任何异常逃逸都会中断调用方（ChatMessageAdapter 构造）的消息列表渲染流程
            System.Diagnostics.Debug.WriteLine($"分析师卡片解析失败，回退富文本展示: {ex.Message}");
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        // 必须显式指定 TypeInfoResolver：options 在首次使用时冻结，
        // 缺失时经 GetTypeInfo/Deserialize 会抛 InvalidOperationException
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() }
    };

    // 从 JsonOptions 自身缓存各结果类型的 JsonTypeInfo，供 Deserialize 非泛型分发；
    // 保证 TypeInfo 与 options 绑定一致，避免运行时解析器缺失异常
    private static readonly JsonTypeInfo<FundamentalAnalysisResult> FundamentalTypeInfo =
        (JsonTypeInfo<FundamentalAnalysisResult>)JsonOptions.GetTypeInfo(typeof(FundamentalAnalysisResult));
    private static readonly JsonTypeInfo<FinancialAnalysisResult> FinancialTypeInfo =
        (JsonTypeInfo<FinancialAnalysisResult>)JsonOptions.GetTypeInfo(typeof(FinancialAnalysisResult));
    private static readonly JsonTypeInfo<TechnicalAnalysisResult> TechnicalTypeInfo =
        (JsonTypeInfo<TechnicalAnalysisResult>)JsonOptions.GetTypeInfo(typeof(TechnicalAnalysisResult));
    private static readonly JsonTypeInfo<MarketSentimentAnalysisResult> SentimentTypeInfo =
        (JsonTypeInfo<MarketSentimentAnalysisResult>)JsonOptions.GetTypeInfo(typeof(MarketSentimentAnalysisResult));
    private static readonly JsonTypeInfo<NewsEventAnalysisResult> NewsTypeInfo =
        (JsonTypeInfo<NewsEventAnalysisResult>)JsonOptions.GetTypeInfo(typeof(NewsEventAnalysisResult));
    private static readonly JsonTypeInfo<CryptoMetricsAnalysisResult> CryptoTypeInfo =
        (JsonTypeInfo<CryptoMetricsAnalysisResult>)JsonOptions.GetTypeInfo(typeof(CryptoMetricsAnalysisResult));
    private static readonly JsonTypeInfo<CoordinatorResult> CoordinatorTypeInfo =
        (JsonTypeInfo<CoordinatorResult>)JsonOptions.GetTypeInfo(typeof(CoordinatorResult));

    /// <summary>
    /// 从可能包含散文前缀/后缀的文本中提取 JSON 对象候选片段
    /// </summary>
    private static string? ExtractJsonCandidate(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            return IsValidJsonObject(trimmed) ? trimmed : null;

        var first = trimmed.IndexOf('{');
        var last = trimmed.LastIndexOf('}');
        if (first < 0 || last <= first)
            return null;

        var candidate = trimmed[first..(last + 1)];
        return IsValidJsonObject(candidate) ? candidate : null;
    }

    private static bool IsValidJsonObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static AnalystCardViewModel? BuildFrom(FundamentalAnalysisResult? r)
    {
        if (r is null) return null;

        var (ratingText, tone) = MapInvestmentRating(r.GrowthValue.InvestmentRating);
        var bars = new[]
        {
            MaybeBar("行业成长性", r.Fundamentals.IndustryGrowthScore),
            MaybeBar("业务质量", r.Fundamentals.BusinessQualityScore),
            MaybeBar("竞争力强度", r.Competition.CompetenceStrengthScore),
            MaybeBar("增长持续性", r.GrowthValue.GrowthSustainabilityScore)
        }.Where(b => b != null).Select(b => b!).ToList();

        var points = CollectNonEmpty(
            $"行业：{r.Fundamentals.Industry}",
            r.Fundamentals.CoreBusiness,
            r.Fundamentals.ProfitabilityOverview,
            r.Competition.CoreCompetence,
            r.GrowthValue.GrowthDrivers);
        points.AddRange(r.GrowthValue.InvestmentHighlights);
        points.RemoveAll(string.IsNullOrWhiteSpace);

        return new AnalystCardViewModel(
            ratingText,
            tone,
            bars,
            points,
            CollectKeyValues(("公司名称", r.BasicInfo.Name), ("目标参考", r.GrowthValue.ValuationTarget)),
            CollectNonEmpty(r.GrowthValue.KeyRisk));
    }

    private static AnalystCardViewModel? BuildFrom(FinancialAnalysisResult? r)
    {
        if (r is null) return null;

        var (ratingText, tone) = MapFinancialStability(r.HealthAssessment.OverallStability);
        var bars = new[]
        {
            MaybeBar("偿债能力", r.HealthAssessment.SolvencyScore),
            MaybeBar("财务稳健", r.HealthAssessment.StabilityScore),
            MaybeBar("利润质量", r.ProfitQuality.ProfitQualityScore),
            MaybeBar("现金流质量", r.CashFlow.CashFlowQualityScore),
            MaybeBar("造假风险", r.RiskWarning.FraudRiskScore)
        }.Where(b => b != null).Select(b => b!).ToList();

        var points = CollectNonEmpty(
            r.HealthAssessment.CoreInsight,
            r.HealthAssessment.SolvencyAssessment,
            r.ProfitQuality.ProfitSustainability,
            r.CashFlow.EfficiencyDescription);

        var risks = CollectNonEmpty(r.RiskWarning.KeyRiskIndicators.ToArray());
        risks.AddRange(r.RiskWarning.MonitoringPoints);
        risks.Add(r.RiskWarning.FraudRiskRationale);
        risks.RemoveAll(string.IsNullOrWhiteSpace);

        return new AnalystCardViewModel(
            ratingText,
            tone,
            bars,
            points,
            CollectKeyValues(
                ("ROE", FormatPercent(r.ProfitQuality.ROE)),
                ("净利率", FormatPercent(r.ProfitQuality.NetMargin)),
                ("资产负债率", FormatPercent(r.HealthAssessment.DebtRatio))),
            risks);
    }

    private static AnalystCardViewModel? BuildFrom(TechnicalAnalysisResult? r)
    {
        if (r is null) return null;

        var (ratingText, tone) = MapInvestmentRating(r.Strategy.TechnicalRating);
        var bars = new[]
        {
            MaybeBar("趋势强度", r.PatternTrend.TrendStrengthScore),
            MaybeBar("形态可靠性", r.PatternTrend.PatternReliabilityScore),
            MaybeBar("支撑强度", r.PriceLevels.SupportStrengthScore),
            MaybeBar("阻力强度", r.PriceLevels.ResistanceStrengthScore),
            MaybeBar("突破概率", r.PriceLevels.BreakoutProbabilityScore)
        }.Where(b => b != null).Select(b => b!).ToList();

        var points = CollectNonEmpty(
            $"当前趋势：{r.PatternTrend.CurrentTrend.GetDescription()}（{r.PatternTrend.TimeFrame.GetDescription()}）",
            r.PatternTrend.KeyPatterns,
            r.Indicators.TrendIndicatorSignals,
            r.Indicators.MomentumIndicatorSignals,
            r.Indicators.IndicatorSynergyDescription);

        return new AnalystCardViewModel(
            ratingText,
            tone,
            bars,
            points,
            CollectKeyValues(
                ("当前价", FormatPrice(r.PriceLevels.CurrentPrice)),
                ("目标区间", FormatRange(r.Strategy.TargetPriceLow, r.Strategy.TargetPriceHigh)),
                ("止损位", FormatPrice(r.Strategy.StopLossPrice)),
                ("持仓周期", r.Strategy.HoldingPeriod.GetDescription()),
                ("风险等级", r.Strategy.RiskLevel.GetDescription())),
            CollectNonEmpty(
                r.Strategy.RiskLevel == Level.High ? "风险等级偏高，注意仓位控制" : null));
    }

    private static AnalystCardViewModel? BuildFrom(MarketSentimentAnalysisResult? r)
    {
        if (r is null) return null;

        var (ratingText, tone) = MapAtmosphere(r.SentimentAssessment.OverallAtmosphere);
        var bars = new[]
        {
            MaybeBar("情绪强度", r.SentimentAssessment.EmotionIntensityScore),
            MaybeBar("氛围强度", r.SentimentAssessment.AtmosphereIntensityScore),
            MaybeBar("散户活跃度", r.BehaviorAnalysis.RetailActivityScore),
            MaybeBar("偏差程度", r.BehaviorAnalysis.BiasSeverityScore)
        }.Where(b => b != null).Select(b => b!).ToList();

        var points = CollectNonEmpty(
            $"主导情绪：{r.SentimentAssessment.DominantEmotion.GetDescription()}",
            r.SentimentAssessment.ConfidenceTrendDescription,
            $"主力资金：{r.CapitalFlowAnalysis.MainCapitalFlow.GetDescription()}，机构动向：{r.CapitalFlowAnalysis.InstitutionTrend.GetDescription()}",
            r.BehaviorAnalysis.InstitutionMainTrend,
            r.ShortTermStrategy.ShortTermOpportunities,
            $"操作建议：{r.ShortTermStrategy.OperationRecommendation.GetDescription()} / 仓位：{r.ShortTermStrategy.PositionRecommendation.GetDescription()}",
            r.ShortTermStrategy.BestTiming);

        return new AnalystCardViewModel(
            ratingText,
            tone,
            bars,
            points,
            CollectKeyValues(
                ("热点板块", r.ShortTermStrategy.HotSectors),
                ("止损参考", r.ShortTermStrategy.StopLossPosition),
                ("目标区间", r.ShortTermStrategy.TargetPriceRange)),
            CollectNonEmpty(r.ShortTermStrategy.PsychologicalTrapToAvoid));
    }

    private static AnalystCardViewModel? BuildFrom(NewsEventAnalysisResult? r)
    {
        if (r is null) return null;

        var (ratingText, tone) = MapEventNature(r.EventAnalysis.EventNature);
        var bars = new[]
        {
            MaybeBar("信息可信度", r.EventAnalysis.CredibilityScore),
            MaybeBar("事件重要性", r.EventAnalysis.ImportanceScore),
            MaybeBar("基本面影响", r.ImpactEvaluation.FundamentalImpactScore),
            MaybeBar("情绪强度", r.ImpactEvaluation.SentimentIntensityScore)
        }.Where(b => b != null).Select(b => b!).ToList();

        var points = CollectNonEmpty(
            r.EventAnalysis.EventSummary,
            $"事件性质：{r.EventAnalysis.EventNature.GetDescription()}（{r.EventAnalysis.InformationSource.GetDescription()}）",
            r.ImpactEvaluation.FundamentalImpactLogic,
            r.InvestmentGuidance.CoreInvestmentLogic,
            r.InvestmentGuidance.SpecificActionAdvice);
        points.AddRange(r.InvestmentGuidance.FocusPoints);
        points.RemoveAll(string.IsNullOrWhiteSpace);

        return new AnalystCardViewModel(
            ratingText,
            tone,
            bars,
            points,
            CollectKeyValues(
                ("应对策略", r.InvestmentGuidance.ResponseStrategy.GetDescription()),
                ("影响范围", r.ImpactEvaluation.ImpactScope.GetDescription()),
                ("持续时长", r.ImpactEvaluation.ExpectedTimeframe)),
            CollectNonEmpty(r.InvestmentGuidance.KeyRiskAlert));
    }

    private static AnalystCardViewModel? BuildFrom(CryptoMetricsAnalysisResult? r)
    {
        if (r is null) return null;

        var bars = new[]
        {
            MaybeBar("市值规模", r.MarketCap.MarketCapScore),
            MaybeBar("流动性", r.Liquidity.LiquidityScore),
            MaybeBar("波动风险", r.VolatilityRisk.RiskScore),
            MaybeBar("市场结构", r.MarketStructure.StructureScore)
        }.Where(b => b != null).Select(b => b!).ToList();

        return new AnalystCardViewModel(
            null,
            AnalystRatingTone.Neutral,
            bars,
            CollectNonEmpty(
                r.MarketCap.CirculatingAssessment,
                r.Liquidity.VolumeDistributionNotes,
                r.MarketStructure.KeyObservations),
            CollectKeyValues(("最大回撤", r.VolatilityRisk.MaxDrawdown ?? string.Empty)),
            CollectNonEmpty(
                r.VolatilityRisk.RiskScore is > 7 ? "波动风险评分偏高，注意控制仓位与杠杆" : null));
    }

    private static AnalystCardViewModel? BuildFrom(CoordinatorResult? r)
    {
        if (r is null) return null;

        var (ratingText, tone) = MapInvestmentRating(r.InvestmentRating);
        var bars = new[]
        {
            MaybeBar("综合评分", r.OverallScore),
            MaybeBar("基本面", r.DimensionScores.Fundamental),
            MaybeBar("技术面", r.DimensionScores.Technical),
            MaybeBar("财务面", r.DimensionScores.Financial),
            MaybeBar("市场情绪", r.DimensionScores.Sentiment),
            MaybeBar("新闻事件", r.DimensionScores.News),
            ConfidenceBar(r.ConfidencePercentage)
        }.Where(b => b != null).Select(b => b!).ToList();

        var points = CollectNonEmpty(
            r.Summary,
            r.ConsensusAnalysis,
            r.DisagreementAnalysis);
        points.AddRange(r.InvestmentHighlights);
        points.AddRange(r.OperationSuggestions);
        points.RemoveAll(string.IsNullOrWhiteSpace);

        return new AnalystCardViewModel(
            ratingText,
            tone,
            bars,
            points,
            CollectKeyValues(
                ("目标价", r.TargetPrice),
                ("价格预期", r.PriceChangeExpectation),
                ("投资周期", JoinNonEmpty(" ", r.TimeHorizon.GetDescription(), r.TimeHorizonDescription)),
                ("风险等级", r.RiskLevel.GetDescription())),
            CollectNonEmpty(r.RiskFactors.ToArray()));
    }

    private static (string?, AnalystRatingTone) MapInvestmentRating(InvestmentRating rating) => rating switch
    {
        InvestmentRating.StrongBuy or InvestmentRating.Buy => ("买入", AnalystRatingTone.Positive),
        InvestmentRating.Hold => ("持有", AnalystRatingTone.Neutral),
        InvestmentRating.Reduce => ("减持", AnalystRatingTone.Negative),
        InvestmentRating.Sell or InvestmentRating.StrongSell => ("卖出", AnalystRatingTone.Negative),
        _ => (null, AnalystRatingTone.Neutral)
    };

    private static (string?, AnalystRatingTone) MapFinancialStability(FinancialStability stability) => stability switch
    {
        FinancialStability.Strong => ("稳健", AnalystRatingTone.Positive),
        FinancialStability.Medium => ("一般", AnalystRatingTone.Neutral),
        FinancialStability.Weak => ("偏弱", AnalystRatingTone.Negative),
        _ => (null, AnalystRatingTone.Neutral)
    };

    private static (string?, AnalystRatingTone) MapAtmosphere(MarketAtmosphere atmosphere) => atmosphere switch
    {
        MarketAtmosphere.ExtremelyOptimistic or MarketAtmosphere.Optimistic => ("乐观", AnalystRatingTone.Positive),
        MarketAtmosphere.Neutral => ("中性", AnalystRatingTone.Neutral),
        MarketAtmosphere.Pessimistic or MarketAtmosphere.ExtremelyPessimistic => ("悲观", AnalystRatingTone.Negative),
        _ => (null, AnalystRatingTone.Neutral)
    };

    private static (string?, AnalystRatingTone) MapEventNature(EventNature nature) => nature switch
    {
        EventNature.MajorPositive or EventNature.Positive => ("利好", AnalystRatingTone.Positive),
        EventNature.Neutral => ("中性", AnalystRatingTone.Neutral),
        EventNature.Negative or EventNature.MajorNegative => ("利空", AnalystRatingTone.Negative),
        _ => (null, AnalystRatingTone.Neutral)
    };

    private static AnalystScoreBarItem? MaybeBar(string label, float value)
        => value > 0 ? new AnalystScoreBarItem(label, value) : null;

    private static AnalystScoreBarItem? MaybeBar(string label, float? value)
        => value is > 0 ? new AnalystScoreBarItem(label, value.Value) : null;

    /// <summary>置信度为 0-100 的百分比，归一化为 1-10 分制</summary>
    private static AnalystScoreBarItem? ConfidenceBar(float confidence)
        => confidence > 0 ? new AnalystScoreBarItem("置信度", Math.Clamp(confidence / 10f, 0f, 10f)) : null;

    private static List<string> CollectNonEmpty(params string?[] values)
        => values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList();

    private static IReadOnlyList<AnalystKeyValueItem> CollectKeyValues(params (string Label, string Value)[] items)
        => items
            .Where(i => !string.IsNullOrWhiteSpace(i.Value))
            .Select(i => new AnalystKeyValueItem(i.Label, i.Value.Trim()))
            .ToList();

    private static string JoinNonEmpty(string separator, params string?[] values)
        => string.Join(separator, values.Where(v => !string.IsNullOrWhiteSpace(v)));

    private static string FormatPrice(decimal? price)
        => price.HasValue ? $"{price.Value:0.####}" : string.Empty;

    private static string FormatRange(decimal? low, decimal? high)
    {
        if (low.HasValue && high.HasValue)
            return $"{low.Value:0.####} - {high.Value:0.####}";
        return low.HasValue ? $"{low.Value:0.####}" : string.Empty;
    }

    private static string FormatPercent(float? value)
        => value.HasValue ? $"{value.Value:0.##}%" : string.Empty;
}

/// <summary>
/// 评级徽标色调：A 股惯例红涨绿跌，Positive 用红色、Negative 用绿色
/// </summary>
public enum AnalystRatingTone
{
    Positive,
    Neutral,
    Negative
}