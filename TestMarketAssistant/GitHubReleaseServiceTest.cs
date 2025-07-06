using MarketAssistant.Infrastructure;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TestMarketAssistant;

[TestClass]
public class GitHubReleaseServiceTest
{
    private GitHubReleaseService _gitHubReleaseService;
    private Mock<IHttpClientFactory> _httpClientFactoryMock;
    private Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private HttpClient _httpClient;

    [TestInitialize]
    public void Initialize()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        _gitHubReleaseService = new GitHubReleaseService(_httpClientFactoryMock.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _httpClient?.Dispose();
    }

    [TestMethod]
    public async Task GetLatestReleaseAsync_WithValidResponse_ReturnsReleaseInfo()
    {
        // Arrange
        var releaseInfo = new ReleaseInfo
        {
            TagName = "v1.0.0",
            Name = "Release 1.0.0",
            Body = "Release notes",
            HtmlUrl = "https://github.com/owner/MarketAssistant/releases/tag/v1.0.0",
            PublishedAt = DateTime.UtcNow,
            Assets = new List<ReleaseAsset>
            {
                new ReleaseAsset
                {
                    Name = "MarketAssistant-1.0.0.zip",
                    DownloadUrl = "https://github.com/owner/MarketAssistant/releases/download/v1.0.0/MarketAssistant-1.0.0.zip",
                    Size = 1024,
                    ContentType = "application/zip"
                }
            }
        };

        var jsonResponse = JsonSerializer.Serialize(releaseInfo);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _gitHubReleaseService.GetLatestReleaseAsync();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("v1.0.0", result.TagName);
        Assert.AreEqual("Release 1.0.0", result.Name);
        Assert.AreEqual("Release notes", result.Body);
        Assert.AreEqual("https://github.com/owner/MarketAssistant/releases/tag/v1.0.0", result.HtmlUrl);
        Assert.AreEqual(1, result.Assets.Count);
        Assert.AreEqual("MarketAssistant-1.0.0.zip", result.Assets[0].Name);
    }

    [TestMethod]
    public async Task GetLatestReleaseAsync_WithHttpException_ReturnsNull()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _gitHubReleaseService.GetLatestReleaseAsync();

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetLatestReleaseAsync_WithInvalidJson_ReturnsNull()
    {
        // Arrange
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("invalid json", Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _gitHubReleaseService.GetLatestReleaseAsync();

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_WithNewerVersion_ReturnsTrue()
    {
        // Arrange
        var releaseInfo = new ReleaseInfo
        {
            TagName = "v2.0.0",
            Name = "Release 2.0.0",
            Body = "New release",
            HtmlUrl = "https://github.com/owner/MarketAssistant/releases/tag/v2.0.0",
            PublishedAt = DateTime.UtcNow,
            Assets = new List<ReleaseAsset>()
        };

        var jsonResponse = JsonSerializer.Serialize(releaseInfo);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _gitHubReleaseService.CheckForUpdateAsync("1.0.0");

        // Assert
        Assert.IsTrue(result.HasNewVersion);
        Assert.IsNotNull(result.ReleaseInfo);
        Assert.AreEqual("v2.0.0", result.ReleaseInfo.TagName);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_WithSameVersion_ReturnsFalse()
    {
        // Arrange
        var releaseInfo = new ReleaseInfo
        {
            TagName = "v1.0.0",
            Name = "Release 1.0.0",
            Body = "Current release",
            HtmlUrl = "https://github.com/owner/MarketAssistant/releases/tag/v1.0.0",
            PublishedAt = DateTime.UtcNow,
            Assets = new List<ReleaseAsset>()
        };

        var jsonResponse = JsonSerializer.Serialize(releaseInfo);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _gitHubReleaseService.CheckForUpdateAsync("1.0.0");

        // Assert
        Assert.IsFalse(result.HasNewVersion);
        Assert.IsNotNull(result.ReleaseInfo);
        Assert.AreEqual("v1.0.0", result.ReleaseInfo.TagName);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_WithOlderVersion_ReturnsFalse()
    {
        // Arrange
        var releaseInfo = new ReleaseInfo
        {
            TagName = "v1.0.0",
            Name = "Release 1.0.0",
            Body = "Old release",
            HtmlUrl = "https://github.com/owner/MarketAssistant/releases/tag/v1.0.0",
            PublishedAt = DateTime.UtcNow,
            Assets = new List<ReleaseAsset>()
        };

        var jsonResponse = JsonSerializer.Serialize(releaseInfo);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _gitHubReleaseService.CheckForUpdateAsync("2.0.0");

        // Assert
        Assert.IsFalse(result.HasNewVersion);
        Assert.IsNotNull(result.ReleaseInfo);
        Assert.AreEqual("v1.0.0", result.ReleaseInfo.TagName);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_WithVersionPrefix_HandlesCorrectly()
    {
        // Arrange
        var releaseInfo = new ReleaseInfo
        {
            TagName = "v2.0.0",
            Name = "Release 2.0.0",
            Body = "New release",
            HtmlUrl = "https://github.com/owner/MarketAssistant/releases/tag/v2.0.0",
            PublishedAt = DateTime.UtcNow,
            Assets = new List<ReleaseAsset>()
        };

        var jsonResponse = JsonSerializer.Serialize(releaseInfo);
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _gitHubReleaseService.CheckForUpdateAsync("v1.0.0");

        // Assert
        Assert.IsTrue(result.HasNewVersion);
        Assert.IsNotNull(result.ReleaseInfo);
        Assert.AreEqual("v2.0.0", result.ReleaseInfo.TagName);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_WithNullReleaseInfo_ReturnsFalse()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _gitHubReleaseService.CheckForUpdateAsync("1.0.0");

        // Assert
        Assert.IsFalse(result.HasNewVersion);
        Assert.IsNull(result.ReleaseInfo);
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_WithValidUrl_ReturnsTrue()
    {
        // Arrange
        var downloadUrl = "https://github.com/owner/MarketAssistant/releases/download/v1.0.0/MarketAssistant-1.0.0.zip";
        var savePath = Path.GetTempFileName();
        var fileContent = "test file content";

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fileContent)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        try
        {
            // Act
            var result = await _gitHubReleaseService.DownloadUpdateAsync(downloadUrl, savePath);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(File.Exists(savePath));
            var savedContent = await File.ReadAllTextAsync(savePath);
            Assert.AreEqual(fileContent, savedContent);
        }
        finally
        {
            // Cleanup
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }
        }
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_WithHttpException_ReturnsFalse()
    {
        // Arrange
        var downloadUrl = "https://github.com/owner/MarketAssistant/releases/download/v1.0.0/MarketAssistant-1.0.0.zip";
        var savePath = Path.GetTempFileName();

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Download failed"));

        try
        {
            // Act
            var result = await _gitHubReleaseService.DownloadUpdateAsync(downloadUrl, savePath);

            // Assert
            Assert.IsFalse(result);
        }
        finally
        {
            // Cleanup
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }
        }
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_WithInvalidPath_ReturnsFalse()
    {
        // Arrange
        var downloadUrl = "https://github.com/owner/MarketAssistant/releases/download/v1.0.0/MarketAssistant-1.0.0.zip";
        var savePath = "C:\\invalid\\path\\file.zip"; // 无效路径

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("test content")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        // Act
        var result = await _gitHubReleaseService.DownloadUpdateAsync(downloadUrl, savePath);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task DownloadUpdateAsync_WithHttpErrorStatus_ReturnsFalse()
    {
        // Arrange
        var downloadUrl = "https://github.com/owner/MarketAssistant/releases/download/v1.0.0/MarketAssistant-1.0.0.zip";
        var savePath = Path.GetTempFileName();

        var httpResponse = new HttpResponseMessage(HttpStatusCode.NotFound);

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(httpResponse);

        try
        {
            // Act
            var result = await _gitHubReleaseService.DownloadUpdateAsync(downloadUrl, savePath);

            // Assert
            Assert.IsFalse(result);
        }
        finally
        {
            // Cleanup
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }
        }
    }
}