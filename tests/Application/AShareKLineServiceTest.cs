using System.Net;
using System.Text.Json;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.DataProviders;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace TestMarketAssistant.Application;

/// <summary>
/// AShareKLineService 单元测试（使用 Mock HttpMessageHandler，不依赖真实网络）
/// </summary>
[TestClass]
public class AShareKLineServiceTest
{
    private const string SampleKLineJson = """
    [
        {"t":"2025-06-20","o":"38.50","h":"39.20","l":"38.10","c":"39.00","v":"1234567","a":"48000000","pc":"38.40"},
        {"t":"2025-06-23","o":"39.10","h":"39.80","l":"38.90","c":"39.50","v":"987654","a":"39000000","pc":"39.00"},
        {"t":"2025-06-24","o":"39.60","h":"40.10","l":"39.30","c":"39.80","v":"1100000","a":"43800000","pc":"39.50"}
    ]
    """;

    private static IServiceProvider BuildServiceProvider(
        HttpStatusCode statusCode,
        string responseContent,
        Action<HttpRequestMessage>? requestInspector = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                requestInspector?.Invoke(request);
                return new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(responseContent)
                };
            });

        var mockUserSettingService = new Mock<IUserSettingService>();
        mockUserSettingService.Setup(s => s.CurrentSetting)
            .Returns(new UserSetting { ZhiTuApiToken = "test-token-123" });

        var services = new ServiceCollection();
        services.AddSingleton(mockUserSettingService.Object);
        services.AddLogging();

        services.AddHttpClient("ZhiTu", client =>
        {
            client.BaseAddress = new Uri("https://api.zhituapi.com");
        })
        .ConfigurePrimaryHttpMessageHandler(() => handlerMock.Object);

        services.AddSingleton<IKLineService, AShareKLineService>();

        return services.BuildServiceProvider();
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_Daily_ShouldParseCorrectly()
    {
        // Arrange
        var sp = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        var result = await service.GetKLineDataAsync("sz002475", KLineType.Daily, 100);

        // Assert
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(new DateTime(2025, 6, 20), result[0].Timestamp);
        Assert.AreEqual(38.50m, result[0].Open);
        Assert.AreEqual(39.20m, result[0].High);
        Assert.AreEqual(38.10m, result[0].Low);
        Assert.AreEqual(39.00m, result[0].Close);
        Assert.AreEqual(1234567m, result[0].Volume);
        Assert.AreEqual(48000000m, result[0].Amount);
        Assert.AreEqual(38.40m, result[0].PreClose);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_Daily_ShouldCalculateChangeCorrectly()
    {
        // Arrange
        var sp = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        var result = await service.GetKLineDataAsync("sz002475", KLineType.Daily, 100);

        // Assert - 第一条: close=39.00, preClose=38.40 → change=0.60, pctChg≈1.5625%
        Assert.AreEqual(0.60m, result[0].Change);
        Assert.AreEqual(0.60m / 38.40m * 100, result[0].PctChg);

        // 第二条: close=39.50, preClose=39.00 → change=0.50
        Assert.AreEqual(0.50m, result[1].Change);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_Daily_ShouldSortByTimestamp()
    {
        // Arrange - 故意乱序
        var json = """
        [
            {"t":"2025-06-24","o":"39.60","h":"40.10","l":"39.30","c":"39.80","v":"1100000","a":"43800000","pc":"39.50"},
            {"t":"2025-06-20","o":"38.50","h":"39.20","l":"38.10","c":"39.00","v":"1234567","a":"48000000","pc":"38.40"},
            {"t":"2025-06-23","o":"39.10","h":"39.80","l":"38.90","c":"39.50","v":"987654","a":"39000000","pc":"39.00"}
        ]
        """;
        var sp = BuildServiceProvider(HttpStatusCode.OK, json);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        var result = await service.GetKLineDataAsync("002475", KLineType.Daily, 100);

        // Assert - 应按时间升序
        Assert.AreEqual(new DateTime(2025, 6, 20), result[0].Timestamp);
        Assert.AreEqual(new DateTime(2025, 6, 23), result[1].Timestamp);
        Assert.AreEqual(new DateTime(2025, 6, 24), result[2].Timestamp);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_ShouldTruncateToCount()
    {
        // Arrange - 返回 3 条，请求 count=2，应取最后 2 条
        var sp = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        var result = await service.GetKLineDataAsync("sz002475", KLineType.Daily, 2);

        // Assert
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(new DateTime(2025, 6, 23), result[0].Timestamp);
        Assert.AreEqual(new DateTime(2025, 6, 24), result[1].Timestamp);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_ShouldUseRelativeUrl_WithCorrectPath()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var sp = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson, req => capturedRequest = req);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        await service.GetKLineDataAsync("sz002475", KLineType.Daily, 100);

        // Assert - 验证请求 URL 格式正确
        Assert.IsNotNull(capturedRequest);
        var uri = capturedRequest!.RequestUri!;
        Assert.IsTrue(uri.IsAbsoluteUri);
        Assert.AreEqual("api.zhituapi.com", uri.Host);
        Assert.IsTrue(uri.AbsolutePath.StartsWith("/hs/history/002475.SZ/d/n"));
        Assert.IsTrue(uri.Query.Contains("token=test-token-123"));
        Assert.IsTrue(uri.Query.Contains("st="));
        Assert.IsTrue(uri.Query.Contains("et="));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("sz002475", "002475.SZ")]
    [DataRow("SH600519", "600519.SH")]
    [DataRow("688001", "688001.SH")]
    [DataRow("300750", "300750.SZ")]
    [DataRow("600519.SH", "600519.SH")]
    public async Task GetKLineDataAsync_ShouldConvertSymbolCorrectly(string inputCode, string expectedInPath)
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var sp = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson, req => capturedRequest = req);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        await service.GetKLineDataAsync(inputCode, KLineType.Daily, 100);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var path = capturedRequest!.RequestUri!.AbsolutePath;
        Assert.IsTrue(path.Contains(expectedInPath), $"URL路径 '{path}' 应包含 '{expectedInPath}'");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_ApiError400_ShouldThrowFriendlyExceptionWithBody()
    {
        // Arrange
        var sp = BuildServiceProvider(HttpStatusCode.BadRequest, "{\"error\":\"invalid token\"}");
        var service = sp.GetRequiredService<IKLineService>();

        // Act & Assert
        var ex = await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => service.GetKLineDataAsync("sz002475", KLineType.Daily, 100));
        Assert.IsTrue(ex.Message.Contains("400"), "异常消息应包含状态码");
        Assert.IsTrue(ex.Message.Contains("invalid token"), "异常消息应包含API返回的错误内容");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_ApiError400_EmptyBody_ShouldThrowFriendlyException()
    {
        // Arrange
        var sp = BuildServiceProvider(HttpStatusCode.BadRequest, "");
        var service = sp.GetRequiredService<IKLineService>();

        // Act & Assert
        var ex = await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => service.GetKLineDataAsync("sz002475", KLineType.Daily, 100));
        Assert.IsTrue(ex.Message.Contains("400"));
        Assert.IsTrue(ex.Message.Contains("请稍后重试"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_EmptyResponse_ShouldThrowFriendlyException()
    {
        // Arrange
        var sp = BuildServiceProvider(HttpStatusCode.OK, "[]");
        var service = sp.GetRequiredService<IKLineService>();

        // Act & Assert
        await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => service.GetKLineDataAsync("sz002475", KLineType.Daily, 100));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_EmptyCode_ShouldThrowFriendlyException()
    {
        // Arrange
        var sp = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        // Act & Assert
        await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => service.GetKLineDataAsync("", KLineType.Daily, 100));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(KLineType.Daily, "/d/")]
    [DataRow(KLineType.Weekly, "/w/")]
    [DataRow(KLineType.Monthly, "/m/")]
    [DataRow(KLineType.Minute5, "/5/")]
    [DataRow(KLineType.Minute15, "/15/")]
    public async Task GetKLineDataAsync_ShouldMapIntervalCorrectly(KLineType kLineType, string expectedInterval)
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var sp = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson, req => capturedRequest = req);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        await service.GetKLineDataAsync("sz002475", kLineType, 100);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var path = capturedRequest!.RequestUri!.AbsolutePath;
        Assert.IsTrue(path.Contains(expectedInterval), $"URL路径 '{path}' 应包含 '{expectedInterval}'");
    }
}
