using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Settings;

/// <summary>
/// 数据源类型枚举
/// </summary>
public enum MarketDataSource
{
    /// <summary>
    /// A股
    /// </summary>
    AStocks,
    /// <summary>
    /// 港股
    /// </summary>
    HKStocks,
    /// <summary>
    /// 美股
    /// </summary>
    USStocks,
    /// <summary>
    /// 虚拟币
    /// </summary>
    Crypto
}

/// <summary>
/// 用户设置类
/// </summary>
public class UserSetting : INotifyPropertyChanged
{
    public string ModelId { get; set; } = "";

    public string EmbeddingModelId { get; set; } = "BAAI/bge-m3";

    public string Endpoint { get; set; } = "https://api.siliconflow.cn";

    public string ApiKey { get; set; } = "";

    private bool _loadKnowledge;
    public bool LoadKnowledge
    {
        get => _loadKnowledge;
        set => SetProperty(ref _loadKnowledge, value);
    }

    [JsonIgnore]
    public const string VectorCollectionName = "knowledge";

    private string _knowledgeFileDirectory = "";
    public string KnowledgeFileDirectory
    {
        get => _knowledgeFileDirectory;
        set => SetProperty(ref _knowledgeFileDirectory, value);
    }

    public bool Notification { get; set; }

    public string ZhiTuApiToken { get; set; } = "";

    /// <summary>
    /// 浏览器路径，如果为空则自动检测
    /// </summary>
    private string _browserPath = "";
    public string BrowserPath
    {
        get => _browserPath;
        set => SetProperty(ref _browserPath, value);
    }

    /// <summary>
    /// 日志文件路径
    /// </summary>
    private string _logPath = "";
    public string LogPath
    {
        get => _logPath;
        set => SetProperty(ref _logPath, value);
    }

    private bool _enableWebSearch;
    /// <summary>
    /// 是否启用Web Search功能
    /// </summary>
    public bool EnableWebSearch
    {
        get => _enableWebSearch;
        set => SetProperty(ref _enableWebSearch, value);
    }

    /// <summary>
    /// Web Search API Key
    /// </summary>
    public string WebSearchApiKey { get; set; } = "";

    /// <summary>
    /// Web Search服务商
    /// </summary>
    public string WebSearchProvider { get; set; } = "Bing";

    /// <summary>
    /// 市场分析师角色配置
    /// </summary>
    public MarketAnalystRoleSettings AnalystRoleSettings { get; set; } = new MarketAnalystRoleSettings();

    /// <summary>
    /// 当前选择的数据源
    /// </summary>
    private MarketDataSource _selectedDataSource = MarketDataSource.AStocks;
    public MarketDataSource SelectedDataSource
    {
        get => _selectedDataSource;
        set => SetProperty(ref _selectedDataSource, value);
    }

    /// <summary>
    /// 是否首次启动应用
    /// </summary>
    private bool _isFirstLaunch = true;
    public bool IsFirstLaunch
    {
        get => _isFirstLaunch;
        set => SetProperty(ref _isFirstLaunch, value);
    }

    #region INotifyPropertyChanged Implementation

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}