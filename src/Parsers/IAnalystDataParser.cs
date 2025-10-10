using MarketAssistant.Models;

namespace MarketAssistant.Parsers;

/// <summary>
/// 分析师数据解析器接口
/// </summary>
public interface IAnalystDataParser
{
    /// <summary>
    /// 异步解析分析师返回的数据
    /// </summary>
    /// <param name="content">分析师返回的文本内容</param>
    /// <returns>解析后的结构化数据</returns>
    Task<AnalystResult> ParseDataAsync(string content);
}

