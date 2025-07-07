using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Infrastructure;

public class GitHubReleaseService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const string GITHUB_API_BASE_URL = "https://api.github.com/repos/X2Agent/MarketAssistant/releases";
    private const string GITHUB_API_LATEST_URL = "https://api.github.com/repos/X2Agent/MarketAssistant/releases/latest";

    public GitHubReleaseService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// 获取所有发布版本信息（包含正式版和预览版）
    /// </summary>
    /// <returns>所有发布版本信息列表</returns>
    public async Task<List<ReleaseInfo>?> GetAllReleasesAsync()
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "MarketAssistant");
            
            var response = await httpClient.GetFromJsonAsync<List<ReleaseInfo>>(GITHUB_API_BASE_URL);
            return response;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取所有发布版本信息失败: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"异常详情: {ex}");
            return null;
        }
    }

    /// <summary>
    /// 获取最新正式版本信息
    /// </summary>
    /// <returns>最新正式版本信息</returns>
    public async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "MarketAssistant");
            var response = await httpClient.GetFromJsonAsync<ReleaseInfo>(GITHUB_API_LATEST_URL);
            return response;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取最新版本信息失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取所有正式版本（非预览版）
    /// </summary>
    /// <returns>所有正式版本信息列表</returns>
    public async Task<List<ReleaseInfo>?> GetStableReleasesAsync()
    {
        var allReleases = await GetAllReleasesAsync();
        return allReleases?.Where(r => !r.Prerelease && !r.Draft).ToList();
    }

    /// <summary>
    /// 获取所有预览版本
    /// </summary>
    /// <returns>所有预览版本信息列表</returns>
    public async Task<List<ReleaseInfo>?> GetPrereleaseVersionsAsync()
    {
        var allReleases = await GetAllReleasesAsync();
        return allReleases?.Where(r => r.Prerelease && !r.Draft).ToList();
    }

    /// <summary>
    /// 检查是否有新版本
    /// </summary>
    /// <param name="currentVersion">当前版本</param>
    /// <param name="includePrerelease">是否包含预览版</param>
    /// <returns>是否有新版本</returns>
    public async Task<(bool HasNewVersion, ReleaseInfo? ReleaseInfo)> CheckForUpdateAsync(string currentVersion, bool includePrerelease = true)
    {
        ReleaseInfo? releaseInfo;

        if (includePrerelease)
        {
            var allReleases = await GetAllReleasesAsync();
            releaseInfo = allReleases?.Where(r => !r.Draft).OrderByDescending(r => r.PublishedAt).FirstOrDefault();
        }
        else
        {
            releaseInfo = await GetLatestReleaseAsync();
        }

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
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "MarketAssistant");
            var response = await httpClient.GetAsync(downloadUrl);
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
    /// 比较版本号（支持三位数和四位数版本号格式）
    /// </summary>
    /// <param name="version1">版本1</param>
    /// <param name="version2">版本2</param>
    /// <returns>比较结果：1表示version1>version2，-1表示version1<version2，0表示相等</returns>
    private int CompareVersions(string version1, string version2)
    {
        try
        {
            var v1 = ParseVersionInfo(version1);
            var v2 = ParseVersionInfo(version2);

            // 使用 System.Version 进行版本号比较
            var result = v1.Version.CompareTo(v2.Version);
            if (result != 0)
                return result;

            // 如果主版本号相同，比较预发布版本
            // 根据 SemVer 规范：正式版本 > 预发布版本
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
            System.Diagnostics.Debug.WriteLine($"版本号比较失败: {ex.Message}");
            // 如果解析失败，回退到字符串比较
            return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 解析版本号字符串（支持三位数和四位数版本号）
    /// </summary>
    /// <param name="version">版本号字符串</param>
    /// <returns>解析后的版本信息</returns>
    private (Version Version, string Prerelease) ParseVersionInfo(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("版本号不能为空", nameof(version));

        // 分离主版本号和预发布标识符
        var parts = version.Split('-', 2);
        var mainVersion = parts[0];
        var prerelease = parts.Length > 1 ? parts[1] : string.Empty;

        // 验证版本号格式
        if (!IsValidVersionFormat(mainVersion))
            throw new FormatException($"无效的版本号格式: {mainVersion}");

        // 使用 System.Version 解析版本号
        // System.Version 支持 Major.Minor、Major.Minor.Build、Major.Minor.Build.Revision 格式
        if (Version.TryParse(mainVersion, out var parsedVersion))
        {
            return (parsedVersion, prerelease);
        }

        // 如果解析失败，尝试补全版本号格式
        var versionParts = mainVersion.Split('.');
        if (versionParts.Length >= 2)
        {
            // 确保至少有 Major.Minor 格式
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
    /// <param name="version">版本号字符串</param>
    /// <returns>是否为有效格式</returns>
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
    /// <param name="prerelease1">预发布版本1</param>
    /// <param name="prerelease2">预发布版本2</param>
    /// <returns>比较结果</returns>
    private int ComparePrerelease(string prerelease1, string prerelease2)
    {
        var parts1 = prerelease1.Split('.');
        var parts2 = prerelease2.Split('.');

        for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
        {
            var part1 = i < parts1.Length ? parts1[i] : string.Empty;
            var part2 = i < parts2.Length ? parts2[i] : string.Empty;

            // 如果一个部分为空，另一个不为空，空的部分较小
            if (string.IsNullOrEmpty(part1) && !string.IsNullOrEmpty(part2))
                return -1;
            if (!string.IsNullOrEmpty(part1) && string.IsNullOrEmpty(part2))
                return 1;

            // 尝试按数字比较
            if (int.TryParse(part1, out var num1) && int.TryParse(part2, out var num2))
            {
                if (num1 != num2)
                    return num1.CompareTo(num2);
            }
            else
            {
                // 按字符串比较
                var result = string.Compare(part1, part2, StringComparison.OrdinalIgnoreCase);
                if (result != 0)
                    return result;
            }
        }

        return 0;
    }
}

public class ReleaseInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("assets_url")]
    public string AssetsUrl { get; set; } = string.Empty;

    [JsonPropertyName("upload_url")]
    public string UploadUrl { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("target_commitish")]
    public string TargetCommitish { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<ReleaseAsset> Assets { get; set; } = new List<ReleaseAsset>();

    [JsonPropertyName("tarball_url")]
    public string TarballUrl { get; set; } = string.Empty;

    [JsonPropertyName("zipball_url")]
    public string ZipballUrl { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
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