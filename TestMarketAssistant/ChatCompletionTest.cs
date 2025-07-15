using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TestMarketAssistant;

[TestClass]
public class ChatCompletionTest : BaseKernelTest
{
    IChatCompletionService _chatCompletionService;

    [TestInitialize]
    public void Initialize()
    {
        BaseInitialize(); // 调用基类初始化方法
        _chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
    }

    [TestMethod]
    public async Task TestChatAsync()
    {
        var history = new ChatHistory();
        history.AddUserMessage("股票 sz002594 的价格");
        var r = await _chatCompletionService.GetChatMessageContentAsync(history, executionSettings: new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        }, kernel: _kernel);
    }

    [TestMethod]
    public async Task TestMACDAsync()
    {
        var history = new ChatHistory();
        history.AddUserMessage("获取股票 sz002594 的K线日线MACD数据");
        var r = await _chatCompletionService.GetChatMessageContentAsync(history, executionSettings: new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        }, kernel: _kernel);
    }
}
