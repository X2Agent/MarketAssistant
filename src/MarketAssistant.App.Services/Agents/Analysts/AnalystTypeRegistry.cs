using System.Reflection;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 统一发现当前应用中已加载的分析师实现类型。
/// </summary>
public static class AnalystTypeRegistry
{
    private static readonly Lazy<IReadOnlyList<Type>> _cachedTypes =
        new(DiscoverConcreteAnalystTypes, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<Type> GetConcreteAnalystTypes() => _cachedTypes.Value;

    private static IReadOnlyList<Type> DiscoverConcreteAnalystTypes()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(IsConcreteAnalystType)
            .DistinctBy(type => type.FullName)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null)!;
        }
    }

    private static bool IsConcreteAnalystType(Type type)
    {
        return type.IsSubclassOf(typeof(AnalystAgentBase)) && !type.IsAbstract;
    }
}
