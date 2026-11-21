using System.Reflection;
using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.PromptConfiguration;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Agents.Analysts;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant.Analysis;

/// <summary>
/// 分析师扩展点一致性：新增分析师要在多处同步登记（YAML 提示词、AnalystNames 常量、AnalystSignalParser 映射、
/// SupportedMarkets、各市场的 Keyed 工具注册），漏掉任何一处都不会报错而是静默降级——提示词回退到共用配置、
/// 信号被当作未知作者丢弃、工具缺失时分析师没有数据照常出报告。
/// 这里以 AnalystTypeRegistry 为唯一事实来源逐项核对。
/// </summary>
[TestClass]
public sealed class AnalystRegistrationConsistencyTest
{
    /// <summary>不产出方向性信号的分析师：协调分析师以 CoordinatorResult 为准，项目指标分析师没有方向性字段。</summary>
    private static readonly string[] SignalExemptNames = [AnalystNames.Coordinator, AnalystNames.CryptoMetrics];

    private static readonly string PromptsDirectory = Path.Combine(AppContext.BaseDirectory, "config", "prompts");

    private static readonly HashSet<string> RegisteredNames = typeof(AnalystNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.Name != nameof(AnalystNames.SystemNotice))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    [TestMethod]
    [TestCategory("Unit")]
    public void EveryAnalyst_ShouldHaveRegisteredName()
    {
        foreach (var type in GetProductionAnalystTypes())
        {
            Assert.Contains(GetAnalystName(type), RegisteredNames,
                $"{type.Name} 对应的分析师名未在 AnalystNames 注册（消息作者名、信号解析、卡片分发都以它为准）");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void EveryAnalyst_InEverySupportedMarket_ShouldResolvePromptConfigNamedAfterIt()
    {
        var loader = new AnalystPromptLoader(NullLogger<AnalystPromptLoader>.Instance);

        foreach (var type in GetProductionAnalystTypes())
        {
            var analystName = GetAnalystName(type);
            var supportedMarkets = Enum.GetValues<MarketType>()
                .Where(market => SupportedMarketsAttribute.SupportsMarket(type, market))
                .ToList();
            Assert.IsNotEmpty(supportedMarkets, $"{type.Name} 没有任何受支持的市场，永远不会被启用");

            foreach (var market in supportedMarkets)
            {
                // 文件缺失，或 name / instructions 为空时，加载器会抛出异常
                var config = loader.GetConfig(market, analystName);

                Assert.AreEqual(analystName, config.Name,
                    $"{market}/{analystName} 的 YAML name 必须与分析师名一致（Token 归属、信号解析按它匹配）");
            }
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void EveryAnalyst_ShouldBeMappedBySignalParserOrExplicitlyExempt()
    {
        foreach (var type in GetProductionAnalystTypes())
        {
            var analystName = GetAnalystName(type);
            // 空对象就足以触发映射分支：结果模型的字段均有缺省值，映射成功时产出字段全为 null 的快照
            var snapshot = AnalystSignalParser.Parse(analystName, "{}");

            if (SignalExemptNames.Contains(analystName))
            {
                Assert.IsNull(snapshot, $"{analystName} 在豁免名单内却已有信号映射，请把它移出 SignalExemptNames");
                continue;
            }

            Assert.IsNotNull(snapshot,
                $"{analystName} 缺少 AnalystSignalParser 映射；若它不产出方向性信号，请加入 SignalExemptNames 并在 Agents/AGENTS.md 说明");
        }
    }

    // 工具缺失时 ResolveToolsFor 只记错误日志并继续，分析师会在没有数据的情况下照常出报告。
    // 市场模块的注册在运行期是固定的，漏注册只可能出在开发期，所以在这里一次性拦住：
    // 含新增市场漏模块，也含只缺部分工具的情形
    [TestMethod]
    [TestCategory("Unit")]
    public void EveryAnalyst_RequiredTools_ShouldBeRegisteredForEverySupportedMarket()
    {
        var modules = typeof(AnalystTypeRegistry).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IMarketModule).IsAssignableFrom(type))
            .Select(type => (IMarketModule)Activator.CreateInstance(type)!)
            .ToList();
        var services = new ServiceCollection();
        foreach (var module in modules)
        {
            module.Register(services);
        }

        foreach (var market in Enum.GetValues<MarketType>())
        {
            Assert.IsTrue(modules.Any(module => module.MarketType == market),
                $"{market} 没有对应的 IMarketModule，该市场的 Keyed 工具一个都不会注册");
        }

        var registered = services
            .Where(descriptor => descriptor.IsKeyedService)
            .Select(descriptor => (descriptor.ServiceType, descriptor.ServiceKey))
            .ToHashSet();
        var missing = new List<string>();
        foreach (var type in GetProductionAnalystTypes())
        {
            var requiredToolTypes = type.GetCustomAttributes<RequiresToolsAttribute>()
                .Select(attribute => attribute.ToolInterfaceType)
                .ToList();

            foreach (var market in Enum.GetValues<MarketType>().Where(item => SupportedMarketsAttribute.SupportsMarket(type, item)))
            {
                missing.AddRange(requiredToolTypes
                    .Where(toolType => !registered.Contains((toolType, market)))
                    .Select(toolType => $"{type.Name}@{market}:{toolType.Name}"));
            }
        }

        Assert.IsEmpty(missing, $"分析师声明的工具在对应市场没有 Keyed 注册：{string.Join("；", missing)}");
    }

    // 覆写文件名拼错只会被静默忽略并回退到共用配置，覆写在不支持的市场下也永远不会生效
    [TestMethod]
    [TestCategory("Unit")]
    public void PromptFiles_ShouldBelongToKnownAnalystAndSupportedMarket()
    {
        var analystsByName = GetProductionAnalystTypes().ToDictionary(GetAnalystName, StringComparer.Ordinal);

        foreach (var file in Directory.GetFiles(PromptsDirectory, "*.yaml"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            Assert.IsTrue(analystsByName.ContainsKey(name), $"共用提示词 {name}.yaml 没有对应的分析师类型");
        }

        foreach (var directory in Directory.GetDirectories(PromptsDirectory))
        {
            var marketName = Path.GetFileName(directory);
            Assert.IsTrue(Enum.TryParse<MarketType>(marketName, out var market),
                $"提示词覆写目录 {marketName} 不是有效的 MarketType");

            foreach (var file in Directory.GetFiles(directory, "*.yaml"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                Assert.IsTrue(analystsByName.TryGetValue(name, out var type),
                    $"覆写提示词 {marketName}/{name}.yaml 没有对应的分析师类型");
                Assert.IsTrue(SupportedMarketsAttribute.SupportsMarket(type, market),
                    $"覆写提示词 {marketName}/{name}.yaml 所属分析师不支持该市场，覆写不会生效");
            }
        }
    }

    /// <summary>仅取应用自身的分析师类型，排除测试程序集里用于探针的子类。</summary>
    private static List<Type> GetProductionAnalystTypes() =>
        [.. AnalystTypeRegistry.GetConcreteAnalystTypes()
            .Where(type => type.Assembly == typeof(AnalystTypeRegistry).Assembly)];

    /// <summary>分析师名约定：类型名去掉 Agent 后缀（与 AnalystAgentFactory 解析提示词的规则一致）。</summary>
    private static string GetAnalystName(Type type) =>
        type.Name.EndsWith("Agent", StringComparison.Ordinal) ? type.Name[..^"Agent".Length] : type.Name;
}
