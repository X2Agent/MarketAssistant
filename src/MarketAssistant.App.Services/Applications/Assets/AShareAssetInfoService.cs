using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Market;
using MarketAssistant.DataProviders.AShare;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// A股资产信息服务实现。
/// 详情以财联社为主数据源，财联社不覆盖的标的（基金/ETF 等，实测返回全 null）回退新浪个股行情。
/// </summary>
public class AShareAssetInfoService(
    ClsQuoteClient clsClient,
    SinaStockQuoteClient sinaStockQuoteClient,
    SinaFundFlowClient sinaFundFlowClient,
    ILogger<AShareAssetInfoService> logger) : IAssetInfoService
{
    /// <summary>首页热门资产展示条数（2 列 × 5 行）</summary>
    private const int HotAssetCount = 10;

    private readonly ClsQuoteClient _clsClient = clsClient;
    private readonly SinaStockQuoteClient _sinaStockQuoteClient = sinaStockQuoteClient;
    private readonly SinaFundFlowClient _sinaFundFlowClient = sinaFundFlowClient;
    private readonly ILogger<AShareAssetInfoService> _logger = logger;

    public async Task<List<(string Name, string Code)>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        try
        {
            // HTTP 访问与解析由 ClsQuoteClient 负责
            var items = await _clsClient.SearchStocksAsync(keyword, cancellationToken);
            return [.. items.Select(i => (i.Name, i.StockId))];
        }
        catch (OperationCanceledException)
        {
            // 用户继续输入或切换市场导致的取消，交由调用方按取消处理
            throw;
        }
        catch (Exception ex)
        {
            // 不吞异常：返回空列表会让"数据源不可用"伪装成"没有匹配结果"
            _logger.LogError(ex, "搜索股票失败: {Keyword}", keyword);
            throw new FriendlyException($"搜索股票失败: {ex.Message}", ex);
        }
    }

    /// <summary>数据源正常但查无该资产时的名称占位（见 <see cref="IAssetInfoService.GetAssetInfoAsync"/>）。</summary>
    private const string UnknownStockName = "未知股票";

    public async Task<AssetInfo> GetAssetInfoAsync(string code, string market = "", CancellationToken cancellationToken = default)
    {
        var assetInfo = new AssetInfo
        {
            Code = code,
            Name = UnknownStockName,
            Market = market,
            MarketType = MarketType.AShare
        };

        try
        {
            var fullCode = string.IsNullOrEmpty(market) ? code : $"{market}{code}";
            var clsCode = StockSymbolConverter.ToClsFormat(fullCode);
            if (string.IsNullOrEmpty(clsCode))
                return assetInfo;

            var data = await _clsClient.GetStockQuoteAsync(
                clsCode, "secu_name,secu_code,last_px,change", cancellationToken);
            if (data is null)
                return assetInfo;

            // 股票名称
            if (!string.IsNullOrWhiteSpace(data.SecurityName))
                assetInfo.Name = data.SecurityName.Trim();

            // 股票代码 & 市场：统一走交易所前缀拆分（SH/SZ/BJ，大小写不敏感）。
            // 未识别前缀时保留既有市场归属、代码原样落入，不做默认猜测。
            var rawCode = data.SecurityCode?.Trim() ?? "";
            var (prefixMarket, prefixCode) = StockSymbolConverter.SplitExchangePrefix(rawCode);
            if (prefixMarket.Length > 0)
            {
                assetInfo.Market = prefixMarket;
            }

            // 财联社未返回代码时保留调用方传入的代码，避免卡片连代码都空掉
            if (prefixCode.Length > 0)
            {
                assetInfo.Code = prefixCode;
            }

            // 当前价格（CLS 对停牌/无成交返回 null，保留默认空串表示「未提供」，不伪造 0）
            if (data.LastPrice is { } lastPrice)
            {
                assetInfo.CurrentPrice = PriceFormatter.Format(lastPrice, MarketType.AShare, assetInfo.Code);
            }
            else
            {
                _logger.LogDebug("财联社未返回 {Code} 的最新价（可能停牌或暂无成交）", code);
            }

            // 涨跌幅（CLS 的 change 为小数比率，如 -0.0082 表示 -0.82%）。
            // 下游 PriceChangeColorConverter 以 InvariantCulture 解析该字符串
            if (data.Change is { } changeRatio)
            {
                assetInfo.ChangePercentage = PercentFormatter.Format(changeRatio * 100);
            }

            // 财联社对基金/ETF 等标的不覆盖（实测 secu_name 与行情字段全为 null），回退新浪个股行情。
            // 只在名称缺失时兜底：财联社有名称但无最新价属停牌，不能拿新浪的旧价伪装成实时行情。
            if (assetInfo.Name == UnknownStockName)
            {
                await FillFromSinaAsync(assetInfo, clsCode, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 不吞异常：返回"未知股票"会让"数据源不可用"伪装成"资产不存在"
            _logger.LogError(ex, "获取股票详细数据失败: {Code}", code);
            throw new FriendlyException($"获取股票信息失败: {ex.Message}", ex);
        }

        return assetInfo;
    }

    /// <summary>
    /// 新浪兜底：补名称、最新价与按昨收派生的涨跌幅。
    /// 新浪不返回涨跌幅，但返回昨收，故与详情页 K 线口径一致地现算。
    /// </summary>
    private async Task FillFromSinaAsync(AssetInfo assetInfo, string sinaCode, CancellationToken cancellationToken)
    {
        var quote = await _sinaStockQuoteClient.GetStockQuoteAsync(sinaCode, cancellationToken);
        if (quote is null)
        {
            _logger.LogInformation("财联社与新浪均未返回 {Code} 的资产详情", sinaCode);
            return;
        }

        assetInfo.Name = quote.Name;
        assetInfo.CurrentPrice = PriceFormatter.Format(quote.CurrentPrice, MarketType.AShare, assetInfo.Code);

        if (quote.PreClose is { } preClose && preClose > 0m)
        {
            assetInfo.ChangePercentage = PercentFormatter.Format((quote.CurrentPrice - preClose) / preClose * 100m);
        }
    }

    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        // HTTP 访问、GBK 解码与解析由 SinaFundFlowClient 负责；此处仅做业务映射。
        try
        {
            var items = await _sinaFundFlowClient.GetTopNetInflowAsync(HotAssetCount);

            return [.. items.Select(item =>
            {
                // 交易所标识统一拆分（SH/SZ/BJ）：新浪符号恒带两字符前缀，
                // 未识别时市场留空、代码原样落入（不再无条件截掉前两字符）
                var (market, code) = StockSymbolConverter.SplitExchangePrefix(item.Symbol);

                return new HotAsset
                {
                    Name = item.Name,
                    Code = code,
                    Market = market,
                    CurrentPrice = item.Price,
                    ChangePercentage = PercentFormatter.Format(item.ChangeRatio * 100),
                    MetricLabel = "净流入",
                    MetricValue = MarketValueFormatter.FormatAmount((decimal)item.NetAmount),
                    MarketType = MarketType.AShare
                };
            })];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHotAssetsAsync: {Message}", ex.Message);
            throw new Infrastructure.Core.FriendlyException($"获取热门股票失败: {ex.Message}", ex);
        }
    }
}
