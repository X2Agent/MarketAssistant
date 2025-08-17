using MarketAssistant.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;

namespace MarketAssistant.Infrastructure;

public interface IUserSemanticKernelService
{
    Kernel GetKernel();
}

internal class UserSemanticKernelService(
        IUserSettingService userSettingService,
        PlaywrightService playwrightService) : IUserSemanticKernelService
{

    public Kernel GetKernel()
    {
        var userSetting = userSettingService.CurrentSetting;
        if (string.IsNullOrWhiteSpace(userSetting.ModelId))
            throw new ArgumentException("ModelId 不能为空。", nameof(userSetting.ModelId));
        if (string.IsNullOrWhiteSpace(userSetting.ApiKey))
            throw new ArgumentException("ApiKey 不能为空。", nameof(userSetting.ApiKey));
        if (string.IsNullOrWhiteSpace(userSetting.Endpoint))
            throw new ArgumentException("Endpoint 不能为空。", nameof(userSetting.Endpoint));

        var builder = Kernel.CreateBuilder();

        // 使用用户提供的配置添加聊天补全服务
        builder.AddOpenAIChatCompletion(
           userSetting.ModelId,
           new Uri(userSetting.Endpoint),
           userSetting.ApiKey);

        builder.Plugins
            .AddFromType<ConversationSummaryPlugin>()
            .AddFromType<TextPlugin>();

        builder.Plugins.AddFromPromptDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "Yaml"));

        // 加载已启用的 MCP 服务器暴露的工具
        try
        {
            var mcpFunctions = McpPlugin.GetKernelFunctionsAsync().GetAwaiter().GetResult();
            builder.Plugins.AddFromFunctions("mcp", mcpFunctions);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载 MCP 插件失败: {ex.Message}");
        }

        return builder.Build();
    }
}
