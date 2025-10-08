using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.Avalonia.ViewModels;

/// <summary>
/// MCP服务器配置页ViewModel - 对应 MCPServerConfigViewModel
/// </summary>
public partial class MCPConfigPageViewModel : ViewModelBase
{
    private readonly MCPServerConfigService? _configService;

    [ObservableProperty]
    private ObservableCollection<MCPServerConfig> _serverConfigs = new();

    [ObservableProperty]
    private MCPServerConfig? _selectedConfig;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _showDeleteConfirmation;

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

    public MCPConfigPageViewModel() : this(null, null)
    {
        // 设计时构造函数
    }

    public MCPConfigPageViewModel(MCPServerConfigService? configService, ILogger<MCPConfigPageViewModel>? logger) 
        : base(logger)
    {
        _configService = configService;
        LoadServerConfigs();
    }

    /// <summary>
    /// 加载服务器配置列表
    /// </summary>
    private void LoadServerConfigs()
    {
        // TODO: 暂时跳过，等待服务实现
    }

    /// <summary>
    /// 添加服务器
    /// </summary>
    [RelayCommand]
    private void AddServer()
    {
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
        if (_editingConfig == null || _configService == null) return;

        // 更新配置对象
        SaveUIToConfig(_editingConfig);

        // 保存到服务
        _configService.AddOrUpdateConfig(_editingConfig);
        
        // 刷新列表
        LoadServerConfigs();
        IsEditing = false;
        _editingConfig = null;
    }

    /// <summary>
    /// 取消编辑
    /// </summary>
    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        _editingConfig = null;
    }

    /// <summary>
    /// 测试连接
    /// </summary>
    [RelayCommand]
    private async Task TestConnection()
    {
        // TODO: 实现测试连接逻辑
        await Task.CompletedTask;
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
        if (SelectedConfig == null || _configService == null) return;

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
        if (value != null && !IsEditing)
        {
            EditServer();
        }
    }
}
