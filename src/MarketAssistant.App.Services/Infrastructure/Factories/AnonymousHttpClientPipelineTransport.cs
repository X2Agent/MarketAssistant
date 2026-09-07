using System.ClientModel.Primitives;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 为明确允许匿名访问的 OpenAI-compatible 端点移除 SDK 自动添加的 Authorization 请求头。
/// </summary>
internal sealed class AnonymousHttpClientPipelineTransport(HttpClient httpClient)
    : HttpClientPipelineTransport(httpClient)
{
    protected override void OnSendingRequest(PipelineMessage message, HttpRequestMessage httpRequest)
    {
        base.OnSendingRequest(message, httpRequest);
        httpRequest.Headers.Authorization = null;
    }
}
