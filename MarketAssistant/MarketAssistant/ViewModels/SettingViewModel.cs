using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using MarketAssistant.Vectors;
using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using System.Collections.ObjectModel;
using YamlDotNet.Serialization;

namespace MarketAssistant.ViewModels;

public partial class SettingViewModel : ViewModelBase
{
    private readonly IUserSettingService _userSettingService;
    private readonly IBrowserService _browserService;
    private readonly VectorStore _vectorStore;
    private readonly IUserEmbeddingService _dynamicEmbeddingService;
    private readonly IRagIngestionService _ingestionService;

    // UserSetting对象，包含所有用户设置
    private UserSetting _userSetting = new();
    public UserSetting UserSetting
    {
        get => _userSetting;
        set
        {
            if (SetProperty(ref _userSetting, value))
            {
                OnPropertyChanged(nameof(IsKnowledgeDirectoryValid));
            }
        }
    }

    // 模型列表 - ViewModel特有属性
    private ObservableCollection<string> _models = [];
    public ObservableCollection<string> Models
    {
        get => _models;
        set => SetProperty(ref _models, value);
    }

    // 判断知识库目录是否有效 - 计算属性
    public bool IsKnowledgeDirectoryValid => !string.IsNullOrEmpty(UserSetting.KnowledgeFileDirectory) && Directory.Exists(UserSetting.KnowledgeFileDirectory);

    // 是否正在向量化
    private bool _isVectorizing;
    public bool IsVectorizing
    {
        get => _isVectorizing;
        set => SetProperty(ref _isVectorizing, value);
    }

    // Web Search服务商列表
    public List<string> WebSearchProviders { get; } = new List<string> { "Bing", "Brave", "Tavily" };

    // API密钥获取URL
    public string ModelApiUrl { get; } = "https://cloud.siliconflow.cn/i/z4lbHdBE";
    public string ZhiTuApiUrl { get; } = "https://www.zhituapi.com/gettoken.html";

    // 打开API密钥网站命令
    [RelayCommand]
    private async Task OpenModelApiWebsite() => await OpenUrlAsync(ModelApiUrl);

    [RelayCommand]
    private async Task OpenZhiTuApiWebsite() => await OpenUrlAsync(ZhiTuApiUrl);

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

    // 选择知识库目录命令
    public IRelayCommand SelectKnowledgeDirectoryCommand { get; }

    // 向量化文档命令
    public IRelayCommand VectorizeDocumentsCommand { get; }

    // 选择浏览器路径命令
    public IRelayCommand SelectBrowserPathCommand { get; }

    // 选择日志路径命令
    public IRelayCommand SelectLogPathCommand { get; }

    // 保存命令
    public IRelayCommand SaveCommand { get; }

    // 重置命令
    public IRelayCommand ResetCommand { get; }

    // 导航到MCP服务器配置页面命令
    public IRelayCommand NavigateToMCPConfigCommand { get; }

    public SettingViewModel(
        ILogger<SettingViewModel> logger,
        IUserSettingService userSettingService,
        IBrowserService browserService,
        VectorStore vectorStore,
        IUserEmbeddingService dynamicEmbeddingService,
        IRagIngestionService ingestionService) : base(logger)
    {
        _userSettingService = userSettingService;
        _browserService = browserService;
        _vectorStore = vectorStore;
        _dynamicEmbeddingService = dynamicEmbeddingService;
        _ingestionService = ingestionService;

        // 初始化命令
        SelectKnowledgeDirectoryCommand = new RelayCommand(SelectKnowledgeDirectory);
        VectorizeDocumentsCommand = new RelayCommand(VectorizeDocuments, () => IsKnowledgeDirectoryValid && !IsVectorizing);
        SelectBrowserPathCommand = new RelayCommand(SelectBrowserPath);
        SelectLogPathCommand = new RelayCommand(SelectLogPath);
        SaveCommand = new RelayCommand(SaveSettings);
        ResetCommand = new RelayCommand(ResetSettings);
        NavigateToMCPConfigCommand = new RelayCommand(async () => await Shell.Current.GoToAsync("mcpconfig"));

        // 使用异步初始化方法
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        // 先加载模型列表
        await LoadModelsAsync();
        // 再加载设置
        LoadSettings();
    }

    private async void SelectKnowledgeDirectory()
    {
        await SafeExecuteAsync(async () =>
        {
            // 使用MAUI的跨平台文件夹选择API
            var folderResult = await FolderPicker.Default.PickAsync();

            if (folderResult != null && folderResult.IsSuccessful)
            {
                UserSetting.KnowledgeFileDirectory = folderResult.Folder.Path;
                // 手动触发IsKnowledgeDirectoryValid属性通知
                OnPropertyChanged(nameof(IsKnowledgeDirectoryValid));
                // 更新VectorizeDocumentsCommand的可执行状态
                VectorizeDocumentsCommand.NotifyCanExecuteChanged();
            }
        }, "选择知识库目录");
    }

    private async void SelectBrowserPath()
    {
        await SafeExecuteAsync(async () =>
        {
            // 创建适用于不同平台的文件类型过滤器
            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, [ ".exe" ] },
                { DevicePlatform.MacCatalyst, [ "*" ]},
                { DevicePlatform.macOS, [ "*" ] }
            });

            // 使用MAUI的跨平台文件选择API
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择浏览器可执行文件",
                FileTypes = fileTypes
            });

            if (result != null)
            {
                UserSetting.BrowserPath = result.FullPath;
            }
            else
            {
                // 如果用户取消选择，尝试自动检测浏览器路径
                if (_browserService != null)
                {
                    var (browserPath, found) = _browserService.CheckBrowser();
                    if (found)
                    {
                        UserSetting.BrowserPath = browserPath;
                    }
                }
            }
        });
    }

    private async void SelectLogPath()
    {
        await SafeExecuteAsync(async () =>
        {
            // 使用MAUI的跨平台文件夹选择API
            var folderResult = await FolderPicker.Default.PickAsync();

            if (folderResult != null && folderResult.IsSuccessful)
            {
                UserSetting.LogPath = Path.Combine(folderResult.Folder.Path, "logs");
            }
        }, "选择日志路径");
    }

    private void SaveSettings()
    {
        try
        {
            IsBusy = true;

            // 获取当前保存的LogPath用于比较
            var currentLogPath = _userSettingService.CurrentSetting.LogPath;

            // 直接使用UserSetting对象更新设置
            _userSettingService.UpdateSettings(UserSetting);

            // 检测日志路径是否发生变更
            if (!string.Equals(currentLogPath, UserSetting.LogPath, StringComparison.OrdinalIgnoreCase))
            {
                WeakReferenceMessenger.Default.Send(new ToastMessage("设置已保存！日志路径已更改，重启应用后生效。"));
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new ToastMessage("操作成功！"));
            }
        }
        catch (Exception ex)
        {
            // 处理异常
            Logger?.LogError(ex, "保存设置时出错");
            WeakReferenceMessenger.Default.Send(new ToastMessage("保存设置失败！"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 加载模型列表
    /// </summary>
    private async Task LoadModelsAsync()
    {
        try
        {
            IsBusy = true;

            // 清空当前模型列表
            Models.Clear();

            // 从YAML文件加载模型
            var modelsFromYaml = await LoadModelsFromYamlAsync();

            foreach (var model in modelsFromYaml)
            {
                Models.Add(model);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "加载模型列表时出错");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 从yaml文件加载模型列表
    /// </summary>
    private async Task<List<string>> LoadModelsFromYamlAsync()
    {
        var models = new List<string>();
        // 获取yaml文件路径
        string yamlContent;

        try
        {
            // 尝试从应用包中读取
            using var stream = await FileSystem.Current.OpenAppPackageFileAsync("config/models.yaml");
            using var reader = new StreamReader(stream);
            yamlContent = await reader.ReadToEndAsync();

            // 解析yaml内容
            var deserializer = new DeserializerBuilder().Build();
            var yamlData = deserializer.Deserialize<Dictionary<string, List<string>>>(yamlContent);

            // 返回模型列表
            if (yamlData != null && yamlData.ContainsKey("models"))
            {
                models = yamlData["models"];
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "读取yaml文件时出错");
        }

        return models;
    }

    private void LoadSettings()
    {
        try
        {
            IsBusy = true;

            // 直接使用UserSetting对象
            UserSetting = _userSettingService.CurrentSetting;

            // 通知所有相关属性变更，以便UI更新
            OnPropertyChanged(nameof(EnableAnalysisSynthesizer));
            OnPropertyChanged(nameof(EnableFundamentalAnalyst));
            OnPropertyChanged(nameof(EnableMarketSentimentAnalyst));
            OnPropertyChanged(nameof(EnableFinancialAnalyst));
            OnPropertyChanged(nameof(EnableTechnicalAnalyst));
            OnPropertyChanged(nameof(EnableNewsEventAnalyst));
            OnPropertyChanged(nameof(IsKnowledgeDirectoryValid));
        }
        catch (Exception ex)
        {
            // 处理异常
            Logger?.LogError(ex, "加载设置时出错");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetSettings()
    {
        // 使用UserSettingService重置设置
        _userSettingService.ResetSettings();

        // 重新加载设置到ViewModel
        LoadSettings();
    }

    /// <summary>
    /// 向量化文档方法
    /// </summary>
    private async void VectorizeDocuments()
    {
        if (!IsKnowledgeDirectoryValid)
        {
            WeakReferenceMessenger.Default.Send(new ToastMessage("请先选择有效的知识库目录"));
            return;
        }

        try
        {
            IsVectorizing = true;

            // 使用动态服务创建嵌入生成器
            var embeddingGenerator = _dynamicEmbeddingService.CreateEmbeddingGenerator();

            // 获取目录中的所有PDF和DOCX文件
            var pdfFiles = Directory.GetFiles(UserSetting.KnowledgeFileDirectory, "*.pdf", SearchOption.AllDirectories);
            var docxFiles = Directory.GetFiles(UserSetting.KnowledgeFileDirectory, "*.docx", SearchOption.AllDirectories);

            int processedFiles = 0;
            int totalFiles = pdfFiles.Length + docxFiles.Length;

            if (totalFiles == 0)
            {
                WeakReferenceMessenger.Default.Send(new ToastMessage("未找到PDF或DOCX文件"));
                return;
            }

            // 处理PDF文件
            foreach (var pdfFile in pdfFiles)
            {
                try
                {
                    var collection = _vectorStore.GetCollection<string, TextParagraph>(UserSetting.VectorCollectionName);
                    await collection.EnsureCollectionExistsAsync();
                    await _ingestionService.IngestFileAsync(collection, pdfFile, embeddingGenerator);
                    processedFiles++;
                    WeakReferenceMessenger.Default.Send(new ToastMessage($"处理进度: {processedFiles}/{totalFiles}"));
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "处理PDF文件时出错: {pdfFile}", pdfFile);
                }
            }

            // 处理DOCX文件
            foreach (var docxFile in docxFiles)
            {
                try
                {
                    var collection = _vectorStore.GetCollection<string, TextParagraph>(UserSetting.VectorCollectionName);
                    await collection.EnsureCollectionExistsAsync();
                    await _ingestionService.IngestFileAsync(collection, docxFile, embeddingGenerator);
                    processedFiles++;
                    WeakReferenceMessenger.Default.Send(new ToastMessage($"处理进度: {processedFiles}/{totalFiles}"));
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "处理DOCX文件时出错: {docxFile}", docxFile);
                }
            }

            WeakReferenceMessenger.Default.Send(new ToastMessage($"向量化完成，共处理{processedFiles}个文件"));
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "向量化文档时出错");
            WeakReferenceMessenger.Default.Send(new ToastMessage($"向量化失败: {ex.Message}"));
        }
        finally
        {
            IsVectorizing = false;
        }
    }
}