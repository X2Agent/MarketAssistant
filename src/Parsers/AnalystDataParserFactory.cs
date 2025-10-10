using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Parsers;

/// <summary>
/// 分析师数据解析器工厂 - 创建不同类型的解析器
/// </summary>
public class AnalystDataParserFactory
{
    private readonly Kernel _kernel;
    private readonly ILoggerFactory? _loggerFactory;

    public AnalystDataParserFactory(Kernel kernel, ILoggerFactory? loggerFactory = null)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 创建解析器实例
    /// </summary>
    /// <param name="strategy">解析策略</param>
    /// <returns>解析器实例</returns>
    public IAnalystDataParser CreateParser(ParsingStrategy strategy = ParsingStrategy.Hybrid)
    {
        var logger = _loggerFactory?.CreateLogger<AnalystDataParserFactory>();
        logger?.LogInformation("创建解析器，策略: {Strategy}", strategy);

        return strategy switch
        {
            ParsingStrategy.RegexOnly => new RegexAnalystDataParser(),
            ParsingStrategy.AIOnly => new AIAnalystDataParser(_kernel),
            ParsingStrategy.Hybrid => new HybridAnalystDataParser(_kernel, _loggerFactory?.CreateLogger<HybridAnalystDataParser>()),
            _ => new HybridAnalystDataParser(_kernel, _loggerFactory?.CreateLogger<HybridAnalystDataParser>())
        };
    }

    /// <summary>
    /// 根据内容特征推荐解析策略
    /// </summary>
    /// <param name="content">待解析内容</param>
    /// <returns>推荐的解析策略</returns>
    public ParsingStrategy RecommendStrategy(string content)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 解析策略枚举
/// </summary>
public enum ParsingStrategy
{
    /// <summary>
    /// 仅使用正则表达式解析
    /// </summary>
    RegexOnly,

    /// <summary>
    /// 仅使用AI解析
    /// </summary>
    AIOnly,

    /// <summary>
    /// 混合解析（正则+AI回退）
    /// </summary>
    Hybrid
}

/// <summary>
/// 依赖注入配置扩展
/// </summary>
public static class ParserServiceExtensions
{
    /// <summary>
    /// 注册解析器相关服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddAnalystDataParsers(this IServiceCollection services)
    {
        // 注册解析器工厂
        services.AddSingleton<AnalystDataParserFactory>();

        // 注册默认解析器
        services.AddScoped(provider =>
        {
            var factory = provider.GetRequiredService<AnalystDataParserFactory>();
            return factory.CreateParser();
        });

        return services;
    }
}