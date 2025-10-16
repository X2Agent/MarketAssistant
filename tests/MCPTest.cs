using MarketAssistant.Agents.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace TestMarketAssistant;

[TestClass]
public class MCPTest : BaseKernelTest
{
    [TestMethod]
    public async Task TestTavilyMCPAsync()
    {
        var kernel = CreateKernelWithChatCompletion();

        kernel.Plugins.AddFromFunctions("tavily", await McpPlugin.GetKernelFunctionsAsync());

        OpenAIPromptExecutionSettings executionSettings = new()
        {
            Temperature = 0,
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
        };

        var prompt = "帮我搜索一下关于google新协议A2A的相关内容";
        var result = await kernel.InvokePromptAsync(prompt, new(executionSettings)).ConfigureAwait(false);
        Console.WriteLine($"\n\n{prompt}\n{result}");
    }
}
