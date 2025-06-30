using CommunityToolkit.Maui.Converters;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace MarketAssistant.Converts;

/// <summary>
/// 将字典转换为字符串，每行一个键值对，格式为KEY=value
/// </summary>
[AcceptEmptyServiceProvider]
public class DictionaryToStringConverter : BaseConverter<Dictionary<string, string>, string, string>
{
    /// <summary>
    /// 默认返回值，当转换失败时返回
    /// </summary>
    public override string DefaultConvertReturnValue { get; set; } = string.Empty;

    /// <summary>
    /// 默认返回值，当转换回失败时返回
    /// </summary>
    public override Dictionary<string, string> DefaultConvertBackReturnValue { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// 将字典转换为字符串
    /// </summary>
    /// <param name="value">字典</param>
    /// <param name="parameter">分隔符，默认为=</param>
    /// <param name="culture">文化信息</param>
    /// <returns>字符串，每行一个键值对</returns>
    public override string ConvertFrom(Dictionary<string, string> value, string parameter, CultureInfo? culture = null)
    {
        if (value == null || value.Count == 0)
        {
            return string.Empty;
        }

        string separator = parameter ?? "=";
        return string.Join(Environment.NewLine, value.Select(kv => $"{kv.Key}{separator}{kv.Value}"));
    }

    /// <summary>
    /// 将字符串转换为字典
    /// </summary>
    /// <param name="value">字符串，每行一个键值对</param>
    /// <param name="parameter">分隔符，默认为=</param>
    /// <param name="culture">文化信息</param>
    /// <returns>字典</returns>
    public override Dictionary<string, string> ConvertBackTo(string value, string parameter, CultureInfo? culture = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, string>();
        }

        string separator = parameter ?? "=";
        var result = new Dictionary<string, string>();

        string[] lines = value.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            int separatorIndex = line.IndexOf(separator);
            if (separatorIndex > 0)
            {
                string key = line.Substring(0, separatorIndex).Trim();
                string val = line.Substring(separatorIndex + separator.Length).Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    result[key] = val;
                }
            }
        }

        return result;
    }
}