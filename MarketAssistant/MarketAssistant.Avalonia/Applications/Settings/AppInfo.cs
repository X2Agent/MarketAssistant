using System.Reflection;

namespace MarketAssistant.Applications.Settings;

/// <summary>
/// 应用信息类 - 统一管理应用相关信息和常量
/// </summary>
public static class AppInfo
{
    #region 应用基本信息
    
    /// <summary>
    /// 应用名称（用于文件系统路径等）
    /// </summary>
    public const string AppName = "MarketAssistant";
    
    /// <summary>
    /// 应用包名（程序集名称）
    /// </summary>
    public static string PackageName => Assembly.GetExecutingAssembly().GetName().Name ?? "MarketAssistant.Avalonia";
    
    /// <summary>
    /// 应用版本
    /// </summary>
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    
    /// <summary>
    /// 应用产品名称
    /// </summary>
    public const string ProductName = "Market Assistant";
    
    /// <summary>
    /// 应用标题
    /// </summary>
    public const string Title = "Market Assistant";
    
    /// <summary>
    /// 公司名称
    /// </summary>
    public const string Company = "X2Agent";
    
    /// <summary>
    /// 版权信息
    /// </summary>
    public const string Copyright = "Copyright © X2Agent";
    
    /// <summary>
    /// 应用描述
    /// </summary>
    public const string Description = "AI大模型构建的股票分析助手";
    
    /// <summary>
    /// 应用根目录
    /// </summary>
    public static string AppRootDirectory => AppDomain.CurrentDomain.BaseDirectory;
    
    #endregion
    
    #region 应用显示信息
    
    /// <summary>
    /// 应用显示名称（用于UI显示）
    /// </summary>
    public const string AppDisplayName = "Market Assistant";
    
    #endregion
    
    #region GitHub相关常量
    
    /// <summary>
    /// GitHub仓库所有者
    /// </summary>
    public const string GitHubOwner = "X2Agent";
    
    /// <summary>
    /// GitHub仓库名称
    /// </summary>
    public const string GitHubRepo = "MarketAssistant";
    
    /// <summary>
    /// GitHub API基础URL
    /// </summary>
    public const string GitHubApiBaseUrl = "https://api.github.com";
    
    /// <summary>
    /// 用户代理字符串
    /// </summary>
    public const string UserAgent = "MarketAssistant";
    
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
    
    /// <summary>
    /// 应用数据目录名称
    /// </summary>
    public const string AppDataDirectoryName = "AppData";
    
    #endregion
    
    #region 网站和URL常量
    
    /// <summary>
    /// 官方网站
    /// </summary>
    public const string OfficialWebsite = "https://haoai.xyz/market-assistant.html";
    
    /// <summary>
    /// 更新日志地址
    /// </summary>
    public const string ChangelogUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases";
    
    /// <summary>
    /// 意见反馈地址
    /// </summary>
    public const string FeedbackUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/issues";
    
    /// <summary>
    /// GitHub仓库地址
    /// </summary>
    public const string GitHubRepoUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}";
    
    /// <summary>
    /// 许可证地址
    /// </summary>
    public const string LicenseUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/blob/main/LICENSE";
    
    #endregion
    

}