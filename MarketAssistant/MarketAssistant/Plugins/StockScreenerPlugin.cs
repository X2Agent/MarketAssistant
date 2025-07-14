using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace MarketAssistant.Plugins;

/// <summary>
/// 股票筛选条件
/// </summary>
public class StockScreeningCriteria
{
    /// <summary>
    /// 指标代码（如 "mc", "pettm", "roediluted" 等）
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 指标显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 最小值
    /// </summary>
    public decimal? MinValue { get; set; }

    /// <summary>
    /// 最大值
    /// </summary>
    public decimal? MaxValue { get; set; }

    /// <summary>
    /// 指标类型（basic, market, snowball）
    /// </summary>
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// 股票筛选参数
/// </summary>
public class StockCriteria
{
    /// <summary>
    /// 筛选条件列表
    /// </summary>
    [Description("筛选条件")]
    public List<StockScreeningCriteria> Criteria { get; set; } = new();

    /// <summary>
    /// 市场类型：全部A股、沪市A股、深市A股等
    /// </summary>
    public string Market { get; set; } = "全部A股";

    /// <summary>
    /// 行业分类：全部、科技、金融等
    /// </summary>
    [Description("行业分类")]
    public string Industry { get; set; } = "全部";

    /// <summary>
    /// 返回数量限制
    /// </summary>
    public int Limit { get; set; } = 20;
}

/// <summary>
/// 雪球网股票筛选结果实体
/// </summary>
public class ScreenerStockInfo
{
    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 股票代码（如：SZ300316）
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前价
    /// </summary>
    public decimal Current { get; set; }

    /// <summary>
    /// 当日涨跌幅(%)
    /// </summary>
    public decimal Pct { get; set; }

    /// <summary>
    /// 当日成交额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 总市值（元）
    /// </summary>
    public decimal Mc { get; set; }

    /// <summary>
    /// 流通市值（元）
    /// </summary>
    public decimal Fmc { get; set; }

    /// <summary>
    /// 本日成交量(万)
    /// </summary>
    public decimal Volume { get; set; }

    /// <summary>
    /// 当日量比
    /// </summary>
    public decimal VolumeRatio { get; set; }

    /// <summary>
    /// 当日换手率(%)
    /// </summary>
    public decimal Tr { get; set; }

    /// <summary>
    /// 市盈率TTM
    /// </summary>
    public decimal PeTtm { get; set; }

    /// <summary>
    /// 市盈率LYR
    /// </summary>
    public decimal PeLyr { get; set; }

    /// <summary>
    /// 市净率MRQ
    /// </summary>
    public decimal Pb { get; set; }

    /// <summary>
    /// 市销率(倍)
    /// </summary>
    public decimal Psr { get; set; }

    /// <summary>
    /// 净资产收益率(%)
    /// </summary>
    public decimal RoeDiluted { get; set; }

    /// <summary>
    /// 每股净资产
    /// </summary>
    public decimal Bps { get; set; }

    /// <summary>
    /// 每股收益
    /// </summary>
    public decimal Eps { get; set; }

    /// <summary>
    /// 净利润（元）
    /// </summary>
    public decimal NetProfit { get; set; }

    /// <summary>
    /// 营业收入（元）
    /// </summary>
    public decimal TotalRevenue { get; set; }

    /// <summary>
    /// 股息收益率(%)
    /// </summary>
    public decimal DyL { get; set; }

    /// <summary>
    /// 净利润同比增长(%)
    /// </summary>
    public decimal Npay { get; set; }

    /// <summary>
    /// 营业收入同比增长(%)
    /// </summary>
    public decimal Oiy { get; set; }

    /// <summary>
    /// 总资产报酬率(%)
    /// </summary>
    public decimal Niota { get; set; }

    /// <summary>
    /// 累计关注人数
    /// </summary>
    public decimal Follow { get; set; }

    /// <summary>
    /// 累计讨论次数
    /// </summary>
    public decimal Tweet { get; set; }

    /// <summary>
    /// 累计交易分享数
    /// </summary>
    public decimal Deal { get; set; }

    /// <summary>
    /// 一周新增关注
    /// </summary>
    public decimal Follow7d { get; set; }

    /// <summary>
    /// 一周新增讨论数
    /// </summary>
    public decimal Tweet7d { get; set; }

    /// <summary>
    /// 一周新增交易分享数
    /// </summary>
    public decimal Deal7d { get; set; }

    /// <summary>
    /// 一周关注增长率(%)
    /// </summary>
    public decimal Follow7dPct { get; set; }

    /// <summary>
    /// 一周讨论增长率(%)
    /// </summary>
    public decimal Tweet7dPct { get; set; }

    /// <summary>
    /// 一周交易分享增长率(%)
    /// </summary>
    public decimal Deal7dPct { get; set; }

    /// <summary>
    /// 近5日涨跌幅(%)
    /// </summary>
    public decimal Pct5 { get; set; }

    /// <summary>
    /// 近10日涨跌幅(%)
    /// </summary>
    public decimal Pct10 { get; set; }

    /// <summary>
    /// 近20日涨跌幅(%)
    /// </summary>
    public decimal Pct20 { get; set; }

    /// <summary>
    /// 近60日涨跌幅(%)
    /// </summary>
    public decimal Pct60 { get; set; }

    /// <summary>
    /// 近120日涨跌幅(%)
    /// </summary>
    public decimal Pct120 { get; set; }

    /// <summary>
    /// 近250日涨跌幅(%)
    /// </summary>
    public decimal Pct250 { get; set; }

    /// <summary>
    /// 年初至今涨跌幅(%)
    /// </summary>
    public decimal PctCurrentYear { get; set; }

    /// <summary>
    /// 当日振幅(%)
    /// </summary>
    public decimal ChgPct { get; set; }

    /// <summary>
    /// 动态数据字典，存储其他指标
    /// </summary>
    public Dictionary<string, string> ExtraData { get; set; } = new();
}

/// <summary>
/// 雪球网股票筛选插件，通过Playwright自动化操作xueqiu.com股票筛选器
/// </summary>
public sealed class StockScreenerPlugin
{
    private readonly PlaywrightService _playwrightService;
    private readonly ILogger<StockScreenerPlugin> _logger;
    private const string XUEQIU_SCREENER_URL = "https://xueqiu.com/stock/screener";

    /// <summary>
    /// 雪球网支持的所有筛选指标定义（根据实际HTML结构更新）
    /// </summary>
    private static readonly Dictionary<string, StockScreeningCriteria> SupportedCriteria = new()
    {
        // 基本指标 (15个)
        { "pettm", new StockScreeningCriteria { Code = "pettm", DisplayName = "市盈率TTM", Type = "basic" } },
        { "roediluted", new StockScreeningCriteria { Code = "roediluted", DisplayName = "净资产收益率", Type = "basic" } },
        { "bps", new StockScreeningCriteria { Code = "bps", DisplayName = "每股净资产", Type = "basic" } },
        { "pelyr", new StockScreeningCriteria { Code = "pelyr", DisplayName = "市盈率LYR", Type = "basic" } },
        { "npay", new StockScreeningCriteria { Code = "npay", DisplayName = "净利润同比增长", Type = "basic" } },
        { "eps", new StockScreeningCriteria { Code = "eps", DisplayName = "每股收益", Type = "basic" } },
        { "netprofit", new StockScreeningCriteria { Code = "netprofit", DisplayName = "净利润", Type = "basic" } },
        { "dy_l", new StockScreeningCriteria { Code = "dy_l", DisplayName = "股息收益率", Type = "basic" } },
        { "psr", new StockScreeningCriteria { Code = "psr", DisplayName = "市销率(倍)", Type = "basic" } },
        { "pb", new StockScreeningCriteria { Code = "pb", DisplayName = "市净率MRQ", Type = "basic" } },
        { "total_revenue", new StockScreeningCriteria { Code = "total_revenue", DisplayName = "营业收入", Type = "basic" } },
        { "mc", new StockScreeningCriteria { Code = "mc", DisplayName = "总市值", Type = "basic" } },
        { "fmc", new StockScreeningCriteria { Code = "fmc", DisplayName = "流通市值", Type = "basic" } },
        { "niota", new StockScreeningCriteria { Code = "niota", DisplayName = "总资产报酬率", Type = "basic" } },
        { "oiy", new StockScreeningCriteria { Code = "oiy", DisplayName = "营业收入同比增长", Type = "basic" } },
        
        // 雪球指标 (9个)
        { "deal", new StockScreeningCriteria { Code = "deal", DisplayName = "累计交易分享数", Type = "snowball" } },
        { "follow7d", new StockScreeningCriteria { Code = "follow7d", DisplayName = "一周新增关注", Type = "snowball" } },
        { "deal7dpct", new StockScreeningCriteria { Code = "deal7dpct", DisplayName = "一周交易分享增长率", Type = "snowball" } },
        { "deal7d", new StockScreeningCriteria { Code = "deal7d", DisplayName = "一周新增交易分享数", Type = "snowball" } },
        { "tweet7dpct", new StockScreeningCriteria { Code = "tweet7dpct", DisplayName = "一周讨论增长率", Type = "snowball" } },
        { "tweet", new StockScreeningCriteria { Code = "tweet", DisplayName = "累计讨论次数", Type = "snowball" } },
        { "follow7dpct", new StockScreeningCriteria { Code = "follow7dpct", DisplayName = "一周关注增长率", Type = "snowball" } },
        { "follow", new StockScreeningCriteria { Code = "follow", DisplayName = "累计关注人数", Type = "snowball" } },
        { "tweet7d", new StockScreeningCriteria { Code = "tweet7d", DisplayName = "一周新增讨论数", Type = "snowball" } },
        
        // 行情指标 (14个)
        { "pct", new StockScreeningCriteria { Code = "pct", DisplayName = "当日涨跌幅", Type = "market" } },
        { "pct5", new StockScreeningCriteria { Code = "pct5", DisplayName = "近5日涨跌幅", Type = "market" } },
        { "pct60", new StockScreeningCriteria { Code = "pct60", DisplayName = "近60日涨跌幅", Type = "market" } },
        { "amount", new StockScreeningCriteria { Code = "amount", DisplayName = "当日成交额", Type = "market" } },
        { "chgpct", new StockScreeningCriteria { Code = "chgpct", DisplayName = "当日振幅", Type = "market" } },
        { "pct20", new StockScreeningCriteria { Code = "pct20", DisplayName = "近20日涨跌幅", Type = "market" } },
        { "pct120", new StockScreeningCriteria { Code = "pct120", DisplayName = "近120日涨跌幅", Type = "market" } },
        { "pct250", new StockScreeningCriteria { Code = "pct250", DisplayName = "近250日涨跌幅", Type = "market" } },
        { "volume", new StockScreeningCriteria { Code = "volume", DisplayName = "本日成交量", Type = "market" } },
        { "current", new StockScreeningCriteria { Code = "current", DisplayName = "当前价", Type = "market" } },
        { "volume_ratio", new StockScreeningCriteria { Code = "volume_ratio", DisplayName = "当日量比", Type = "market" } },
        { "pct_current_year", new StockScreeningCriteria { Code = "pct_current_year", DisplayName = "年初至今涨跌幅", Type = "market" } },
        { "pct10", new StockScreeningCriteria { Code = "pct10", DisplayName = "近10日涨跌幅", Type = "market" } },
        { "tr", new StockScreeningCriteria { Code = "tr", DisplayName = "当日换手率", Type = "market" } }
    };

    public StockScreenerPlugin(
        PlaywrightService playwrightService,
        ILogger<StockScreenerPlugin> logger)
    {
        _playwrightService = playwrightService ?? throw new ArgumentNullException(nameof(playwrightService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 从雪球网筛选股票
    /// </summary>
    [KernelFunction("screen_stocks"), Description("根据具体指标筛选股票")]
    public async Task<List<ScreenerStockInfo>> ScreenStocksAsync(
        [Description("筛选条件")] StockCriteria criteria)
    {
        if (criteria == null)
            throw new ArgumentNullException(nameof(criteria));

        try
        {
            _logger.LogInformation("开始筛选股票，共 {Count} 个条件", criteria.Criteria.Count);

            return await _playwrightService.ExecuteWithPageAsync(async page =>
            {
                // 访问雪球选股器
                await page.GotoAsync(XUEQIU_SCREENER_URL, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });

                // 等待页面加载完成
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await Task.Delay(1000);

                // 设置市场和行业
                await SetMarketType(page, criteria.Market);
                await SetIndustry(page, criteria.Industry);

                // 设置所有指标条件
                foreach (var criterion in criteria.Criteria)
                {
                    if (SupportedCriteria.ContainsKey(criterion.Code))
                    {
                        var supportedCriterion = SupportedCriteria[criterion.Code];
                        await SetSpecificCriteria(page, criterion.Code, criterion.MinValue, criterion.MaxValue, supportedCriterion.DisplayName);
                    }
                    else
                    {
                        _logger.LogWarning("不支持的指标代码: {Code}", criterion.Code);
                    }
                }

                // 开始选股
                await TriggerScreening(page);

                // 等待结果加载
                await Task.Delay(2000);

                // 获取股票列表
                var stocks = await ExtractXueqiuStockList(page, criteria.Limit);

                _logger.LogInformation("成功获取 {Count} 只股票", stocks.Count);
                return stocks;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "筛选股票时发生错误");
            throw new InvalidOperationException($"筛选股票失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取所有支持的筛选指标
    /// </summary>
    [KernelFunction("get_supported_criteria"), Description("获取支持的所有筛选指标列表")]
    public List<StockScreeningCriteria> GetSupportedCriteria()
    {
        return SupportedCriteria.Values.ToList();
    }

    /// <summary>
    /// 根据指标类型获取支持的筛选指标
    /// </summary>
    [KernelFunction("get_criteria_by_type"), Description("根据指标类型获取支持的筛选指标")]
    public List<StockScreeningCriteria> GetCriteriaByType(
        [Description("指标类型：basic(基本指标)、market(行情指标)、snowball(雪球指标)")] string type)
    {
        return SupportedCriteria.Values.Where(c => c.Type == type).ToList();
    }

    #region 私有方法

    /// <summary>
    /// 设置市场类型
    /// </summary>
    private async Task SetMarketType(IPage page, string market)
    {
        try
        {
            // 根据实际HTML结构选择市场
            var marketSelect = await page.QuerySelectorAsync(".stockScreener-range-market select");
            if (marketSelect != null)
            {
                var marketValue = market switch
                {
                    "全部A股" => "sh_sz",
                    "沪市A股" => "sha",
                    "深市A股" => "sza",
                    _ => "sh_sz" // 默认全部A股
                };

                await marketSelect.SelectOptionAsync([marketValue]);
                await Task.Delay(1000);
                _logger.LogInformation("已设置市场类型: {Market} -> {Value}", market, marketValue);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置市场类型时发生错误: {Market}", market);
        }
    }

    /// <summary>
    /// 设置行业
    /// </summary>
    private async Task SetIndustry(IPage page, string industry)
    {
        try
        {
            if (string.IsNullOrEmpty(industry) || industry == "全部")
            {
                _logger.LogDebug("使用默认行业筛选条件：全部");
                return;
            }

            // 查找行业选择下拉框
            var industrySelect = await page.QuerySelectorAsync(".stockScreener-range-industry select");
            if (industrySelect == null)
            {
                _logger.LogWarning("未找到行业选择下拉框");
                return;
            }

            // 根据行业名称获取对应的值
            var industryValue = GetIndustryValue(industry);
            if (string.IsNullOrEmpty(industryValue))
            {
                _logger.LogWarning("未找到行业 '{Industry}' 对应的值，使用模糊匹配", industry);

                // 尝试模糊匹配
                var options = await industrySelect.QuerySelectorAllAsync("option");
                foreach (var option in options)
                {
                    var text = await option.InnerTextAsync();
                    if (text.Contains(industry))
                    {
                        industryValue = await option.GetAttributeAsync("value") ?? "";
                        _logger.LogInformation("通过模糊匹配找到行业: {Industry} -> {Value}", text, industryValue);
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(industryValue))
            {
                await industrySelect.SelectOptionAsync(industryValue);
                _logger.LogInformation("已选择行业: {Industry} (值: {Value})", industry, industryValue);

                // 等待页面更新
                await page.WaitForTimeoutAsync(500);
            }
            else
            {
                _logger.LogWarning("无法找到匹配的行业: {Industry}", industry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置行业时发生错误: {Industry}", industry);
        }
    }

    /// <summary>
    /// 根据行业名称获取对应的雪球行业代码
    /// </summary>
    private string GetIndustryValue(string industryName)
    {
        // 雪球行业代码映射表
        var industryMapping = new Dictionary<string, string>
        {
            // 农林牧渔
            { "种植业", "S1101" },
            { "渔业", "S1102" },
            { "林业", "S1103" },
            { "饲料", "S1104" },
            { "农产品加工", "S1105" },
            { "养殖业", "S1107" },
            { "动物保健", "S1108" },
            { "农业综合", "S1109" },
            
            // 化工
            { "化学原料", "S2202" },
            { "化学制品", "S2203" },
            { "化学纤维", "S2204" },
            { "塑料", "S2205" },
            { "橡胶", "S2206" },
            { "农化制品", "S2208" },
            { "非金属材料", "S2209" },
            
            // 钢铁
            { "冶钢原料", "S2303" },
            { "普钢", "S2304" },
            { "特钢", "S2305" },
            
            // 有色金属
            { "金属新材料", "S2402" },
            { "工业金属", "S2403" },
            { "贵金属", "S2404" },
            { "小金属", "S2405" },
            { "能源金属", "S2406" },
            
            // 电子
            { "半导体", "S2701" },
            { "元件", "S2702" },
            { "光学光电子", "S2703" },
            { "其他电子", "S2704" },
            { "消费电子", "S2705" },
            { "电子化学品", "S2706" },
            
            // 汽车
            { "汽车零部件", "S2802" },
            { "汽车服务", "S2803" },
            { "摩托车及其他", "S2804" },
            { "乘用车", "S2805" },
            { "商用车", "S2806" },
            
            // 家用电器
            { "白色家电", "S3301" },
            { "黑色家电", "S3302" },
            { "小家电", "S3303" },
            { "厨卫电器", "S3304" },
            { "照明设备", "S3305" },
            { "家电零部件", "S3306" },
            { "其他家电", "S3307" },
            
            // 食品饮料
            { "食品加工", "S3404" },
            { "白酒", "S3405" },
            { "非白酒", "S3406" },
            { "饮料乳品", "S3407" },
            { "休闲食品", "S3408" },
            { "调味发酵品", "S3409" },
            
            // 纺织服装
            { "纺织制造", "S3501" },
            { "服装家纺", "S3502" },
            { "饰品", "S3503" },
            
            // 轻工制造
            { "造纸", "S3601" },
            { "包装印刷", "S3602" },
            { "家居用品", "S3603" },
            { "文娱用品", "S3605" },
            
            // 医药生物
            { "化学制药", "S3701" },
            { "中药", "S3702" },
            { "生物制品", "S3703" },
            { "医药商业", "S3704" },
            { "医疗器械", "S3705" },
            { "医疗服务", "S3706" },
            
            // 公用事业
            { "电力", "S4101" },
            { "燃气", "S4103" },
            
            // 交通运输
            { "物流", "S4208" },
            { "铁路公路", "S4209" },
            { "航空机场", "S4210" },
            { "航运港口", "S4211" },
            
            // 房地产
            { "房地产开发", "S4301" },
            { "房地产服务", "S4303" },
            
            // 商业贸易
            { "贸易", "S4502" },
            { "一般零售", "S4503" },
            { "专业连锁", "S4504" },
            { "互联网电商", "S4506" },
            { "旅游零售", "S4507" },
            
            // 社会服务
            { "体育", "S4606" },
            { "本地生活服务", "S4607" },
            { "专业服务", "S4608" },
            { "酒店餐饮", "S4609" },
            { "旅游及景区", "S4610" },
            { "教育", "S4611" },
            
            // 银行
            { "国有大型银行", "S4802" },
            { "股份制银行", "S4803" },
            { "城商行", "S4804" },
            { "农商行", "S4805" },
            { "其他银行", "S4806" },
            
            // 非银金融
            { "证券", "S4901" },
            { "保险", "S4902" },
            { "多元金融", "S4903" },
            
            // 综合
            { "综合", "S5101" },
            
            // 建筑材料
            { "水泥", "S6101" },
            { "玻璃玻纤", "S6102" },
            { "装修建材", "S6103" },
            
            // 建筑装饰
            { "房屋建设", "S6201" },
            { "装修装饰", "S6202" },
            { "基础建设", "S6203" },
            { "专业工程", "S6204" },
            { "工程咨询服务", "S6206" },
            
            // 电力设备
            { "电机", "S6301" },
            { "其他电源设备", "S6303" },
            { "光伏设备", "S6305" },
            { "风电设备", "S6306" },
            { "电池", "S6307" },
            { "电网设备", "S6308" },
            
            // 机械设备
            { "通用设备", "S6401" },
            { "专用设备", "S6402" },
            { "轨交设备", "S6405" },
            { "工程机械", "S6406" },
            { "自动化设备", "S6407" },
            
            // 国防军工
            { "航天装备", "S6501" },
            { "航空装备", "S6502" },
            { "地面兵装", "S6503" },
            { "航海装备", "S6504" },
            { "军工电子", "S6505" },
            
            // 计算机
            { "计算机设备", "S7101" },
            { "IT服务", "S7103" },
            { "软件开发", "S7104" },
            
            // 传媒
            { "游戏", "S7204" },
            { "广告营销", "S7205" },
            { "影视院线", "S7206" },
            { "数字媒体", "S7207" },
            { "社交", "S7208" },
            { "出版", "S7209" },
            { "电视广播", "S7210" },
            
            // 通信
            { "通信服务", "S7301" },
            { "通信设备", "S7302" },
            
            // 煤炭
            { "煤炭开采", "S7401" },
            { "焦炭", "S7402" },
            
            // 石油石化
            { "油气开采", "S7501" },
            { "油服工程", "S7502" },
            { "炼化及贸易", "S7503" },
            
            // 环保
            { "环境治理", "S7601" },
            { "环保设备", "S7602" },
            
            // 美容护理
            { "个护用品", "S7701" },
            { "化妆品", "S7702" },
            { "医疗美容", "S7703" }
        };

        return industryMapping.TryGetValue(industryName, out var value) ? value : "";
    }

    /// <summary>
    /// 设置特定指标的筛选条件，支持隐藏元素的强制点击
    /// </summary>
    private async Task SetSpecificCriteria(IPage page, string value, decimal? min, decimal? max, string displayName)
    {
        try
        {
            // 第1步：尝试多种选择器找到复选框
            var checkbox = await FindCheckboxElement(page, value, displayName);
            if (checkbox == null)
            {
                _logger.LogWarning("未找到 {DisplayName} (value={Value}) 复选框", displayName, value);
                return;
            }

            // 第2步：勾选复选框
            await CheckElementWithFallback(checkbox, displayName);
            await Task.Delay(1000); // 等待条件输入框出现

            // 第3步：设置条件值
            await SetConditionValues(page, value, min, max, displayName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置 {DisplayName} 筛选条件时发生错误", displayName);
        }
    }

    /// <summary>
    /// 查找复选框元素
    /// </summary>
    private async Task<IElementHandle?> FindCheckboxElement(IPage page, string value, string displayName)
    {
        // 尝试精确选择器
        var checkbox = await page.QuerySelectorAsync($"label[title='{displayName}'] input[type='checkbox'][value='{value}']");

        // 如果没找到，尝试通用选择器
        if (checkbox == null)
        {
            checkbox = await page.QuerySelectorAsync($"input[type='checkbox'][value='{value}']");
        }

        return checkbox;
    }

    /// <summary>
    /// 勾选元素，支持JavaScript fallback
    /// </summary>
    private async Task CheckElementWithFallback(IElementHandle checkbox, string displayName)
    {
        _logger.LogInformation("正在设置 {DisplayName} 指标", displayName);

        try
        {
            // 首先尝试普通点击
            await checkbox.CheckAsync();
            _logger.LogInformation("通过CheckAsync成功勾选 {DisplayName}", displayName);
        }
        catch
        {
            // 如果普通点击失败，使用JavaScript强制点击
            _logger.LogInformation("CheckAsync失败，尝试JavaScript强制点击 {DisplayName}", displayName);
            await checkbox.EvaluateAsync("element => element.click()");
            _logger.LogInformation("通过JavaScript成功勾选 {DisplayName}", displayName);
        }
    }

    /// <summary>
    /// 设置条件输入框的值（最小值和最大值）
    /// </summary>
    private async Task SetConditionValues(IPage page, string value, decimal? min, decimal? max, string displayName)
    {
        var conditionElement = await FindConditionElement(page, value, displayName);
        if (conditionElement == null)
        {
            // 延迟重试一次
            await Task.Delay(1000);
            conditionElement = await FindConditionElement(page, value, displayName);
            if (conditionElement == null)
            {
                _logger.LogWarning("延迟后仍未找到 {DisplayName} 条件输入框", displayName);
                return;
            }
            _logger.LogInformation("延迟后找到了 {DisplayName} 条件输入框", displayName);
        }

        // 设置最小值和最大值
        await SetInputValue(conditionElement, "input.min", min, "最小值", displayName);
        await SetInputValue(conditionElement, "input.max", max, "最大值", displayName);
    }

    /// <summary>
    /// 查找条件输入框元素
    /// </summary>
    private async Task<IElementHandle?> FindConditionElement(IPage page, string value, string displayName)
    {
        var conditionElement = await page.QuerySelectorAsync($".stockScreener-selected-condition[data-field='{value}']");
        if (conditionElement == null)
        {
            _logger.LogWarning("未找到 {DisplayName} 条件输入框，可能需要等待更长时间", displayName);
        }
        return conditionElement;
    }

    /// <summary>
    /// 设置输入框的值
    /// </summary>
    private async Task SetInputValue(IElementHandle conditionElement, string inputSelector, decimal? value, string valueType, string displayName)
    {
        if (!value.HasValue) return;

        var input = await conditionElement.QuerySelectorAsync(inputSelector);
        if (input != null)
        {
            await input.FillAsync(value.Value.ToString());
            _logger.LogInformation("已设置 {DisplayName} {ValueType}: {Value}", displayName, valueType, value.Value);
        }
        else
        {
            _logger.LogWarning("未找到 {DisplayName} 的 {ValueType} 输入框", displayName, valueType);
        }
    }

    /// <summary>
    /// 触发选股
    /// </summary>
    private async Task TriggerScreening(IPage page)
    {
        try
        {
            // 根据实际HTML结构点击开始选股按钮：<input type="button" value="开始选股" class="submit search">
            var startButton = await page.QuerySelectorAsync("input[type='button'][value='开始选股'].submit.search");
            if (startButton != null)
            {
                _logger.LogInformation("找到开始选股按钮，准备点击");
                await startButton.ClickAsync();
            }
            else
            {
                _logger.LogWarning("未找到开始选股按钮");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "触发选股时发生错误");
        }
    }

    /// <summary>
    /// 提取雪球股票列表 - 动态解析表头和数据
    /// </summary>
    private async Task<List<ScreenerStockInfo>> ExtractXueqiuStockList(IPage page, int limit)
    {
        var stocks = new List<ScreenerStockInfo>();

        try
        {
            // 等待雪球的筛选结果表格加载 - 使用更精确的选择器
            var resultTableSelector = ".stockScreener-search-result-table.mainTable.overflowx table.portfolio";
            await page.WaitForSelectorAsync(resultTableSelector, new PageWaitForSelectorOptions
            {
                Timeout = 10000
            });

            // 首先获取表头信息，确定列的顺序 - 只从结果表格中获取
            var headers = await page.QuerySelectorAllAsync($"{resultTableSelector} thead tr th");
            var columnMap = new Dictionary<int, string>();

            for (int i = 0; i < headers.Count; i++)
            {
                var dataKey = await headers[i].GetAttributeAsync("data-key");
                if (!string.IsNullOrEmpty(dataKey))
                {
                    columnMap[i] = dataKey;
                    _logger.LogDebug("列 {Index}: {DataKey}", i, dataKey);
                }
            }

            _logger.LogInformation("检测到 {Count} 列数据", columnMap.Count);

            // 获取所有股票行 - 只从结果表格中获取
            var rows = await page.QuerySelectorAllAsync($"{resultTableSelector} tbody tr");

            if (rows.Count == 0)
            {
                _logger.LogWarning("未找到股票数据行");
                return stocks;
            }

            _logger.LogInformation("找到 {Count} 行股票数据", rows.Count);

            // 处理每一行，但限制数量
            var processedCount = 0;
            foreach (var row in rows.Take(limit))
            {
                try
                {
                    var stock = await ExtractXueqiuStockInfo(row, columnMap);
                    if (stock != null)
                    {
                        stocks.Add(stock);
                        processedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "提取第 {Index} 行股票信息时发生错误", processedCount + 1);
                }
            }

            _logger.LogInformation("成功提取 {Count} 只股票信息", stocks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "提取雪球股票列表时发生错误");
        }

        return stocks;
    }

    /// <summary>
    /// 从表格行提取雪球股票信息 - 根据列映射动态解析
    /// </summary>
    private async Task<ScreenerStockInfo?> ExtractXueqiuStockInfo(IElementHandle row, Dictionary<int, string> columnMap)
    {
        try
        {
            var cells = await row.QuerySelectorAllAsync("td");
            if (cells.Count == 0) return null;

            var stock = new ScreenerStockInfo();

            // 根据列映射动态解析数据
            for (int i = 0; i < cells.Count && i < columnMap.Count; i++)
            {
                if (!columnMap.ContainsKey(i)) continue;

                var dataKey = columnMap[i];
                var cellText = await cells[i].InnerTextAsync();

                try
                {
                    await ParseCellValue(stock, dataKey, cells[i], cellText.Trim());
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "解析列 {DataKey} 时发生错误: {Value}", dataKey, cellText);
                    // 将无法解析的数据存储到 ExtraData 中
                    stock.ExtraData[dataKey] = cellText.Trim();
                }
            }

            // 验证基本字段是否存在
            if (string.IsNullOrEmpty(stock.Name))
            {
                _logger.LogWarning("股票名称为空，跳过此行");
                return null;
            }

            return stock;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提取雪球股票信息时发生错误");
            return null;
        }
    }

    /// <summary>
    /// 解析单元格数据并设置到股票对象相应属性
    /// </summary>
    private async Task ParseCellValue(ScreenerStockInfo stock, string dataKey, IElementHandle cell, string cellText)
    {
        switch (dataKey)
        {
            case "symbol":
                // 股票名称和代码
                var linkElement = await cell.QuerySelectorAsync("a");
                if (linkElement != null)
                {
                    stock.Name = await linkElement.InnerTextAsync();

                    // 从title属性中提取股票代码，格式如："泓淋电力 (SZ301439)"
                    var title = await linkElement.GetAttributeAsync("title");
                    if (!string.IsNullOrEmpty(title))
                    {
                        var codeMatch = Regex.Match(title, @"\(([^)]+)\)");
                        if (codeMatch.Success)
                        {
                            stock.Symbol = codeMatch.Groups[1].Value;
                        }
                    }
                }
                break;

            case "current":
                stock.Current = ParseDecimalValue(cellText);
                break;

            case "pct":
                stock.Pct = ParsePercentageValue(cellText);
                break;

            case "amount":
                stock.Amount = ParseChineseAmountValue(cellText);
                break;

            case "mc":
                stock.Mc = ParseChineseAmountValue(cellText);
                break;

            case "fmc":
                stock.Fmc = ParseChineseAmountValue(cellText);
                break;

            case "volume":
                stock.Volume = ParseChineseAmountValue(cellText);
                break;

            case "volume_ratio":
                stock.VolumeRatio = ParseDecimalValue(cellText);
                break;

            case "tr":
                stock.Tr = ParsePercentageValue(cellText);
                break;

            case "pettm":
                stock.PeTtm = ParseDecimalValue(cellText);
                break;

            case "pelyr":
                stock.PeLyr = ParseDecimalValue(cellText);
                break;

            case "pb":
                stock.Pb = ParseDecimalValue(cellText);
                break;

            case "psr":
                stock.Psr = ParseDecimalValue(cellText);
                break;

            case "roediluted":
                stock.RoeDiluted = ParsePercentageValue(cellText);
                break;

            case "bps":
                stock.Bps = ParseDecimalValue(cellText);
                break;

            case "eps":
                stock.Eps = ParseDecimalValue(cellText);
                break;

            case "netprofit":
                stock.NetProfit = ParseChineseAmountValue(cellText);
                break;

            case "total_revenue":
                stock.TotalRevenue = ParseChineseAmountValue(cellText);
                break;

            case "dy_l":
                stock.DyL = ParsePercentageValue(cellText);
                break;

            case "npay":
                stock.Npay = ParsePercentageValue(cellText);
                break;

            case "oiy":
                stock.Oiy = ParsePercentageValue(cellText);
                break;

            case "niota":
                stock.Niota = ParsePercentageValue(cellText);
                break;

            case "follow":
                stock.Follow = ParseDecimalValue(cellText);
                break;

            case "tweet":
                stock.Tweet = ParseDecimalValue(cellText);
                break;

            case "deal":
                stock.Deal = ParseDecimalValue(cellText);
                break;

            case "follow7d":
                stock.Follow7d = ParseDecimalValue(cellText);
                break;

            case "tweet7d":
                stock.Tweet7d = ParseDecimalValue(cellText);
                break;

            case "deal7d":
                stock.Deal7d = ParseDecimalValue(cellText);
                break;

            case "follow7dpct":
                stock.Follow7dPct = ParsePercentageValue(cellText);
                break;

            case "tweet7dpct":
                stock.Tweet7dPct = ParsePercentageValue(cellText);
                break;

            case "deal7dpct":
                stock.Deal7dPct = ParsePercentageValue(cellText);
                break;

            case "pct5":
                stock.Pct5 = ParsePercentageValue(cellText);
                break;

            case "pct10":
                stock.Pct10 = ParsePercentageValue(cellText);
                break;

            case "pct20":
                stock.Pct20 = ParsePercentageValue(cellText);
                break;

            case "pct60":
                stock.Pct60 = ParsePercentageValue(cellText);
                break;

            case "pct120":
                stock.Pct120 = ParsePercentageValue(cellText);
                break;

            case "pct250":
                stock.Pct250 = ParsePercentageValue(cellText);
                break;

            case "pct_current_year":
                stock.PctCurrentYear = ParsePercentageValue(cellText);
                break;

            case "chgpct":
                stock.ChgPct = ParsePercentageValue(cellText);
                break;

            default:
                // 其他未定义的字段存储到 ExtraData 中
                stock.ExtraData[dataKey] = cellText;
                break;
        }
    }

    /// <summary>
    /// 解析普通数值
    /// </summary>
    private decimal ParseDecimalValue(string text)
    {
        if (string.IsNullOrEmpty(text) || text == "-") return 0;

        // 移除可能的颜色span标签中的内容
        var cleanText = Regex.Replace(text, @"<[^>]*>", "");
        cleanText = cleanText.Replace(",", "").Trim();

        if (decimal.TryParse(cleanText, out var value))
        {
            return value;
        }
        return 0;
    }

    /// <summary>
    /// 解析百分比值
    /// </summary>
    private decimal ParsePercentageValue(string text)
    {
        if (string.IsNullOrEmpty(text) || text == "-") return 0;

        // 移除可能的颜色span标签中的内容
        var cleanText = Regex.Replace(text, @"<[^>]*>", "");
        var match = Regex.Match(cleanText, @"([+-]?\d+\.?\d*)%");

        if (match.Success && decimal.TryParse(match.Groups[1].Value, out var value))
        {
            return value;
        }
        return 0;
    }

    /// <summary>
    /// 解析中文金额（转换为不带单位的真实数值）
    /// </summary>
    private decimal ParseChineseAmountValue(string text)
    {
        if (string.IsNullOrEmpty(text) || text == "-") return 0;

        // 移除可能的颜色span标签中的内容
        var cleanText = Regex.Replace(text, @"<[^>]*>", "");

        // 提取数字部分
        var numberMatch = Regex.Match(cleanText, @"([+-]?\d+\.?\d*)");
        if (!numberMatch.Success) return 0;

        if (!decimal.TryParse(numberMatch.Groups[1].Value, out var value)) return 0;

        // 根据单位进行转换为原始数值（不带单位）
        if (cleanText.Contains("亿"))
        {
            return value * 100000000; // 亿转换为原始数值
        }
        else if (cleanText.Contains("万"))
        {
            return value * 10000; // 万转换为原始数值
        }
        else
        {
            return value; // 无单位，直接返回
        }
    }



    #endregion
}
