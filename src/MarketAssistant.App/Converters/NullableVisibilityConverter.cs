using System.Globalization;
using Avalonia.Data.Converters;

namespace MarketAssistant.Converters;

/// <summary>
/// �ɿ�ֵ�ɼ���ת���� - ��ֵΪ null ʱ���� false������ IsVisible ��
/// </summary>
public class NullableVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // ����Ƿ�Ϊ null
        if (value == null)
            return false;

        // ����ַ����Ƿ�Ϊ��
        if (value is string str && string.IsNullOrWhiteSpace(str))
            return false;

        // ����ֵ���ͣ�����Ƿ���ֵ
        var type = value.GetType();

        // ����� Nullable<T> ����
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var hasValueProperty = type.GetProperty("HasValue");
            if (hasValueProperty != null)
            {
                var hasValue = (bool)(hasValueProperty.GetValue(value) ?? false);
                return hasValue;
            }
        }

        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("NullableVisibilityConverter does not support ConvertBack");
    }
}
