using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Settings;

/// <summary>
/// 版本检查结果
/// </summary>
public class UpdateCheckResult
{
    /// <summary>
    /// 是否有新版本
    /// </summary>
    public bool HasNewVersion { get; set; }

    /// <summary>
    /// 最新发布版本信息
    /// </summary>
    public ReleaseInfo? LatestRelease { get; set; }

    /// <summary>
    /// 当前版本号
    /// </summary>
    public string? CurrentVersion { get; set; }
}

/// <summary>
/// Release 信息模型
/// </summary>
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

/// <summary>
/// Release 资产信息
/// </summary>
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