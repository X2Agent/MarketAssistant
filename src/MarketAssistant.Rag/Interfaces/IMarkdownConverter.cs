namespace MarketAssistant.Rag.Interfaces;

public interface IMarkdownConverter
{
    bool CanConvert(string filePath);

    Task<string> ConvertToMarkdownAsync(string filePath);
}
