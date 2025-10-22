using MarketAssistant.Services.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

        var service = kernel.Services.GetRequiredService<McpService>();
        var configs = McpService.GetEnabledConfigs();
        var functions = await service.GetKernelFunctionsAsync(configs);
        kernel.Plugins.AddFromFunctions("tavily", functions);

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
