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
    /// <summary>
    /// 模型服务商 ID（对应 ModelProviderCatalog 中的 Id）
    /// </summary>
    public string ProviderId { get; set; } = "";

    /// <summary>
    /// 按服务商保存模型 ID，切换服务商时恢复各自选择。
    /// </summary>
    public Dictionary<string, string> ProviderModelIds { get; set; } = new();

    public string EmbeddingModelId { get; set; } = "jina-embeddings-v5-text-small";

    public string EmbeddingEndpoint { get; set; } = "https://api.jina.ai";

    [JsonIgnore]
    public string EmbeddingApiKey { get; set; } = "";

    /// <summary>
    /// 按服务商保存自定义 Endpoint，空值表示使用目录默认地址。
    /// </summary>
    public Dictionary<string, string> ProviderEndpoints { get; set; } = new();

    /// <summary>
    /// 按服务商 ID 存储各自的 API Key
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> ProviderApiKeys { get; set; } = new();

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

    /// <summary>
    /// 是否显示桌面通知。默认开启，避免新安装用户创建告警后无感知。
    /// </summary>
    public bool Notification { get; set; } = true;

    [JsonIgnore]
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
    [JsonIgnore]
    public string CoinGeckoApiKey { get; set; } = "";

    /// <summary>
    /// Binance API Key（交易功能必须）
    /// </summary>
    [JsonIgnore]
    public string BinanceApiKey { get; set; } = "";

    /// <summary>
    /// Binance Secret Key（交易功能必须）
    /// </summary>
    [JsonIgnore]
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
    [JsonIgnore]
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

    /// <summary>
    /// 深拷贝一份独立副本。
    /// 设置页编辑必须基于副本（草稿模式）：直接编辑单例本体会让未保存的修改
    /// （如交易模式改为实盘、半填的密钥）随任何一次切市场的整体落盘而静默生效。
    /// 不能用 JSON 往返实现——密钥字段带 [JsonIgnore]，序列化会丢失。
    /// </summary>
    public UserSetting Clone()
    {
        var clone = new UserSetting
        {
            ProviderId = ProviderId,
            EmbeddingModelId = EmbeddingModelId,
            EmbeddingEndpoint = EmbeddingEndpoint,
            EmbeddingApiKey = EmbeddingApiKey,
            ProviderApiKeys = new Dictionary<string, string>(ProviderApiKeys, StringComparer.Ordinal),
            ProviderModelIds = new Dictionary<string, string>(ProviderModelIds, StringComparer.Ordinal),
            ProviderEndpoints = new Dictionary<string, string>(ProviderEndpoints, StringComparer.Ordinal),
            LoadKnowledge = LoadKnowledge,
            KnowledgeFileDirectory = KnowledgeFileDirectory,
            Notification = Notification,
            ZhiTuApiToken = ZhiTuApiToken,
            ThemeMode = ThemeMode,
            CurrentMarketType = CurrentMarketType,
            CryptoTradingMode = CryptoTradingMode,
            CoinGeckoApiKey = CoinGeckoApiKey,
            BinanceApiKey = BinanceApiKey,
            BinanceSecretKey = BinanceSecretKey,
            BinanceFuturesTestnetApiKey = BinanceFuturesTestnetApiKey,
            BinanceFuturesTestnetSecretKey = BinanceFuturesTestnetSecretKey,
            LogPath = LogPath,
            EnableWebSearch = EnableWebSearch,
            WebSearchApiKey = WebSearchApiKey,
            WebSearchProvider = WebSearchProvider,
            EnabledAnalystRoles = new Dictionary<string, bool>(EnabledAnalystRoles, StringComparer.Ordinal),
            InvestmentPreference = InvestmentPreference.Clone()
        };
        return clone;
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