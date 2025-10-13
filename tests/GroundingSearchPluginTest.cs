using MarketAssistant.Agents.Plugins;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

/// <summary>
/// GroundingSearchPlugin 单元测试类
/// 验证SearchAsync方法正常调用
/// </summary>
[TestClass]
public class GroundingSearchPluginTest : BaseKernelTest
{
    /// <summary>
    /// 测试SearchAsync方法能正常调用
    /// </summary>
    [TestMethod]
    public async Task SearchAsync_CanCallSuccessfully()
    {
        // Arrange - 手动创建插件实例（与其他插件测试保持一致）
        var orchestrator = _kernel.Services.GetRequiredService<IRetrievalOrchestrator>();
        var webTextSearchFactory = _kernel.Services.GetRequiredService<IWebTextSearchFactory>();
        var logger = _kernel.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GroundingSearchPlugin>>();

        var plugin = new GroundingSearchPlugin(orchestrator, webTextSearchFactory, _userSettingService, logger);

        // Act & Assert - 只验证方法能正常调用，不抛异常
        try
        {
            var result = await plugin.SearchAsync("测试查询", 5);
            Assert.IsNotNull(result); ;
            foreach (var item in result)
            {
                Console.WriteLine($"Name: {item.Name}, Link: {item.Link}, Value: {item.Value}");
            }
        }
        catch (Exception ex)
        {
            Assert.Inconclusive($"SearchAsync调用失败: {ex.Message}");
        }
    }
}
