using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Agents.Tools.Models.AShare;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.Tools;

/// <summary>
/// IShareFinancialTools 接口测试(仅测试 A股 实现,虚拟币已改用 ICryptoMetricsTools)
/// </summary>
[TestClass]
public class FinancialDataToolsTest
{
    private ServiceProvider? _serviceProvider;
    private string? _zhiTuApiToken;

    public TestContext? TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        // 从环境变量读取智兔 API 令牌（不在代码中硬编码，避免提交到仓库）
        _zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN");

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 注册命名 HttpClient（含 ZhiTu、Cls 等 BaseAddress 与弹性策略），与生产配置一致
        services.AddNamedMarketHttpClients();

        // 通过 Mock 注入带真实密钥的 UserSetting（避免依赖本地 Preferences 存储）
        var userSetting = new UserSetting
        {
            ZhiTuApiToken = _zhiTuApiToken ?? ""
        };
        var userSettingServiceMock = new Mock<IUserSettingService>();
        userSettingServiceMock.Setup(x => x.CurrentSetting).Returns(userSetting);
        services.AddSingleton<IUserSettingService>(userSettingServiceMock.Object);

        // 注册被测试的服务（仅 A股）
        services.AddKeyedSingleton<IShareFinancialTools, AShareFinancialTools>(MarketType.AShare);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    /// <summary>
    /// 断言智兔 API 令牌已配置（缺失则测试失败，而非跳过）
    /// </summary>
    private void RequireZhiTuToken()
    {
        if (string.IsNullOrEmpty(_zhiTuApiToken))
        {
            Assert.Fail("ZHITU_API_TOKEN 环境变量未配置，无法调用智兔 API 进行真实场景验证");
        }
    }

    #region A股财务数据测试

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetBalanceSheetAsync_AShare_ShouldReturnValidData()
    {
        // 智兔 API 需要令牌
        RequireZhiTuToken();

        // Arrange - 贵州茅台 SH600519
        var service = _serviceProvider!.GetRequiredKeyedService<IShareFinancialTools>(MarketType.AShare);

        // Act - 真实调用智兔 API 获取资产负债表
        var balanceSheets = await service.GetBalanceSheetAsync("SH600519");

        // Assert - 验证真实资产负债表数据（关键字段非空 + 数值合理性，证明 API 真实返回）
        Assert.IsNotNull(balanceSheets);
        Assert.IsTrue(balanceSheets.Count > 0, "应返回至少一条资产负债表数据");

        var latest = balanceSheets[0];
        Assert.IsFalse(string.IsNullOrEmpty(latest.EndDate), $"截止日期不应为空，实际: {latest.EndDate}");
        Assert.IsTrue(latest.TotalAssets > 0, $"资产总计应大于0，实际: {latest.TotalAssets}");
        Assert.IsTrue(latest.TotalLiabilities > 0, $"负债合计应大于0，实际: {latest.TotalLiabilities}");
        Assert.IsTrue(latest.TotalEquity > 0, $"所有者权益合计应大于0，实际: {latest.TotalEquity}");
        Assert.IsTrue(latest.TotalCurrentAssets > 0, $"流动资产合计应大于0，实际: {latest.TotalCurrentAssets}");
        Assert.IsTrue(latest.TotalCurrentLiabilities >= 0, $"流动负债合计应非负，实际: {latest.TotalCurrentLiabilities}");
        Assert.IsTrue(latest.MonetaryFunds > 0, $"货币资金应大于0，实际: {latest.MonetaryFunds}");
        Assert.IsTrue(latest.PaidInCapital > 0, $"实收资本应大于0，实际: {latest.PaidInCapital}");
        // 会计恒等式校验：资产总计 = 负债合计 + 所有者权益合计
        Assert.IsTrue(latest.TotalAssets.HasValue && latest.TotalLiabilities.HasValue && latest.TotalEquity.HasValue,
            "资产总计、负债合计、所有者权益合计均不应为空，无法校验会计恒等式");
        var equitySum = latest.TotalLiabilities.Value + latest.TotalEquity.Value;
        Assert.AreEqual(latest.TotalAssets.Value, equitySum, 1m,
            $"会计恒等式不成立: 资产总计({latest.TotalAssets}) ≠ 负债合计({latest.TotalLiabilities}) + 所有者权益合计({latest.TotalEquity})");

        TestContext?.WriteLine($"SH600519 资产负债表 截止日期: {latest.EndDate}, 资产总计: {latest.TotalAssets}, 负债合计: {latest.TotalLiabilities}, 所有者权益: {latest.TotalEquity}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetIncomeStatementAsync_AShare_ShouldReturnValidData()
    {
        // 智兔 API 需要令牌
        RequireZhiTuToken();

        // Arrange - 贵州茅台 SH600519
        var service = _serviceProvider!.GetRequiredKeyedService<IShareFinancialTools>(MarketType.AShare);

        // Act - 真实调用智兔 API 获取利润表
        var incomeStatements = await service.GetIncomeStatementAsync("SH600519");

        // Assert - 验证真实利润表数据（关键字段非空 + 数值合理性，证明 API 真实返回）
        Assert.IsNotNull(incomeStatements);
        Assert.IsTrue(incomeStatements.Count > 0, "应返回至少一条利润表数据");

        var latest = incomeStatements[0];
        Assert.IsFalse(string.IsNullOrEmpty(latest.EndDate), $"截止日期不应为空，实际: {latest.EndDate}");
        Assert.IsTrue(latest.OperatingRevenue > 0, $"营业收入应大于0，实际: {latest.OperatingRevenue}");
        Assert.IsTrue(latest.TotalOperatingRevenue > 0, $"营业总收入应大于0，实际: {latest.TotalOperatingRevenue}");
        Assert.IsTrue(latest.OperatingCost > 0, $"营业成本应大于0，实际: {latest.OperatingCost}");
        Assert.IsTrue(latest.OperatingProfit > 0, $"营业利润应大于0，实际: {latest.OperatingProfit}");
        Assert.IsTrue(latest.TotalProfit > 0, $"利润总额应大于0，实际: {latest.TotalProfit}");
        Assert.IsTrue(latest.NetProfit > 0, $"净利润应大于0，实际: {latest.NetProfit}");
        Assert.IsTrue(latest.NetProfitAttributableToParent > 0, $"归母净利润应大于0，实际: {latest.NetProfitAttributableToParent}");
        Assert.IsTrue(latest.BasicEarningsPerShare > 0, $"基本每股收益应大于0，实际: {latest.BasicEarningsPerShare}");
        // 利润逻辑校验：营业总收入 >= 营业收入
        Assert.IsTrue(latest.TotalOperatingRevenue >= latest.OperatingRevenue,
            $"营业总收入({latest.TotalOperatingRevenue})应大于等于营业收入({latest.OperatingRevenue})");
        // 利润逻辑校验：利润总额 >= 营业利润（营业外收支净额通常较小，但利润总额应包含营业利润）
        Assert.IsTrue(latest.TotalProfit >= latest.OperatingProfit,
            $"利润总额({latest.TotalProfit})应大于等于营业利润({latest.OperatingProfit})");

        TestContext?.WriteLine($"SH600519 利润表 截止日期: {latest.EndDate}, 营业收入: {latest.OperatingRevenue}, 净利润: {latest.NetProfit}, 基本每股收益: {latest.BasicEarningsPerShare}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetCashFlowStatementAsync_AShare_ShouldReturnValidData()
    {
        // 智兔 API 需要令牌
        RequireZhiTuToken();

        // Arrange - 贵州茅台 SH600519
        var service = _serviceProvider!.GetRequiredKeyedService<IShareFinancialTools>(MarketType.AShare);

        // Act - 真实调用智兔 API 获取现金流量表
        var cashFlowStatements = await service.GetCashFlowStatementAsync("SH600519");

        // Assert - 验证真实现金流量表数据（关键字段非空 + 数值合理性，证明 API 真实返回）
        Assert.IsNotNull(cashFlowStatements);
        Assert.IsTrue(cashFlowStatements.Count > 0, "应返回至少一条现金流量表数据");

        var latest = cashFlowStatements[0];
        Assert.IsFalse(string.IsNullOrEmpty(latest.EndDate), $"截止日期不应为空，实际: {latest.EndDate}");
        Assert.IsTrue(latest.NetCashFlowFromOperating > 0, $"经营活动现金流量净额应大于0，实际: {latest.NetCashFlowFromOperating}");
        Assert.IsTrue(latest.TotalCashInflowsFromOperating > 0, $"经营活动现金流入小计应大于0，实际: {latest.TotalCashInflowsFromOperating}");
        Assert.IsTrue(latest.TotalCashOutflowsFromOperating > 0, $"经营活动现金流出小计应大于0，实际: {latest.TotalCashOutflowsFromOperating}");
        Assert.IsTrue(latest.CashFromSalesAndServices > 0, $"销售商品提供劳务收到的现金应大于0，实际: {latest.CashFromSalesAndServices}");
        Assert.IsTrue(latest.EndingCashBalance > 0, $"期末现金余额应大于0，实际: {latest.EndingCashBalance}");
        Assert.IsTrue(latest.BeginningCashBalance > 0, $"期初现金余额应大于0，实际: {latest.BeginningCashBalance}");
        // 现金流逻辑校验：经营活动现金流入小计 >= 销售商品提供劳务收到的现金
        Assert.IsTrue(latest.TotalCashInflowsFromOperating >= latest.CashFromSalesAndServices,
            $"经营活动现金流入小计({latest.TotalCashInflowsFromOperating})应大于等于销售商品提供劳务收到的现金({latest.CashFromSalesAndServices})");
        // 现金流逻辑校验：经营活动净额 = 流入小计 - 流出小计
        Assert.IsTrue(latest.TotalCashInflowsFromOperating.HasValue && latest.TotalCashOutflowsFromOperating.HasValue && latest.NetCashFlowFromOperating.HasValue,
            "经营活动现金流入/流出/净额均不应为空，无法校验勾稽关系");
        var expectedNet = latest.TotalCashInflowsFromOperating.Value - latest.TotalCashOutflowsFromOperating.Value;
        Assert.AreEqual(expectedNet, latest.NetCashFlowFromOperating.Value, 1m,
            $"经营活动净额勾稽不成立: 流入({latest.TotalCashInflowsFromOperating}) - 流出({latest.TotalCashOutflowsFromOperating}) ≠ 净额({latest.NetCashFlowFromOperating})");

        TestContext?.WriteLine($"SH600519 现金流量表 截止日期: {latest.EndDate}, 经营活动净额: {latest.NetCashFlowFromOperating}, 期末现金余额: {latest.EndingCashBalance}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetFinancialRatiosAsync_AShare_ShouldReturnValidData()
    {
        // 智兔 API 需要令牌
        RequireZhiTuToken();

        // Arrange - 贵州茅台 SH600519
        var service = _serviceProvider!.GetRequiredKeyedService<IShareFinancialTools>(MarketType.AShare);

        // Act - 真实调用智兔 API 获取财务主要指标
        var ratios = await service.GetFinancialRatiosAsync("SH600519");

        // Assert - 验证真实财务指标数据（关键字段非空 + 数值合理性，证明 API 真实返回）
        Assert.IsNotNull(ratios);
        Assert.IsTrue(ratios.Count > 0, "应返回至少一条财务指标数据");

        var latest = ratios[0];
        Assert.IsFalse(string.IsNullOrEmpty(latest.EndDate), $"截止日期不应为空，实际: {latest.EndDate}");
        Assert.IsTrue(latest.BasicEarningsPerShare > 0, $"基本每股收益应大于0，实际: {latest.BasicEarningsPerShare}");
        Assert.IsTrue(latest.NetAssetsPerShare > 0, $"每股净资产应大于0，实际: {latest.NetAssetsPerShare}");
        Assert.IsTrue(latest.ReturnOnEquity > 0, $"净资产收益率应大于0，实际: {latest.ReturnOnEquity}");
        Assert.IsTrue(latest.GrossMargin > 0, $"销售毛利率应大于0，实际: {latest.GrossMargin}");
        Assert.IsTrue(latest.NetProfitMargin > 0, $"净利率应大于0，实际: {latest.NetProfitMargin}");
        Assert.IsTrue(latest.AssetLiabilityRatio >= 0, $"资产负债率应非负，实际: {latest.AssetLiabilityRatio}");
        // 茅台是高毛利白酒企业，毛利率应处于较高水平（> 50%）
        Assert.IsTrue(latest.GrossMargin > 50, $"茅台销售毛利率应大于50%，实际: {latest.GrossMargin}");
        // 茅台资产负债率应处于较低水平（< 50%）
        Assert.IsTrue(latest.AssetLiabilityRatio < 50, $"茅台资产负债率应小于50%，实际: {latest.AssetLiabilityRatio}");

        TestContext?.WriteLine($"SH600519 财务指标 截止日期: {latest.EndDate}, ROE: {latest.ReturnOnEquity}%, 毛利率: {latest.GrossMargin}%, 净利率: {latest.NetProfitMargin}%, 资产负债率: {latest.AssetLiabilityRatio}%");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetCapitalStructureAsync_AShare_ShouldReturnValidData()
    {
        // 智兔 API 需要令牌
        RequireZhiTuToken();

        // Arrange - 贵州茅台 SH600519
        var service = _serviceProvider!.GetRequiredKeyedService<IShareFinancialTools>(MarketType.AShare);

        // Act - 真实调用智兔 API 获取股本结构
        var capitalStructure = await service.GetCapitalStructureAsync("SH600519");

        // Assert - 验证真实股本结构数据（关键字段非空 + 数值合理性，证明 API 真实返回）
        Assert.IsNotNull(capitalStructure);
        Assert.IsTrue(capitalStructure.Count > 0, "应返回至少一条股本结构数据");

        var latest = capitalStructure[0];
        Assert.IsFalse(string.IsNullOrEmpty(latest.ChangeDate), $"变动日期不应为空，实际: {latest.ChangeDate}");
        Assert.IsTrue(latest.TotalShares > 0, $"总股本应大于0，实际: {latest.TotalShares}");
        Assert.IsTrue(latest.CirculatingAShares > 0, $"已上市流通A股应大于0，实际: {latest.CirculatingAShares}");
        // 茅台总股本约 12.56 亿股，校验量级合理性
        Assert.IsTrue(latest.TotalShares > 100000000, $"茅台总股本应大于1亿股，实际: {latest.TotalShares}");
        // 已上市流通A股应小于等于总股本
        Assert.IsTrue(latest.CirculatingAShares <= latest.TotalShares,
            $"已上市流通A股({latest.CirculatingAShares})应小于等于总股本({latest.TotalShares})");
        // 限售流通股 + 已上市流通A股 = 总股本（若限售流通股字段有值）
        if (latest.RestrictedShares.HasValue)
        {
            var shareSum = latest.CirculatingAShares!.Value + latest.RestrictedShares!.Value;
            Assert.IsTrue(Math.Abs(latest.TotalShares.Value - shareSum) <= 1m,
                $"股本勾稽不成立: 流通A股({latest.CirculatingAShares}) + 限售流通股({latest.RestrictedShares}) ≠ 总股本({latest.TotalShares})");
        }

        TestContext?.WriteLine($"SH600519 股本结构 变动日期: {latest.ChangeDate}, 总股本: {latest.TotalShares}, 流通A股: {latest.CirculatingAShares}, 限售流通股: {latest.RestrictedShares}");
    }

    #endregion
}
