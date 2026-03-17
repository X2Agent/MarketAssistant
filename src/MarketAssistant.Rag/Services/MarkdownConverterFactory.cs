using MarketAssistant.Rag.Interfaces;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// Markdown转换器工厂
/// 基于现有的IMarkdownConverter接口，管理各种文档格式的转换器
/// </summary>
public class MarkdownConverterFactory
{
    private readonly IEnumerable<IMarkdownConverter> _converters;

    public MarkdownConverterFactory(IEnumerable<IMarkdownConverter> converters)
    {
        _converters = converters ?? throw new ArgumentNullException(nameof(converters));
    }

    public IMarkdownConverter? GetConverter(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        return _converters.FirstOrDefault(c => c.CanConvert(filePath));
    }

    public bool CanConvert(string filePath)
    {
        return GetConverter(filePath) != null;
    }
}
