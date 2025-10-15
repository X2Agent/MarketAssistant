using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag;
using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using System.Collections.ObjectModel;
using YamlDotNet.Serialization;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 设置页ViewModel
/// </summary>
public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly IRagIngestionService _ragIngestionService;
    private readonly INotificationService _notificationService;
    private readonly IUserSettingService _userSettingService;
    private readonly IEmbeddingFactory _embeddingFactory;
    private readonly VectorStore _vectorStore;
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

    // 向量化进度（0-100）
    [ObservableProperty]
    private int _vectorizingProgress;

    // 向量化进度文本
    [ObservableProperty]
    private string _vectorizingProgressText = "";

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

    /// <summary>
    /// 构造函数（使用依赖注入）
    /// </summary>
    public SettingsPageViewModel(
        IRagIngestionService ragIngestionService,
        INotificationService notificationService,
        IUserSettingService userSettingService,
        IEmbeddingFactory embeddingFactory,
        VectorStore vectorStore,
        ILogger<SettingsPageViewModel> logger) : base(logger)
    {
        _ragIngestionService = ragIngestionService;
        _notificationService = notificationService;
        _userSettingService = userSettingService;
        _embeddingFactory = embeddingFactory;
        _vectorStore = vectorStore;
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
        // 加载用户设置
        UserSetting = _userSettingService.CurrentSetting;
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

        await SafeExecuteAsync(async () =>
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
        }, "选择知识库目录");
    }

    /// <summary>
    /// 选择浏览器路径
    /// </summary>
    [RelayCommand]
    private async Task SelectBrowserPath()
    {
        if (_storageProvider == null) return;

        await SafeExecuteAsync(async () =>
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
        }, "选择浏览器路径");
    }

    /// <summary>
    /// 选择日志路径
    /// </summary>
    [RelayCommand]
    private async Task SelectLogPath()
    {
        if (_storageProvider == null) return;

        await SafeExecuteAsync(async () =>
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
        }, "选择日志路径");
    }

    /// <summary>
    /// 向量化文档
    /// </summary>
    [RelayCommand]
    private async Task VectorizeDocuments()
    {
        if (!IsKnowledgeDirectoryValid)
        {
            _notificationService.ShowWarning("知识库目录无效，请先选择有效的目录");
            Logger?.LogWarning("知识库目录无效，无法进行向量化");
            return;
        }

        try
        {
            IsVectorizing = true;
            VectorizingProgress = 0;
            VectorizingProgressText = "准备中...";

            Logger?.LogInformation("开始向量化知识库目录: {Directory}", UserSetting.KnowledgeFileDirectory);

            // 创建嵌入生成器（只在实际需要时创建）
            var embeddingGenerator = _embeddingFactory.Create();
            
            // 使用 UserSetting 中定义的集合名称
            var collectionName = UserSetting.VectorCollectionName;
            var collection = _vectorStore.GetCollection<string, TextParagraph>(collectionName);
            await collection.EnsureCollectionExistsAsync();
            Logger?.LogInformation("使用向量集合: {CollectionName}", collectionName);

            // 支持的文件扩展名：PDF、DOCX、Markdown
            var supportedExtensions = new[] { ".pdf", ".docx", ".md" };

            // 扫描目录获取所有支持的文件
            var files = Directory.GetFiles(UserSetting.KnowledgeFileDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (files.Count == 0)
            {
                _notificationService.ShowWarning($"未找到支持的文档（支持：{string.Join(", ", supportedExtensions)}）");
                Logger?.LogWarning("知识库目录中没有找到支持的文档");
                return;
            }

            var totalFiles = files.Count;
            Logger?.LogInformation("找到 {Count} 个文档需要向量化", totalFiles);
            _notificationService.ShowInfo($"开始向量化 {totalFiles} 个文档...");

            var successCount = 0;
            var failedCount = 0;
            var failedFiles = new List<string>();

            // 逐个处理文件
            for (int i = 0; i < totalFiles; i++)
            {
                var file = files[i];
                var fileName = Path.GetFileName(file);
                var fileExtension = Path.GetExtension(file).ToUpperInvariant();

                try
                {
                    // 更新进度
                    var currentIndex = i + 1;
                    VectorizingProgress = (int)((double)currentIndex / totalFiles * 100);
                    VectorizingProgressText = $"正在处理 {currentIndex}/{totalFiles}: {fileName}";

                    Logger?.LogInformation("正在处理 ({Index}/{Total}): {FileName} [{Extension}]", 
                        currentIndex, totalFiles, fileName, fileExtension);

                    // 执行向量化
                    await _ragIngestionService.IngestFileAsync(collection, file, embeddingGenerator);

                    successCount++;
                    Logger?.LogInformation("✓ 成功向量化: {FileName}", fileName);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    failedFiles.Add(fileName);
                    Logger?.LogError(ex, "✗ 向量化失败: {FileName} - {ErrorMessage}", fileName, ex.Message);
                    
                    // 单个文件失败不中断整体流程，继续处理下一个
                }
            }

            // 显示完成消息
            VectorizingProgress = 100;
            if (failedCount == 0)
            {
                VectorizingProgressText = $"✅ 全部完成！共 {successCount} 个文件";
                _notificationService.ShowSuccess($"✅ 所有文档向量化完成！\n成功处理 {successCount} 个文件");
                Logger?.LogInformation("向量化完成：成功 {Success}/{Total} 个", successCount, totalFiles);
            }
            else
            {
                VectorizingProgressText = $"⚠️ 完成（部分失败）: {successCount} 成功, {failedCount} 失败";
                var failedList = string.Join("\n- ", failedFiles.Take(5));
                if (failedFiles.Count > 5)
                {
                    failedList += $"\n... 还有 {failedFiles.Count - 5} 个";
                }
                
                _notificationService.ShowWarning(
                    $"向量化完成：\n✓ 成功 {successCount} 个\n✗ 失败 {failedCount} 个\n\n失败文件：\n- {failedList}");
                
                Logger?.LogWarning("向量化完成：成功 {Success} 个，失败 {Failed} 个，总计 {Total} 个", 
                    successCount, failedCount, totalFiles);
            }
        }
        catch (Exception ex)
        {
            VectorizingProgressText = "向量化失败";
            Logger?.LogError(ex, "向量化过程发生严重错误");
            _notificationService.ShowError($"向量化失败：{ex.Message}");
        }
        finally
        {
            IsVectorizing = false;
        }
    }

    /// <summary>
    /// 保存设置
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        SafeExecute(() =>
        {
            _userSettingService.UpdateSettings(UserSetting);
            _notificationService.ShowSuccess("设置已保存");
            Logger?.LogInformation("保存设置");
        }, "保存设置");
    }

    /// <summary>
    /// 重置设置为默认值
    /// </summary>
    [RelayCommand]
    private void Reset()
    {
        SafeExecute(() =>
        {
            _userSettingService.ResetSettings();
            UserSetting = _userSettingService.CurrentSetting;
            OnPropertyChanged(nameof(UserSetting));
            _notificationService.ShowSuccess("设置已重置为默认值");
            Logger?.LogInformation("重置设置为默认值");
        }, "重置设置");
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
        await SafeExecuteAsync(async () =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            await Task.CompletedTask;
        }, "打开链接");
    }
}