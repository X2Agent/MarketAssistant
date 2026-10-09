using MarketAssistant.DataProviders.AShare;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// <see cref="SinaStockQuoteClient"/> 解析与防御性校验单元测试（纯逻辑，不触网）。
/// </summary>
/// <remarks>
/// 该解析器面对的是无官方文档的非公开接口，字段下标可能随接口调整漂移。
/// 若错位而不校验，会向盯盘用户展示"看似合理实则完全错误"的盘口价格——
/// 危害大于不展示。因此"校验失败必须返回 null"是本测试集的核心断言。
/// 字段布局按 2026-10 真实抓包校准：0 名称 / 1 今开 / 2 昨收 / 3 最新 / 4 最高 / 5 最低 /
/// 6 竞买价 / 7 竞卖价 / 8 成交量(股) / 9 成交额(元) /
/// 10..19 买一~买五「量,价」对 / 20..29 卖一~卖五「量,价」对 / 30 日期 / 31 时间。
/// 该接口不返回逐笔成交，故无逐笔用例。
/// </remarks>
[TestClass]
public sealed class SinaStockQuoteClientTest
{
    /// <summary>2026-10-08 收盘后真实抓包的 sh511010（国债ETF国泰）响应。</summary>
    private const string RealCapturedResponse =
        "var hq_str_sh511010=\"国债ETF国泰,140.735,140.735,140.705,140.766,140.696,140.705,140.706," +
        "19345000,2721943053.000,43000,140.705,73000,140.704,111400,140.703,2400,140.698,20100,140.697," +
        "3300,140.706,200,140.708,1500,140.709,2700,140.710,20200,140.711," +
        "2026-10-08,15:34:59,00,D|400|56282.00\";";

    /// <summary>
    /// 构造一条符合新浪个股行情真实字段布局的响应。
    /// 布局：0 名称 / 1 今开 / 2 昨收 / 3 最新 / 4 最高 / 5 最低 / 6 竞买价 / 7 竞卖价 /
    /// 8 成交量 / 9 成交额 / 10..19 买一~买五「量,价」/ 20..29 卖一~卖五「量,价」/ 30 日期 / 31 时间。
    /// </summary>
    private static string BuildResponse(
        string name = "贵州茅台",
        string open = "1790.00",
        string preClose = "1795.00",
        string current = "1800.00",
        string high = "1810.00",
        string low = "1780.00",
        string volume = "2516757",
        string amount = "3145413157.000",
        string? bidBlock = null,
        string? askBlock = null)
    {
        bidBlock ??= "100,1799.99,200,1799.98,300,1799.97,400,1799.96,500,1799.95";
        askBlock ??= "150,1800.01,250,1800.02,350,1800.03,450,1800.04,550,1800.05";

        var fields = new List<string>
        {
            name, open, preClose, current, high, low, "1799.99", "1800.01", volume, amount
        };
        fields.AddRange(bidBlock.Split(','));
        fields.AddRange(askBlock.Split(','));
        fields.Add("2026-10-08");
        fields.Add("15:34:59");

        return $"var hq_str_sh600519=\"{string.Join(',', fields)}\";";
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_ValidPayload_ShouldParseNamePriceAndDepth()
    {
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse());

        Assert.IsNotNull(quote);
        Assert.AreEqual("贵州茅台", quote.Name);
        Assert.AreEqual(1800.00m, quote.CurrentPrice);
        Assert.AreEqual(5, quote.Bids.Count);
        Assert.AreEqual(5, quote.Asks.Count);
        Assert.AreEqual(1799.99m, quote.Bids[0].Price, "买一应价格最高");
        Assert.AreEqual(1800.01m, quote.Asks[0].Price, "卖一应价格最低");
        Assert.AreEqual(100m, quote.Bids[0].Quantity, "档位为「量,价」成对字段");
        Assert.AreEqual(150m, quote.Asks[0].Quantity);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_ValidPayload_ShouldParseSnapshotFields()
    {
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse());

        Assert.IsNotNull(quote);
        Assert.AreEqual(1790.00m, quote.Open);
        Assert.AreEqual(1795.00m, quote.PreClose);
        Assert.AreEqual(1810.00m, quote.High);
        Assert.AreEqual(1780.00m, quote.Low);
        Assert.AreEqual(2516757m, quote.Volume);
        Assert.AreEqual(3145413157.000m, quote.Amount);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_RealCapturedPayload_ShouldParseExactly()
    {
        // 回归守卫：早期版本买盘从下标 6、卖盘从 16 且按「价,量」解析，
        // 与真实布局（10..19 / 20..29 的「量,价」对）完全错位，导致盘口恒为"暂无数据"。
        var quote = SinaStockQuoteClient.ParseResponse(RealCapturedResponse);

        Assert.IsNotNull(quote, "真实抓包载荷必须能解析");
        Assert.AreEqual("国债ETF国泰", quote.Name);
        Assert.AreEqual(140.705m, quote.CurrentPrice);
        Assert.AreEqual(140.735m, quote.Open);
        Assert.AreEqual(140.735m, quote.PreClose);
        Assert.AreEqual(140.766m, quote.High);
        Assert.AreEqual(140.696m, quote.Low);
        Assert.AreEqual(19345000m, quote.Volume);
        Assert.AreEqual(2721943053.000m, quote.Amount);

        Assert.HasCount(5, quote.Bids);
        Assert.HasCount(5, quote.Asks);
        Assert.AreEqual(140.705m, quote.Bids[0].Price);
        Assert.AreEqual(43000m, quote.Bids[0].Quantity);
        Assert.AreEqual(140.697m, quote.Bids[4].Price);
        Assert.AreEqual(20100m, quote.Bids[4].Quantity);
        Assert.AreEqual(140.706m, quote.Asks[0].Price);
        Assert.AreEqual(3300m, quote.Asks[0].Quantity);
        Assert.AreEqual(140.711m, quote.Asks[4].Price);
        Assert.AreEqual(20200m, quote.Asks[4].Quantity);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_ShouldSortBidsDescendingAndAsksAscending()
    {
        // 故意让买盘、卖盘乱序，验证排序生效而非依赖源顺序
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse(
            bidBlock: "1,1799.90,2,1799.99,3,1799.95,4,1799.98,5,1799.92",
            askBlock: "1,1800.10,2,1800.01,3,1800.05,4,1800.02,5,1800.08"));

        Assert.IsNotNull(quote);
        var bids = quote.Bids.Select(level => level.Price).ToList();
        var asks = quote.Asks.Select(level => level.Price).ToList();

        CollectionAssert.AreEqual(bids.OrderByDescending(price => price).ToList(), bids, "买盘应降序");
        CollectionAssert.AreEqual(asks.OrderBy(price => price).ToList(), asks, "卖盘应升序");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_ZeroPriceLevels_ShouldSkipInsteadOfTreatAsRealPrice()
    {
        // 停牌/集合竞价时部分档位为 0，不能当作真实报价参与校验与展示
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse(
            bidBlock: "100,1799.99,0,0,300,1799.97,0,0,500,1799.95"));

        Assert.IsNotNull(quote);
        Assert.AreEqual(3, quote.Bids.Count, "价格为 0 的档位应被跳过");
        Assert.IsTrue(quote.Bids.All(level => level.Price > 0m));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_MissingSnapshotFields_ShouldBeNullNotZero()
    {
        // 快照字段缺失/为 0 时必须是 null（「未提供」），绝不可静默归零伪造行情
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse(
            open: "0", preClose: "0", high: "0", low: "0", volume: "0", amount: "0"));

        Assert.IsNotNull(quote, "快照字段缺失不影响盘口可用性");
        Assert.IsNull(quote.Open);
        Assert.IsNull(quote.PreClose);
        Assert.IsNull(quote.High);
        Assert.IsNull(quote.Low);
        Assert.IsNull(quote.Volume);
        Assert.IsNull(quote.Amount);
        Assert.HasCount(5, quote.Bids);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_FieldShifted_WrongPricesFarFromCurrent_ShouldReturnNull()
    {
        // 核心防线模拟：若新浪调整字段顺序导致下标错位，
        // 解析出的"价格"会远离最新价——此时必须返回 null 而不是展示错误盘口。
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse(
            current: "1800.00",
            bidBlock: "100,0.0001,200,0.0002,300,0.0003,400,0.0004,500,0.0005",
            askBlock: "1,99999.00,1,99999.00,1,99999.00,1,99999.00,1,99999.00"));

        Assert.IsNull(quote, "价格远离最新价说明字段错位，必须拒绝而非返回错误数据");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_CrossedBook_BidAboveAsk_ShouldReturnNull()
    {
        // 买一高于卖一即盘口倒挂，正常行情不会出现，判定为数据异常
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse(
            bidBlock: "100,1800.50,200,1800.40,300,1800.30,400,1800.20,500,1800.10",
            askBlock: "150,1800.01,250,1800.02,350,1800.03,450,1800.04,550,1800.05"));

        Assert.IsNull(quote, "盘口倒挂应判定为字段错位");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_TooFewFields_ShouldReturnNull()
    {
        // 响应结构变化（字段减少）时必须拒绝，而不是越界读取
        const string shortResponse = "var hq_str_sh600519=\"贵州茅台,1790.00,1795.00,1800.00\";";

        Assert.IsNull(SinaStockQuoteClient.ParseResponse(shortResponse));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_InvalidOrEmptyCurrentPrice_ShouldReturnNull()
    {
        Assert.IsNull(SinaStockQuoteClient.ParseResponse(BuildResponse(current: "0")));
        Assert.IsNull(SinaStockQuoteClient.ParseResponse(BuildResponse(current: string.Empty)));
        Assert.IsNull(SinaStockQuoteClient.ParseResponse(BuildResponse(current: "abc")));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_MalformedOrUnrelatedText_ShouldReturnNull()
    {
        Assert.IsNull(SinaStockQuoteClient.ParseResponse(string.Empty));
        Assert.IsNull(SinaStockQuoteClient.ParseResponse("var hq_str_s_sh000001=\"上证指数,3094.6\";"));
        Assert.IsNull(SinaStockQuoteClient.ParseResponse("<html>403 Forbidden</html>"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetStockQuoteAsync_UnsupportedOrEmptyCode_ShouldReturnNullWithoutRequest()
    {
        var client = new SinaStockQuoteClient(new ThrowingHttpClientFactory());

        Assert.IsNull(await client.GetStockQuoteAsync(string.Empty));
        Assert.IsNull(await client.GetStockQuoteAsync("   "));
        Assert.IsNull(await client.GetStockQuoteAsync("s_sh000001"), "指数简版代码不受支持");
        Assert.IsNull(await client.GetStockQuoteAsync("600519"), "缺少市场前缀");
    }

    /// <summary>
    /// 故意在创建客户端时抛异常的工厂：验证不支持的代码在发起 HTTP 之前就被拦截。
    /// </summary>
    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("不应发起请求");
    }
}
