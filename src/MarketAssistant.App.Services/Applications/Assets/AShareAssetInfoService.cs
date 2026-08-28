using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// A股资产信息服务实现
/// </summary>
public class AShareAssetInfoService : IAssetInfoService
{
    private readonly ClsQuoteClient _clsClient;
    private readonly SinaFundFlowClient _sinaFundFlowClient;
    private readonly ILogger<AShareAssetInfoService> _logger;

    public AShareAssetInfoService(
        ClsQuoteClient clsClient,
        SinaFundFlowClient sinaFundFlowClient,
        ILogger<AShareAssetInfoService> logger)
    {
        _clsClient = clsClient ?? throw new ArgumentNullException(nameof(clsClient));
        _sinaFundFlowClient = sinaFundFlowClient ?? throw new ArgumentNullException(nameof(sinaFundFlowClient));
        _logger = logger;
    }

    public async Task<List<(string Name, string Code)>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        try
        {
            // HTTP 访问与解析由 ClsQuoteClient 负责
            var items = await _clsClient.SearchStocksAsync(keyword, cancellationToken);
            return items.Select(i => (i.Name, i.StockId)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "搜索股票失败，返回空结果");
            return [];
        }
    }

    public async Task<AssetInfo> GetAssetInfoAsync(string code, string market = "", CancellationToken cancellationToken = default)
    {
        var assetInfo = new AssetInfo
        {
            Code = code,
            Name = "未知股票",
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

            // 股票代码 & 市场
            var rawCode = data.SecurityCode?.Trim() ?? "";
            if (rawCode.StartsWith("SH", StringComparison.OrdinalIgnoreCase))
            {
                assetInfo.Market = "SH";
                assetInfo.Code = rawCode[2..];
            }
            else if (rawCode.StartsWith("SZ", StringComparison.OrdinalIgnoreCase))
            {
                assetInfo.Market = "SZ";
                assetInfo.Code = rawCode[2..];
            }
            else
            {
                assetInfo.Code = rawCode;
            }

            // 当前价格
            assetInfo.CurrentPrice = PriceFormatter.Format(data.LastPrice);

            // 涨跌幅（CLS 的 change 为小数比率，如 -0.0082 表示 -0.82%）
            var changeRatio = data.Change;
            assetInfo.ChangePercentage = $"{changeRatio * 100:+0.00;-0.00;0.00}%";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取股票详细数据异常: {Code}", code);
        }

        return assetInfo;
    }

    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        // HTTP 访问、GBK 解码与解析由 SinaFundFlowClient 负责；此处仅做业务映射。
        try
        {
            var items = await _sinaFundFlowClient.GetTopNetInflowAsync(12);

            return items.Select(item =>
            {
                var market = item.Symbol.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ? "SH" :
                             item.Symbol.StartsWith("sz", StringComparison.OrdinalIgnoreCase) ? "SZ" :
                             item.Symbol.StartsWith("bj", StringComparison.OrdinalIgnoreCase) ? "BJ" : "";

                return new HotAsset
                {
                    Name = item.Name,
                    Code = item.Symbol[2..],
                    Market = market,
                    CurrentPrice = item.Price,
                    ChangePercentage = $"{item.ChangeRatio * 100:+0.00;-0.00;0.00}%",
                    MetricLabel = "净流入",
                    MetricValue = item.NetAmount.ToString("F0"),
                    MarketType = MarketType.AShare
                };
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHotAssetsAsync: {Message}", ex.Message);
            throw new Infrastructure.Core.FriendlyException($"获取热门股票失败: {ex.Message}", ex);
        }
    }
}
