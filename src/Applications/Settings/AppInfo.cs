using System.Reflection;

namespace MarketAssistant.Applications.Settings;

/// <summary>
/// 应用信息类 - 统一管理应用相关信息和常量
/// </summary>
public static class AppInfo
{
    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();

    #region 应用基本信息

    /// <summary>
    /// 应用名称（用于文件系统路径等，对应 AssemblyName）
    /// </summary>
    public static string AppName => _assembly.GetName().Name ?? "MarketAssistant";

    /// <summary>
    /// 应用版本（优先使用语义化版本）
    /// </summary>
    public static string Version
    {
        get
        {
            // 尝试获取语义化版本（对应 csproj 中的 <Version>）
            var version = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            // 去除可能包含的 commit hash (例如 1.0.1+git_hash)
            if (!string.IsNullOrEmpty(version) && version.Contains('+'))
            {
                version = version.Split('+')[0];
            }

            // 如果获取失败，回退到 AssemblyVersion (通常是 1.0.1.0 格式)
            if (string.IsNullOrEmpty(version))
            {
                version = _assembly.GetName().Version?.ToString();
            }

            // 如果还是空，返回默认值
            return version ?? "1.0.0";
        }
    }


    /// <summary>
    /// 应用产品名称
    /// </summary>
    public static string Product => _assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Market Assistant";

    /// <summary>
    /// 应用标题（对应 AssemblyTitle，如果为空则使用 Product）
    /// </summary>
    public static string Title => _assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? Product;

    /// <summary>
    /// 公司名称
    /// </summary>
    public static string Company => _assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "X2Agent";

    /// <summary>
    /// 版权信息
    /// </summary>
    public static string Copyright => _assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright © X2Agent";

    /// <summary>
    /// 应用描述
    /// </summary>
    public static string Description => _assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "AI大模型构建的股票分析助手";

    #endregion

    #region GitHub相关常量

    /// <summary>
    /// GitHub API基础URL
    /// </summary>
    public const string GitHubApiBaseUrl = "https://api.github.com";

    /// <summary>
    /// 用户代理字符串
    /// </summary>
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0";

    #endregion

    #region 文件和目录常量

    /// <summary>
    /// 偏好设置文件名
    /// </summary>
    public const string PreferencesFileName = "preferences.json";

    /// <summary>
    /// MCP服务器配置文件名称
    /// </summary>
    public const string MCPServerConfigFileName = "mcpservers.json";

    /// <summary>
    /// 日志目录名称
    /// </summary>
    public const string LogsDirectoryName = "logs";

    /// <summary>
    /// 缓存目录名称
    /// </summary>
    public const string CacheDirectoryName = "Cache";

    #endregion

    #region 网站和URL常量

    /// <summary>
    /// 官方网站
    /// </summary>
    public const string OfficialWebsite = "https://haoai.xyz/market-assistant.html";

    /// <summary>
    /// 更新日志地址
    /// </summary>
    public static string ChangelogUrl => $"https://github.com/{Company}/{AppName}/releases";

    /// <summary>
    /// 意见反馈地址
    /// </summary>
    public static string FeedbackUrl => $"https://github.com/{Company}/{AppName}/issues";

    /// <summary>
    /// GitHub仓库地址
    /// </summary>
    public static string GitHubRepoUrl => $"https://github.com/{Company}/{AppName}";

    /// <summary>
    /// 许可证地址
    /// </summary>
    public static string LicenseUrl => $"https://github.com/{Company}/{AppName}/blob/main/LICENSE";

    /// <summary>
    /// 官方QQ群号码
    /// </summary>
    public const string QQGroupNumber = "1012116859";

    /// <summary>
    /// 加入QQ群链接
    /// </summary>
    public const string QQGroupUrl = "https://qm.qq.com/q/lFE1Cc8d5m";

    #endregion
}