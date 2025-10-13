using System.Net.Sockets;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 将技术异常转换为用户友好的错误消息
/// </summary>
public static class ErrorMessageMapper
{
    /// <summary>
    /// 获取用户友好的错误消息
    /// </summary>
    public static string GetUserFriendlyMessage(Exception exception)
    {
        return exception switch
        {
            // 友好异常直接返回消息
            FriendlyException => exception.Message,
            
            // 取消相关异常
            TaskCanceledException => "请求超时，请稍后重试",
            OperationCanceledException => "操作已取消",
            
            // 网络相关异常
            HttpRequestException => "网络连接失败，请检查网络设置",
            SocketException => "网络连接失败，请检查网络设置",
            TimeoutException => "操作超时，请稍后重试",
            
            // 权限相关异常
            UnauthorizedAccessException => "权限不足，请以管理员身份运行或检查文件权限",
            
            // 参数相关异常（子类在前）
            ArgumentNullException => "参数错误，请检查输入",
            ArgumentOutOfRangeException => "参数超出有效范围",
            ArgumentException => "参数错误，请检查输入",
            
            // 操作相关异常（子类在前）
            ObjectDisposedException => "资源已释放，请重新打开",
            InvalidOperationException => "操作无效，请刷新后重试",
            NotSupportedException => "当前操作不受支持",
            NotImplementedException => "该功能尚未实现",
            
            // 文件IO相关异常（子类在前）
            FileNotFoundException => "文件未找到",
            DirectoryNotFoundException => "目录未找到",
            PathTooLongException => "文件路径过长",
            IOException => "文件读写失败，请检查权限或磁盘空间",
            
            // 数据相关异常
            FormatException => "数据格式错误",
            JsonException => "数据解析失败，请检查数据格式",
            
            // 系统资源相关异常
            OutOfMemoryException => "内存不足，请关闭其他应用后重试",
            StackOverflowException => "程序错误，请重启应用",
            
            // 数据库相关异常
            Microsoft.Data.Sqlite.SqliteException => "数据库错误，数据可能已损坏",
            
            InvalidCastException => "数据类型转换失败",
            NullReferenceException => "数据访问错误，请刷新后重试",
            
            _ => "操作失败，请稍后重试"
        };
    }

    /// <summary>
    /// 获取带上下文的用户友好错误消息
    /// </summary>
    public static string GetUserFriendlyMessageWithContext(Exception exception, string operationName)
    {
        var baseMessage = GetUserFriendlyMessage(exception);
        return $"{operationName}失败：{baseMessage}";
    }
}

