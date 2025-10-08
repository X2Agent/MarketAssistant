namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 导航消息
/// </summary>
public class NavigationMessage
{
    public string PageName { get; }
    public object? Parameter { get; }

    public NavigationMessage(string pageName, object? parameter = null)
    {
        PageName = pageName;
        Parameter = parameter;
    }
}

