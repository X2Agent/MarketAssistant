using Microsoft.SemanticKernel.ChatCompletion;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Cache;

/// <summary>
/// 缓存的分析结果
/// </summary>
public class CachedAnalysisResult
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string StockSymbol { get; set; } = string.Empty;

    /// <summary>
    /// 聊天历史
    /// </summary>
    public ChatHistory ChatHistory { get; set; } = new();
}



/// <summary>
/// ChatHistory的JSON转换器
/// </summary>
public class ChatHistoryJsonConverter : JsonConverter<ChatHistory>
{
    public override ChatHistory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var chatHistory = new ChatHistory();
        
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected StartArray token");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                var messageData = JsonSerializer.Deserialize<ChatMessageData>(ref reader, options);
                if (messageData != null)
                {
                    // 根据角色添加消息
                    if (messageData.Role == AuthorRole.User)
                    {
                        chatHistory.AddUserMessage(messageData.Content ?? string.Empty);
                    }
                    else if (messageData.Role == AuthorRole.Assistant)
                    {
                        chatHistory.AddAssistantMessage(messageData.Content ?? string.Empty);
                    }
                    else if (messageData.Role == AuthorRole.System)
                    {
                        chatHistory.AddSystemMessage(messageData.Content ?? string.Empty);
                    }
                }
            }
        }

        return chatHistory;
    }

    public override void Write(Utf8JsonWriter writer, ChatHistory value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var message in value)
        {
            var messageData = new ChatMessageData
            {
                Role = message.Role,
                Content = message.Content ?? string.Empty,
                AuthorName = message.AuthorName
            };
            JsonSerializer.Serialize(writer, messageData, options);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// 聊天消息数据传输对象
    /// </summary>
    private class ChatMessageData
    {
        public AuthorRole Role { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? AuthorName { get; set; }
    }
}
