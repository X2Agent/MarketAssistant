using Microsoft.Extensions.VectorData;

namespace MarketAssistant.Filtering;

/// <summary>
/// 语义缓存过滤器的抽象基类：集中常量、TTL、工具方法与缓存记录模型。
/// </summary>
internal abstract class CacheBaseFilter
{
    protected const string CollectionName = "llm_responses";
    protected const string RecordIdKey = "CacheRecordId";

    protected static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);

    protected static bool IsExpired(CacheRecord record)
    {
        if (DateTimeOffset.TryParse(record.ExpiresAt, out var expiresAt))
        {
            return DateTimeOffset.UtcNow >= expiresAt;
        }
        return false;
    }

    protected sealed class CacheRecord
    {
        [VectorStoreKey]
        public string Id { get; set; } = string.Empty;

        [VectorStoreData]
        public string Prompt { get; set; } = string.Empty;

        [VectorStoreData]
        public string Result { get; set; } = string.Empty;

        [VectorStoreData]
        public string ExpiresAt { get; set; } = DateTimeOffset.UtcNow.Add(DefaultTtl).ToString("O");

        [VectorStoreVector(Dimensions: 1536)]
        public ReadOnlyMemory<float> PromptEmbedding { get; set; }
    }
}


