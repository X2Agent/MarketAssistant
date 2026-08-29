using MarketAssistant.Agents.Tools.Models;
using MarketAssistant.Agents.Tools.Models.Crypto;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 虚拟币市场指标工具接口
/// </summary>
/// <remarks>
/// 职责：提供市场深度数据、综合市场指标和衍生计算指标
/// 数据源：币安（市场深度）、CoinGecko（市场指标、交易量分布）
/// 
/// 注意：本接口不包含已由其他接口提供的功能：
/// - 24小时行情和项目基本面 → ICryptoBasicTools
/// - 资金费率、持仓量、多空比 → ICryptoSentimentTools  
/// - KDJ、MACD等技术指标 → ITechnicalDataTools
/// </remarks>
public interface ICryptoMetricsTools : IFinancialTools
{
    /// <param name="symbol">交易对符号（如BTCUSDT）</param>
    /// <param name="interval">时间间隔</param>
    /// <param name="limit">返回数据条数（默认500，最大1000）</param>
    /// <param name="startTime">起始时间（Unix时间戳毫秒，可选）</param>
    /// <param name="endTime">结束时间（Unix时间戳毫秒，可选）</param>
    /// <remarks>
    /// 数据源：币安API - /api/v3/klines
    /// 用于技术分析、回测和趋势判断
    /// </remarks>
    Task<CryptoOHLCV> GetOHLCVAsync(string symbol, MarketInterval interval = MarketInterval.OneDay, int limit = 500, long? startTime = null, long? endTime = null, CancellationToken cancellationToken = default);

    /// <param name="symbol">交易对符号（如BTCUSDT）</param>
    /// <param name="limit">返回档位数量（5/10/20/50/100/500/1000/5000）</param>
    /// <remarks>
    /// 数据源：币安API - /api/v3/depth
    /// 用于分析流动性、支撑压力位、买卖价差
    /// </remarks>
    Task<CryptoOrderBookDepth> GetOrderBookDepthAsync(string symbol, int limit = 100, CancellationToken cancellationToken = default);

    /// <param name="symbol">交易对符号（如BTCUSDT）</param>
    /// <param name="limit">返回成交笔数（默认500，最大1000）</param>
    /// <remarks>
    /// 数据源：币安API - /api/v3/trades
    /// 用于分析买卖力量对比、成交活跃度
    /// </remarks>
    Task<CryptoRecentTrades> GetRecentTradesAsync(string symbol, int limit = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取综合市场指标（市值、供应量、排名、流通率等数值指标）
    /// </summary>
    /// <param name="symbol">代币符号（如BTC、ETH）</param>
    /// <remarks>
    /// 数据源：CoinGecko - /api/v3/coins/markets
    /// 提供市值、供应量、排名、历史高低点、流通率等数值型市场指标
    /// 注意：项目描述等基本面信息请使用 ICryptoBasicTools.GetProjectInfoAsync
    /// </remarks>
    Task<CryptoMarketMetrics> GetMarketMetricsAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取交易量分布（不同交易所的交易量占比）
    /// </summary>
    /// <param name="symbol">代币符号（如BTC、ETH）</param>
    /// <remarks>
    /// 数据源：CoinGecko - /api/v3/coins/{id}/tickers
    /// 用于分析流动性分布、交易所选择
    /// </remarks>
    Task<List<VolumeDistribution>> GetVolumeDistributionAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取波动性指标（历史波动率、ATR、最大回撤、夏普比率）
    /// </summary>
    /// <param name="symbol">交易对符号（如BTCUSDT）</param>
    /// <param name="days">统计天数（默认30天）</param>
    /// <remarks>
    /// 基于历史K线数据计算
    /// 用于风险评估、仓位管理、策略制定
    /// </remarks>
    Task<CryptoVolatilityMetrics> GetVolatilityMetricsAsync(string symbol, int days = 30, CancellationToken cancellationToken = default);
}
