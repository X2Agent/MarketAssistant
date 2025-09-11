using MarketAssistant.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;

namespace MarketAssistant.Infrastructure;

public interface IKernelFactory
{
    Kernel CreateKernel();
    bool TryCreateKernel(out Kernel kernel, out string error);
    void Invalidate();
}

internal class KernelFactory : IKernelFactory
{
    private readonly IUserSettingService _userSettingService;
    private readonly object _lock = new();
    private Kernel? _cached;
    private string? _lastError;

    public KernelFactory(IUserSettingService userSettingService)
    {
        _userSettingService = userSettingService;
    }

    public Kernel CreateKernel()
    {
        if (TryCreateKernel(out var kernel, out var error)) return kernel;
        throw new InvalidOperationException($"Kernel 初始化失败: {error}");
    }

    public bool TryCreateKernel(out Kernel kernel, out string error)
    {
        lock (_lock)
        {
            if (_cached != null)
            {
                kernel = _cached;
                error = string.Empty;
                return true;
            }
            if (!string.IsNullOrEmpty(_lastError))
            {
                kernel = null!;
                error = _lastError!;
                return false;
            }
            try
            {
                _cached = Build();
                kernel = _cached;
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                kernel = null!;
                error = _lastError;
                return false;
            }
        }
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _cached = null;
            _lastError = null;
        }
    }

    private Kernel Build()
    {
        var userSetting = _userSettingService.CurrentSetting;
        if (string.IsNullOrWhiteSpace(userSetting.ModelId))
            throw new ArgumentException("ModelId 不能为空", nameof(userSetting.ModelId));
        if (string.IsNullOrWhiteSpace(userSetting.ApiKey))
            throw new ArgumentException("ApiKey 不能为空", nameof(userSetting.ApiKey));
        if (string.IsNullOrWhiteSpace(userSetting.Endpoint))
            throw new ArgumentException("Endpoint 不能为空", nameof(userSetting.Endpoint));

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            userSetting.ModelId,
            new Uri(userSetting.Endpoint),
            userSetting.ApiKey);

        builder.Plugins
            .AddFromType<ConversationSummaryPlugin>()
            .AddFromType<TextPlugin>();

        builder.Plugins.AddFromPromptDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "Yaml"));

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
