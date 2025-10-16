using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Core;

namespace MarketAssistant.Infrastructure.Factories;

public interface IKernelFactory
{
    Kernel CreateKernel();
    bool TryCreateKernel(out Kernel kernel, out string error);
    void Invalidate();
}

public class KernelFactory : IKernelFactory
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
        throw new FriendlyException(error);
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
            throw new FriendlyException("AI 功能未配置：请先在设置页面选择 AI 模型");
        if (string.IsNullOrWhiteSpace(userSetting.ApiKey))
            throw new FriendlyException("AI 功能未配置：请先在设置页面配置 API Key");
        if (string.IsNullOrWhiteSpace(userSetting.Endpoint))
            throw new FriendlyException("AI 功能未配置：请先在设置页面配置 API 端点");

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            userSetting.ModelId,
            new Uri(userSetting.Endpoint),
            userSetting.ApiKey);

        builder.Plugins
            .AddFromType<ConversationSummaryPlugin>()
            .AddFromType<TextPlugin>();

        builder.Plugins.AddFromPromptDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Agents", "Plugins", "Yaml"));

        return builder.Build();
    }
}

