using System.Globalization;
using Microsoft.Maui.Controls;

namespace MarketAssistant.Converts
{
    /// <summary>
    /// 双向转换器，用于RadioButton的IsChecked属性与字符串值的双向绑定
    /// </summary>
    public class RadioButtonEqualityConverter : IValueConverter
    {
        /// <summary>
        /// 将字符串值转换为布尔值，用于RadioButton的IsChecked属性
        /// </summary>
        /// <param name="value">源值（字符串）</param>
        /// <param name="targetType">目标类型</param>
        /// <param name="parameter">参数（要比较的值）</param>
        /// <param name="culture">文化信息</param>
        /// <returns>如果源值等于参数，则返回true，否则返回false</returns>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            return value.ToString() == parameter.ToString();
        }

        /// <summary>
        /// 将布尔值转换回字符串值，用于更新源属性
        /// </summary>
        /// <param name="value">目标值（布尔值）</param>
        /// <param name="targetType">目标类型</param>
        /// <param name="parameter">参数（要设置的值）</param>
        /// <param name="culture">文化信息</param>
        /// <returns>如果布尔值为true，则返回参数值，否则返回null</returns>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isChecked)
            {
                if (isChecked && parameter != null)
                {
                    return parameter.ToString();
                }
                // 重要：未选中时不回写源，避免被置为 null 导致界面不刷新
                return Binding.DoNothing;
            }

            return Binding.DoNothing;
        }
    }
}