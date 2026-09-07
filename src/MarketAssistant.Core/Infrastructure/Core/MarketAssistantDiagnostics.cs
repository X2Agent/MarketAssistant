using System.Diagnostics;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// MarketAssistant 统一诊断源。宿主可通过 OpenTelemetry AddSource 订阅。
/// Activity 仅记录低基数标识与运行元数据，不记录提示词、响应正文、工具参数或 Secret。
/// </summary>
public static class MarketAssistantDiagnostics
{
    public const string SourceName = "MarketAssistant";

    public static readonly ActivitySource Source = new(SourceName);

    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return Source.StartActivity(name, kind);
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        activity.SetTag("error.type", exception.GetType().FullName);
    }
}
