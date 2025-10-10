using System.Globalization;
using Avalonia.Data.Converters;

namespace MarketAssistant.Converts
{
    /// <summary>
    /// 将字典转换为字符串，每行一个键值对，格式为KEY=value
    /// </summary>
    public class DictionaryToStringConverter : IValueConverter
    {
        /// <summary>
        /// 将字典转换为字符串
        /// </summary>
        /// <param name="value">字典</param>
        /// <param name="targetType">目标类型</param>
        /// <param name="parameter">分隔符，默认为=</param>
        /// <param name="culture">文化信息</param>
        /// <returns>字符串，每行一个键值对</returns>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not Dictionary<string, string> dictionary || dictionary.Count == 0)
            {
                return string.Empty;
            }

            string separator = parameter?.ToString() ?? "=";
            return string.Join(Environment.NewLine, dictionary.Select(kv => $"{kv.Key}{separator}{kv.Value}"));
        }

        /// <summary>
        /// 将字符串转换为字典
        /// </summary>
        /// <param name="value">字符串，每行一个键值对</param>
        /// <param name="targetType">目标类型</param>
        /// <param name="parameter">分隔符，默认为=</param>
        /// <param name="culture">文化信息</param>
        /// <returns>字典</returns>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string stringValue || string.IsNullOrWhiteSpace(stringValue))
            {
                return new Dictionary<string, string>();
            }

            string separator = parameter?.ToString() ?? "=";
            var result = new Dictionary<string, string>();

            string[] lines = stringValue.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries);
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
}