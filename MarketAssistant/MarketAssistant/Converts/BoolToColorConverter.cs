using System.Globalization;

namespace MarketAssistant.Converts
{
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                if (parameter is string paramString)
                {
                    var colors = paramString.Split(',');
                    if (colors.Length >= 2)
                    {
                        return boolValue ? colors[0] : colors[1];
                    }
                }
                return boolValue ? Colors.Green : Colors.Red;
            }
            return Colors.Gray;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}