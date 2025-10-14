using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

namespace MarketAssistant.Applications.Settings;

/// <summary>
/// GitHub Release 服务实现
/// </summary>
public class GitHubReleaseService : IReleaseService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubReleaseService> _logger;
    private readonly string _githubApiBaseUrl = $"{AppInfo.GitHubApiBaseUrl}/repos/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases";
    private readonly string _githubApiLatestUrl = $"{AppInfo.GitHubApiBaseUrl}/repos/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases/latest";

    private CachedData<List<ReleaseInfo>>? _cachedReleases;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(10);
    private RateLimitInfo? _rateLimitInfo;

    public GitHubReleaseService(IHttpClientFactory httpClientFactory, ILogger<GitHubReleaseService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 检查是否有新版本
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(string currentVersion, bool includePrerelease = true)
    {
        try
        {
            _logger.LogInformation("开始检查更新：当前版本 {CurrentVersion}，包含预览版：{IncludePrerelease}",
                currentVersion, includePrerelease);

            if (string.IsNullOrWhiteSpace(currentVersion))
            {
                throw new FriendlyException("当前版本号不能为空");
            }

            // 检查速率限制
            if (!CheckRateLimit())
            {
                var waitTime = _rateLimitInfo!.ResetTime - DateTime.UtcNow;
                throw new FriendlyException($"GitHub API 速率限制已达上限，请等待 {waitTime.TotalMinutes:F0} 分钟后重试");
            }

            // 获取最新版本
            ReleaseInfo? latestRelease;
            if (includePrerelease)
            {
                var allReleases = await GetAllReleasesInternalAsync();
                latestRelease = allReleases
                    .Where(r => !r.Draft)
                    .OrderByDescending(r => r.PublishedAt)
                    .FirstOrDefault();
            }
            else
            {
                latestRelease = await GetLatestReleaseInternalAsync();
            }

            if (latestRelease == null)
            {
                throw new FriendlyException("未找到可用的发布版本");
            }

            // 比较版本号
            var latestVersion = latestRelease.TagName.TrimStart('v');
            var current = currentVersion.TrimStart('v');

            var comparison = CompareVersions(latestVersion, current);
            var hasNewVersion = comparison > 0;

            var result = new UpdateCheckResult
            {
                HasNewVersion = hasNewVersion,
                LatestRelease = latestRelease,
                CurrentVersion = currentVersion
            };

            if (hasNewVersion)
            {
                _logger.LogInformation("发现新版本：{LatestVersion}（当前：{CurrentVersion}）",
                    latestVersion, current);
            }
            else
            {
                _logger.LogInformation("当前已是最新版本：{CurrentVersion}", current);
            }

            return result;
        }
        catch (FriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查更新失败");
            throw new FriendlyException($"检查更新失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 下载更新文件
    /// </summary>
    public async Task<string> DownloadUpdateAsync(
        string downloadUrl,
        string savePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new FriendlyException("下载地址不能为空");
            }

            if (string.IsNullOrWhiteSpace(savePath))
            {
                throw new FriendlyException("保存路径不能为空");
            }

            _logger.LogInformation("开始下载更新文件：{Url} -> {SavePath}", downloadUrl, savePath);

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent);
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new FriendlyException($"下载失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var totalMb = totalBytes / 1024.0 / 1024.0;
            _logger.LogInformation("文件大小：{Size:F2} MB", totalMb);

            // 确保目录存在
            var directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            var lastProgressReport = DateTime.UtcNow;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                // 报告进度（每100ms报告一次，避免过于频繁）
                if (progress != null && DateTime.UtcNow - lastProgressReport > TimeSpan.FromMilliseconds(100))
                {
                    if (totalBytes > 0)
                    {
                        var percentage = (double)totalRead / totalBytes;
                        progress.Report(percentage);
                    }
                    lastProgressReport = DateTime.UtcNow;
                }
            }

            // 确保报告100%
            progress?.Report(1.0);

            _logger.LogInformation("更新文件下载完成：{SavePath}（{Size:F2} MB）", savePath, totalRead / 1024.0 / 1024.0);
            return savePath;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "下载更新失败：HTTP 请求异常");
            throw new FriendlyException($"下载失败：{ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("下载已取消");
            throw new OperationCanceledException("下载已取消", ex, cancellationToken);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "下载更新失败：文件 IO 异常");
            throw new FriendlyException($"文件保存失败：{ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "下载更新失败：无权限访问文件");
            throw new FriendlyException($"无权限保存文件：{ex.Message}", ex);
        }
        catch (FriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载更新失败：未知异常");
            throw new FriendlyException($"下载失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 清除缓存的版本信息
    /// </summary>
    public void ClearCache()
    {
        _cachedReleases = null;
        _logger.LogInformation("已清除版本信息缓存");
    }

    /// <summary>
    /// 获取所有发布版本（内部方法）
    /// </summary>
    private async Task<List<ReleaseInfo>> GetAllReleasesInternalAsync()
    {
        // 检查缓存
        if (_cachedReleases != null && !_cachedReleases.IsExpired(_cacheExpiration))
        {
            _logger.LogDebug("从缓存返回所有版本信息");
            return _cachedReleases.Data;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent);
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await httpClient.GetAsync(_githubApiBaseUrl);
            UpdateRateLimitInfo(response);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("GitHub API 速率限制已达上限");
                // 如果有缓存数据，返回缓存
                if (_cachedReleases != null)
                {
                    return _cachedReleases.Data;
                }
                throw new FriendlyException("GitHub API 速率限制已达上限");
            }

            response.EnsureSuccessStatusCode();
            var releases = await response.Content.ReadFromJsonAsync<List<ReleaseInfo>>();

            if (releases == null)
            {
                throw new FriendlyException("获取版本信息失败：响应数据为空");
            }

            _cachedReleases = new CachedData<List<ReleaseInfo>>(releases);
            _logger.LogInformation("成功获取 {Count} 个版本信息", releases.Count);
            return releases;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "获取版本信息失败：HTTP 请求异常");
            // 如果有缓存，返回缓存数据作为降级方案
            if (_cachedReleases != null)
            {
                _logger.LogInformation("返回缓存的版本信息作为降级方案");
                return _cachedReleases.Data;
            }
            throw new FriendlyException($"网络请求失败：{ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "获取版本信息失败：请求超时");
            if (_cachedReleases != null)
            {
                return _cachedReleases.Data;
            }
            throw new FriendlyException("请求超时", ex);
        }
        catch (FriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取版本信息失败：未知异常");
            if (_cachedReleases != null)
            {
                return _cachedReleases.Data;
            }
            throw new FriendlyException($"获取版本信息失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取最新正式版本（内部方法）
    /// </summary>
    private async Task<ReleaseInfo> GetLatestReleaseInternalAsync()
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent);
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var response = await httpClient.GetAsync(_githubApiLatestUrl);
            UpdateRateLimitInfo(response);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("GitHub API 速率限制已达上限");
                throw new FriendlyException("GitHub API 速率限制已达上限");
            }

            response.EnsureSuccessStatusCode();
            var release = await response.Content.ReadFromJsonAsync<ReleaseInfo>();

            if (release == null)
            {
                throw new FriendlyException("获取版本信息失败：响应数据为空");
            }

            _logger.LogInformation("成功获取最新版本信息：{Version}", release.TagName);
            return release;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "获取最新版本信息失败：HTTP 请求异常");
            throw new FriendlyException($"网络请求失败：{ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "获取最新版本信息失败：请求超时");
            throw new FriendlyException("请求超时", ex);
        }
        catch (FriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取最新版本信息失败：未知异常");
            throw new FriendlyException($"获取版本信息失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 检查 GitHub API 速率限制
    /// </summary>
    private bool CheckRateLimit()
    {
        if (_rateLimitInfo == null || _rateLimitInfo.IsExpired())
        {
            return true;
        }

        if (_rateLimitInfo.Remaining <= 0)
        {
            var waitTime = _rateLimitInfo.ResetTime - DateTime.UtcNow;
            if (waitTime > TimeSpan.Zero)
            {
                _logger.LogWarning("GitHub API 速率限制已达上限，需等待 {WaitTime} 后重试", waitTime);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 从响应头更新速率限制信息
    /// </summary>
    private void UpdateRateLimitInfo(HttpResponseMessage response)
    {
        try
        {
            if (response.Headers.TryGetValues("X-RateLimit-Limit", out var limitValues) &&
                response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues) &&
                response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues))
            {
                var limit = int.Parse(limitValues.First());
                var remaining = int.Parse(remainingValues.First());
                var resetTimestamp = long.Parse(resetValues.First());
                var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp).UtcDateTime;

                _rateLimitInfo = new RateLimitInfo(limit, remaining, resetTime);

                _logger.LogDebug("GitHub API 速率限制：{Remaining}/{Limit}，重置时间：{ResetTime}",
                    remaining, limit, resetTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析 GitHub API 速率限制信息失败");
        }
    }

    /// <summary>
    /// 比较版本号（支持三位数和四位数版本号格式）
    /// </summary>
    private int CompareVersions(string version1, string version2)
    {
        try
        {
            var v1 = ParseVersionInfo(version1);
            var v2 = ParseVersionInfo(version2);

            var result = v1.Version.CompareTo(v2.Version);
            if (result != 0)
                return result;

            if (string.IsNullOrEmpty(v1.Prerelease) && !string.IsNullOrEmpty(v2.Prerelease))
                return 1;
            if (!string.IsNullOrEmpty(v1.Prerelease) && string.IsNullOrEmpty(v2.Prerelease))
                return -1;
            if (!string.IsNullOrEmpty(v1.Prerelease) && !string.IsNullOrEmpty(v2.Prerelease))
                return ComparePrerelease(v1.Prerelease, v2.Prerelease);

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "版本号比较失败：{Version1} vs {Version2}", version1, version2);
            return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 解析版本号字符串（支持三位数和四位数版本号）
    /// </summary>
    private (Version Version, string Prerelease) ParseVersionInfo(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("版本号不能为空", nameof(version));

        var parts = version.Split('-', 2);
        var mainVersion = parts[0];
        var prerelease = parts.Length > 1 ? parts[1] : string.Empty;

        if (!IsValidVersionFormat(mainVersion))
            throw new FormatException($"无效的版本号格式: {mainVersion}");

        if (Version.TryParse(mainVersion, out var parsedVersion))
        {
            return (parsedVersion, prerelease);
        }

        var versionParts = mainVersion.Split('.');
        if (versionParts.Length >= 2)
        {
            var normalizedVersion = string.Join(".", versionParts.Take(4));
            if (Version.TryParse(normalizedVersion, out var normalizedParsedVersion))
            {
                return (normalizedParsedVersion, prerelease);
            }
        }

        throw new FormatException($"无法解析版本号: {version}");
    }

    /// <summary>
    /// 验证版本号格式是否有效
    /// </summary>
    private bool IsValidVersionFormat(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var parts = version.Split('.');
        if (parts.Length < 2 || parts.Length > 4)
            return false;

        return parts.All(part => int.TryParse(part, out var num) && num >= 0);
    }

    /// <summary>
    /// 比较预发布版本标识符
    /// </summary>
    private int ComparePrerelease(string prerelease1, string prerelease2)
    {
        var parts1 = prerelease1.Split('.');
        var parts2 = prerelease2.Split('.');

        for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
        {
            var part1 = i < parts1.Length ? parts1[i] : string.Empty;
            var part2 = i < parts2.Length ? parts2[i] : string.Empty;

            if (string.IsNullOrEmpty(part1) && !string.IsNullOrEmpty(part2))
                return -1;
            if (!string.IsNullOrEmpty(part1) && string.IsNullOrEmpty(part2))
                return 1;

            if (int.TryParse(part1, out var num1) && int.TryParse(part2, out var num2))
            {
                if (num1 != num2)
                    return num1.CompareTo(num2);
            }
            else
            {
                var result = string.Compare(part1, part2, StringComparison.OrdinalIgnoreCase);
                if (result != 0)
                    return result;
            }
        }

        return 0;
    }
}

/// <summary>
/// 缓存数据包装类
/// </summary>
internal class CachedData<T>
{
    public T Data { get; }
    public DateTime Timestamp { get; }

    public CachedData(T data)
    {
        Data = data;
        Timestamp = DateTime.UtcNow;
    }

    public bool IsExpired(TimeSpan expiration)
    {
        return DateTime.UtcNow - Timestamp > expiration;
    }
}

/// <summary>
/// GitHub API 速率限制信息
/// </summary>
internal class RateLimitInfo
{
    public int Limit { get; }
    public int Remaining { get; }
    public DateTime ResetTime { get; }

    public RateLimitInfo(int limit, int remaining, DateTime resetTime)
    {
        Limit = limit;
        Remaining = remaining;
        ResetTime = resetTime;
    }

    public bool IsExpired()
    {
        return DateTime.UtcNow >= ResetTime;
    }
}
