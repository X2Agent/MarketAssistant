using MarketAssistant.Applications.Crypto;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MarketAssistant.Tests.Crypto;

/// <summary>
/// 币安API鉴权服务测试
/// </summary>
[TestClass]
public class BinanceAuthServiceTest
{
    private ILogger<BinanceAuthService> _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            // 不需要额外的日志提供程序
        });
        _logger = loggerFactory.CreateLogger<BinanceAuthService>();
    }

    /// <summary>
    /// 测试HMAC签名生成
    /// 使用币安文档中的示例数据验证签名是否正确
    /// 文档示例：https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api/request-security#hmac-keys
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void TestHmacSignature_WithBinanceExample()
    {
        // Arrange - 使用币安文档中的示例密钥（仅用于测试）
        var authService = new BinanceAuthService(
            _logger,
            CreateUserSettingService(
                "vmPUZE6mv9SD5VNHk4HlWFsOr6aKE2zvsw0MuIgwCIPy6utIco14y7Ju91duEh8A",
                "NhqPtmdSJYdKjVHjA7PZj4Mge3R5YNiP1e3UZjInClVN65XAbvqqM6A7H5fATj0j"));

        // Act - 构建币安文档中的示例payload（不包含timestamp，手动测试签名算法）
        // 文档示例：symbol=LTCBTC&side=BUY&type=LIMIT&timeInForce=GTC&quantity=1&price=0.1&recvWindow=5000&timestamp=1499827319559
        var testPayload = "symbol=LTCBTC&side=BUY&type=LIMIT&timeInForce=GTC&quantity=1&price=0.1&recvWindow=5000&timestamp=1499827319559";

        // 使用反射调用私有方法进行测试（仅用于单元测试）
        var generateSignatureMethod = typeof(BinanceAuthService)
            .GetMethod("GenerateSignature", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var signature = generateSignatureMethod?.Invoke(
            null,
            new object[]
            {
                testPayload,
                "NhqPtmdSJYdKjVHjA7PZj4Mge3R5YNiP1e3UZjInClVN65XAbvqqM6A7H5fATj0j"
            }) as string;

        // Assert - 验证签名是否与文档示例一致
        var expectedSignature = "c8db56825ae71d6d79447849e617115f4a920fa2acdcab2b053c4b2838bd6b71";

        Console.WriteLine($"生成的签名: {signature}");
        Console.WriteLine($"期望的签名: {expectedSignature}");

        Assert.IsNotNull(signature);
        Assert.AreEqual(expectedSignature, signature.ToLowerInvariant());
    }

    /// <summary>
    /// 测试签名查询字符串生成（实际使用场景）
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void TestSignQueryString_ShouldAddTimestampAndSignature()
    {
        // Arrange
        var authService = new BinanceAuthService(
            _logger,
            CreateUserSettingService("test-api-key", "test-secret-key"));

        // Act
        var queryString = "symbol=BTCUSDT&side=BUY&type=MARKET&quantity=0.001";
        var signedQuery = authService.SignQueryString(queryString);

        // Assert
        Console.WriteLine($"原始查询: {queryString}");
        Console.WriteLine($"签名后查询: {signedQuery}");

        Assert.IsTrue(signedQuery.Contains("timestamp="));
        Assert.IsTrue(signedQuery.Contains("signature="));
        Assert.IsTrue(signedQuery.Contains(queryString));
    }

    /// <summary>
    /// 测试配置验证 - 需要SecretKey
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void TestConfigValidation_RequiresSecretKey()
    {
        // Arrange
        var authService = new BinanceAuthService(
            _logger,
            CreateUserSettingService("test-api-key", string.Empty));

        // Act & Assert
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => authService.SignQueryString("symbol=BTCUSDT"));
        Console.WriteLine($"预期异常: {exception.Message}");
        Assert.IsTrue(exception.Message.Contains("Secret Key"));
    }

    /// <summary>
    /// 测试添加鉴权 Header
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void TestAddAuthHeaders_ShouldSetApiKeyHeader()
    {
        var authService = new BinanceAuthService(
            _logger,
            CreateUserSettingService("test-api-key", "test-secret-key"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com");

        authService.AddAuthHeaders(request);

        Assert.IsTrue(request.Headers.TryGetValues("X-MBX-APIKEY", out var values));
        Assert.AreEqual("test-api-key", values.Single());
    }

    private static IUserSettingService CreateUserSettingService(string apiKey, string secretKey)
    {
        return new FakeUserSettingService(new UserSetting
        {
            BinanceApiKey = apiKey,
            BinanceSecretKey = secretKey
        });
    }

    private sealed class FakeUserSettingService : IUserSettingService
    {
        public FakeUserSettingService(UserSetting setting)
        {
            CurrentSetting = setting;
        }

        public UserSetting CurrentSetting { get; private set; }

        public void LoadSettings()
        {
        }

        public void SaveSettings()
        {
        }

        public void UpdateSettings(UserSetting setting)
        {
            CurrentSetting = setting;
        }

        public void ResetSettings()
        {
            CurrentSetting = new UserSetting();
        }
    }
}
