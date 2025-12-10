using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Services.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Serialization;

namespace MarketAssistant.ViewModels.Demo;

public partial class ChatSidebarDemoViewModel : ViewModelBase
{
  public override string Title => "Chat Demo";

  [ObservableProperty]
  private ChatSidebarViewModel _targetViewModel;

  private readonly JsonSerializerOptions _jsonOptions;

  public ChatSidebarDemoViewModel() : base(NullLogger.Instance)
  {
    // 初始化 TargetViewModel
    var mcpService = new McpService(NullLogger<McpService>.Instance);

    TargetViewModel = new ChatSidebarViewModel(
        NullLogger<ChatSidebarViewModel>.Instance,
        new MockChatClientFactory(),
        NullLoggerFactory.Instance,
        mcpService
    );

    _jsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      Converters = { new JsonStringEnumConverter() }
    };
  }

  [RelayCommand]
  private void AddTextMessage(string content)
  {
    TargetViewModel.ChatMessages.Add(new ChatMessageAdapter(new ChatMessage(ChatRole.Assistant, content) { AuthorName = "AI Assistant" }));
  }

  [RelayCommand]
  private void AddUserMessage(string content)
  {
    TargetViewModel.ChatMessages.Add(new ChatMessageAdapter(new ChatMessage(ChatRole.User, content) { AuthorName = "User" }));
  }

  [RelayCommand]
  private void AddMarkdownMessage()
  {
    var markdown = @"# Markdown 测试

这是一个 **Markdown** 消息测试。

- 列表项 1
- 列表项 2

```csharp
public void Hello()
{
    Console.WriteLine(""Hello World"");
}
```

[链接](https://www.google.com)
";
    TargetViewModel.ChatMessages.Add(new ChatMessageAdapter(new ChatMessage(ChatRole.Assistant, markdown) { AuthorName = "AI Assistant" }));
  }

  [RelayCommand]
  private void AddAdaptiveCard(string json)
  {
    TargetViewModel.ChatMessages.Add(new ChatMessageAdapter(new ChatMessage(ChatRole.Assistant, json) { AuthorName = "AI Assistant" }));
  }

  [RelayCommand]
  private void ClearMessages()
  {
    TargetViewModel.ChatMessages.Clear();
  }

  // Coordinator
  public string CoordinatorCardJson => JsonSerializer.Serialize(new CoordinatorResult
  {
    OverallScore = 8.5f,
    InvestmentRating = InvestmentRating.Buy,
    TargetPrice = "55.00 - 60.00 元",
    PriceChangeExpectation = "综合判断预计上涨 15-20%",
    TimeHorizon = Duration.MediumTerm,
    TimeHorizonDescription = "中期 6-12 个月",
    RiskLevel = Level.Medium,
    ConfidencePercentage = 85,
    DimensionScores = new AnalysisDimensionScores
    {
      Fundamental = 8.0f,
      Technical = 7.5f,
      Financial = 8.0f, // Added Financial to be complete
      Sentiment = 9.0f,
      News = 8.5f
    },
    InvestmentHighlights = new List<string> { "业绩超预期", "技术面突破", "行业政策利好" },
    RiskFactors = new List<string> { "宏观经济波动", "原材料价格上涨" },
    OperationSuggestions = new List<string> { "建议在 50 元附近建仓", "止损位设在 45 元", "目标价 60 元分批止盈" },
    ConsensusAnalysis = "所有分析师均认为该公司基本面稳健，且近期有重大利好消息驱动，市场情绪高涨。",
    DisagreementAnalysis = "技术分析师认为短期有回调风险，而基本面分析师认为长期价值被低估。综合来看，短期回调是买入机会。",
    Summary = "基本面优秀，技术面配合，建议逢低买入。",
    KeyIndicators = new List<KeyIndicator>
        {
            new KeyIndicator { AnalystSource = "基本面分析师", Category = "财务数据", Name = "ROE", Value = "15.2%", Signal = "健康", Suggestion = "持续关注" },
            new KeyIndicator { AnalystSource = "技术分析师", Category = "技术指标", Name = "MACD", Value = "金叉", Signal = "买入", Suggestion = "右侧交易" }
        }
  }, _jsonOptions);

  // Financial
  public string FinancialCardJson => JsonSerializer.Serialize(new FinancialAnalysisResult
  {
    HealthAssessment = new FinancialHealth
    {
      SolvencyScore = 8,
      CurrentRatio = 2.5f,
      QuickRatio = 1.8f,
      SolvencyAssessment = "偿债能力强，流动资产充足。",
      DebtRatio = 45.5f,
      DebtRatioTrend = TrendChange.Stable,
      DebtStructureAssessment = DebtStructureAssessment.Healthy,
      OverallStability = FinancialStability.Strong,
      StabilityScore = 9,
      CoreInsight = "财务结构稳健，无重大债务风险。"
    },
    ProfitQuality = new ProfitabilityQuality
    {
      GrossMargin = 30.5f,
      NetMargin = 12.8f,
      NetMarginTrend = TrendChange.Rising,
      ProfitTrendAssessment = ProfitTrendAssessment.SteadyGrowth,
      ROE = 15.2f,
      ROA = 8.5f,
      IndustryComparison = Level.High,
      ProfitQualityLevel = Level.High,
      ProfitQualityScore = 9,
      ProfitSustainability = "核心业务盈利能力强，具有可持续性。"
    },
    CashFlow = new CashFlowAssessment
    {
      OperatingCashFlow = 100000000,
      CashFlowToNetIncomeRatio = 1.2f,
      CashFlowQualityScore = 9,
      FreeCashFlowStatus = FreeCashFlowStatus.Positive,
      FreeCashFlowTrend = FreeCashFlowTrend.Improving,
      FreeCashFlowSustainabilityScore = 8,
      CashConversionCycle = 45,
      CashConversionCycleTrend = TrendChange.Falling,
      EfficiencyDescription = "营运资本管理效率高。"
    },
    RiskWarning = new FinancialRiskWarning
    {
      KeyRiskIndicators = new List<string> { "应收账款周转天数略有增加" },
      FraudRiskLevel = Level.Low,
      FraudRiskScore = 2,
      FraudRiskRationale = "财务报表逻辑自洽，无明显造假迹象。",
      MonitoringPoints = new List<string> { "关注原材料价格波动对毛利率的影响" }
    }
  }, _jsonOptions);

  // Fundamental
  public string FundamentalCardJson => JsonSerializer.Serialize(new FundamentalAnalysisResult
  {
    BasicInfo = new StockBasicInfo { Symbol = "SH600000", Name = "浦发银行", CurrentPrice = 10.5m, DailyChangePercent = 1.2f, DailyChangeAmount = 0.12m },
    Fundamentals = new CompanyFundamentals
    {
      Industry = "银行业",
      IndustryGrowthScore = 6,
      CoreBusiness = "商业银行业务",
      BusinessQualityScore = 8,
      ProfitabilityOverview = "盈利能力稳定，分红率高。",
      ProfitabilityTrend = ProfitabilityTrend.Average,
      FinancialHealthOverview = "资本充足率达标，资产质量改善。",
      CashFlowStatus = CashFlowStatus.Healthy
    },
    Competition = new IndustryCompetitiveness
    {
      IndustryLifecycle = IndustryLifecycle.Maturity,
      LifecycleConfidenceScore = 9,
      MarketPosition = MarketPosition.SecondTier,
      MarketShareDescription = "股份制银行前列。",
      CoreCompetence = "长三角区域优势。",
      CompetenceStrengthScore = 7,
      BarrierLevel = Level.High,
      BarrierDescription = "牌照壁垒和资金壁垒。"
    },
    GrowthValue = new GrowthAndValue
    {
      GrowthDrivers = "零售转型和数字化赋能。",
      GrowthSustainabilityScore = 7,
      ValuationDescription = "PB 0.5倍，处于历史低位。",
      InvestmentRating = InvestmentRating.Buy,
      ValuationTarget = "合理估值修复至 0.7倍 PB。",
      InvestmentHighlights = new List<string> { "低估值高股息", "资产质量拐点" },
      KeyRisk = "宏观经济下行导致坏账增加。"
    }
  }, _jsonOptions);

  // News
  public string NewsCardJson => JsonSerializer.Serialize(new NewsEventAnalysisResult
  {
    EventAnalysis = new EventInterpretation
    {
      EventType = EventType.Earnings,
      EventSummary = "发布2024年三季度财报，净利润同比增长15%。",
      InformationSource = InformationSource.Official,
      CredibilityScore = 10,
      EventNature = EventNature.Positive,
      ImportanceScore = 8
    },
    ImpactEvaluation = new ImpactAssessment
    {
      FundamentalImpact = ImpactDirection.Positive,
      FundamentalImpactScore = 8,
      FundamentalImpactLogic = "业绩超预期验证了公司经营改善。",
      SentimentImpact = ImpactDirection.Positive,
      SentimentIntensityScore = 7,
      SentimentChangeExpectation = "短期提振市场信心。",
      ImpactScope = ImpactScope.CompanySpecific,
      ImpactDuration = Duration.MediumTerm,
      ExpectedTimeframe = "未来1-2个季度",
      MarketExpectedReaction = MarketReactionExpectation.RationalReaction,
      PriceChangeExpectation = PriceChangeExpectation.Rise,
      CapitalFlowExpectation = CapitalFlowDirection.NetInflow,
      CapitalScaleEstimate = "预计吸引中长期资金配置。"
    },
    InvestmentGuidance = new InvestmentInsight
    {
      InvestmentImpactAssessment = InvestmentImpactAssessment.Opportunity,
      CoreInvestmentLogic = "业绩拐点确认，估值有望修复。",
      ResponseStrategy = OperationRecommendation.Buy,
      SpecificActionAdvice = "建议逢低吸纳。",
      FocusPoints = new List<string> { "后续季度业绩持续性" },
      KeyRiskAlert = "宏观经济不及预期。"
    }
  }, _jsonOptions);

  // Sentiment
  public string SentimentCardJson => JsonSerializer.Serialize(new MarketSentimentAnalysisResult
  {
    SentimentAssessment = new MarketSentiment
    {
      DominantEmotion = DominantEmotion.Greed,
      EmotionIntensityScore = 7,
      VIXLevel = "15.5",
      InvestorConfidenceLevel = Level.High,
      ConfidenceTrendDescription = "信心逐步回升。",
      OverallAtmosphere = MarketAtmosphere.Optimistic,
      AtmosphereIntensityScore = 8
    },
    CapitalFlowAnalysis = new CapitalFlow
    {
      MainCapitalFlow = CapitalFlowDirection.NetInflow,
      MainCapitalAmount = 500000000,
      MainCapitalConsecutiveDays = 3,
      InstitutionTrend = InstitutionTrend.Increasing,
      InstitutionPositionChange = "机构仓位小幅提升。",
      NorthboundCapitalFlow = CapitalFlowDirection.NetInflow,
      NorthboundCapitalAmount = 200000000,
      NorthboundCapitalPercentage = 15.5f,
      MarginFinancingChange = "融资余额增加。",
      MarginTradingChange = "融券余额减少。",
      LeverageDescription = "杠杆资金情绪回暖。"
    },
    BehaviorAnalysis = new InvestorBehavior
    {
      MainBehaviorBias = BehaviorBias.HerdMentality,
      BiasSeverityScore = 6,
      RetailInvestorCharacteristics = RetailInvestorCharacteristics.ChasingRally,
      RetailActivityScore = 8,
      InstitutionBehaviorConsistency = BehaviorConsistency.Consistent,
      InstitutionMainTrend = "一致看多。",
      RiskPreference = RiskPreference.HighRisk,
      RiskPreferenceChange = "风险偏好提升。"
    },
    ShortTermStrategy = new ShortTermInsight
    {
      MarketRhythm = MarketRhythm.OneSidedTrend,
      MarketRhythmRationale = "量价齐升，趋势向好。",
      HotSectors = "科技、新能源。",
      HotnessSustainabilityAssessment = "热点具有持续性。",
      ShortTermOpportunities = "关注板块轮动机会。",
      OperationRecommendation = OperationRecommendation.Buy,
      PositionRecommendation = PositionRecommendation.Aggressive,
      BestTiming = "回调即买入。",
      TargetPriceRange = "短期看高一线。"
    }
  }, _jsonOptions);

  // Technical
  public string TechnicalCardJson => JsonSerializer.Serialize(new TechnicalAnalysisResult
  {
    PatternTrend = new ChartPatternTrend
    {
      CurrentTrend = TrendDirection.Uptrend,
      TrendStrengthScore = 8,
      KeyPatterns = "突破箱体震荡。",
      PatternReliabilityScore = 9,
      TimeFrame = TimeFrame.Daily,
      TimeFrameConsistencyScore = 8
    },
    PriceLevels = new KeyPriceLevels
    {
      CurrentPrice = 52.5m,
      SupportLevels = new List<decimal> { 50.0m, 48.5m },
      SupportStrengthScore = 8,
      ResistanceLevels = new List<decimal> { 55.0m, 58.0m },
      ResistanceStrengthScore = 7,
      BreakoutDirection = BreakoutDirection.UpwardBreakout,
      BreakoutProbabilityScore = 8
    },
    Indicators = new TechnicalIndicators
    {
      TrendIndicatorSignals = "均线多头排列。",
      TrendIndicatorReliabilityScore = 9,
      MomentumIndicatorSignals = "MACD金叉向上。",
      MomentumIndicatorReliabilityScore = 8,
      VolumeStatus = VolumeStatus.Expanding,
      PriceVolumeRelationship = PriceVolumeRelationship.Healthy,
      IndicatorConsistency = Level.High,
      IndicatorSynergyDescription = "量价配合理想。"
    },
    Strategy = new TradingStrategy
    {
      TechnicalRating = InvestmentRating.Buy,
      OperationDirection = OperationRecommendation.Buy,
      TargetPriceLow = 55.0m,
      TargetPriceHigh = 58.0m,
      StopLossPrice = 49.5m,
      HoldingPeriod = Duration.ShortTerm,
      RiskLevel = Level.Medium
    }
  }, _jsonOptions);


}
