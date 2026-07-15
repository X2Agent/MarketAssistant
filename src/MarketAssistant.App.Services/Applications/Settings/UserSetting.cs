using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using MarketAssistant.Trading.Models;

namespace MarketAssistant.Applications.Settings;

/// <summary>
/// 用户设置类
/// </summary>
public class UserSetting : INotifyPropertyChanged
{
    public string ModelId { get; set; } = "";

    public string EmbeddingModelId { get; set; } = "jina-embeddings-v5-text-small";

    public string EmbeddingEndpoint { get; set; } = "https://api.jina.ai";

    public string EmbeddingApiKey { get; set; } = "";

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
    /// 主题模式：Default=跟随系统, Light=浅色, Dark=深色
    /// </summary>
    private string _themeMode = "Default";
    public string ThemeMode
    {
        get => _themeMode;
        set => SetProperty(ref _themeMode, value);
    }

    /// <summary>
    /// 当前市场类型
    /// </summary>
    private MarketType _currentMarketType = MarketType.AShare;
    public MarketType CurrentMarketType
    {
        get => _currentMarketType;
        set => SetProperty(ref _currentMarketType, value);
    }

    /// <summary>
    /// 虚拟币交易模式
    /// </summary>
    private CryptoTradingMode _cryptoTradingMode = CryptoTradingMode.LiveSpot;
    public CryptoTradingMode CryptoTradingMode
    {
        get => _cryptoTradingMode;
        set => SetProperty(ref _cryptoTradingMode, value);
    }

    /// <summary>
    /// CoinGecko API 密钥（Demo 版免费，需在 https://www.coingecko.com/api/dashboard 注册获取）
    /// /coins/markets 等端点现要求携带 Demo Key，留空可能导致虚拟币筛选失败
    /// </summary>
    public string CoinGeckoApiKey { get; set; } = "";

    /// <summary>
    /// Binance API Key（交易功能必须）
    /// </summary>
    public string BinanceApiKey { get; set; } = "";

    /// <summary>
    /// Binance Secret Key（交易功能必须）
    /// </summary>
    public string BinanceSecretKey { get; set; } = "";

    /// <summary>
    /// Binance Futures Testnet API Key（在 demo-fapi.binance.com 生成）
    /// </summary>
    public string BinanceFuturesTestnetApiKey { get; set; } = "";

    /// <summary>
    /// Binance Futures Testnet Secret Key
    /// </summary>
    public string BinanceFuturesTestnetSecretKey { get; set; } = "";

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
    private string _webSearchProvider = "Bing";
    public string WebSearchProvider
    {
        get => _webSearchProvider;
        set => SetProperty(ref _webSearchProvider, value);
    }

    /// <summary>
    /// 启用的分析师角色字典 Key: AgentName, Value: IsEnabled
    /// </summary>
    public Dictionary<string, bool> EnabledAnalystRoles { get; set; } = new();

    private InvestmentPreference _investmentPreference = new();
    public InvestmentPreference InvestmentPreference
    {
        get => _investmentPreference;
        set => SetProperty(ref _investmentPreference, value);
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