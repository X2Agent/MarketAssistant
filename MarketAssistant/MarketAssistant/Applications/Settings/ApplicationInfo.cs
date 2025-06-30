namespace MarketAssistant.Applications.Settings;

public static class ApplicationInfo
{
    /// <summary>
    /// 应用名称
    /// </summary>
    public const string AppName = "Market Assistant";

    /// <summary>
    /// 应用版本
    /// </summary>
    public static string Version => AppInfo.VersionString;

    /// <summary>
    /// 应用描述
    /// </summary>
    public const string Description = "AI大模型构建的股票分析助手";

    /// <summary>
    /// 官方网站
    /// </summary>
    public const string OfficialWebsite = "https://github.com/X2Agent/MarketAssistant";

    /// <summary>
    /// 更新日志地址
    /// </summary>
    public const string ChangelogUrl = $"{GitHubRepo}/releases";

    /// <summary>
    /// 意见反馈地址
    /// </summary>
    public const string FeedbackUrl = $"{GitHubRepo}/issues";

    /// <summary>
    /// GitHub仓库地址
    /// </summary>
    public const string GitHubRepo = "https://github.com/X2Agent/MarketAssistant";

    /// <summary>
    /// 许可证地址
    /// </summary>
    public const string LicenseUrl = $"{GitHubRepo}/blob/main/LICENSE";
}