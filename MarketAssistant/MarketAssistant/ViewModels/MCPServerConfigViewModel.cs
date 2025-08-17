using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

public class MCPServerConfigViewModel : ViewModelBase
{
    private readonly MCPServerConfigService _configService;

    // MCP服务器配置列表
    private ObservableCollection<MCPServerConfig> _serverConfigs = new ObservableCollection<MCPServerConfig>();
    public ObservableCollection<MCPServerConfig> ServerConfigs
    {
        get => _serverConfigs;
        set => SetProperty(ref _serverConfigs, value);
    }

    // 当前编辑中的服务器配置（内部使用）
    private MCPServerConfig _editingConfig;

    // 服务器属性（直接平铺到ViewModel中）
    public string Id => _editingConfig?.Id;

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value) && _editingConfig != null)
            {
                _editingConfig.Name = value;
            }
        }
    }

    private string _description;
    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value) && _editingConfig != null)
            {
                _editingConfig.Description = value;
            }
        }
    }

    private string _transportType;
    public string TransportType
    {
        get => _transportType;
        set
        {
            if (SetProperty(ref _transportType, value) && _editingConfig != null)
            {
                _editingConfig.TransportType = value;
                TestConnectionCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    private string _command;
    public string Command
    {
        get => _command;
        set
        {
            if (SetProperty(ref _command, value) && _editingConfig != null)
            {
                _editingConfig.Command = value;
                TestConnectionCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    private string _arguments;
    public string Arguments
    {
        get => _arguments;
        set
        {
            if (SetProperty(ref _arguments, value) && _editingConfig != null)
            {
                _editingConfig.Arguments = value;
            }
        }
    }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value) && _editingConfig != null)
            {
                _editingConfig.IsEnabled = value;
            }
        }
    }

    // 环境变量集合
    public ObservableCollection<KeyValuePair<string, string?>> EnvironmentVariables { get; } = new();

    private string _environmentVariablesText = "";
    public string EnvironmentVariablesText
    {
        get => _environmentVariablesText;
        set
        {
            if (SetProperty(ref _environmentVariablesText, value))
            {
                // 将文本转换为环境变量集合
                UpdateEnvironmentVariablesFromText(value);
            }
        }
    }

    // 当前选中的服务器配置（为了保持兼容性）
    public MCPServerConfig SelectedConfig
    {
        get => _editingConfig;
        set
        {
            if (value != null)
            {
                _editingConfig = value;
                UpdatePropertiesFromConfig(value);
            }
        }
    }

    // 是否正在编辑
    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    // 是否显示删除确认对话框
    private bool _showDeleteConfirmation;
    public bool ShowDeleteConfirmation
    {
        get => _showDeleteConfirmation;
        set => SetProperty(ref _showDeleteConfirmation, value);
    }

    // 命令
    public IRelayCommand AddServerCommand { get; }
    public IRelayCommand EditServerCommand { get; }
    public IRelayCommand DeleteServerCommand { get; }
    public IRelayCommand SaveServerCommand { get; }
    public IRelayCommand CancelEditCommand { get; }
    public IRelayCommand ConfirmDeleteCommand { get; }
    public IRelayCommand CancelDeleteCommand { get; }
    public IRelayCommand TestConnectionCommand { get; }

    public MCPServerConfigViewModel(ILogger<MCPServerConfigViewModel> logger) : base(logger)
    {
        _configService = MCPServerConfigService.Instance;

        // 初始化命令
        AddServerCommand = new RelayCommand(AddServer);
        EditServerCommand = new RelayCommand(EditServer, CanEditServer);
        DeleteServerCommand = new RelayCommand(DeleteServer, CanDeleteServer);
        SaveServerCommand = new RelayCommand(SaveServer, CanSaveServer);
        CancelEditCommand = new RelayCommand(CancelEdit);
        ConfirmDeleteCommand = new RelayCommand(ConfirmDelete);
        CancelDeleteCommand = new RelayCommand(CancelDelete);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanTestConnection);

        // 加载服务器配置
        LoadServerConfigs();

        // 初始化编辑配置
        ResetEditingConfig();
    }

    // 加载服务器配置
    private void LoadServerConfigs()
    {
        ServerConfigs.Clear();
        foreach (var config in _configService.ServerConfigs)
        {
            ServerConfigs.Add(config);
        }
    }

    // 添加新服务器
    private void AddServer()
    {
        ResetEditingConfig();
        IsEditing = true;
    }

    // 编辑服务器
    private void EditServer()
    {
        if (_editingConfig != null)
        {
            // 直接编辑当前配置，不需要创建副本
            IsEditing = true;
        }
    }

    // 是否可以编辑服务器
    private bool CanEditServer()
    {
        return SelectedConfig != null;
    }

    // 删除服务器
    private void DeleteServer()
    {
        if (_editingConfig != null)
        {
            ShowDeleteConfirmation = true;
        }
    }

    // 是否可以删除服务器
    private bool CanDeleteServer()
    {
        return _editingConfig != null;
    }

    // 确认删除服务器
    private void ConfirmDelete()
    {
        if (_editingConfig != null)
        {
            _configService.DeleteConfig(_editingConfig.Id);
            LoadServerConfigs();
            ShowDeleteConfirmation = false;
            WeakReferenceMessenger.Default.Send(new ToastMessage("已删除服务器配置"));
        }
    }

    // 取消删除
    private void CancelDelete()
    {
        ShowDeleteConfirmation = false;
    }

    // 保存服务器配置
    private void SaveServer()
    {
        if (_editingConfig != null)
        {
            // 验证必填字段
            if (string.IsNullOrWhiteSpace(Name))
            {
                WeakReferenceMessenger.Default.Send(new ToastMessage("请输入服务器名称"));
                return;
            }

            if (string.IsNullOrWhiteSpace(Command))
            {
                string fieldName = TransportType == "stdio" ? "命令" : "URL";
                WeakReferenceMessenger.Default.Send(new ToastMessage($"请输入{fieldName}"));
                return;
            }

            // 同步环境变量
            SyncEnvironmentVariables();

            // 保存配置
            _configService.AddOrUpdateConfig(_editingConfig);
            LoadServerConfigs();
            IsEditing = false;
            WeakReferenceMessenger.Default.Send(new ToastMessage("已保存服务器配置"));
        }
    }

    // 是否可以保存服务器
    private bool CanSaveServer()
    {
        return _editingConfig != null;
    }

    private bool CanTestConnection()
    {
        return _editingConfig != null && !string.IsNullOrWhiteSpace(Command);
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            if (_editingConfig == null)
            {
                WeakReferenceMessenger.Default.Send(new ToastMessage("请先选择或新建服务器配置"));
                return;
            }

            var kernelFunctions = await McpPlugin.GetKernelFunctionsAsync(_editingConfig);
            var count = kernelFunctions.Count();
            if (count < 0)
            {
                throw new Exception("未发现任何工具");
            }

            WeakReferenceMessenger.Default.Send(new ToastMessage($"连接成功，发现 {count} 个工具"));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new ToastMessage($"连接失败：{ex.Message}"));
        }
    }

    // 取消编辑
    private void CancelEdit()
    {
        IsEditing = false;
        ResetEditingConfig();
    }

    // 重置编辑中的配置
    private void ResetEditingConfig()
    {
        _editingConfig = new MCPServerConfig
        {
            Id = Guid.NewGuid().ToString(),
            Name = "",
            Description = "",
            TransportType = "stdio",
            Command = "",
            Arguments = "",
            IsEnabled = true,
            EnvironmentVariables = new()
        };

        UpdatePropertiesFromConfig(_editingConfig);
    }

    // 添加环境变量
    public void AddEnvironmentVariable(string key, string? value)
    {
        if (_editingConfig != null && !string.IsNullOrWhiteSpace(key))
        {
            EnvironmentVariables.Add(new KeyValuePair<string, string?>(key, value));
        }
    }

    // 删除环境变量
    public void RemoveEnvironmentVariable(string key)
    {
        if (_editingConfig != null)
        {
            var item = EnvironmentVariables.FirstOrDefault(x => x.Key == key);
            if (!EqualityComparer<KeyValuePair<string, string?>>.Default.Equals(item, default))
            {
                EnvironmentVariables.Remove(item);
            }
        }
    }

    // 从配置更新属性
    private void UpdatePropertiesFromConfig(MCPServerConfig config)
    {
        _name = config.Name;
        _description = config.Description;
        _transportType = config.TransportType;
        _command = config.Command;
        _arguments = config.Arguments;
        _isEnabled = config.IsEnabled;

        EnvironmentVariables.Clear();
        foreach (var kvp in config.EnvironmentVariables)
        {
            EnvironmentVariables.Add(kvp);
        }

        // 更新环境变量文本
        UpdateEnvironmentVariablesText();

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(TransportType));
        OnPropertyChanged(nameof(Command));
        OnPropertyChanged(nameof(Arguments));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(Id));
    }

    // 同步环境变量到配置
    private void SyncEnvironmentVariables()
    {
        if (_editingConfig != null)
        {
            _editingConfig.EnvironmentVariables.Clear();
            foreach (var kvp in EnvironmentVariables)
            {
                _editingConfig.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }
    }

    // 更新环境变量文本
    private void UpdateEnvironmentVariablesText()
    {
        var lines = new List<string>();
        foreach (var kvp in EnvironmentVariables)
        {
            lines.Add($"{kvp.Key}={kvp.Value}");
        }
        _environmentVariablesText = string.Join(Environment.NewLine, lines);
        OnPropertyChanged(nameof(EnvironmentVariablesText));
    }

    // 从文本更新环境变量集合
    private void UpdateEnvironmentVariablesFromText(string text)
    {
        EnvironmentVariables.Clear();

        if (!string.IsNullOrWhiteSpace(text))
        {
            string[] lines = text.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                int separatorIndex = line.IndexOf('=');
                if (separatorIndex > 0)
                {
                    string key = line.Substring(0, separatorIndex).Trim();
                    string val = line.Substring(separatorIndex + 1).Trim();
                    if (!string.IsNullOrEmpty(key))
                    {
                        EnvironmentVariables.Add(new KeyValuePair<string, string?>(key, val));
                    }
                }
            }
        }
    }
}