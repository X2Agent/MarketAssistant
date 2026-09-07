using System.Net;
using System.Text;
using MarketAssistant.DataProviders.AShare;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// SinaFundFlowClient 解析测试：用伪造 GBK 编码响应验证流式解码与字段容错。
/// </summary>
[TestClass]
public sealed class SinaFundFlowClientTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetTopNetInflowAsync_GbkResponse_ShouldDecodeAndParse()
    {
        EnsureEncodingProvider();
        const string json = """
            [
              {"symbol":"sh600519","name":"贵州茅台","trade":"1500.00","changeratio":"-0.0082","netamount":"123456789.5"},
              {"symbol":"sz000001","name":"平安银行","trade":11.2,"changeratio":0.021,"netamount":9876543},
              {"symbol":false,"name":false,"trade":false,"changeratio":false,"netamount":false}
            ]
            """;
        var client = CreateClient(Encoding.GetEncoding("gbk").GetBytes(json));

        var items = await client.GetTopNetInflowAsync(8);

        Assert.AreEqual(2, items.Count, "symbol 非法的条目应被跳过");
        Assert.AreEqual("sh600519", items[0].Symbol);
        Assert.AreEqual("贵州茅台", items[0].Name, "GBK 中文应正确解码");
        Assert.AreEqual("1500.00", items[0].Price);
        Assert.AreEqual(-0.0082, items[0].ChangeRatio, 1e-9);
        Assert.AreEqual(123456789.5, items[0].NetAmount, 1e-9);
        // 非字符串类型的 trade 字段按容错约定返回空串，数值字段仍可解析
        Assert.AreEqual(string.Empty, items[1].Price);
        Assert.AreEqual(0.021, items[1].ChangeRatio, 1e-9);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetTopNetInflowAsync_NonArrayResponse_ShouldReturnEmpty()
    {
        EnsureEncodingProvider();
        var client = CreateClient(Encoding.GetEncoding("gbk").GetBytes("{}"));

        var items = await client.GetTopNetInflowAsync(8);

        Assert.IsNotNull(items);
        Assert.AreEqual(0, items.Count);
    }

    /// <summary>触发 SinaFundFlowClient 静态构造，注册 CodePagesEncodingProvider。</summary>
    private static void EnsureEncodingProvider() =>
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(SinaFundFlowClient).TypeHandle);

    private static SinaFundFlowClient CreateClient(byte[] responseBody) =>
        new(new FakeHttpClientFactory(responseBody));

    private sealed class FakeHttpClientFactory(byte[] responseBody) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new FakeHandler(responseBody))
            {
                BaseAddress = new Uri("https://vip.stock.finance.sina.com.cn/")
            };
    }

    private sealed class FakeHandler(byte[] responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseBody)
            });
    }
}
