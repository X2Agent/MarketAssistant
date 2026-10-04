using MarketAssistant.DataProviders.AShare;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// <see cref="SinaStockQuoteClient"/> 解析与防御性校验单元测试（纯逻辑，不触网）。
/// </summary>
/// <remarks>
/// 该解析器面对的是无官方文档的非公开接口，字段下标可能随接口调整漂移。
/// 若错位而不校验，会向盯盘用户展示"看似合理实则完全错误"的盘口价格——
/// 危害大于不展示。因此"校验失败必须返回 null"是本测试集的核心断言。
/// </remarks>
[TestClass]
public sealed class SinaStockQuoteClientTest
{
    /// <summary>
    /// 构造一条符合新浪个股行情字段布局的响应。
    /// 布局：0 名称 / 1 开盘 / 2 昨收 / 3 最新 / 4 最高 / 5 最低
    /// 6..15 买一~买五（价,量）/ 16..25 卖一~卖五（价,量）/ 26 逐笔
    /// </summary>
    private static string BuildResponse(
        string name = "贵州茅台",
        string current = "1800.00",
        string? bidBlock = null,
        string? askBlock = null,
        string ticks = "1800.00,100,1799.99,200",
        bool padToIndex26 = true)
    {
        bidBlock ??= "1799.99,100,1799.98,200,1799.97,300,1799.96,400,1799.95,500";
        askBlock ??= "1800.01,150,1800.02,250,1800.03,350,1800.04,450,1800.05,550";

        var fields = new List<string>
        {
            name, "1790.00", "1795.00", current, "1810.00", "1780.00"
        };
        fields.AddRange(bidBlock.Split(','));
        fields.AddRange(askBlock.Split(','));

        if (padToIndex26)
        {
            // 补齐到下标 26（逐笔字段），使 ticks 恰好落在 fields[26]
            // 注意用 < 26 而非 <= 26：后者会多补一段，导致逐笔被推到下标 27
            while (fields.Count < 26)
            {
                fields.Add("0");
            }
        }

        fields.Add(ticks);

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
        Assert.AreEqual(100m, quote.Bids[0].Quantity);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_ShouldSortBidsDescendingAndAsksAscending()
    {
        // 故意让买盘、卖盘乱序，验证排序生效而非依赖源顺序
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse(
            bidBlock: "1799.90,1,1799.99,2,1799.95,3,1799.98,4,1799.92,5",
            askBlock: "1800.10,1,1800.01,2,1800.05,3,1800.02,4,1800.08,5"));

        Assert.IsNotNull(quote);
        var bids = quote.Bids.Select(level => level.Price).ToList();
        var asks = quote.Asks.Select(level => level.Price).ToList();

        CollectionAssert.AreEqual(bids.OrderByDescending(price => price).ToList(), bids, "买盘应降序");
        CollectionAssert.AreEqual(asks.OrderBy(price => price).ToList(), asks, "卖盘应升序");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_ShouldParseTicks()
    {
        // 真实结构：下标 26 起为「逐笔(价,量)xN, 日期, 时间」，且逐笔本身含逗号
        var quote = SinaStockQuoteClient.ParseResponse(
            BuildResponse(ticks: "1800.00,100,1799.99,200,1800.01,300,2026-10-04,15:00:00"));

        Assert.IsNotNull(quote);
        Assert.AreEqual(3, quote.Ticks.Count, "日期/时间不应被当作成交");
        Assert.AreEqual(1800.00m, quote.Ticks[0].Price);
        Assert.AreEqual(100m, quote.Ticks[0].Quantity);
        Assert.AreEqual(1800.01m, quote.Ticks[2].Price);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_OddTickCount_ShouldIgnoreTrailingElement()
    {
        // 奇数段说明尾部落单，不应构造出 quantity 为 0 的伪成交
        var quote = SinaStockQuoteClient.ParseResponse(
            BuildResponse(ticks: "1800.00,100,1799.99,2026-10-04,15:00:00"));

        Assert.IsNotNull(quote);
        Assert.AreEqual(1, quote.Ticks.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_EmptyTicksSection_ShouldYieldNoTicksButKeepOrderBook()
    {
        // 停牌时逐笔为空，但五档仍可能有效，不应因逐笔缺失而丢弃整条数据
        var quote = SinaStockQuoteClient.ParseResponse(
            BuildResponse(ticks: ",2026-10-04,15:00:00"));

        Assert.IsNotNull(quote, "逐笔缺失不应影响盘口可用性");
        Assert.AreEqual(0, quote.Ticks.Count);
        Assert.AreEqual(5, quote.Bids.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_ZeroPriceLevels_ShouldSkipInsteadOfTreatAsRealPrice()
    {
        // 停牌/集合竞价时部分档位为 0，不能当作真实报价参与校验与展示
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse(
            bidBlock: "1799.99,100,0,0,1799.97,300,0,0,1799.95,500"));

        Assert.IsNotNull(quote);
        Assert.AreEqual(3, quote.Bids.Count, "价格为 0 的档位应被跳过");
        Assert.IsTrue(quote.Bids.All(level => level.Price > 0m));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_FieldShifted_WrongPricesFarFromCurrent_ShouldReturnNull()
    {
        // 核心防线模拟：若新浪调整字段顺序导致下标错位，
        // 解析出的"价格"会远离最新价——此时必须返回 null 而不是展示错误盘口。
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse(
            current: "1800.00",
            bidBlock: "0.0001,100,0.0002,200,0.0003,300,0.0004,400,0.0005,500",
            askBlock: "99999.00,1,99999.00,1,99999.00,1,99999.00,1,99999.00,1"));

        Assert.IsNull(quote, "价格远离最新价说明字段错位，必须拒绝而非返回错误数据");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseResponse_CrossedBook_BidAboveAsk_ShouldReturnNull()
    {
        // 买一高于卖一即盘口倒挂，正常行情不会出现，判定为数据异常
        var quote = SinaStockQuoteClient.ParseResponse(BuildResponse(
            bidBlock: "1800.50,100,1800.40,200,1800.30,300,1800.20,400,1800.10,500",
            askBlock: "1800.01,150,1800.02,250,1800.03,350,1800.04,450,1800.05,550"));

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
