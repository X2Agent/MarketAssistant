using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Mcp;
using MarketAssistant.Services.Navigation;
using MarketAssistant.Services.Notification;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// MCP服务器配置页ViewModel - 对应 MCPServerConfigViewModel
/// </summary>
public partial class MCPConfigPageViewModel : ViewModelBase, INavigationAware
{
    public override string Title => "MCP服务器配置";

    private readonly MCPServerConfigService _configService;
    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly McpService _mcpService;
    private readonly McpToolContextProvider _mcpToolProvider;

    [ObservableProperty]
    private ObservableCollection<MCPServerConfig> _serverConfigs = new();

    [ObservableProperty]
    private MCPServerConfig? _selectedConfig;

    [ObservableProperty]
    private bool _isEditing;

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

    /// <summary>
    /// 是否为 stdio 传输类型（控制参数与环境变量输入区的可见性）
    /// </summary>
    public bool IsStdio => TransportType == "stdio";

    /// <summary>
    /// 命令/URL 输入框占位提示，随传输类型切换
    /// </summary>
    public string CommandPlaceholder => TransportType switch
    {
        "stdio" => "请输入命令，如 npx",
        "sse" => "请输入 URL，如 http://localhost:3000/sse",
        _ => "请输入 URL，如 http://localhost:3000/mcp"
    };

    /// <summary>
    /// 命令/URL 输入框下方说明文字，随传输类型切换
    /// </summary>
    public string CommandHint => TransportType == "stdio"
        ? "提示：命令参数与环境变量请在下方对应输入框填写"
        : $"提示：URL 示例 http://localhost:3000/{(TransportType == "sse" ? "sse" : "mcp")}";

    [ObservableProperty]
    private string _command = string.Empty;

    [ObservableProperty]
    private string _arguments = string.Empty;

    [ObservableProperty]
    private string _environmentVariablesText = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _allowAllTools;

    [ObservableProperty]
    private ObservableCollection<McpToolSelectionItem> _toolItems = new();

    // 表单校验错误信息（保存/测试时触发，修正后即时清除）
    [ObservableProperty]
    private string? _nameError;

    [ObservableProperty]
    private string? _commandError;

    private MCPServerConfig? _editingConfig;

    // 正在编辑的列表源配置（用于切换确认被拒时回退选中项）
    private MCPServerConfig? _editingSource;

    // 回退选中项期间抑制选择变更处理，避免递归触发确认
    private bool _suppressSelectionChanged;

    public MCPConfigPageViewModel(
        MCPServerConfigService configService,
        INotificationService notificationService,
        IDialogService dialogService,
        McpService mcpService,
        McpToolContextProvider mcpToolProvider,
        ILogger<MCPConfigPageViewModel>? logger)
        : base(logger)
    {
        _configService = configService;
        _notificationService = notificationService;
        _dialogService = dialogService;
        _mcpService = mcpService;
        _mcpToolProvider = mcpToolProvider;
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

            PromoteLegacyToolWhitelistConfigs();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "加载MCP服务器配置失败");
            _notificationService?.ShowError(ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, "加载配置"));
        }
    }

    /// <summary>
    /// 识别旧版本保存的配置并提示重新勾选工具白名单。
    /// 旧配置 JSON 无 ToolsSchemaVersion 字段（反序列化为 0）；空白名单在现行语义下
    /// 不再向 Agent 暴露任何工具，需提示用户重新勾选或开启“允许全部工具”。
    /// 提示后仅升级已启用服务器的版本号并落盘，保证每台服务器只提示一次；
    /// 禁用服务器保持旧版本号，待其启用并保存时自然升级。
    /// </summary>
    private void PromoteLegacyToolWhitelistConfigs()
    {
        var legacyEnabledConfigs = ServerConfigs
            .Where(config => config.IsEnabled && config.ToolsSchemaVersion < MCPServerConfig.CurrentToolsSchemaVersion)
            .ToList();

        if (legacyEnabledConfigs.Count == 0)
        {
            return;
        }

        var serverNames = legacyEnabledConfigs
            .Select(config => string.IsNullOrWhiteSpace(config.Name) ? config.Id : config.Name);
        _notificationService?.ShowWarning(
            "MCP 工具白名单机制已升级：未勾选工具的服务器默认不向 Agent 暴露任何工具。" +
            $"请进入 MCP 配置页为「{string.Join("、", serverNames)}」重新勾选工具，或开启“允许全部工具”。",
            10000);

        foreach (var config in legacyEnabledConfigs)
        {
            config.ToolsSchemaVersion = MCPServerConfig.CurrentToolsSchemaVersion;
        }

        _configService.SaveConfigs();
    }

    /// <summary>
    /// 添加服务器
    /// </summary>
    [RelayCommand]
    private void AddServer()
    {
        // 清空选中项，避免与编辑状态冲突
        SelectedConfig = null;
        _editingSource = null;

        _editingConfig = new MCPServerConfig
        {
            Id = Guid.NewGuid().ToString(),
            TransportType = "stdio",
            IsEnabled = true,
            ToolsSchemaVersion = MCPServerConfig.CurrentToolsSchemaVersion
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

        _editingSource = SelectedConfig;

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
            EnvironmentVariables = new Dictionary<string, string?>(SelectedConfig.EnvironmentVariables),
            Category = SelectedConfig.Category,
            AllowedTools = [.. SelectedConfig.AllowedTools],
            AllowAllTools = SelectedConfig.AllowAllTools,
            ToolsSchemaVersion = SelectedConfig.ToolsSchemaVersion
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

        // 校验必填字段
        if (!ValidateForm())
        {
            return;
        }

        try
        {
            // 更新配置对象
            SaveUIToConfig(_editingConfig);

            // 保存到服务
            _configService.AddOrUpdateConfig(_editingConfig);

            // 刷新列表并使工具缓存失效
            LoadServerConfigs();
            _mcpToolProvider.Invalidate();
            IsEditing = false;
            _editingConfig = null;
            _editingSource = null;

            _notificationService?.ShowSuccess("保存成功");
            Logger?.LogInformation("MCP服务器配置已保存: {Name}", Name);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "保存MCP服务器配置失败");
            _notificationService?.ShowError(ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, "保存配置"));
        }
    }

    /// <summary>
    /// 取消编辑（存在未保存修改时需用户确认）
    /// </summary>
    [RelayCommand]
    private async Task CancelEdit()
    {
        if (HasUnsavedChanges())
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "未保存的修改",
                "当前有未保存的修改，确定放弃并退出编辑吗？",
                "放弃修改",
                "继续编辑");

            if (!confirmed) return;
        }

        ForceCancelEdit();
    }

    /// <summary>
    /// 直接退出编辑状态，不做脏检查（用于页面导航离开等自动取消场景）
    /// </summary>
    private void ForceCancelEdit()
    {
        SelectedConfig = null;
        IsEditing = false;
        _editingConfig = null;
        _editingSource = null;
        NameError = null;
        CommandError = null;
    }

    /// <summary>
    /// 测试连接
    /// </summary>
    [RelayCommand]
    private async Task TestConnection()
    {
        if (_editingConfig == null) return;

        // 校验必填字段
        if (!ValidateForm())
        {
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
                IsEnabled = true,
                EnvironmentVariables = ParseEnvironmentVariables(),
                Category = _editingConfig.Category,
                AllowedTools = [.. _editingConfig.AllowedTools],
                AllowAllTools = AllowAllTools,
                ToolsSchemaVersion = MCPServerConfig.CurrentToolsSchemaVersion
            };

            // 设置超时
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                var tools = await _mcpService.GetAIToolsAsync([testConfig], cts.Token);
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
                TestStatus = "连接失败";
                _notificationService?.ShowError(ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, "连接"));
                Logger?.LogError(ex, "MCP服务器测试连接失败: {Name}", Name);
            }
        }
        finally
        {
            IsTesting = false;

            // 3秒后清除状态信息（TestStatus 为 UI 绑定属性，回调经 Dispatcher 切回 UI 线程，
            // 不依赖当前同步上下文，避免链路中加入 ConfigureAwait(false) 后调度失效）
            _ = Task.Delay(3000).ContinueWith(
                _ => Avalonia.Threading.Dispatcher.UIThread.Post(() => TestStatus = string.Empty));
        }
    }

    /// <summary>
    /// 删除服务器（显示确认对话框）
    /// </summary>
    [RelayCommand]
    private async Task DeleteServer()
    {
        if (SelectedConfig == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "确认删除",
            "确定要删除此服务器配置吗？此操作无法撤销。",
            "删除",
            "取消");

        if (confirmed)
        {
            _configService.DeleteConfig(SelectedConfig.Id);
            LoadServerConfigs();
            _mcpToolProvider.Invalidate();
            IsEditing = false;
            _editingConfig = null;
            _editingSource = null;
            _notificationService?.ShowSuccess("删除成功");
        }
    }

    /// <summary>
    /// 连接服务器并加载工具列表，供勾选工具白名单。
    /// </summary>
    [RelayCommand]
    private async Task LoadToolsAsync()
    {
        if (_editingConfig is null)
            return;

        IsTesting = true;
        TestStatus = "正在连接服务器获取工具列表...";
        try
        {
            // 先把表单内容写入编辑配置，确保用最新的连接参数建立会话
            SaveUIToConfig(_editingConfig);
            var tools = await _mcpService.GetServerToolsAsync(_editingConfig);
            var selectedNames = ToolItems
                .Where(item => item.IsSelected)
                .Select(item => item.Name)
                .ToHashSet(StringComparer.Ordinal);
            ToolItems.Clear();
            foreach (var (name, description) in tools)
            {
                ToolItems.Add(new McpToolSelectionItem
                {
                    Name = name,
                    Description = description,
                    IsSelected = selectedNames.Contains(name)
                });
            }
            TestStatus = $"已加载 {tools.Count} 个工具";
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "加载 MCP 工具列表失败");
            TestStatus = $"加载工具列表失败: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
            // TestStatus 为 UI 绑定属性，回调经 Dispatcher 切回 UI 线程（同上，不依赖同步上下文）
            _ = Task.Delay(3000).ContinueWith(
                _ => Avalonia.Threading.Dispatcher.UIThread.Post(() => TestStatus = string.Empty));
        }
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
        AllowAllTools = config.AllowAllTools;

        // 依据已保存的白名单回填勾选态（描述在“加载工具列表”后补全）
        ToolItems.Clear();
        foreach (var toolName in config.AllowedTools)
        {
            ToolItems.Add(new McpToolSelectionItem { Name = toolName, IsSelected = true });
        }

        if (!config.AllowAllTools && config.AllowedTools.Count == 0)
        {
            TestStatus = "该服务器尚未勾选任何工具，当前不会暴露任何工具，请点击加载工具列表后勾选";
        }

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

        // 重新载入表单时清除历史校验错误
        NameError = null;
        CommandError = null;
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
        config.AllowAllTools = AllowAllTools;
        config.ToolsSchemaVersion = MCPServerConfig.CurrentToolsSchemaVersion;
        config.AllowedTools = AllowAllTools
            ? []
            : ToolItems.Where(item => item.IsSelected).Select(item => item.Name).ToList();
        config.EnvironmentVariables = ParseEnvironmentVariables();
    }

    /// <summary>
    /// 解析环境变量文本（格式: KEY=VALUE，每行一个）
    /// </summary>
    private Dictionary<string, string?> ParseEnvironmentVariables()
    {
        var result = new Dictionary<string, string?>();

        if (!string.IsNullOrWhiteSpace(EnvironmentVariablesText))
        {
            var lines = EnvironmentVariablesText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    result[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 校验名称必填
    /// </summary>
    private void ValidateName()
    {
        NameError = string.IsNullOrWhiteSpace(Name) ? "请输入服务器名称" : null;
    }

    /// <summary>
    /// 校验命令/URL必填及URL格式（sse/streamableHttp 时 Command 字段存储 URL）
    /// </summary>
    private void ValidateCommand()
    {
        if (string.IsNullOrWhiteSpace(Command))
        {
            CommandError = TransportType == "stdio" ? "请输入命令" : "请输入 URL";
        }
        else if (TransportType != "stdio" && !Uri.TryCreate(Command, UriKind.Absolute, out _))
        {
            CommandError = "URL 格式不正确，例如 http://localhost:3000/mcp";
        }
        else
        {
            CommandError = null;
        }
    }

    /// <summary>
    /// 校验表单必填项，返回是否全部通过
    /// </summary>
    private bool ValidateForm()
    {
        ValidateName();
        ValidateCommand();
        return NameError is null && CommandError is null;
    }

    partial void OnNameChanged(string value)
    {
        // 已显示错误时即时重新校验，便于用户修正后错误提示消失
        if (NameError is not null)
        {
            ValidateName();
        }
    }

    partial void OnCommandChanged(string value)
    {
        if (CommandError is not null)
        {
            ValidateCommand();
        }
    }

    partial void OnTransportTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsStdio));
        OnPropertyChanged(nameof(CommandPlaceholder));
        OnPropertyChanged(nameof(CommandHint));
        if (CommandError is not null)
        {
            ValidateCommand();
        }
    }

    partial void OnSelectedConfigChanged(MCPServerConfig? value)
    {
        if (value is null || _suppressSelectionChanged)
        {
            return;
        }

        // 存在未保存修改时先确认，用户拒绝则回退选中项
        if (IsEditing && HasUnsavedChanges() && !ReferenceEquals(value, _editingSource))
        {
            _ = HandleSelectionChangeAsync(value);
            return;
        }

        EditServer();
    }

    /// <summary>
    /// 处理带未保存修改的选中切换：确认丢弃后加载目标配置，否则回退选中项
    /// </summary>
    private async Task HandleSelectionChangeAsync(MCPServerConfig target)
    {
        try
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "未保存的修改",
                $"服务器「{(target.Name is { Length: > 0 } n ? n : target.Id)}」有未保存的修改，切换后将丢弃这些修改。是否继续？",
                "丢弃并切换",
                "继续编辑");

            if (confirmed)
            {
                EditServer();
            }
            else
            {
                _suppressSelectionChanged = true;
                try
                {
                    SelectedConfig = _editingSource;
                }
                finally
                {
                    _suppressSelectionChanged = false;
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "处理MCP服务器切换确认失败");
            EditServer();
        }
    }

    /// <summary>
    /// 判断当前表单是否存在未保存的修改
    /// </summary>
    private bool HasUnsavedChanges()
    {
        if (_editingConfig is null || !IsEditing)
        {
            return false;
        }

        return !string.Equals(Name, _editingConfig.Name, StringComparison.Ordinal)
            || !string.Equals(Description, _editingConfig.Description, StringComparison.Ordinal)
            || !string.Equals(TransportType, _editingConfig.TransportType, StringComparison.Ordinal)
            || !string.Equals(Command, _editingConfig.Command, StringComparison.Ordinal)
            || !string.Equals(Arguments, _editingConfig.Arguments, StringComparison.Ordinal)
            || IsEnabled != _editingConfig.IsEnabled
            || !AreDictionariesEqual(ParseEnvironmentVariables(), _editingConfig.EnvironmentVariables)
            || !AreAllowedToolsEqual();
    }

    /// <summary>
    /// 比较两个环境变量字典是否一致（忽略顺序）
    /// </summary>
    private static bool AreDictionariesEqual(Dictionary<string, string?> left, Dictionary<string, string?> right)
        => left.Count == right.Count && left.All(kv => right.TryGetValue(kv.Key, out var value) && value == kv.Value);

    /// <summary>
    /// 比较当前勾选的工具白名单（含允许全部工具开关）与编辑配置是否一致
    /// </summary>
    private bool AreAllowedToolsEqual()
    {
        if (AllowAllTools != _editingConfig!.AllowAllTools)
        {
            return false;
        }

        if (AllowAllTools)
        {
            return true;
        }

        var selected = ToolItems.Where(item => item.IsSelected).Select(item => item.Name).ToList();
        return selected.Count == _editingConfig.AllowedTools.Count
            && selected.All(_editingConfig.AllowedTools.Contains);
    }

    public void OnNavigatedFrom()
    {
        // 离开页面时如果正在编辑但未保存，自动取消编辑状态
        if (IsEditing)
        {
            ForceCancelEdit();
        }
    }

    public void OnNavigatedTo(object? parameter)
    {
        // 默认调用：首次进入
        OnNavigatedTo(parameter, isReactivation: false);
    }

    public void OnNavigatedTo(object? parameter, bool isReactivation)
    {
        // 首次进入时加载配置；GoBack 重新激活时也刷新，确保数据最新
        LoadServerConfigs();
    }
}
