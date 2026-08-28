using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// 递归验证模型结构化输出中的 DataAnnotations 和枚举值。
/// </summary>
public static class StructuredOutputValidator
{
    public static IReadOnlyList<string> Validate(object? value)
    {
        if (value is null)
        {
            return ["结构化输出不能为空"];
        }

        var errors = new List<string>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        // NullabilityInfoContext 非线程安全（内部有可变缓存），且本类可能被并发调用，
        // 故每次 Validate 新建实例而非共享 static readonly
        var nullabilityContext = new NullabilityInfoContext();
        ValidateNode(value, "$", errors, visited, nullabilityContext);
        return errors;
    }

    private static void ValidateNode(
        object value,
        string path,
        List<string> errors,
        HashSet<object> visited,
        NullabilityInfoContext nullabilityContext)
    {
        var type = value.GetType();
        if (IsTerminalType(type))
        {
            if (type.IsEnum && !Enum.IsDefined(type, value))
            {
                errors.Add($"{path}: 枚举值 {value} 无效");
            }

            return;
        }

        if (!type.IsValueType && !visited.Add(value))
        {
            return;
        }

        if (value is IEnumerable enumerable)
        {
            var index = 0;
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    errors.Add($"{path}[{index}]: 值不能为空");
                }
                else
                {
                    ValidateNode(item, $"{path}[{index}]", errors, visited, nullabilityContext);
                }

                index++;
            }

            return;
        }

        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(
            value,
            new ValidationContext(value),
            validationResults,
            validateAllProperties: true);

        foreach (var result in validationResults)
        {
            var members = result.MemberNames.Any()
                ? string.Join(", ", result.MemberNames.Select(member => $"{path}.{member}"))
                : path;
            errors.Add($"{members}: {result.ErrorMessage}");
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var propertyValue = property.GetValue(value);
            if (propertyValue is null)
            {
                if (nullabilityContext.Create(property).ReadState == NullabilityState.NotNull)
                {
                    errors.Add($"{path}.{property.Name}: 值不能为空");
                }

                continue;
            }

            ValidateNode(propertyValue, $"{path}.{property.Name}", errors, visited, nullabilityContext);
        }
    }

    private static bool IsTerminalType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType.IsPrimitive
            || underlyingType.IsEnum
            || underlyingType == typeof(string)
            || underlyingType == typeof(decimal)
            || underlyingType == typeof(DateTime)
            || underlyingType == typeof(DateTimeOffset)
            || underlyingType == typeof(TimeSpan)
            || underlyingType == typeof(Guid);
    }
}
