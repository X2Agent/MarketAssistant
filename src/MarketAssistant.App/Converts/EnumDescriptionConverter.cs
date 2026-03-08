using Avalonia.Data.Converters;
using MarketAssistant.Infrastructure.Extensions;
using System.Globalization;

namespace MarketAssistant.Converts;

public class EnumDescriptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            return enumValue.GetDescription();
        }

        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
