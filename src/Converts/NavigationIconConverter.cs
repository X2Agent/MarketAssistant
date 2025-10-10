using Avalonia.Data.Converters;
using MarketAssistant.ViewModels;
using System.Globalization;

namespace MarketAssistant.Converts
{
    /// <summary>
    /// 导航图标转换器，根据选中状态返回对应的 SVG 路径
    /// </summary>
    public class NavigationIconConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count != 2 ||
                values[0] is not NavigationItemViewModel navigationItem ||
                values[1] is not bool isSelected)
            {
                return null;
            }

            // 直接返回 SVG 路径，让 Svg 控件处理
            return isSelected ? navigationItem.SelectedIconPath : navigationItem.IconPath;
        }
    }
}
