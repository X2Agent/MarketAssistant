namespace MarketAssistant.Vectors.Interfaces;

/// <summary>
/// 原始文档读取器接口：负责将文档流转换为纯文本，后续由清洗/分块处理。
/// </summary>
public interface IRawDocumentReader
{
    /// <summary>
    /// 是否支持该文件。
    /// </summary>
    bool CanRead(string filePath);

    /// <summary>
    /// 读取全文为字符串（尽量保留自然段换行）。
    /// </summary>
    string ReadAllText(Stream stream);
}


