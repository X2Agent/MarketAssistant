using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Technical;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币技术分析工具实现（基于币安 K 线数据计算）
/// </summary>
public sealed class CryptoTechnicalTools : ITechnicalDataTools
{
    private readonly ILogger<CryptoTechnicalTools> _logger;
    private readonly IServiceProvider _serviceProvider;

    public CryptoTechnicalTools(ILogger<CryptoTechnicalTools> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    [Description("获取近30日最新日线KDJ，支持BTC、ETH等币种")]
    public async Task<TechnicalKDJ> GetKDJAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            // KDJ requires at least 9 periods, but using more (250) ensures initial EMA smoothing stability if used internally (though KDJ is simpler).
            // Increasing limit to consistent 250 for safety and potential history usage.
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 250);

            if (klineData == null || klineData.Count < 9)
            {
                throw new InvalidOperationException($"K线数据不足，无法计算KDJ指标: {assetSymbol}");
            }

            var kdj = CalculateKDJ(klineData);
            kdj.T = klineData.Last().Timestamp.ToString("yyyy-MM-dd");

            _logger.LogInformation("成功计算虚拟币KDJ指标: {Symbol}, K={K}, D={D}, J={J}",
                assetSymbol, kdj.K, kdj.D, kdj.J);

            return kdj;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币KDJ指标失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"计算虚拟币KDJ指标失败: {ex.Message} (交易对: {assetSymbol})", ex);
        }
    }

    [Description("获取近30日最新日线MACD，支持BTC、ETH等币种")]
    public async Task<TechnicalMACD> GetMACDAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            // MACD uses EMA26, needs significantly more than 26 periods for EMA to converge from initial seed. 250 is safer.
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 250);

            if (klineData == null || klineData.Count < 26)
            {
                throw new InvalidOperationException($"K线数据不足，无法计算MACD指标: {assetSymbol}");
            }

            var macd = CalculateMACD(klineData);
            macd.T = klineData.Last().Timestamp.ToString("yyyy-MM-dd");

            _logger.LogInformation("成功计算虚拟币MACD指标: {Symbol}, DIFF={Diff}, DEA={Dea}, MACD={Macd}",
                assetSymbol, macd.Diff, macd.Dea, macd.Macd);

            return macd;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币MACD指标失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"计算虚拟币MACD指标失败: {ex.Message} (交易对: {assetSymbol})", ex);
        }
    }

    [Description("获取近30日最新日线BOLL，支持BTC、ETH等币种")]
    public async Task<TechnicalBoll> GetBOLLAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 250);

            if (klineData == null || klineData.Count < 20)
            {
                throw new InvalidOperationException($"K线数据不足，无法计算BOLL指标: {assetSymbol}");
            }

            var boll = CalculateBOLL(klineData);
            boll.T = klineData.Last().Timestamp.ToString("yyyy-MM-dd");

            _logger.LogInformation("成功计算虚拟币BOLL指标: {Symbol}, 上轨={U}, 中轨={M}, 下轨={D}",
                assetSymbol, boll.U, boll.M, boll.D);

            return boll;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币BOLL指标失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"计算虚拟币BOLL指标失败: {ex.Message} (交易对: {assetSymbol})", ex);
        }
    }

    [Description("获取近30日最新日线MA，支持BTC、ETH等币种")]
    public async Task<TechnicalMA> GetMAAsync([Description("虚拟币代码（如BTC、ETH）")] string assetSymbol)
    {
        try
        {
            var klineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
            var klineData = await klineService.GetKLineDataAsync(assetSymbol, KLineType.Daily, 250);

            if (klineData == null || klineData.Count < 3)
            {
                throw new InvalidOperationException($"K线数据不足，无法计算MA指标: {assetSymbol}");
            }

            var ma = CalculateMA(klineData);
            ma.T = klineData.Last().Timestamp.ToString("yyyy-MM-dd");

            _logger.LogInformation("成功计算虚拟币MA指标: {Symbol}, MA5={MA5}, MA10={MA10}, MA20={MA20}",
                assetSymbol, ma.MA5, ma.MA10, ma.MA20);

            return ma;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "计算虚拟币MA指标失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"计算虚拟币MA指标失败: {ex.Message} (交易对: {assetSymbol})", ex);
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetKDJAsync);
        yield return AIFunctionFactory.Create(GetMACDAsync);
        yield return AIFunctionFactory.Create(GetBOLLAsync);
        yield return AIFunctionFactory.Create(GetMAAsync);
    }

    #region 技术指标计算方法

    /// <summary>
    /// 计算KDJ指标（随机指标，衡量超买超卖状态）
    /// 计算流程：RSV（未成熟随机值）-> K值 -> D值 -> J值
    /// 
    /// 公式说明：
    /// 1. RSV = (当日收盘价 - N日内最低价) / (N日内最高价 - N日内最低价) × 100
    /// 2. K值 = 2/3 × 前一日K值 + 1/3 × 当日RSV（初始K值=50）
    /// 3. D值 = 2/3 × 前一日D值 + 1/3 × 当日K值（初始D值=50）
    /// 4. J值 = 3 × K值 - 2 × D值
    /// 
    /// 指标含义：
    /// - K值和D值范围通常在0-100之间，J值可能超出此范围
    /// - K > 80 超买区域，K < 20 超卖区域
    /// - K线上穿D线为金叉（买入信号），K线下穿D线为死叉（卖出信号）
    /// - J值更敏感，可提前预示K、D值的走向
    /// </summary>
    /// <param name="period">计算周期，默认9日</param>
    private TechnicalKDJ CalculateKDJ(List<KLineData> klineData, int period = 9)
    {
        // 初始化K、D值为50（中性位置）
        decimal prevK = 50m;
        decimal prevD = 50m;
        decimal currentK = 50m;
        decimal currentD = 50m;

        // 从第period个数据点开始计算（需要period天的数据来确定最高最低价）
        for (int i = period - 1; i < klineData.Count; i++)
        {
            // 步骤1：计算RSV（未成熟随机值）
            // 取最近period天的K线数据
            var recentData = klineData.Skip(i - period + 1).Take(period).ToList();
            var low = recentData.Min(x => x.Low);    // N日内最低价
            var high = recentData.Max(x => x.High);  // N日内最高价
            var close = klineData[i].Close;          // 当日收盘价

            // RSV公式：(收盘价-最低价)/(最高价-最低价) × 100
            // 特殊情况：如果最高价等于最低价（极少见），RSV设为50
            var rsv = high == low ? 50m : (close - low) / (high - low) * 100m;

            // 步骤2：计算K值（平滑RSV）
            // K值采用指数移动平均，权重为1/3，即对RSV进行平滑处理
            currentK = (2m / 3m) * prevK + (1m / 3m) * rsv;

            // 步骤3：计算D值（平滑K值）
            // D值是K值的移动平均，进一步平滑，反应更慢但更可靠
            currentD = (2m / 3m) * prevD + (1m / 3m) * currentK;

            // 更新前一日的K、D值供下一次迭代使用
            prevK = currentK;
            prevD = currentD;
        }

        // 步骤4：计算J值（超前指标）
        // J = 3K - 2D，J值更敏感，可能超出0-100范围
        var currentJ = 3m * currentK - 2m * currentD;

        return new TechnicalKDJ
        {
            K = Math.Round(currentK, 2),
            D = Math.Round(currentD, 2),
            J = Math.Round(currentJ, 2)
        };
    }

    /// <summary>
    /// 计算MACD指标（指数平滑异同移动平均线，判断趋势强度和转折点）
    /// 
    /// 公式说明：
    /// 1. EMA12 = 12日指数移动平均线（短期趋势）
    /// 2. EMA26 = 26日指数移动平均线（长期趋势）
    /// 3. DIFF（快线/DIF）= EMA12 - EMA26
    /// 4. DEA（慢线/信号线）= DIFF的9日EMA
    /// 5. MACD柱（柱状图）= (DIFF - DEA) × 2
    /// 
    /// 指标含义：
    /// - DIFF > 0 表示短期均线在长期均线之上，市场偏多
    /// - DIFF线上穿DEA线为金叉（买入信号），下穿为死叉（卖出信号）
    /// - MACD柱由负转正为买入信号，由正转负为卖出信号
    /// - MACD柱的长度表示多空力量的强弱
    /// </summary>
    private TechnicalMACD CalculateMACD(List<KLineData> klineData)
    {
        // 提取收盘价序列
        var closePrices = klineData.Select(x => x.Close).ToList();

        // 步骤1：计算12日和26日指数移动平均线
        // EMA相比SMA对近期价格赋予更高权重，反应更灵敏
        var ema12List = CalculateEMA(closePrices, 12);
        var ema26List = CalculateEMA(closePrices, 26);

        // 步骤2：计算DIFF（离差值）
        // DIFF = 快线 - 慢线，反映短期与长期趋势的差异
        var diffList = new List<decimal>();
        int startIndex = 26 - 1; // 从第26个数据点开始（EMA26至少需要26个数据点）

        for (int i = startIndex; i < closePrices.Count; i++)
        {
            var diff = ema12List[i] - ema26List[i];
            diffList.Add(diff);
        }

        // 步骤3：计算DEA（DIFF的9日EMA，即信号线）
        // DEA对DIFF进行平滑，作为交易信号的触发线
        var deaList = CalculateEMA(diffList, 9);

        // 步骤4：获取最新的各项指标值
        var lastDiff = diffList.Last();
        var lastDea = deaList.Last();
        var lastEma12 = ema12List.Last();
        var lastEma26 = ema26List.Last();

        // 步骤5：计算MACD柱状图值
        // MACD = (DIFF - DEA) × 2，乘以2是为了放大柱状图的视觉效果
        // 柱状图的高度和方向直观反映多空力量对比
        var macdBar = (lastDiff - lastDea) * 2m;

        return new TechnicalMACD
        {
            Ema12 = Math.Round(lastEma12, 2),
            Ema26 = Math.Round(lastEma26, 2),
            Diff = Math.Round(lastDiff, 2),
            Dea = Math.Round(lastDea, 2),
            Macd = Math.Round(macdBar, 2)
        };
    }

    /// <summary>
    /// 计算布林带指标（Bollinger Bands，衡量价格波动区间和超买超卖）
    /// 
    /// 公式说明：
    /// 1. 中轨（MB）= N日简单移动平均线（SMA）
    /// 2. 标准差（σ）= sqrt(Σ(收盘价 - 中轨)² / (N-1))  // 使用样本标准差
    /// 3. 上轨（UP）= 中轨 + K × 标准差
    /// 4. 下轨（DN）= 中轨 - K × 标准差
    /// 
    /// 指标含义：
    /// - 中轨代表价格的平均成本，也是支撑/阻力位
    /// - 上下轨构成价格波动的正常区间（约95%的价格在此范围内，当K=2时）
    /// - 价格触及上轨表示超买，触及下轨表示超卖
    /// - 布林带收窄（上下轨距离缩小）表示波动率降低，可能酝酿突破
    /// - 布林带扩张表示波动率增加，趋势加强
    /// </summary>
    /// <param name="period">计算周期，默认20日</param>
    /// <param name="multiplier">标准差倍数，默认2倍（覆盖约95%的价格波动）</param>
    private TechnicalBoll CalculateBOLL(List<KLineData> klineData, int period = 20, decimal multiplier = 2m)
    {
        // 取最近period天的收盘价
        var closePrices = klineData.TakeLast(period).Select(x => x.Close).ToList();

        // 步骤1：计算中轨（N日简单移动平均线）
        var middle = closePrices.Average();

        // 步骤2：计算样本标准差
        // 注意：这里使用样本标准差（除以N-1），而非总体标准差（除以N）
        // 样本标准差更适用于对未来波动的估计，是金融领域的标准做法
        var variance = closePrices.Sum(x => (x - middle) * (x - middle)) / (period - 1);
        var stdDev = (decimal)Math.Sqrt((double)variance);

        // 步骤3：计算上轨和下轨
        // 上轨 = 中轨 + K倍标准差（通常K=2，对应95%置信区间）
        // 下轨 = 中轨 - K倍标准差
        var upper = middle + multiplier * stdDev;
        var lower = middle - multiplier * stdDev;

        return new TechnicalBoll
        {
            U = Math.Round(upper, 2),
            M = Math.Round(middle, 2),
            D = Math.Round(lower, 2)
        };
    }

    /// <summary>
    /// 计算多周期移动平均线（MA，判断趋势方向和支撑阻力位）
    /// 
    /// 公式说明：
    /// MA(N) = (P1 + P2 + ... + PN) / N，其中P为收盘价
    /// 
    /// 常用周期及含义：
    /// - MA3/MA5：超短期均线，反映最近几天的价格趋势
    /// - MA10/MA15：短期均线，判断短期走势
    /// - MA20/MA30：中短期均线，重要的支撑阻力位（MA20即布林带中轨）
    /// - MA60：季线，中期趋势的重要参考
    /// - MA120/MA200：半年线/年线，长期趋势和牛熊分界线
    /// - MA250：年线（按交易日计算），长期投资者关注的重要均线
    /// 
    /// 使用技巧：
    /// - 短期均线上穿长期均线为金叉（看涨），下穿为死叉（看跌）
    /// - 价格在均线之上为多头排列，之下为空头排列
    /// - 均线可作为动态支撑位和阻力位
    /// </summary>
    private TechnicalMA CalculateMA(List<KLineData> klineData)
    {
        var closePrices = klineData.Select(x => x.Close).ToList();

        // 计算多个周期的简单移动平均线
        // 如果数据不足，对应周期的MA值会返回null
        return new TechnicalMA
        {
            MA3 = CalculateSMA(closePrices, 3),      // 3日均线
            MA5 = CalculateSMA(closePrices, 5),      // 5日均线（周线）
            MA10 = CalculateSMA(closePrices, 10),    // 10日均线（两周线）
            MA15 = CalculateSMA(closePrices, 15),    // 15日均线
            MA20 = CalculateSMA(closePrices, 20),    // 20日均线（月线）
            MA30 = CalculateSMA(closePrices, 30),    // 30日均线
            MA60 = CalculateSMA(closePrices, 60),    // 60日均线（季线）
            MA120 = CalculateSMA(closePrices, 120),  // 120日均线（半年线）
            MA200 = CalculateSMA(closePrices, 200),  // 200日均线
            MA250 = CalculateSMA(closePrices, 250)   // 250日均线（年线）
        };
    }

    /// <summary>
    /// 计算简单移动平均线（Simple Moving Average, SMA）
    /// 
    /// 公式：SMA = (P1 + P2 + ... + PN) / N
    /// 其中P为收盘价，N为周期
    /// </summary>
    /// <param name="prices">价格序列（通常为收盘价）</param>
    /// <param name="period">计算周期（天数）</param>
    /// <returns>如果数据不足返回null，否则返回保留2位小数的MA值</returns>
    private decimal? CalculateSMA(List<decimal> prices, int period)
    {
        // 数据不足时返回null（例如只有100天数据，无法计算MA120）
        if (prices.Count < period) return null;

        // 取最近period天的价格计算平均值，保留2位小数
        return Math.Round(prices.TakeLast(period).Average(), 2);
    }

    /// <summary>
    /// 计算指数移动平均线（Exponential Moving Average, EMA）
    /// EMA对近期价格赋予更高权重，相比SMA反应更灵敏，更适合捕捉趋势变化
    /// 
    /// 公式说明：
    /// 1. 平滑系数 α = 2 / (N + 1)，其中N为周期
    /// 2. EMA(初始) = SMA(N)  // 第一个EMA值使用简单移动平均
    /// 3. EMA(t) = α × Price(t) + (1 - α) × EMA(t-1)
    ///    简化为：EMA(t) = EMA(t-1) + α × (Price(t) - EMA(t-1))
    /// 
    /// 特点：
    /// - 对最新价格的权重更高（权重随时间指数级衰减）
    /// - 相比SMA更快反应价格变化，但也更容易受短期波动影响
    /// - 广泛用于MACD、布林带等衍生指标
    /// </summary>
    /// <param name="prices">价格序列</param>
    /// <param name="period">计算周期</param>
    /// <returns>与输入价格列表等长的EMA列表（前period-1个值为0占位）</returns>
    private List<decimal> CalculateEMA(List<decimal> prices, int period)
    {
        var emaList = new List<decimal>();
        if (prices.Count < period) return emaList;

        // 计算平滑系数（权重因子）
        // α = 2/(N+1)，例如12日EMA的α = 2/13 ≈ 0.1538
        var multiplier = 2m / (period + 1);

        // 前period-1个位置填充0（占位，保持索引对齐）
        // 这样emaList[i]对应prices[i]，便于后续计算
        for (int i = 0; i < period - 1; i++)
        {
            emaList.Add(0m);
        }

        // 第period个值使用SMA作为EMA的初始值
        // 这是标准做法，为EMA提供一个合理的起始点
        var sma = prices.Take(period).Average();
        emaList.Add(sma);

        // 从第period+1个数据点开始计算真正的EMA
        // EMA(t) = EMA(t-1) + α × (Price(t) - EMA(t-1))
        // 等价于：EMA(t) = α × Price(t) + (1-α) × EMA(t-1)
        for (int i = period; i < prices.Count; i++)
        {
            // 使用差值公式：新EMA = 旧EMA + α × (新价格 - 旧EMA)
            // 这种形式计算更稳定，避免浮点误差累积
            var ema = (prices[i] - emaList[i - 1]) * multiplier + emaList[i - 1];
            emaList.Add(ema);
        }

        return emaList;
    }

    #endregion
}
