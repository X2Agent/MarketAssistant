using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Avalonia.Infrastructure;
using System.Collections.ObjectModel;
using YamlDotNet.Serialization;

namespace MarketAssistant.Avalonia.ViewModels;

/// <summary>
/// 设置页ViewModel - 完全基于 SettingViewModel
/// </summary>
public partial class SettingsPageViewModel : ViewModelBase
{
    private IStorageProvider? _storageProvider;

    // UserSetting对象，包含所有用户设置
    [ObservableProperty]
    private UserSetting _userSetting = new();

    // 模型列表 - ViewModel特有属性
    [ObservableProperty]
    private ObservableCollection<string> _models = [];

    // 判断知识库目录是否有效 - 计算属性
    public bool IsKnowledgeDirectoryValid => !string.IsNullOrEmpty(UserSetting.KnowledgeFileDirectory) && Directory.Exists(UserSetting.KnowledgeFileDirectory);

    // 是否正在向量化
    [ObservableProperty]
    private bool _isVectorizing;

    // Web Search服务商列表
    public List<string> WebSearchProviders { get; } = new List<string> { "Bing", "Brave", "Tavily" };

    // API密钥获取URL
    public string ModelApiUrl { get; } = "https://cloud.siliconflow.cn/i/z4lbHdBE";
    public string ZhiTuApiUrl { get; } = "https://www.zhituapi.com/gettoken.html";

    // 协调分析师启用状态 - 计算属性
    public bool EnableAnalysisSynthesizer => UserSetting.AnalystRoleSettings.EnableAnalysisSynthesizer;

    // 基本面分析师启用状态 - 计算属性
    public bool EnableFundamentalAnalyst => UserSetting.AnalystRoleSettings.EnableFundamentalAnalyst;

    // 市场情绪分析师启用状态 - 代理属性
    public bool EnableMarketSentimentAnalyst
    {
        get => UserSetting.AnalystRoleSettings.EnableMarketSentimentAnalyst;
        set
        {
            if (UserSetting.AnalystRoleSettings.EnableMarketSentimentAnalyst != value)
            {
                UserSetting.AnalystRoleSettings.EnableMarketSentimentAnalyst = value;
                OnPropertyChanged();
            }
        }
    }

    // 财务分析师启用状态 - 代理属性
    public bool EnableFinancialAnalyst
    {
        get => UserSetting.AnalystRoleSettings.EnableFinancialAnalyst;
        set
        {
            if (UserSetting.AnalystRoleSettings.EnableFinancialAnalyst != value)
            {
                UserSetting.AnalystRoleSettings.EnableFinancialAnalyst = value;
                OnPropertyChanged();
            }
        }
    }

    // 技术分析师启用状态 - 代理属性
    public bool EnableTechnicalAnalyst
    {
        get => UserSetting.AnalystRoleSettings.EnableTechnicalAnalyst;
        set
        {
            if (UserSetting.AnalystRoleSettings.EnableTechnicalAnalyst != value)
            {
                UserSetting.AnalystRoleSettings.EnableTechnicalAnalyst = value;
                OnPropertyChanged();
            }
        }
    }

    // 新闻事件分析师启用状态 - 代理属性
    public bool EnableNewsEventAnalyst
    {
        get => UserSetting.AnalystRoleSettings.EnableNewsEventAnalyst;
        set
        {
            if (UserSetting.AnalystRoleSettings.EnableNewsEventAnalyst != value)
            {
                UserSetting.AnalystRoleSettings.EnableNewsEventAnalyst = value;
                OnPropertyChanged();
            }
        }
    }

    public SettingsPageViewModel()
    {
        // 使用异步初始化方法
        _ = InitializeAsync();
    }

    /// <summary>
    /// 设置 StorageProvider（从 View 调用）
    /// </summary>
    public void SetStorageProvider(IStorageProvider? storageProvider)
    {
        _storageProvider = storageProvider;
    }

    private async Task InitializeAsync()
    {
        // 先加载模型列表
        await LoadModelsAsync();
        // TODO: 加载设置（需要依赖注入 IUserSettingService）
    }

    /// <summary>
    /// 打开API密钥网站命令
    /// </summary>
    [RelayCommand]
    private Task OpenModelApiWebsite() => OpenUrlAsync(ModelApiUrl);

    [RelayCommand]
    private Task OpenZhiTuApiWebsite() => OpenUrlAsync(ZhiTuApiUrl);

    /// <summary>
    /// 选择知识库目录
    /// </summary>
    [RelayCommand]
    private async Task SelectKnowledgeDirectory()
    {
        if (_storageProvider == null) return;

        try
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择知识库目录",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                UserSetting.KnowledgeFileDirectory = folders[0].Path.LocalPath;
                OnPropertyChanged(nameof(UserSetting));
                OnPropertyChanged(nameof(IsKnowledgeDirectoryValid));
            }
        }
        catch (Exception ex)
        {
            // 忽略错误或记录日志
            System.Diagnostics.Debug.WriteLine($"选择知识库目录失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 选择浏览器路径
    /// </summary>
    [RelayCommand]
    private async Task SelectBrowserPath()
    {
        if (_storageProvider == null) return;

        try
        {
            // 定义可执行文件类型
            var executableFileType = new FilePickerFileType("可执行文件")
            {
                Patterns = new[] { "*.exe", "*" },
                MimeTypes = new[] { "application/octet-stream" }
            };

            var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择浏览器可执行文件",
                AllowMultiple = false,
                FileTypeFilter = new[] { executableFileType, FilePickerFileTypes.All }
            });

            if (files.Count > 0)
            {
                UserSetting.BrowserPath = files[0].Path.LocalPath;
                OnPropertyChanged(nameof(UserSetting));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"选择浏览器路径失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 选择日志路径
    /// </summary>
    [RelayCommand]
    private async Task SelectLogPath()
    {
        if (_storageProvider == null) return;

        try
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择日志路径",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                UserSetting.LogPath = Path.Combine(folders[0].Path.LocalPath, "logs");
                OnPropertyChanged(nameof(UserSetting));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"选择日志路径失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 向量化文档方法
    /// </summary>
    [RelayCommand]
    private async Task VectorizeDocuments()
    {
        // TODO: 实现向量化逻辑（需要依赖注入相关服务）
        await Task.CompletedTask;
    }

    /// <summary>
    /// 保存设置
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        // TODO: 实现保存逻辑（需要依赖注入 IUserSettingService）
    }

    /// <summary>
    /// 重置设置
    /// </summary>
    [RelayCommand]
    private void Reset()
    {
        // TODO: 实现重置逻辑（需要依赖注入 IUserSettingService）
    }

    /// <summary>
    /// 导航到MCP服务器配置页面
    /// </summary>
    [RelayCommand]
    private void NavigateToMCPConfig()
    {
        WeakReferenceMessenger.Default.Send(new NavigationMessage("MCPConfig"));
    }

    /// <summary>
    /// 加载模型列表
    /// </summary>
    private async Task LoadModelsAsync()
    {
        try
        {
            // 清空当前模型列表
            Models.Clear();

            // 从YAML文件加载模型
            var modelsFromYaml = await LoadModelsFromYamlAsync();

            foreach (var model in modelsFromYaml)
            {
                Models.Add(model);
            }
        }
        catch (Exception)
        {
            // 忽略加载错误
        }
    }

    /// <summary>
    /// 从yaml文件加载模型列表
    /// </summary>
    private async Task<List<string>> LoadModelsFromYamlAsync()
    {
        try
        {
            var models = new List<string>();

            // 读取config/models.yaml文件
            var configPath = Path.Combine(AppContext.BaseDirectory, "config", "models.yaml");
            if (!File.Exists(configPath))
            {
                return models;
            }

            var yamlContent = await File.ReadAllTextAsync(configPath);

            // 解析yaml内容
            var deserializer = new DeserializerBuilder().Build();
            var yamlData = deserializer.Deserialize<Dictionary<string, List<string>>>(yamlContent);

            // 返回模型列表
            if (yamlData != null && yamlData.ContainsKey("models"))
            {
                models = yamlData["models"];
            }

            return models;
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// 打开URL
    /// </summary>
    private async Task OpenUrlAsync(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // 忽略打开错误
        }
        await Task.CompletedTask;
    }
}