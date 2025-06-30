using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Infrastructure;

public class GitHubReleaseService
{
    private readonly HttpClient _httpClient;
    private const string GITHUB_API_URL = "https://api.github.com/repos/owner/MarketAssistant/releases/latest";
    
    public GitHubReleaseService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MarketAssistant");
    }

    /// <summary>
    /// 获取最新版本信息
    /// </summary>
    /// <returns>最新版本信息</returns>
    public async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<ReleaseInfo>(GITHUB_API_URL);
            return response;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取最新版本信息失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 检查是否有新版本
    /// </summary>
    /// <param name="currentVersion">当前版本</param>
    /// <returns>是否有新版本</returns>
    public async Task<(bool HasNewVersion, ReleaseInfo? ReleaseInfo)> CheckForUpdateAsync(string currentVersion)
    {
        var releaseInfo = await GetLatestReleaseAsync();
        if (releaseInfo == null)
        {
            return (false, null);
        }

        // 移除版本号前的'v'字符
        var latestVersion = releaseInfo.TagName.StartsWith("v") ? releaseInfo.TagName.Substring(1) : releaseInfo.TagName;
        var current = currentVersion.StartsWith("v") ? currentVersion.Substring(1) : currentVersion;

        // 比较版本号
        var hasNewVersion = CompareVersions(latestVersion, current) > 0;
        return (hasNewVersion, releaseInfo);
    }

    /// <summary>
    /// 下载更新文件
    /// </summary>
    /// <param name="downloadUrl">下载地址</param>
    /// <param name="savePath">保存路径</param>
    /// <returns>下载结果</returns>
    public async Task<bool> DownloadUpdateAsync(string downloadUrl, string savePath)
    {
        try
        {
            var response = await _httpClient.GetAsync(downloadUrl);
            response.EnsureSuccessStatusCode();

            using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fileStream);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"下载更新失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 比较版本号
    /// </summary>
    /// <param name="version1">版本1</param>
    /// <param name="version2">版本2</param>
    /// <returns>比较结果</returns>
    private int CompareVersions(string version1, string version2)
    {
        var v1Parts = version1.Split('.').Select(int.Parse).ToArray();
        var v2Parts = version2.Split('.').Select(int.Parse).ToArray();

        for (int i = 0; i < Math.Max(v1Parts.Length, v2Parts.Length); i++)
        {
            var v1 = i < v1Parts.Length ? v1Parts[i] : 0;
            var v2 = i < v2Parts.Length ? v2Parts[i] : 0;

            if (v1 > v2) return 1;
            if (v1 < v2) return -1;
        }

        return 0;
    }
}

public class ReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public List<ReleaseAsset> Assets { get; set; } = new List<ReleaseAsset>();

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }
}

public class ReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;
}