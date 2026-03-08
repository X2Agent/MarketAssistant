using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.Crypto.CoinDesk;

/// <summary>
/// CoinDesk 新闻 API 响应模型
/// </summary>
public class CoinDeskNewsResponse
{
    [JsonPropertyName("DATA")]
    public List<NewsArticle>? Data { get; set; }
}

/// <summary>
/// 新闻文章模型
/// </summary>
public class NewsArticle
{
    [JsonPropertyName("ID")]
    public int Id { get; set; }

    [JsonPropertyName("TITLE")]
    public string Title { get; set; } = "";

    [JsonPropertyName("BODY")]
    public string Body { get; set; } = "";

    [JsonPropertyName("URL")]
    public string Url { get; set; } = "";

    [JsonPropertyName("KEYWORDS")]
    public string Keywords { get; set; } = "";

    [JsonPropertyName("CREATED_ON")]
    public int CreatedOn { get; set; }

    [JsonPropertyName("AUTHORS")]
    public string Authors { get; set; } = "";

    [JsonPropertyName("SOURCE_DATA")]
    public NewsSource? Source { get; set; }
}

/// <summary>
/// 新闻来源模型
/// </summary>
public class NewsSource
{
    [JsonPropertyName("NAME")]
    public string Name { get; set; } = "";
}
