using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.AShare;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Agents.Tools.Models.AShare;
using MarketAssistant.Agents.Tools.Models.Crypto;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services;
using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;

namespace TestMarketAssistant.Tools;

/// <summary>
/// IBasicDataTools 接口真实场景验证测试（A股实现）
/// 使用真实 API 调用验证工具实现的 authenticity：
/// - A股 GetAssetInfoAsync：调用财联社（cls.cn）行情接口
/// - A股 GetCompanyInfoAsync：调用智兔 API（需 ZhiTuApiToken）
///
/// 环境变量（缺失时对应测试直接 Fail，不跳过）：
/// - ZHITU_API_TOKEN：智兔 API 令牌（A 股公司基本面必需）
/// - JINA_API_KEY：Jina 嵌入密钥（写入 UserSetting，IBasicDataTools 不直接使用）
/// - OPENAI_API_KEY：SiliconFlow LLM 密钥（写入 UserSetting，IBasicDataTools 不直接使用）
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class BasicDataToolsTest
{
    private ServiceProvider? _serviceProvider;
    private string? _zhiTuApiToken;
    private string? _jinaApiKey;
    private string? _siliconFlowApiKey;

    public TestContext? TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        // 从环境变量读取 API 密钥（不在代码中硬编码，避免提交到仓库）
        _zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN");
        _jinaApiKey = Environment.GetEnvironmentVariable("JINA_API_KEY");
        _siliconFlowApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        var services = new ServiceCollection();
        services.AddAShareDataProviders();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 注册命名 HttpClient（含 BaseAddress 与弹性策略），与生产配置一致
        services.AddNamedMarketHttpClients();

        // 通过 Mock 注入带真实密钥的 UserSetting（避免依赖本地 Preferences 存储）
        var userSetting = new UserSetting
        {
            ProviderId = "SiliconFlow",
            ZhiTuApiToken = _zhiTuApiToken ?? "",
            EmbeddingApiKey = _jinaApiKey ?? "",
            EmbeddingEndpoint = "https://api.jina.ai",
            EmbeddingModelId = "jina-embeddings-v5-text-small",
            ProviderApiKeys = new Dictionary<string, string> { ["SiliconFlow"] = _siliconFlowApiKey ?? "" },
            ProviderModelIds = new Dictionary<string, string> { ["SiliconFlow"] = "deepseek-ai/DeepSeek-V3.2" }
        };
        var userSettingServiceMock = new Mock<IUserSettingService>();
        userSettingServiceMock.Setup(x => x.CurrentSetting).Returns(userSetting);
        services.AddSingleton<IUserSettingService>(userSettingServiceMock.Object);

        // 注册被测试的服务（A股 + 虚拟币，含基接口与子接口）
        services.AddKeyedSingleton<IBasicDataTools, AShareBasicTools>(MarketType.AShare);
        services.AddKeyedSingleton<IBasicDataTools, AShareBasicTools>(MarketType.AShare);

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

    /// <summary>
    /// AIFunction 返回 JsonElement 的反序列化选项（MAF 默认 camelCase 序列化，需大小写不敏感反序列化）。
    /// </summary>
    private static readonly JsonSerializerOptions AIFunctionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #region A股基础数据测试

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetAssetInfoAsync_AShare_ShouldReturnValidQuoteInfo()
    {
        // Arrange - 贵州茅台 SH600519，财联社公开行情接口
        var service = (AShareBasicTools)_serviceProvider!.GetRequiredKeyedService<IBasicDataTools>(MarketType.AShare);

        // Act - 验证工具能真实调用财联社 API 并返回结构化数据
        var quoteInfo = await service.GetAssetInfoAsync("SH600519");

        // Assert - 验证真实行情数据（关键字段非空 + 数值合理性，证明 API 真实返回而非空对象）
        Assert.IsNotNull(quoteInfo, "财联社 API 应返回行情数据对象");
        Assert.IsFalse(string.IsNullOrEmpty(quoteInfo.SecurityName), "股票名称不应为空");
        Assert.IsTrue(quoteInfo.SecurityName.Contains("茅台"), $"股票名称应包含'茅台'，实际: {quoteInfo.SecurityName}");
        Assert.IsFalse(string.IsNullOrEmpty(quoteInfo.SecurityCode), "股票代码不应为空");
        Assert.IsTrue(quoteInfo.CurrentPrice > 0, $"当前价格应大于0，实际: {quoteInfo.CurrentPrice}");
        Assert.IsTrue(quoteInfo.MarketCapitalization > 0, $"总市值应大于0，实际: {quoteInfo.MarketCapitalization}");
        Assert.IsTrue(quoteInfo.HighPrice >= quoteInfo.LowPrice, $"最高价({quoteInfo.HighPrice})应大于等于最低价({quoteInfo.LowPrice})");
        Assert.IsTrue(quoteInfo.UpLimitPrice >= quoteInfo.CurrentPrice, $"涨停价({quoteInfo.UpLimitPrice})应大于等于当前价({quoteInfo.CurrentPrice})");
        Assert.IsTrue(quoteInfo.DownLimitPrice <= quoteInfo.CurrentPrice, $"跌停价({quoteInfo.DownLimitPrice})应小于等于当前价({quoteInfo.CurrentPrice})");
        Assert.IsTrue(quoteInfo.TotalShares > 0, $"总股本应大于0，实际: {quoteInfo.TotalShares}");
        TestContext?.WriteLine($"SH600519 名称: {quoteInfo.SecurityName}, 当前价: {quoteInfo.CurrentPrice}, 总市值: {quoteInfo.MarketCapitalization}亿, 涨跌幅: {quoteInfo.PercentageChange}%");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetCompanyInfoAsync_AShare_ShouldReturnValidCompanyInfo()
    {
        // 智兔 API 需要令牌
        RequireZhiTuToken();

        // Arrange
        var service = (AShareBasicTools)_serviceProvider!.GetRequiredKeyedService<IBasicDataTools>(MarketType.AShare);

        // Act - 真实调用智兔 API 获取公司基本面
        var companyInfo = await service.GetCompanyInfoAsync("SH600519");

        // Assert - 验证真实公司基本面数据（关键字段非空 + 内容匹配，证明智兔 API 真实返回）
        Assert.IsNotNull(companyInfo, "公司信息不应为空");
        Assert.IsFalse(string.IsNullOrEmpty(companyInfo.Name), "公司名称不应为空");
        Assert.IsTrue(companyInfo.Name.Contains("茅台"), $"公司名称应包含'茅台'，实际: {companyInfo.Name}");
        Assert.IsFalse(string.IsNullOrEmpty(companyInfo.Market), "上市市场不应为空");
        Assert.IsFalse(string.IsNullOrEmpty(companyInfo.ListingDate), "上市日期不应为空");
        Assert.IsFalse(string.IsNullOrEmpty(companyInfo.BusinessScope), "经营范围不应为空");
        Assert.IsFalse(string.IsNullOrEmpty(companyInfo.Description), "公司简介不应为空");
        TestContext?.WriteLine($"{companyInfo.Name} 市场: {companyInfo.Market}, 上市日期: {companyInfo.ListingDate}, 简介: {companyInfo.Description.Substring(0, Math.Min(50, companyInfo.Description.Length))}...");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task GetAssetInfoAsync_AShare_MultipleSymbols_ShouldAllReturnValidData()
    {
        // Arrange - 验证多只股票的真实行情
        var service = (AShareBasicTools)_serviceProvider!.GetRequiredKeyedService<IBasicDataTools>(MarketType.AShare);
        var symbols = new[] { "SH600519", "SZ000001", "SH600036" };
        var expectedNames = new[] { "茅台", "平安", "招商" };

        for (var i = 0; i < symbols.Length; i++)
        {
            var symbol = symbols[i];
            var expectedName = expectedNames[i];

            // Act - 验证每只股票都能真实调用 API 并返回数据对象
            var quoteInfo = await service.GetAssetInfoAsync(symbol);

            // Assert - 校验 API 连通性 + 真实数据内容（名称匹配 + 价格合理）
            Assert.IsNotNull(quoteInfo, $"{symbol} 行情数据不应为空");
            Assert.IsFalse(string.IsNullOrEmpty(quoteInfo.SecurityName), $"{symbol} 股票名称不应为空");
            Assert.IsTrue(quoteInfo.SecurityName.Contains(expectedName), $"{symbol} 股票名称应包含'{expectedName}'，实际: {quoteInfo.SecurityName}");
            Assert.IsFalse(string.IsNullOrEmpty(quoteInfo.SecurityCode), $"{symbol} 股票代码不应为空");
            Assert.IsTrue(quoteInfo.CurrentPrice > 0, $"{symbol} 当前价格应大于0，实际: {quoteInfo.CurrentPrice}");
            Assert.IsTrue(quoteInfo.MarketCapitalization > 0, $"{symbol} 总市值应大于0，实际: {quoteInfo.MarketCapitalization}");
            TestContext?.WriteLine($"{symbol} 名称: {quoteInfo.SecurityName}, 当前价: {quoteInfo.CurrentPrice}, 总市值: {quoteInfo.MarketCapitalization}亿");
        }
    }

    #endregion

    #region GetFunctions 验证（MAF 工具函数契约）

    [TestMethod]
    [TestCategory("Integration")]
    public void GetFunctions_AShare_ShouldReturnTwoAIFunctions()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IBasicDataTools>(MarketType.AShare);

        // Act
        var functions = service.GetFunctions().ToList();
        var functionNames = functions.Select(f => f.Name).ToList();
        TestContext?.WriteLine($"AShare AIFunction 名称: {string.Join(", ", functionNames)}");

        // Assert - AShareBasicTools 暴露 2 个 AIFunction
        Assert.IsNotNull(functions);
        Assert.AreEqual(2, functions.Count);
        // 使用 Contains 模糊匹配，兼容 AIFunctionFactory 不同版本的命名约定
        Assert.IsTrue(functionNames.Any(n => n.Contains("GetAssetInfo")), "应包含 GetAssetInfo 函数");
        Assert.IsTrue(functionNames.Any(n => n.Contains("GetCompanyInfo")), "应包含 GetCompanyInfo 函数");
    }

    #endregion

    #region AIFunction 真实调用验证（通过 MAF 契约入口）

    [TestMethod]
    [TestCategory("Integration")]
    public async Task AIFunction_GetAssetInfoAsync_AShare_ShouldInvokeRealApi()
    {
        // Arrange - 通过 GetFunctions() 返回的 AIFunction 调用，验证 MAF 契约可真实触发 API
        var service = _serviceProvider!.GetRequiredKeyedService<IBasicDataTools>(MarketType.AShare);
        var getAssetInfoFunction = service.GetFunctions().First(f => f.Name.Contains("GetAssetInfo"));

        // Act - AIFunction.InvokeAsync 返回 JsonElement（MAF 序列化返回值），需反序列化为强类型
        var result = await getAssetInfoFunction.InvokeAsync(new AIFunctionArguments
        {
            ["assetSymbol"] = "SH600519"
        });

        // Assert - 验证 AIFunction 真实返回数据并反序列化为 StockQuoteInfo（非空对象 + 真实字段值）
        Assert.IsNotNull(result, "AIFunction 返回值不应为空");
        Assert.IsInstanceOfType(result, typeof(JsonElement), $"AIFunction 返回值应为 JsonElement 类型，实际: {result.GetType().Name}");
        var jsonElement = (JsonElement)result;
        var quoteInfo = JsonSerializer.Deserialize<StockQuoteInfo>(jsonElement.GetRawText(), AIFunctionJsonOptions)
            ?? throw new AssertFailedException("AIFunction 返回值反序列化为 StockQuoteInfo 失败");
        Assert.IsFalse(string.IsNullOrEmpty(quoteInfo.SecurityName), "股票名称不应为空");
        Assert.IsTrue(quoteInfo.SecurityName.Contains("茅台"), $"股票名称应包含'茅台'，实际: {quoteInfo.SecurityName}");
        Assert.IsTrue(quoteInfo.CurrentPrice > 0, $"当前价格应大于0，实际: {quoteInfo.CurrentPrice}");
        Assert.IsTrue(quoteInfo.MarketCapitalization > 0, $"总市值应大于0，实际: {quoteInfo.MarketCapitalization}");
        TestContext?.WriteLine($"AIFunction 返回 {quoteInfo.SecurityName}({quoteInfo.SecurityCode}), 当前价: {quoteInfo.CurrentPrice}, 总市值: {quoteInfo.MarketCapitalization}亿");
    }

    #endregion
}
