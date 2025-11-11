using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Mcp;
using MarketAssistant.Services.Notification;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// MCP服务器配置页ViewModel - 对应 MCPServerConfigViewModel
/// </summary>
public partial class MCPConfigPageViewModel : ViewModelBase
{
    private readonly MCPServerConfigService _configService;
    private readonly INotificationService _notificationService;
    private readonly McpService _mcpService;

    [ObservableProperty]
    private ObservableCollection<MCPServerConfig> _serverConfigs = new();

    [ObservableProperty]
    private MCPServerConfig? _selectedConfig;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _showDeleteConfirmation;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _testStatus = string.Empty;

    // 编辑中的属性
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _transportType = "stdio";

    [ObservableProperty]
    private string _command = string.Empty;

    [ObservableProperty]
    private string _arguments = string.Empty;

    [ObservableProperty]
    private string _environmentVariablesText = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    private MCPServerConfig? _editingConfig;

    public MCPConfigPageViewModel(
        INotificationService notificationService,
        McpService mcpService,
        ILogger<MCPConfigPageViewModel>? logger)
        : base(logger)
    {
        _configService = MCPServerConfigService.Instance;
        _notificationService = notificationService;
        _mcpService = mcpService;
        LoadServerConfigs();
    }

    /// <summary>
    /// 加载服务器配置列表
    /// </summary>
    private void LoadServerConfigs()
    {
        try
        {
            _configService.LoadConfigs();
            ServerConfigs.Clear();
            foreach (var config in _configService.ServerConfigs)
            {
                ServerConfigs.Add(config);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "加载MCP服务器配置失败");
            _notificationService?.ShowError($"加载配置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 添加服务器
    /// </summary>
    [RelayCommand]
    private void AddServer()
    {
        // 清空选中项，避免与编辑状态冲突
        SelectedConfig = null;

        _editingConfig = new MCPServerConfig
        {
            Id = Guid.NewGuid().ToString(),
            TransportType = "stdio",
            IsEnabled = true
        };

        LoadConfigToUI(_editingConfig);
        IsEditing = true;
    }

    /// <summary>
    /// 编辑服务器
    /// </summary>
    [RelayCommand]
    private void EditServer()
    {
        if (SelectedConfig == null) return;

        // 手动复制配置
        _editingConfig = new MCPServerConfig
        {
            Id = SelectedConfig.Id,
            Name = SelectedConfig.Name,
            Description = SelectedConfig.Description,
            TransportType = SelectedConfig.TransportType,
            Command = SelectedConfig.Command,
            Arguments = SelectedConfig.Arguments,
            IsEnabled = SelectedConfig.IsEnabled,
            EnvironmentVariables = SelectedConfig.EnvironmentVariables != null
                ? new Dictionary<string, string?>(SelectedConfig.EnvironmentVariables)
                : null
        };
        LoadConfigToUI(_editingConfig);
        IsEditing = true;
    }

    /// <summary>
    /// 保存服务器
    /// </summary>
    [RelayCommand]
    private void SaveServer()
    {
        if (_editingConfig == null) return;

        // 验证必填字段
        if (string.IsNullOrWhiteSpace(Name))
        {
            _notificationService?.ShowWarning("请输入服务器名称");
            return;
        }

        if (string.IsNullOrWhiteSpace(Command))
        {
            _notificationService?.ShowWarning("请输入命令或URL");
            return;
        }

        try
        {
            // 更新配置对象
            SaveUIToConfig(_editingConfig);

            // 保存到服务
            _configService.AddOrUpdateConfig(_editingConfig);

            // 刷新列表
            LoadServerConfigs();
            IsEditing = false;
            _editingConfig = null;

            _notificationService?.ShowSuccess("保存成功");
            Logger?.LogInformation("MCP服务器配置已保存: {Name}", Name);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "保存MCP服务器配置失败");
            _notificationService?.ShowError($"保存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 取消编辑
    /// </summary>
    [RelayCommand]
    private void CancelEdit()
    {
        SelectedConfig = null;
        IsEditing = false;
        _editingConfig = null;
    }

    /// <summary>
    /// 测试连接
    /// </summary>
    [RelayCommand]
    private async Task TestConnection()
    {
        if (_editingConfig == null) return;

        // 验证必填字段
        if (string.IsNullOrWhiteSpace(Name))
        {
            _notificationService?.ShowWarning("请输入服务器名称");
            return;
        }

        if (string.IsNullOrWhiteSpace(Command))
        {
            _notificationService?.ShowWarning("请输入命令或URL");
            return;
        }

        IsTesting = true;
        TestStatus = "正在连接...";

        try
        {
            // 创建临时配置用于测试
            var testConfig = new MCPServerConfig
            {
                Id = _editingConfig.Id,
                Name = Name,
                Description = Description,
                TransportType = TransportType,
                Command = Command,
                Arguments = Arguments,
                IsEnabled = true
            };

            // 解析环境变量
            if (!string.IsNullOrWhiteSpace(EnvironmentVariablesText))
            {
                testConfig.EnvironmentVariables = new Dictionary<string, string?>();
                var lines = EnvironmentVariablesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        testConfig.EnvironmentVariables[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }

            // 设置超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // 尝试连接并获取工具列表
            // 使用注入的服务或创建临时实例（仅用于测试）
            var service = _mcpService;
            var shouldDispose = false;

            if (service == null)
            {
                service = new McpService();
                shouldDispose = true;
            }

            try
            {
                var tools = await service.GetAIToolsAsync([testConfig]);
                var toolCount = tools.Count;

                if (toolCount > 0)
                {
                    TestStatus = $"连接成功！发现 {toolCount} 个工具";
                    _notificationService?.ShowSuccess($"连接成功！MCP服务器提供 {toolCount} 个工具");
                    Logger?.LogInformation("MCP服务器测试连接成功: {Name}, 工具数: {Count}", Name, toolCount);
                }
                else
                {
                    TestStatus = "连接成功，但未发现可用工具";
                    _notificationService?.ShowWarning("连接成功，但未发现可用工具");
                    Logger?.LogWarning("MCP服务器连接成功但无工具: {Name}", Name);
                }
            }
            catch (OperationCanceledException)
            {
                TestStatus = "连接超时";
                _notificationService?.ShowError("连接超时，请检查服务器配置");
                Logger?.LogWarning("MCP服务器连接超时: {Name}", Name);
            }
            catch (Exception ex)
            {
                TestStatus = $"连接失败: {ex.Message}";
                _notificationService?.ShowError($"连接失败: {ex.Message}");
                Logger?.LogError(ex, "MCP服务器测试连接失败: {Name}", Name);
            }
            finally
            {
                // 如果是临时创建的服务，需要释放
                if (shouldDispose && service != null)
                {
                    await service.DisposeAsync();
                }
            }
        }
        finally
        {
            IsTesting = false;

            // 3秒后清除状态信息
            _ = Task.Delay(3000).ContinueWith(_ => TestStatus = string.Empty);
        }
    }

    /// <summary>
    /// 删除服务器（显示确认对话框）
    /// </summary>
    [RelayCommand]
    private void DeleteServer()
    {
        ShowDeleteConfirmation = true;
    }

    /// <summary>
    /// 确认删除
    /// </summary>
    [RelayCommand]
    private void ConfirmDelete()
    {
        if (SelectedConfig == null) return;

        _configService.DeleteConfig(SelectedConfig.Id);
        LoadServerConfigs();
        ShowDeleteConfirmation = false;
        IsEditing = false;
    }

    /// <summary>
    /// 取消删除
    /// </summary>
    [RelayCommand]
    private void CancelDelete()
    {
        ShowDeleteConfirmation = false;
    }

    /// <summary>
    /// 将配置加载到UI
    /// </summary>
    private void LoadConfigToUI(MCPServerConfig config)
    {
        Name = config.Name ?? string.Empty;
        Description = config.Description ?? string.Empty;
        TransportType = config.TransportType ?? "stdio";
        Command = config.Command ?? string.Empty;
        Arguments = config.Arguments ?? string.Empty;
        IsEnabled = config.IsEnabled;

        // 环境变量转为文本
        if (config.EnvironmentVariables != null && config.EnvironmentVariables.Count > 0)
        {
            EnvironmentVariablesText = string.Join("\n",
                config.EnvironmentVariables.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        else
        {
            EnvironmentVariablesText = string.Empty;
        }
    }

    /// <summary>
    /// 将UI数据保存到配置
    /// </summary>
    private void SaveUIToConfig(MCPServerConfig config)
    {
        config.Name = Name;
        config.Description = Description;
        config.TransportType = TransportType;
        config.Command = Command;
        config.Arguments = Arguments;
        config.IsEnabled = IsEnabled;

        // 解析环境变量文本
        config.EnvironmentVariables = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(EnvironmentVariablesText))
        {
            var lines = EnvironmentVariablesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    config.EnvironmentVariables[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }
    }

    partial void OnSelectedConfigChanged(MCPServerConfig? value)
    {
        if (value != null)
        {
            EditServer();
        }
    }
}
