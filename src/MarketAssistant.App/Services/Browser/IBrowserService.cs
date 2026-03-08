namespace MarketAssistant.Services.Browser;

/// <summary>
/// 浏览器服务接口
/// </summary>
public interface IBrowserService
{
    /// <summary>
    /// 检查系统上安装的浏览器
    /// </summary>
    /// <returns>浏览器路径，如果未找到则返回空字符串</returns>
    string CheckBrowser();
}
