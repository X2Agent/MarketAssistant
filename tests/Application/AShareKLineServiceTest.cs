using System.Net;
using System.Text.Json;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace TestMarketAssistant.Application;

/// <summary>
/// AShareKLineService 单元测试（使用 Mock HttpMessageHandler，不依赖真实网络）。
/// 覆盖智兔优先、新浪兜底（智兔不覆盖基金/ETF）与双源失败的友好报错。
/// </summary>
[TestClass]
public sealed class AShareKLineServiceTest
{
    private const string SampleKLineJson = """
    [
        {"t":"2025-06-20","o":"38.50","h":"39.20","l":"38.10","c":"39.00","v":"1234567","a":"48000000","pc":"38.40"},
        {"t":"2025-06-23","o":"39.10","h":"39.80","l":"38.90","c":"39.50","v":"987654","a":"39000000","pc":"39.00"},
        {"t":"2025-06-24","o":"39.60","h":"40.10","l":"39.30","c":"39.80","v":"1100000","a":"43800000","pc":"39.50"}
    ]
    """;

    private const string SinaKLineJson = """
    [
        {"day":"2025-06-20","open":"38.50","high":"39.20","low":"38.10","close":"39.00","volume":"1234567"},
        {"day":"2025-06-23","open":"39.10","high":"39.80","low":"38.90","close":"39.50","volume":"987654"},
        {"day":"2025-06-24","open":"39.60","high":"40.10","low":"39.30","close":"39.80","volume":"1100000"}
    ]
    """;

    private static (IServiceProvider, List<HttpRequestMessage>) BuildServiceProvider(
        HttpStatusCode zhiTuStatusCode,
        string zhiTuResponseContent,
        HttpStatusCode sinaStatusCode = HttpStatusCode.OK,
        string sinaResponseContent = "null",
        Action<HttpRequestMessage>? zhiTuRequestInspector = null)
    {
        var zhiTuRequests = new List<HttpRequestMessage>();
        var sinaRequests = new List<HttpRequestMessage>();

        var zhiTuHandlerMock = new Mock<HttpMessageHandler>();
        zhiTuHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                zhiTuRequests.Add(request);
                zhiTuRequestInspector?.Invoke(request);
                return new HttpResponseMessage(zhiTuStatusCode)
                {
                    Content = new StringContent(zhiTuResponseContent)
                };
            });

        var sinaHandlerMock = new Mock<HttpMessageHandler>();
        sinaHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                sinaRequests.Add(request);
                return new HttpResponseMessage(sinaStatusCode)
                {
                    Content = new StringContent(sinaResponseContent)
                };
            });

        var mockUserSettingService = new Mock<IUserSettingService>();
        mockUserSettingService.Setup(s => s.CurrentSetting)
            .Returns(new UserSetting { ZhiTuApiToken = "test-token-123" });

        var services = new ServiceCollection();
        services.AddSingleton(mockUserSettingService.Object);
        services.AddLogging();
        services.AddAShareDataProviders();

        services.AddHttpClient("ZhiTu", client =>
        {
            client.BaseAddress = new Uri("https://api.zhituapi.com");
        })
        .ConfigurePrimaryHttpMessageHandler(() => zhiTuHandlerMock.Object);

        services.AddHttpClient("SinaKLine", client =>
        {
            client.BaseAddress = new Uri("https://quotes.sina.cn");
        })
        .ConfigurePrimaryHttpMessageHandler(() => sinaHandlerMock.Object);

        services.AddSingleton<IKLineService, AShareKLineService>();

        return (services.BuildServiceProvider(), sinaRequests);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_Daily_ShouldParseCorrectly()
    {
        // Arrange
        var (sp, _) = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        var result = await service.GetKLineDataAsync("sz002475", KLineType.Daily, 100);

        // Assert
        Assert.HasCount(3, result);
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
        var (sp, _) = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson);
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
        var (sp, _) = BuildServiceProvider(HttpStatusCode.OK, json);
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
        var (sp, _) = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        var result = await service.GetKLineDataAsync("sz002475", KLineType.Daily, 2);

        // Assert
        Assert.HasCount(2, result);
        Assert.AreEqual(new DateTime(2025, 6, 23), result[0].Timestamp);
        Assert.AreEqual(new DateTime(2025, 6, 24), result[1].Timestamp);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_ShouldUseRelativeUrl_WithCorrectPath()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var (sp, _) = BuildServiceProvider(
            HttpStatusCode.OK,
            SampleKLineJson,
            zhiTuRequestInspector: request => capturedRequest = request);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        await service.GetKLineDataAsync("sz002475", KLineType.Daily, 100);

        // Assert - 验证请求 URL 格式正确（智兔优先，未触发新浪兜底）
        Assert.IsNotNull(capturedRequest);
        var uri = capturedRequest!.RequestUri!;
        Assert.IsTrue(uri.IsAbsoluteUri);
        Assert.AreEqual("api.zhituapi.com", uri.Host);
        Assert.StartsWith("/hs/history/002475.SZ/d/n", uri.AbsolutePath);
        Assert.Contains("token=test-token-123", uri.Query);
        Assert.Contains("st=", uri.Query);
        Assert.Contains("et=", uri.Query);
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
        var (sp, _) = BuildServiceProvider(
            HttpStatusCode.OK,
            SampleKLineJson,
            zhiTuRequestInspector: request => capturedRequest = request);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        await service.GetKLineDataAsync(inputCode, KLineType.Daily, 100);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var path = capturedRequest!.RequestUri!.AbsolutePath;
        Assert.Contains(expectedInPath, path, $"URL路径 '{path}' 应包含 '{expectedInPath}'");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_ZhiTuSuccess_ShouldNotCallSinaFallback()
    {
        // Arrange
        var (sp, sinaRequests) = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        var result = await service.GetKLineDataAsync("sz002475", KLineType.Daily, 100);

        // Assert - 智兔有数据时不应触发新浪兜底
        Assert.HasCount(3, result);
        Assert.IsEmpty(sinaRequests, "智兔成功时不得请求新浪");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_ZhiTuNotFound_ShouldFallBackToSina()
    {
        // 复现线上问题：智兔对基金/ETF 返回 404 {"error":"not found"}，需回退新浪 K 线
        var (sp, sinaRequests) = BuildServiceProvider(
            HttpStatusCode.NotFound,
            "{\"error\":\"not found\"}",
            sinaResponseContent: SinaKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        var result = await service.GetKLineDataAsync("sh511010", KLineType.Daily, 100);

        Assert.HasCount(3, result);
        Assert.AreEqual(new DateTime(2025, 6, 24), result[2].Timestamp);
        Assert.AreEqual(39.80m, result[2].Close);
        Assert.HasCount(1, sinaRequests, "应触发一次新浪兜底请求");
        Assert.Contains("symbol=sh511010", sinaRequests[0].RequestUri!.Query);
        Assert.Contains("scale=240", sinaRequests[0].RequestUri!.Query);
        Assert.Contains("datalen=100", sinaRequests[0].RequestUri!.Query);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_SinaFallback_ShouldDerivePreCloseFromPreviousBar()
    {
        // 新浪不提供昨收：用前一根收盘价派生，保证涨跌额/涨跌幅与主图口径一致
        var (sp, _) = BuildServiceProvider(
            HttpStatusCode.NotFound,
            "{\"error\":\"not found\"}",
            sinaResponseContent: SinaKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        var result = await service.GetKLineDataAsync("sh511010", KLineType.Daily, 100);

        Assert.AreEqual(0m, result[0].PreClose, "首根无前值，昨收记 0");
        Assert.AreEqual(0m, result[0].Change);
        Assert.AreEqual(39.00m, result[1].PreClose);
        Assert.AreEqual(0.50m, result[1].Change);
        Assert.AreEqual(39.50m, result[2].PreClose);
        Assert.AreEqual(0.30m, result[2].Change);
        Assert.AreEqual(0m, result[0].Amount, "日线兜底无成交额，记 0 而非伪造");
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(KLineType.Weekly, "scale=1200")]
    [DataRow(KLineType.Monthly, "scale=7200")]
    [DataRow(KLineType.Minute5, "scale=5")]
    [DataRow(KLineType.Minute15, "scale=15")]
    public async Task GetKLineDataAsync_ShouldMapSinaScalePerKLineType(KLineType kLineType, string expectedScaleQuery)
    {
        var (sp, sinaRequests) = BuildServiceProvider(
            HttpStatusCode.NotFound,
            "{\"error\":\"not found\"}",
            sinaResponseContent: SinaKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        await service.GetKLineDataAsync("sh511010", kLineType, 100);

        Assert.HasCount(1, sinaRequests);
        Assert.Contains(expectedScaleQuery, sinaRequests[0].RequestUri!.Query);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_ZhiTuApiError_BothSourcesEmpty_ShouldThrowFriendlyException()
    {
        // 智兔报错且新浪也无数据时，抛面向用户的友好异常（原文进日志）
        var (sp, _) = BuildServiceProvider(
            HttpStatusCode.BadRequest,
            "{\"error\":\"invalid token\"}",
            sinaResponseContent: "null");
        var service = sp.GetRequiredService<IKLineService>();

        var ex = await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => service.GetKLineDataAsync("sz002475", KLineType.Daily, 100));
        Assert.Contains("数据源暂不可用", ex.Message);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_ZhiTuApiError_EmptySinaBody_ShouldThrowFriendlyException()
    {
        var (sp, _) = BuildServiceProvider(
            HttpStatusCode.BadRequest,
            "",
            sinaResponseContent: "null");
        var service = sp.GetRequiredService<IKLineService>();

        var ex = await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => service.GetKLineDataAsync("sz002475", KLineType.Daily, 100));
        Assert.Contains("数据源暂不可用", ex.Message);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_ZhiTuEmptyArray_ShouldFallBackToSina()
    {
        // 智兔返回空数组同样触发兜底
        var (sp, sinaRequests) = BuildServiceProvider(
            HttpStatusCode.OK,
            "[]",
            sinaResponseContent: SinaKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

        var result = await service.GetKLineDataAsync("sh511010", KLineType.Daily, 100);

        Assert.HasCount(3, result);
        Assert.HasCount(1, sinaRequests);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetKLineDataAsync_EmptyCode_ShouldThrowFriendlyException()
    {
        var (sp, _) = BuildServiceProvider(HttpStatusCode.OK, SampleKLineJson);
        var service = sp.GetRequiredService<IKLineService>();

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
    public async Task GetKLineDataAsync_ShouldMapZhiTuIntervalCorrectly(KLineType kLineType, string expectedInterval)
    {
        // Arrange - 智兔优先路径的周期映射
        HttpRequestMessage? capturedRequest = null;
        var (sp, _) = BuildServiceProvider(
            HttpStatusCode.OK,
            SampleKLineJson,
            zhiTuRequestInspector: request => capturedRequest = request);
        var service = sp.GetRequiredService<IKLineService>();

        // Act
        await service.GetKLineDataAsync("sz002475", kLineType, 100);

        // Assert
        Assert.IsNotNull(capturedRequest);
        var path = capturedRequest!.RequestUri!.AbsolutePath;
        Assert.Contains(expectedInterval, path, $"URL路径 '{path}' 应包含 '{expectedInterval}'");
    }
}
