using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 将技术异常转换为用户友好的错误消息
/// </summary>
public static class ErrorMessageMapper
{
    public static string GetUserFriendlyMessage(Exception exception)
    {
        return exception switch
        {
            // 友好异常直接返回消息
            FriendlyException => exception.Message,

            // 取消相关异常：通过 CancellationToken 区分用户主动取消与超时
            TaskCanceledException tce when !tce.CancellationToken.IsCancellationRequested
                => "AI 模型响应超时，可能是模型服务繁忙或网络不稳定，请稍后重试",
            TaskCanceledException => "请求超时，请稍后重试",
            OperationCanceledException oce when !oce.CancellationToken.IsCancellationRequested
                => "操作超时，请稍后重试",
            OperationCanceledException => "操作已取消",

            // 网络相关异常
            HttpRequestException => "网络连接失败，请检查网络设置",
            SocketException => "网络连接失败，请检查网络设置",
            TimeoutException => "操作超时，请稍后重试",

            // 权限相关异常
            UnauthorizedAccessException => "权限不足，请以管理员身份运行或检查文件权限",

            // LLM 供应商错误：必须透出服务端原文。
            // 典型场景（实测）：模型 slug 下线时服务端返回 404，响应体写明
            // "This model is unavailable for free ... use this slug instead: minimax/minimax-m3"。
            // 若映射成通用文案，用户只能看到"操作失败"，无法自查修复，必须去翻日志。
            // 注意：按类型名匹配，避免 Core 依赖 System.ClientModel。
            Exception ex when IsClientResultException(ex, out var serviceMessage)
                => BuildServiceErrorMessage(serviceMessage),

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
            // 注意：StackOverflowException 在 .NET 上直接终止进程、不可捕获，此处不做映射
            OutOfMemoryException => "内存不足，请关闭其他应用后重试",

            // 数据库相关异常：通过类型名匹配，避免 Core 直接依赖 SQLite 包
            Exception ex when ex.GetType().Name.Contains("Sqlite", StringComparison.Ordinal)
                => "数据库错误，数据可能已损坏",

            InvalidCastException => "数据类型转换失败",
            NullReferenceException => "数据访问错误，请刷新后重试",

            _ => "操作失败，请稍后重试"
        };
    }

    public static string GetUserFriendlyMessageWithContext(Exception exception, string operationName)
    {
        var baseMessage = GetUserFriendlyMessage(exception);
        return $"{operationName}失败：{baseMessage}";
    }

    /// <summary>
    /// 识别 LLM 供应商异常（System.ClientModel.ClientResultException）并取出其 <c>Message</c>。
    /// </summary>
    /// <remarks>
    /// 按类型名 + 反射匹配，而非直接引用该包：<c>MarketAssistant.Core</c> 不应为了
    /// 一条错误文案引入 LLM SDK 依赖。Message 已由 SDK 组合好（状态码 + 服务端原文），
    /// 无需再单独取 Status 与响应体。取不到可用 Message 时返回 false，
    /// 由调用方落入默认文案，避免错误映射器自身抛异常。
    /// </remarks>
    private static bool IsClientResultException(Exception exception, out string message)
    {
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (!string.Equals(type.Name, "ClientResultException", StringComparison.Ordinal))
            {
                continue;
            }

            var value = type.GetProperty("Message")?.GetValue(exception) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                message = string.Empty;
                return false;
            }

            message = value.Trim();
            return true;
        }

        message = string.Empty;
        return false;
    }

    /// <summary>
    /// 拼装供应商错误文案：保留服务端原文（它是唯一能指导用户修复的信息），
    /// 跳过无信息量的状态码行（如 "HTTP 404 (: )"），并限长避免撑破提示浮层。
    /// </summary>
    /// <remarks>
    /// 实测形态：Message 为多行，首行仅是状态码（"HTTP 404 (: )"），
    /// 真正有用的说明在后续行（"This model is unavailable for free ... use this slug
    /// instead: minimax/minimax-m3"）。若只取首行，用户依旧看不到可操作信息。
    /// </remarks>
    private static string BuildServiceErrorMessage(string serviceMessage)
    {
        var lines = serviceMessage
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        // 首选第一条"有实质内容"的行；全为噪声时退回首行，保证不丢失服务端信息
        var detail = lines.FirstOrDefault(line => !IsNoiseLine(line))
            ?? lines.FirstOrDefault()
            ?? string.Empty;

        // 服务端原文过长时截断：既够用户判断，也不破坏 UI 布局。
        // 按 TextElement 截断，避免把 emoji 等代理对切成非法字符串。
        const int MaxLength = 200;
        var elements = new StringInfo(detail);
        var truncated = elements.LengthInTextElements <= MaxLength
            ? detail
            : elements.SubstringByTextElements(0, MaxLength) + "…";
        return $"模型服务返回错误：{truncated}";
    }

    /// <summary>
    /// 判断是否为无信息量的状态码行，例如 "HTTP 404 (: )"、"HTTP 500"。
    /// </summary>
    private static bool IsNoiseLine(string line)
        => line.Contains("(: )", StringComparison.Ordinal)
            || (line.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase)
                && !line.Contains(':', StringComparison.Ordinal));
}

