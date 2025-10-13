namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 面向用户的友好异常，异常消息可以直接展示给用户
/// </summary>
public class FriendlyException : Exception
{
    public FriendlyException(string message) : base(message)
    {
    }

    public FriendlyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

