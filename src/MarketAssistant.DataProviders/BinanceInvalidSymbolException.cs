using System.Net;

namespace MarketAssistant.DataProviders;

/// <summary>币安明确返回交易对不存在（错误码 -1121）。</summary>
public sealed class BinanceInvalidSymbolException()
    : HttpRequestException("币安交易对不存在或当前不可交易", null, HttpStatusCode.BadRequest);
