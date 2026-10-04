namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 导航消息
/// </summary>
public class NavigationMessage(string pageName, object? parameter = null)
{
    public string PageName { get; } = pageName;
    public object? Parameter { get; } = parameter;
}
