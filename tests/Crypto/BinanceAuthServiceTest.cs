using MarketAssistant.Applications.Crypto;
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
    public void TestHmacSignature_WithBinanceExample()
    {
        // Arrange - 使用币安文档中的示例密钥（仅用于测试）
        var config = new BinanceAuthConfig
        {
            ApiKey = "vmPUZE6mv9SD5VNHk4HlWFsOr6aKE2zvsw0MuIgwCIPy6utIco14y7Ju91duEh8A",
            SecretKey = "NhqPtmdSJYdKjVHjA7PZj4Mge3R5YNiP1e3UZjInClVN65XAbvqqM6A7H5fATj0j"
        };

        var authService = new BinanceAuthService(_logger, config);

        // Act - 构建币安文档中的示例payload（不包含timestamp，手动测试签名算法）
        // 文档示例：symbol=LTCBTC&side=BUY&type=LIMIT&timeInForce=GTC&quantity=1&price=0.1&recvWindow=5000&timestamp=1499827319559
        var testPayload = "symbol=LTCBTC&side=BUY&type=LIMIT&timeInForce=GTC&quantity=1&price=0.1&recvWindow=5000&timestamp=1499827319559";

        // 使用反射调用私有方法进行测试（仅用于单元测试）
        var generateSignatureMethod = typeof(BinanceAuthService)
            .GetMethod("GenerateSignature", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var signature = generateSignatureMethod?.Invoke(authService, new object[] { testPayload }) as string;

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
    public void TestSignQueryString_ShouldAddTimestampAndSignature()
    {
        // Arrange
        var config = new BinanceAuthConfig
        {
            ApiKey = "test-api-key",
            SecretKey = "test-secret-key"
        };

        var authService = new BinanceAuthService(_logger, config);

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
    public void TestConfigValidation_RequiresSecretKey()
    {
        // Arrange
        var config = new BinanceAuthConfig
        {
            ApiKey = "test-api-key",
            SecretKey = "" // 缺失SecretKey
        };

        // Act & Assert
        var exception = Assert.ThrowsExactly<ArgumentException>(() => new BinanceAuthService(_logger, config));
        Console.WriteLine($"预期异常: {exception.Message}");
        Assert.IsTrue(exception.Message.Contains("SecretKey"));
    }

    /// <summary>
    /// 测试配置验证 - RecvWindow范围
    /// </summary>
    [TestMethod]
    public void TestConfigValidation_RecvWindowOutOfRange()
    {
        // Arrange - 测试负数
        var config1 = new BinanceAuthConfig
        {
            ApiKey = "test-api-key",
            SecretKey = "test-secret-key",
            RecvWindow = -1
        };

        // Act & Assert
        var exception1 = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BinanceAuthService(_logger, config1));
        Console.WriteLine($"RecvWindow=-1, 预期异常: {exception1.Message}");

        // Arrange - 测试超出最大值
        var config2 = new BinanceAuthConfig
        {
            ApiKey = "test-api-key",
            SecretKey = "test-secret-key",
            RecvWindow = 60001
        };

        // Act & Assert
        var exception2 = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BinanceAuthService(_logger, config2));
        Console.WriteLine($"RecvWindow=60001, 预期异常: {exception2.Message}");
    }
}
