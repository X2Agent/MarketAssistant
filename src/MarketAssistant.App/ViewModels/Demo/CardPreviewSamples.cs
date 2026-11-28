using System.Text.Json;
using System.Text.Json.Serialization;
using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.ViewModels.Demo;

/// <summary>分析师回复在聊天气泡里可能出现的几种文本形态。</summary>
public enum CardPreviewShape
{
    PureJson,
    PrefaceAndJson,
    FencedJson,
    PrefaceAndFencedJson
}

/// <summary>形态下拉框中的一项。</summary>
public sealed record CardPreviewShapeOption(CardPreviewShape Shape, string Label);

/// <summary>预览页上的一个分析师样本：Agent 标识名、该分析师典型的前置说明文字、结构化 JSON。</summary>
public sealed record CardPreviewSample(string Label, string AgentName, string Preface, string Json);

/// <summary>
/// 卡片预览页的样本数据。全部为虚构的示例数据，仅用于界面预览；
/// JSON 由强类型结果模型序列化而来，与线上结构化输出的键名、枚举写法一致。
/// </summary>
public static class CardPreviewSamples
{
    /// <summary>预览页使用的虚构标的代码。</summary>
    public const string StockCode = "sz300000";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly CardPreviewSample Fundamental = new(
        "基本面分析师",
        "FundamentalAnalyst",
        "根据工具获取的数据，我已收集到该公司的实时行情和公司基本面信息。需要说明的是，" +
        "详细财务报表数据（毛利率、净利率、负债率、现金流等具体数值）未能通过工具获取，以下分析基于可用数据并明确标注缺失项。",
        Serialize(new FundamentalAnalysisResult
        {
            BasicInfo = new StockBasicInfo
            {
                Symbol = StockCode,
                Name = "示例光电",
                CurrentPrice = 186.40m,
                DailyChangePercent = 2.35f,
                DailyChangeAmount = 4.28m
            },
            Fundamentals = new CompanyFundamentals
            {
                Industry = "光通信设备",
                IndustryGrowthScore = 8.5f,
                CoreBusiness = "高速光模块研发与制造，主营 800G 数据中心产品",
                BusinessQualityScore = 8.2f,
                ProfitabilityOverview = "盈利能力强，毛利率与净利率均处于行业领先水平",
                ProfitabilityTrend = ProfitabilityTrend.Excellent,
                FinancialHealthOverview = "财务稳健，负债率较低，经营现金流充裕",
                CashFlowStatus = CashFlowStatus.Healthy
            },
            Competition = new IndustryCompetitiveness
            {
                IndustryLifecycle = IndustryLifecycle.Growth,
                LifecycleConfidenceScore = 7.8f,
                MarketPosition = MarketPosition.Leader,
                MarketShareDescription = "全球高速光模块份额位居前列",
                CoreCompetence = "技术迭代速度与头部云厂商的深度绑定",
                CompetenceStrengthScore = 8.4f,
                BarrierLevel = Level.High,
                BarrierDescription = "客户认证周期长，产能与良率壁垒高"
            },
            GrowthValue = new GrowthAndValue
            {
                GrowthDrivers = "AI 算力投资带动 800G/1.6T 光模块需求放量",
                GrowthSustainabilityScore = 8f,
                ValuationDescription = "PE 高于行业均值，但与盈利增速基本匹配",
                InvestmentRating = InvestmentRating.Buy,
                ConfidencePercentage = 72f,
                ValuationTarget = "合理估值区间约 175-210 元",
                InvestmentHighlights = ["下游 AI 资本开支持续高增长", "高端产品占比提升带动毛利率改善"],
                KeyRisk = "云厂商资本开支节奏放缓会直接影响订单能见度"
            }
        }));

    private static readonly CardPreviewSample Sentiment = new(
        "市场情绪分析师",
        "MarketSentimentAnalyst",
        "以下是基于当前行情、资金流向与投资者行为数据的市场情绪分析。",
        Serialize(new MarketSentimentAnalysisResult
        {
            SentimentAssessment = new MarketSentiment
            {
                DominantEmotion = DominantEmotion.Greed,
                EmotionIntensityScore = 7.2f,
                InvestorConfidenceLevel = Level.High,
                ConfidenceTrendDescription = "市场信心连续三周回升，风险偏好上行",
                OverallAtmosphere = MarketAtmosphere.Optimistic,
                AtmosphereIntensityScore = 6.8f
            },
            CapitalFlowAnalysis = new CapitalFlow
            {
                MainCapitalFlow = CapitalFlowDirection.NetInflow,
                MainCapitalAmount = 385_000_000m,
                MainCapitalConsecutiveDays = 3,
                InstitutionTrend = InstitutionTrend.Increasing,
                InstitutionPositionChange = "公募基金与 QFII 持仓环比小幅增加",
                NorthboundCapitalFlow = CapitalFlowDirection.NetInflow,
                NorthboundCapitalAmount = 126_000_000m,
                NorthboundCapitalPercentage = 4.3f,
                MarginFinancingChange = "融资余额近一周增加约 2.1 亿元",
                MarginTradingChange = "融券余额基本持平",
                LeverageDescription = "杠杆水平温和，未见过热迹象"
            },
            BehaviorAnalysis = new InvestorBehavior
            {
                MainBehaviorBias = BehaviorBias.HerdMentality,
                BiasSeverityScore = 5.5f,
                RetailInvestorCharacteristics = RetailInvestorCharacteristics.ChasingRally,
                RetailActivityScore = 6.5f,
                InstitutionBehaviorConsistency = BehaviorConsistency.Consistent,
                InstitutionMainTrend = "机构围绕算力产业链逢低布局",
                RiskPreference = RiskPreference.HighRisk,
                RiskPreferenceChange = "短线资金风险偏好较上周明显提升"
            },
            ShortTermStrategy = new ShortTermInsight
            {
                MarketRhythm = MarketRhythm.FastRotation,
                MarketRhythmRationale = "热点轮动加快，板块持续性偏短",
                HotSectors = "光模块、算力租赁、液冷",
                HotnessSustainabilityAssessment = "题材仍有催化，但追高性价比下降",
                ShortTermOpportunities = "回踩 5 日线附近可关注波段低吸机会",
                OperationRecommendation = OperationRecommendation.Buy,
                ConfidencePercentage = 65f,
                PositionRecommendation = PositionRecommendation.Moderate,
                BestTiming = "回调至关键支撑附近且量能萎缩时",
                TargetPriceRange = "198-210 元",
                StopLossPosition = "跌破 176 元",
                PsychologicalTrapToAvoid = "避免在放量冲高时追涨"
            }
        }));

    private static readonly CardPreviewSample Technical = new(
        "技术分析师",
        "TechnicalAnalyst",
        "已获取日线级别的 K 线与技术指标数据，分析结果如下：",
        Serialize(new TechnicalAnalysisResult
        {
            PatternTrend = new ChartPatternTrend
            {
                CurrentTrend = TrendDirection.Uptrend,
                TrendStrengthScore = 7.5f,
                KeyPatterns = "上升通道内高位震荡，近期形成小型旗形整理",
                PatternReliabilityScore = 7f,
                TimeFrame = TimeFrame.Daily,
                TimeFrameConsistencyScore = 6.5f
            },
            PriceLevels = new KeyPriceLevels
            {
                CurrentPrice = 186.40m,
                SupportLevels = [176m, 168m],
                SupportStrengthScore = 7f,
                ResistanceLevels = [192m, 205m],
                ResistanceStrengthScore = 6.5f,
                BreakoutDirection = BreakoutDirection.UpwardBreakout,
                BreakoutProbabilityScore = 6.8f
            },
            Indicators = new TechnicalIndicators
            {
                TrendIndicatorSignals = "均线多头排列，MACD 在零轴上方金叉后红柱放大",
                TrendIndicatorReliabilityScore = 7.5f,
                MomentumIndicatorSignals = "RSI 位于 62，未进入超买区；KDJ 高位钝化",
                MomentumIndicatorReliabilityScore = 6.5f,
                VolumeStatus = VolumeStatus.Expanding,
                PriceVolumeRelationship = PriceVolumeRelationship.Healthy,
                IndicatorConsistency = Level.High,
                IndicatorSynergyDescription = "趋势与动量指标方向一致，量价配合良好"
            },
            Strategy = new TradingStrategyRecommendation
            {
                TechnicalRating = InvestmentRating.Buy,
                OperationDirection = OperationRecommendation.Buy,
                TargetPriceLow = 198m,
                TargetPriceHigh = 210m,
                StopLossPrice = 176m,
                HoldingPeriod = Duration.MediumTerm,
                RiskLevel = Level.Medium,
                ConfidencePercentage = 68f
            }
        }));

    private static readonly CardPreviewSample Financial = new(
        "财务分析师",
        "FinancialAnalyst",
        "已完成财务数据检索，分析结果如下：",
        Serialize(new FinancialAnalysisResult
        {
            HealthAssessment = new FinancialHealth
            {
                CurrentRatio = 2.8f,
                QuickRatio = 2.1f,
                SolvencyScore = 8f,
                SolvencyAssessment = "流动比率与速动比率均处于安全区间，短期偿债压力小",
                DebtRatio = 32.5f,
                DebtRatioTrend = TrendChange.Stable,
                DebtStructureAssessment = DebtStructureAssessment.Healthy,
                OverallStability = FinancialStability.Strong,
                StabilityScore = 8.2f,
                CoreInsight = "资产负债结构健康，财务杠杆低，抗风险能力强"
            },
            ProfitQuality = new ProfitabilityQuality
            {
                GrossMargin = 34.8f,
                NetMargin = 21.3f,
                NetMarginTrend = TrendChange.Rising,
                ProfitTrendAssessment = ProfitTrendAssessment.SteadyGrowth,
                ROE = 22.4f,
                ROA = 14.6f,
                IndustryComparison = Level.High,
                ProfitQualityLevel = Level.High,
                ProfitQualityScore = 8f,
                ProfitSustainability = "利润主要来自主营业务，随高端产品占比提升具备可持续性"
            },
            CashFlow = new CashFlowAssessment
            {
                OperatingCashFlow = 3_650_000_000m,
                CashFlowToNetIncomeRatio = 0.92f,
                CashFlowQualityScore = 7.5f,
                FreeCashFlowStatus = FreeCashFlowStatus.Positive,
                FreeCashFlowTrend = FreeCashFlowTrend.Improving,
                FreeCashFlowSustainabilityScore = 7f,
                CashConversionCycle = 118,
                CashConversionCycleTrend = TrendChange.Falling,
                EfficiencyDescription = "存货与应收周转同步改善，现金转换周期持续缩短"
            },
            RiskWarning = new FinancialRiskWarning
            {
                KeyRiskIndicators = ["存货与应收账款随扩产同步上升"],
                FraudRiskLevel = Level.Low,
                FraudRiskScore = 2.5f,
                FraudRiskRationale = "未见关联交易异常，经营现金流与利润匹配度较高",
                MonitoringPoints = ["关注存货周转天数变化", "跟踪资本开支对自由现金流的影响"]
            },
            FinancialRating = InvestmentRating.Buy,
            ConfidencePercentage = 74f
        }));

    /// <summary>
    /// 提示词约定"无数据时设为 null"：所有百分比与评级字段留空，用来观察缺值时显示 N/A 而不是孤立的 "%"。
    /// </summary>
    private static readonly CardPreviewSample FinancialMissingData = new(
        "财务分析师（数据缺失）",
        "FinancialAnalyst",
        "部分财报数据未能通过工具获取，缺失项已按约定留空（null），分析结果如下：",
        Serialize(new FinancialAnalysisResult
        {
            HealthAssessment = new FinancialHealth
            {
                SolvencyScore = 5f,
                SolvencyAssessment = "未能获取资产负债表数据，无法量化评估偿债能力",
                OverallStability = FinancialStability.Medium,
                StabilityScore = 5f,
                CoreInsight = "关键财务数据缺失，稳健性仅能做保守的定性判断"
            },
            ProfitQuality = new ProfitabilityQuality
            {
                ProfitQualityScore = 5f,
                ProfitSustainability = "缺少利润表数据，利润质量暂时无法验证"
            },
            CashFlow = new CashFlowAssessment
            {
                CashFlowQualityScore = 5f,
                FreeCashFlowSustainabilityScore = 5f,
                EfficiencyDescription = "现金流量表数据缺失，无法评估现金转换效率"
            },
            RiskWarning = new FinancialRiskWarning
            {
                KeyRiskIndicators = ["财务数据缺失，风险无法被充分识别"],
                FraudRiskLevel = Level.Medium,
                FraudRiskScore = 5f,
                FraudRiskRationale = "数据不足，无法做出有效判断",
                MonitoringPoints = ["待获取完整财报后复核"]
            }
        }));

    private static readonly CardPreviewSample News = new(
        "新闻事件分析师",
        "NewsEventAnalyst",
        "检索到近期相关公告与新闻，事件解读如下：",
        Serialize(new NewsEventAnalysisResult
        {
            EventAnalysis = new EventInterpretation
            {
                EventType = EventType.CompanyAnnouncement,
                EventSummary = "公司公告 800G 光模块进入规模量产阶段",
                InformationSource = InformationSource.Official,
                CredibilityScore = 9f,
                EventNature = EventNature.Positive,
                ConfidencePercentage = 78f,
                ImportanceScore = 8f
            },
            ImpactEvaluation = new ImpactAssessment
            {
                FundamentalImpact = ImpactDirection.Positive,
                FundamentalImpactScore = 7.5f,
                FundamentalImpactLogic = "量产节奏提前将带动下半年营收与毛利率同步改善",
                SentimentImpact = ImpactDirection.Positive,
                SentimentIntensityScore = 7f,
                SentimentChangeExpectation = "短期市场关注度显著提升，预期情绪继续升温",
                ImpactScope = ImpactScope.IndustryWide,
                ImpactDuration = Duration.MediumTerm,
                ExpectedTimeframe = "未来 3-6 个月",
                MarketExpectedReaction = MarketReactionExpectation.RationalReaction,
                PriceChangeExpectation = PriceChangeExpectation.Rise,
                CapitalFlowExpectation = CapitalFlowDirection.NetInflow,
                CapitalScaleEstimate = "预计吸引数亿元规模的增量配置资金"
            },
            InvestmentGuidance = new InvestmentInsight
            {
                InvestmentImpactAssessment = InvestmentImpactAssessment.Opportunity,
                CoreInvestmentLogic = "量产兑现强化业绩确定性，估值有望获得进一步支撑",
                ResponseStrategy = OperationRecommendation.Buy,
                SpecificActionAdvice = "回调至支撑位附近分批布局，避免在公告日高位追涨",
                FocusPoints = ["后续大客户订单披露", "产能爬坡与良率进展"],
                KeyRiskAlert = "若下游资本开支不及预期，量产红利可能被需求端抵消"
            }
        }));

    private static readonly CardPreviewSample Coordinator = new(
        "协调分析师",
        "CoordinatorAnalyst",
        "综合各位分析师的结论与分歧点，最终判断如下：",
        Serialize(new CoordinatorResult
        {
            OverallScore = 7.8f,
            InvestmentRating = InvestmentRating.Buy,
            TargetPrice = "198-212 元",
            TargetPriceValue = 205m,
            ExpectedChangePercent = 10m,
            PriceChangeExpectation = "综合判断预计上涨 8-12%",
            TimeHorizon = Duration.MediumTerm,
            TimeHorizonDescription = "中期 6-12 个月",
            RiskLevel = Level.Medium,
            ConfidencePercentage = 70f,
            DimensionScores = new AnalysisDimensionScores
            {
                Fundamental = 8.2f,
                Technical = 7.4f,
                Financial = 8f,
                Sentiment = 7f,
                News = 7.6f
            },
            InvestmentHighlights =
            [
                "AI 算力需求带动高端光模块持续放量",
                "财务稳健、盈利质量高，抗周期能力强",
                "800G 量产公告强化业绩确定性"
            ],
            RiskFactors = ["下游云厂商资本开支节奏放缓", "短期涨幅较大，存在技术性回调压力"],
            OperationSuggestions =
            [
                "在 176-184 元区间分批建仓（技术支撑与估值相互印证）",
                "跌破 176 元止损，控制单笔亏损在可承受范围内",
                "单一标的仓位不超过总仓位的 15%"
            ],
            ConsensusAnalysis = "各分析师一致认为公司处于景气上行周期，高端产品放量与财务稳健是核心支撑，中期趋势向好，短期不宜追高。",
            DisagreementAnalysis = "基本面与新闻面偏乐观，情绪面提示短线拥挤。经核对资金数据，主力与北向资金仍在净流入，综合判断回调幅度有限，预计中期上涨 8-12%。",
            Summary = "基本面与资金面共振，中期看多，回调即布局机会",
            KeyIndicators =
            [
                new KeyIndicator { AnalystSource = "技术分析师", Category = "技术指标", Name = "MACD", Value = "金叉", Signal = "买入", Suggestion = "关注红柱放大是否持续" },
                new KeyIndicator { AnalystSource = "技术分析师", Category = "技术指标", Name = "RSI", Value = "62", Signal = "中性", Suggestion = "未进入超买区，可继续持有" },
                new KeyIndicator { AnalystSource = "财务分析师", Category = "财务数据", Name = "ROE", Value = "22.4%", Signal = "健康", Suggestion = "盈利能力领先，关注可持续性" },
                new KeyIndicator { AnalystSource = "基本面分析师", Category = "估值指标", Name = "PE", Value = "38 倍", Signal = "合理", Suggestion = "与盈利增速匹配，估值有支撑" },
                new KeyIndicator { AnalystSource = "市场情绪分析师", Category = "市场数据", Name = "主力资金", Value = "连续 3 日净流入", Signal = "强势", Suggestion = "跟踪资金是否持续流入" },
                new KeyIndicator { AnalystSource = "新闻事件分析师", Category = "事件影响", Name = "800G 量产", Value = "官方公告", Signal = "利好", Suggestion = "回调时分批布局，避免追高" }
            ],
            QualityMetrics = new AnalysisQualityMetrics
            {
                DataCompletenessPercent = 85,
                MissingDataDimensions = ["部分季度财报数据缺失"],
                AnalystConsensusPercent = 80,
                LimitationsNote = "部分财务数据缺失，结论对公司盈利兑现节奏的判断存在一定不确定性。",
                OverallQualityLevel = AnalysisQualityLevel.High
            }
        }));

    private static readonly CardPreviewSample CryptoMetrics = new(
        "项目指标分析师",
        "CryptoMetricsAnalyst",
        "已获取链上与交易所公开指标，评估结果如下：",
        Serialize(new CryptoMetricsAnalysisResult
        {
            MarketCap = new CryptoMarketCapAssessment
            {
                MarketCapScore = 8f,
                CirculatingAssessment = "流通量占总供应量约 92%，近期无大额解锁压力"
            },
            Liquidity = new CryptoLiquidityAssessment
            {
                LiquidityScore = 7.5f,
                VolumeDistributionNotes = "成交集中在头部交易所，现货与永续合约深度充足"
            },
            VolatilityRisk = new CryptoVolatilityRisk
            {
                RiskScore = 6f,
                MaxDrawdown = "近 90 天最大回撤约 35%"
            },
            MarketStructure = new CryptoMarketStructure
            {
                StructureScore = 6.5f,
                KeyObservations = "买盘略占优，但上方挂单密集，突破需要增量资金"
            }
        }));

    /// <summary>形态下拉框的全部选项。</summary>
    public static IReadOnlyList<CardPreviewShapeOption> ShapeOptions { get; } =
    [
        new(CardPreviewShape.PrefaceAndJson, "说明文字 + JSON（线上最常见）"),
        new(CardPreviewShape.PureJson, "纯 JSON"),
        new(CardPreviewShape.FencedJson, "代码块 ```json"),
        new(CardPreviewShape.PrefaceAndFencedJson, "说明文字 + 代码块")
    ];

    /// <summary>可单独追加到聊天里的全部样本。</summary>
    public static IReadOnlyList<CardPreviewSample> All { get; } =
        [Fundamental, Sentiment, Technical, Financial, FinancialMissingData, News, Coordinator, CryptoMetrics];

    /// <summary>
    /// 一次完整 A 股分析的消息序列：每位分析师搭配不同的输出形态，模拟线上形态参差不齐的真实情况。
    /// </summary>
    public static IReadOnlyList<(CardPreviewSample Sample, CardPreviewShape Shape)> FullAnalysis { get; } =
    [
        (Fundamental, CardPreviewShape.PrefaceAndJson),
        (FinancialMissingData, CardPreviewShape.PrefaceAndFencedJson),
        (Technical, CardPreviewShape.PureJson),
        (Sentiment, CardPreviewShape.PrefaceAndJson),
        (News, CardPreviewShape.FencedJson),
        (Coordinator, CardPreviewShape.PureJson)
    ];

    /// <summary>按指定形态生成聊天消息正文。</summary>
    public static string Render(CardPreviewSample sample, CardPreviewShape shape) => shape switch
    {
        CardPreviewShape.PureJson => sample.Json,
        CardPreviewShape.PrefaceAndJson => $"{sample.Preface}\n\n{sample.Json}",
        CardPreviewShape.FencedJson => $"```json\n{sample.Json}\n```",
        CardPreviewShape.PrefaceAndFencedJson => $"{sample.Preface}\n\n```json\n{sample.Json}\n```",
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
    };

    private static string Serialize<T>(T model) => JsonSerializer.Serialize(model, JsonOptions);
}
