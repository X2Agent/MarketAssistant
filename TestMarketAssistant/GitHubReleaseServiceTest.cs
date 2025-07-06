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
    private Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private HttpClient _httpClient;

    [TestInitialize]
    public void Initialize()
    {
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        _gitHubReleaseService = new GitHubReleaseService(httpClientFactoryMock.Object);
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
            HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v1.0.0",
            PublishedAt = DateTime.UtcNow,
            Prerelease = false,
            Draft = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            Assets = new List<ReleaseAsset>
            {
                new ReleaseAsset
                {
                    Name = "MarketAssistant-1.0.0.zip",
                    DownloadUrl = "https://github.com/X2Agent/MarketAssistant/releases/download/v1.0.0/MarketAssistant-1.0.0.zip",
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
        Assert.AreEqual("https://github.com/X2Agent/MarketAssistant/releases/tag/v1.0.0", result.HtmlUrl);
        Assert.IsFalse(result.Prerelease);
        Assert.IsFalse(result.Draft);
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
    public async Task GetAllReleasesAsync_WithValidResponse_ReturnsReleaseList()
    {
        // Arrange
        var releases = new List<ReleaseInfo>
        {
            new ReleaseInfo
            {
                TagName = "v2.0.0",
                Name = "Release 2.0.0",
                Body = "Latest stable release",
                HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v2.0.0",
                PublishedAt = DateTime.UtcNow,
                Prerelease = false,
                Draft = false,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                Assets = new List<ReleaseAsset>()
            },
            new ReleaseInfo
            {
                TagName = "v2.0.0-beta.1",
                Name = "Release 2.0.0 Beta 1",
                Body = "Beta release",
                HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v2.0.0-beta.1",
                PublishedAt = DateTime.UtcNow.AddDays(-5),
                Prerelease = true,
                Draft = false,
                CreatedAt = DateTime.UtcNow.AddDays(-6),
                Assets = new List<ReleaseAsset>()
            },
            new ReleaseInfo
            {
                TagName = "v1.0.0",
                Name = "Release 1.0.0",
                Body = "First stable release",
                HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v1.0.0",
                PublishedAt = DateTime.UtcNow.AddDays(-10),
                Prerelease = false,
                Draft = false,
                CreatedAt = DateTime.UtcNow.AddDays(-11),
                Assets = new List<ReleaseAsset>()
            }
        };

        var jsonResponse = JsonSerializer.Serialize(releases);
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
        var result = await _gitHubReleaseService.GetAllReleasesAsync();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("v2.0.0", result[0].TagName);
        Assert.AreEqual("v2.0.0-beta.1", result[1].TagName);
        Assert.AreEqual("v1.0.0", result[2].TagName);
        Assert.IsFalse(result[0].Prerelease);
        Assert.IsTrue(result[1].Prerelease);
        Assert.IsFalse(result[2].Prerelease);
    }

    [TestMethod]
    public async Task GetAllReleasesAsync_WithHttpException_ReturnsNull()
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
        var result = await _gitHubReleaseService.GetAllReleasesAsync();

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetStableReleasesAsync_WithMixedReleases_ReturnsOnlyStableReleases()
    {
        // Arrange
        var releases = new List<ReleaseInfo>
        {
            new ReleaseInfo
            {
                TagName = "v2.0.0",
                Name = "Release 2.0.0",
                Prerelease = false,
                Draft = false,
                PublishedAt = DateTime.UtcNow
            },
            new ReleaseInfo
            {
                TagName = "v2.0.0-beta.1",
                Name = "Release 2.0.0 Beta 1",
                Prerelease = true,
                Draft = false,
                PublishedAt = DateTime.UtcNow.AddDays(-5)
            },
            new ReleaseInfo
            {
                TagName = "v1.9.0-draft",
                Name = "Draft Release",
                Prerelease = false,
                Draft = true,
                PublishedAt = DateTime.UtcNow.AddDays(-3)
            },
            new ReleaseInfo
            {
                TagName = "v1.0.0",
                Name = "Release 1.0.0",
                Prerelease = false,
                Draft = false,
                PublishedAt = DateTime.UtcNow.AddDays(-10)
            }
        };

        var jsonResponse = JsonSerializer.Serialize(releases);
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
        var result = await _gitHubReleaseService.GetStableReleasesAsync();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("v2.0.0", result[0].TagName);
        Assert.AreEqual("v1.0.0", result[1].TagName);
        Assert.IsTrue(result.All(r => !r.Prerelease && !r.Draft));
    }

    [TestMethod]
    public async Task GetPrereleaseVersionsAsync_WithMixedReleases_ReturnsOnlyPrereleases()
    {
        // Arrange
        var releases = new List<ReleaseInfo>
        {
            new ReleaseInfo
            {
                TagName = "v2.0.0",
                Name = "Release 2.0.0",
                Prerelease = false,
                Draft = false,
                PublishedAt = DateTime.UtcNow
            },
            new ReleaseInfo
            {
                TagName = "v2.0.0-beta.1",
                Name = "Release 2.0.0 Beta 1",
                Prerelease = true,
                Draft = false,
                PublishedAt = DateTime.UtcNow.AddDays(-5)
            },
            new ReleaseInfo
            {
                TagName = "v2.0.0-alpha.1",
                Name = "Release 2.0.0 Alpha 1",
                Prerelease = true,
                Draft = false,
                PublishedAt = DateTime.UtcNow.AddDays(-15)
            },
            new ReleaseInfo
            {
                TagName = "v1.9.0-draft",
                Name = "Draft Release",
                Prerelease = true,
                Draft = true,
                PublishedAt = DateTime.UtcNow.AddDays(-3)
            }
        };

        var jsonResponse = JsonSerializer.Serialize(releases);
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
        var result = await _gitHubReleaseService.GetPrereleaseVersionsAsync();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("v2.0.0-beta.1", result[0].TagName);
        Assert.AreEqual("v2.0.0-alpha.1", result[1].TagName);
        Assert.IsTrue(result.All(r => r.Prerelease && !r.Draft));
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
            HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v2.0.0",
            PublishedAt = DateTime.UtcNow,
            Prerelease = false,
            Draft = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
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
            HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v1.0.0",
            PublishedAt = DateTime.UtcNow,
            Prerelease = false,
            Draft = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
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
            HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v1.0.0",
            PublishedAt = DateTime.UtcNow,
            Prerelease = false,
            Draft = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
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
            HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v2.0.0",
            PublishedAt = DateTime.UtcNow,
            Prerelease = false,
            Draft = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
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
    public async Task CheckForUpdateAsync_WithIncludePrereleaseTrue_ReturnsLatestIncludingPrerelease()
    {
        // Arrange - Setup for GetAllReleasesAsync call
        var releases = new List<ReleaseInfo>
        {
            new ReleaseInfo
            {
                TagName = "v2.1.0-beta.1",
                Name = "Release 2.1.0 Beta 1",
                Body = "Latest beta release",
                HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v2.1.0-beta.1",
                PublishedAt = DateTime.UtcNow,
                Prerelease = true,
                Draft = false,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                Assets = new List<ReleaseAsset>()
            },
            new ReleaseInfo
            {
                TagName = "v2.0.0",
                Name = "Release 2.0.0",
                Body = "Latest stable release",
                HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v2.0.0",
                PublishedAt = DateTime.UtcNow.AddDays(-5),
                Prerelease = false,
                Draft = false,
                CreatedAt = DateTime.UtcNow.AddDays(-6),
                Assets = new List<ReleaseAsset>()
            }
        };

        var jsonResponse = JsonSerializer.Serialize(releases);
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
        var result = await _gitHubReleaseService.CheckForUpdateAsync("1.0.0", includePrerelease: true);

        // Assert
        Assert.IsTrue(result.HasNewVersion);
        Assert.IsNotNull(result.ReleaseInfo);
        Assert.AreEqual("v2.1.0-beta.1", result.ReleaseInfo.TagName);
        Assert.IsTrue(result.ReleaseInfo.Prerelease);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_WithIncludePrereleaseFalse_ReturnsLatestStableOnly()
    {
        // Arrange - Setup for GetLatestReleaseAsync call
        var releaseInfo = new ReleaseInfo
        {
            TagName = "v2.0.0",
            Name = "Release 2.0.0",
            Body = "Latest stable release",
            HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v2.0.0",
            PublishedAt = DateTime.UtcNow,
            Prerelease = false,
            Draft = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
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
        var result = await _gitHubReleaseService.CheckForUpdateAsync("1.0.0", includePrerelease: false);

        // Assert
        Assert.IsTrue(result.HasNewVersion);
        Assert.IsNotNull(result.ReleaseInfo);
        Assert.AreEqual("v2.0.0", result.ReleaseInfo.TagName);
        Assert.IsFalse(result.ReleaseInfo.Prerelease);
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
        var downloadUrl = "https://github.com/X2Agent/MarketAssistant/releases/download/v1.0.0/MarketAssistant-1.0.0.zip";
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
        var downloadUrl = "https://github.com/X2Agent/MarketAssistant/releases/download/v1.0.0/MarketAssistant-1.0.0.zip";
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
        var downloadUrl = "https://github.com/X2Agent/MarketAssistant/releases/download/v1.0.0/MarketAssistant-1.0.0.zip";
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
        var downloadUrl = "https://github.com/X2Agent/MarketAssistant/releases/download/v1.0.0/MarketAssistant-1.0.0.zip";
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

    #region Version Comparison Tests

    [TestMethod]
    public void CompareVersions_BasicVersions_ReturnsCorrectResult()
    {
        // 使用反射访问私有方法进行测试
        var method = typeof(GitHubReleaseService).GetMethod("CompareVersions", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Test cases: (version1, version2, expected result)
        var testCases = new[]
        {
            ("2.0.0", "1.0.0", 1),   // 2.0.0 > 1.0.0
            ("1.0.0", "2.0.0", -1),  // 1.0.0 < 2.0.0
            ("1.0.0", "1.0.0", 0),   // 1.0.0 = 1.0.0
            ("1.1.0", "1.0.0", 1),   // 1.1.0 > 1.0.0
            ("1.0.1", "1.0.0", 1),   // 1.0.1 > 1.0.0
            ("1.0.0", "1.0.1", -1),  // 1.0.0 < 1.0.1
            ("2.1.0", "2.0.5", 1),   // 2.1.0 > 2.0.5
        };

        foreach (var (version1, version2, expected) in testCases)
        {
            // Act
            var result = (int)method!.Invoke(_gitHubReleaseService, new object[] { version1, version2 })!;
            
            // Assert
            Assert.AreEqual(expected, result, $"Comparing {version1} with {version2}");
        }
    }

    [TestMethod]
    public void CompareVersions_PrereleaseVersions_ReturnsCorrectResult()
    {
        // 使用反射访问私有方法进行测试
        var method = typeof(GitHubReleaseService).GetMethod("CompareVersions", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Test cases: (version1, version2, expected result)
        var testCases = new[]
        {
            // 正式版本 > 预发布版本
            ("2.0.0", "2.0.0-beta.1", 1),
            ("2.0.0-beta.1", "2.0.0", -1),
            
            // 预发布版本之间的比较
            ("2.0.0-beta.2", "2.0.0-beta.1", 1),
            ("2.0.0-beta.1", "2.0.0-beta.2", -1),
            ("2.0.0-beta.1", "2.0.0-beta.1", 0),
            
            // alpha < beta
            ("2.0.0-beta.1", "2.0.0-alpha.1", 1),
            ("2.0.0-alpha.1", "2.0.0-beta.1", -1),
            
            // 不同主版本号的预发布版本
            ("2.1.0-beta.1", "2.0.0", 1),
            ("2.0.0", "2.1.0-beta.1", -1),
            
            // 复杂的预发布版本
            ("2.0.0-beta.1.1", "2.0.0-beta.1", 1),
            ("2.0.0-beta.1", "2.0.0-beta.1.1", -1),
        };

        foreach (var (version1, version2, expected) in testCases)
        {
            // Act
            var result = (int)method!.Invoke(_gitHubReleaseService, new object[] { version1, version2 })!;
            
            // Assert
            Assert.AreEqual(expected, result, $"Comparing {version1} with {version2}");
        }
    }

    [TestMethod]
    public void CompareVersions_EdgeCases_ReturnsCorrectResult()
    {
        // 使用反射访问私有方法进行测试
        var method = typeof(GitHubReleaseService).GetMethod("CompareVersions", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        // Test cases: (version1, version2, expected result)
        var testCases = new[]
        {
            // 不同长度的版本号
            ("1.0", "1.0.0", 0),
            ("1.0.0", "1.0", 0),
            ("1.0.1", "1.0", 1),
            ("1.0", "1.0.1", -1),
            
            // 单个数字版本
            ("2", "1", 1),
            ("1", "2", -1),
            ("1", "1.0.0", 0),
            
            // 零版本号
            ("0.0.1", "0.0.0", 1),
            ("0.1.0", "0.0.1", 1),
            ("1.0.0", "0.9.9", 1),
        };

        foreach (var (version1, version2, expected) in testCases)
        {
            // Act
            var result = (int)method!.Invoke(_gitHubReleaseService, new object[] { version1, version2 })!;
            
            // Assert
            Assert.AreEqual(expected, result, $"Comparing {version1} with {version2}");
        }
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_WithPrereleaseVersionComparison_ReturnsCorrectResult()
    {
        // Arrange - 当前版本是 2.0.0，最新版本是 2.1.0-beta.1
        var releaseInfo = new ReleaseInfo
        {
            TagName = "v2.1.0-beta.1",
            Name = "Release 2.1.0 Beta 1",
            Body = "Beta release",
            HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v2.1.0-beta.1",
            PublishedAt = DateTime.UtcNow,
            Prerelease = true,
            Draft = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
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
        var result = await _gitHubReleaseService.CheckForUpdateAsync("v2.0.0");

        // Assert - 2.1.0-beta.1 应该大于 2.0.0
        Assert.IsTrue(result.HasNewVersion);
        Assert.IsNotNull(result.ReleaseInfo);
        Assert.AreEqual("v2.1.0-beta.1", result.ReleaseInfo.TagName);
    }

    [TestMethod]
    public async Task CheckForUpdateAsync_WithCurrentPrereleaseVsStable_ReturnsCorrectResult()
    {
        // Arrange - 当前版本是 2.1.0-beta.1，最新稳定版是 2.0.0
        var releaseInfo = new ReleaseInfo
        {
            TagName = "v2.0.0",
            Name = "Release 2.0.0",
            Body = "Stable release",
            HtmlUrl = "https://github.com/X2Agent/MarketAssistant/releases/tag/v2.0.0",
            PublishedAt = DateTime.UtcNow,
            Prerelease = false,
            Draft = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
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
        var result = await _gitHubReleaseService.CheckForUpdateAsync("v2.1.0-beta.1");

        // Assert - 2.0.0 应该小于 2.1.0-beta.1
        Assert.IsFalse(result.HasNewVersion);
        Assert.IsNotNull(result.ReleaseInfo);
        Assert.AreEqual("v2.0.0", result.ReleaseInfo.TagName);
    }

    #endregion
}