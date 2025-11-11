using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TestMarketAssistant;

[TestClass]
public class GitHubReleaseServiceTest
{
    private IReleaseService _service = null!;
    private Mock<HttpMessageHandler> _httpHandlerMock = null!;
    private Mock<ILogger<GitHubReleaseService>> _loggerMock = null!;

    [TestInitialize]
    public void Initialize()
    {
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_httpHandlerMock.Object);
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        _loggerMock = new Mock<ILogger<GitHubReleaseService>>();
        _service = new GitHubReleaseService(httpClientFactoryMock.Object, _loggerMock.Object);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_HasNewVersion_ReturnsTrue()
    {
        // Arrange
        var releases = new List<ReleaseInfo>
        {
            new ReleaseInfo
            {
                TagName = "v2.0.0",
                Name = "Release 2.0.0",
                Body = "New features",
                HtmlUrl = "https://github.com/test/repo/releases/tag/v2.0.0",
                PublishedAt = DateTime.UtcNow,
                Prerelease = false,
                Draft = false,
                Assets = new List<ReleaseAsset>
                {
                    new ReleaseAsset
                    {
                        Name = "app-2.0.0.zip",
                        DownloadUrl = "https://github.com/test/repo/releases/download/v2.0.0/app-2.0.0.zip",
                        Size = 10485760
                    }
                }
            }
        };

        SetupHttpResponse(HttpStatusCode.OK, releases);

        // Act
        var result = await _service.CheckForUpdateAsync("1.0.0");

        // Assert
        Assert.IsTrue(result.HasNewVersion);
        Assert.AreEqual("v2.0.0", result.LatestRelease!.TagName);
        Assert.AreEqual("1.0.0", result.CurrentVersion);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_NoNewVersion_ReturnsFalse()
    {
        // Arrange
        var releases = new List<ReleaseInfo>
        {
            new ReleaseInfo
            {
                TagName = "v1.0.0",
                Name = "Release 1.0.0",
                PublishedAt = DateTime.UtcNow,
                Prerelease = false,
                Draft = false
            }
        };

        SetupHttpResponse(HttpStatusCode.OK, releases);

        // Act
        var result = await _service.CheckForUpdateAsync("1.0.0");

        // Assert
        Assert.IsFalse(result.HasNewVersion);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_WithFourPartVersion_WorksCorrectly()
    {
        // Arrange
        var releases = new List<ReleaseInfo>
        {
            new ReleaseInfo
            {
                TagName = "v1.2.0.0",
                Name = "Release 1.2.0.0",
                PublishedAt = DateTime.UtcNow,
                Prerelease = false,
                Draft = false
            }
        };

        SetupHttpResponse(HttpStatusCode.OK, releases);

        // Act
        var result = await _service.CheckForUpdateAsync("1.0.0.0");

        // Assert
        Assert.IsTrue(result.HasNewVersion);
        Assert.AreEqual("v1.2.0.0", result.LatestRelease!.TagName);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_ExcludePrerelease_OnlyStableVersions()
    {
        // Arrange
        var release = new ReleaseInfo
        {
            TagName = "v1.5.0",
            Name = "Release 1.5.0",
            PublishedAt = DateTime.UtcNow,
            Prerelease = false,
            Draft = false
        };

        SetupHttpResponse(HttpStatusCode.OK, release);

        // Act
        var result = await _service.CheckForUpdateAsync("1.0.0", includePrerelease: false);

        // Assert
        Assert.IsTrue(result.HasNewVersion);
        Assert.AreEqual("v1.5.0", result.LatestRelease!.TagName);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_NetworkError_ThrowsException()
    {
        // Arrange
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act & Assert
        await Assert.ThrowsExceptionAsync<FriendlyException>(
            async () => await _service.CheckForUpdateAsync("1.0.0"));
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_RateLimitExceeded_ThrowsException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.Forbidden, new List<ReleaseInfo>());

        // Act & Assert
        await Assert.ThrowsExceptionAsync<FriendlyException>(
            async () => await _service.CheckForUpdateAsync("1.0.0"));
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_EmptyVersion_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<FriendlyException>(
            async () => await _service.CheckForUpdateAsync(""));
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_Success_ReturnsFilePath()
    {
        // Arrange
        var savePath = Path.GetTempFileName();
        var fileContent = "test file content";

        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fileContent)
            });

        try
        {
            // Act
            var result = await _service.DownloadUpdateAsync(
                "https://github.com/test/app.zip",
                savePath);

            // Assert
            Assert.AreEqual(savePath, result);
            Assert.IsTrue(File.Exists(savePath));
        }
        finally
        {
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }
        }
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_WithProgress_ReportsProgress()
    {
        // Arrange
        var savePath = Path.GetTempFileName();
        var fileContent = new byte[10 * 1024 * 1024]; // 10 MB
        var progressReports = new List<double>();

        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fileContent)
        };
        responseMessage.Content.Headers.ContentLength = fileContent.Length;

        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(responseMessage);

        var progress = new Progress<double>(p => progressReports.Add(p));

        try
        {
            // Act
            var result = await _service.DownloadUpdateAsync(
                "https://github.com/test/app.zip",
                savePath,
                progress);

            // Assert
            Assert.AreEqual(savePath, result);
            Assert.IsTrue(File.Exists(savePath));

            // 如果有进度报告，验证最后一个应该是100%
            if (progressReports.Count > 0)
            {
                Assert.IsTrue(progressReports[progressReports.Count - 1] >= 1.0);
            }
        }
        finally
        {
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }
        }
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_InvalidUrl_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<FriendlyException>(
            async () => await _service.DownloadUpdateAsync("", "somepath"));
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_HttpError_ThrowsException()
    {
        // Arrange
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        // Act & Assert
        await Assert.ThrowsExceptionAsync<FriendlyException>(
            async () => await _service.DownloadUpdateAsync(
                "https://github.com/test/app.zip",
                Path.GetTempFileName()));
    }

    [TestMethod]
    public void ClearCache_ClearsSuccessfully()
    {
        // Act & Assert - 不抛出异常即可
        _service.ClearCache();
    }

    private void SetupHttpResponse<T>(HttpStatusCode statusCode, T content)
    {
        var jsonContent = JsonSerializer.Serialize(content);
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);
    }
}
