using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// A股资产信息服务实现
/// </summary>
public class AShareAssetInfoService(
    ClsQuoteClient clsClient,
    SinaFundFlowClient sinaFundFlowClient,
    ILogger<AShareAssetInfoService> logger) : IAssetInfoService
{
    /// <summary>首页热门资产展示条数（2 列 × 5 行）</summary>
    private const int HotAssetCount = 10;

    private readonly ClsQuoteClient _clsClient = clsClient ?? throw new ArgumentNullException(nameof(clsClient));
    private readonly SinaFundFlowClient _sinaFundFlowClient = sinaFundFlowClient ?? throw new ArgumentNullException(nameof(sinaFundFlowClient));
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

            // 涨跌幅（CLS 的 change 为小数比率，如 -0.0082 表示 -0.82%）。
            // InvariantCulture：下游 PriceChangeColorConverter 以 InvariantCulture 解析该字符串
            var changeRatio = data.Change;
            assetInfo.ChangePercentage = (changeRatio * 100).ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture) + "%";
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

    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        // HTTP 访问、GBK 解码与解析由 SinaFundFlowClient 负责；此处仅做业务映射。
        try
        {
            var items = await _sinaFundFlowClient.GetTopNetInflowAsync(HotAssetCount);

            return [.. items.Select(item =>
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
                    ChangePercentage = (item.ChangeRatio * 100).ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture) + "%",
                    MetricLabel = "净流入",
                    MetricValue = item.NetAmount.ToString("F0"),
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
