using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
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
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        _service = new GitHubReleaseService(httpClientFactoryMock.Object, memoryCache, _loggerMock.Object);
    }

    [TestMethod]
    [TestCategory("Unit")]
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
    [TestCategory("Unit")]
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
    [TestCategory("Unit")]
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
    [TestCategory("Unit")]
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
    [TestCategory("Unit")]
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
        await Assert.ThrowsExactlyAsync<FriendlyException>(
            async () => await _service.CheckForUpdateAsync("1.0.0"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CheckForUpdateAsync_RateLimitExceeded_ThrowsException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.Forbidden, new List<ReleaseInfo>());

        // Act & Assert
        await Assert.ThrowsExactlyAsync<FriendlyException>(
            async () => await _service.CheckForUpdateAsync("1.0.0"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CheckForUpdateAsync_EmptyVersion_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<FriendlyException>(
            async () => await _service.CheckForUpdateAsync(""));
    }

    [TestMethod]
    [TestCategory("Unit")]
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
    [TestCategory("Unit")]
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

        var progress = new SynchronousProgress<double>(progressReports.Add);

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

            // 10MB 文件应触发进度报告，且最终进度应为 100%
            Assert.IsTrue(progressReports.Count > 0, "下载 10MB 文件应触发进度回调");
            Assert.IsTrue(progressReports[progressReports.Count - 1] >= 1.0,
                $"最终进度应 >= 1.0，实际: {progressReports[progressReports.Count - 1]}");
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
    [TestCategory("Unit")]
    public async Task DownloadUpdateAsync_InvalidUrl_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsExactlyAsync<FriendlyException>(
            async () => await _service.DownloadUpdateAsync("", "somepath"));
    }

    [TestMethod]
    [TestCategory("Unit")]
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
        await Assert.ThrowsExactlyAsync<FriendlyException>(
            async () => await _service.DownloadUpdateAsync(
                "https://github.com/test/app.zip",
                Path.GetTempFileName()));
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

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CheckForUpdateAsync_BetaToStable_DetectsUpdate()
    {
        // 场景：当前是 beta 版本，GitHub 上发布了正式版
        var releases = new List<ReleaseInfo>
        {
            new ReleaseInfo
            {
                TagName = "v1.0.0",
                Name = "Release 1.0.0",
                PublishedAt = DateTime.UtcNow,
                Prerelease = false,
                Draft = false,
                Assets = new List<ReleaseAsset>
                {
                    new ReleaseAsset
                    {
                        Name = "MarketAssistant-Setup-1.0.0.exe",
                        DownloadUrl = "https://github.com/X2Agent/MarketAssistant/releases/download/v1.0.0/MarketAssistant-Setup-1.0.0.exe",
                        Size = 50_000_000
                    },
                    new ReleaseAsset
                    {
                        Name = "MarketAssistant-Windows-x64.zip",
                        DownloadUrl = "https://github.com/X2Agent/MarketAssistant/releases/download/v1.0.0/MarketAssistant-Windows-x64.zip",
                        Size = 45_000_000
                    }
                }
            }
        };

        SetupHttpResponse(HttpStatusCode.OK, releases);

        // 当前版本是 beta
        var result = await _service.CheckForUpdateAsync("1.0.0-beta1");

        Assert.IsTrue(result.HasNewVersion, "beta1 → stable 应检测到新版本");
        Assert.AreEqual("v1.0.0", result.LatestRelease!.TagName);

        // 验证资产选择：应优先选 .exe
        var asset = result.LatestRelease.Assets
            .FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(asset, "应找到 .exe 安装包");
        Assert.AreEqual("MarketAssistant-Setup-1.0.0.exe", asset!.Name);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CheckForUpdateAsync_MultipleReleases_PicksHighestVersion()
    {
        // 场景：多个 release，hotfix 发布时间更晚，但 beta 版本号更高
        var releases = new List<ReleaseInfo>
        {
            new ReleaseInfo
            {
                TagName = "v1.0.1",
                Name = "Hotfix 1.0.1",
                PublishedAt = DateTime.UtcNow,  // 最后发布
                Prerelease = false,
                Draft = false
            },
            new ReleaseInfo
            {
                TagName = "v1.1.0-beta1",
                Name = "Beta 1.1.0",
                PublishedAt = DateTime.UtcNow.AddDays(-1),  // 更早发布
                Prerelease = true,
                Draft = false
            },
            new ReleaseInfo
            {
                TagName = "v1.0.0",
                Name = "Release 1.0.0",
                PublishedAt = DateTime.UtcNow.AddDays(-7),
                Prerelease = false,
                Draft = false
            }
        };

        SetupHttpResponse(HttpStatusCode.OK, releases);

        // includePrerelease=true 时应取到 v1.1.0-beta1（版本号最高）
        var result = await _service.CheckForUpdateAsync("1.0.0", includePrerelease: true);
        Assert.IsTrue(result.HasNewVersion);
        Assert.AreEqual("v1.1.0-beta1", result.LatestRelease!.TagName,
            "应按版本号排序取最高，而非按发布时间");
    }

    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CheckForUpdateAsync_SameBetaVersion_NoUpdate()
    {
        // 场景：当前版本和最新版本相同
        var releases = new List<ReleaseInfo>
        {
            new ReleaseInfo
            {
                TagName = "v1.0.0-beta1",
                PublishedAt = DateTime.UtcNow,
                Prerelease = true,
                Draft = false
            }
        };

        SetupHttpResponse(HttpStatusCode.OK, releases);

        var result = await _service.CheckForUpdateAsync("1.0.0-beta1");
        Assert.IsFalse(result.HasNewVersion, "相同版本不应提示更新");
    }
}
