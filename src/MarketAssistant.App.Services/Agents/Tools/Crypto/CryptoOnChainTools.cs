using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Crypto;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.DataProviders.Web3;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Globalization;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币链上数据工具实现：热门代币/交易对追踪（DexScreener）、
/// 代币安全审计与蜜罐检测（GoPlus）。仅注册于 Crypto 市场。
/// 检出蜜罐/高危代币时经统一告警中心发布确认级告警（RequireConfirmation）。
/// </summary>
public sealed class CryptoOnChainTools : IOnChainTools
{
    private readonly ILogger<CryptoOnChainTools> _logger;
    private readonly DexScreenerClient _dexScreenerClient;
    private readonly GoPlusSecurityClient _goPlusClient;
    private readonly IAlertCenterService _alertCenterService;

    public CryptoOnChainTools(
        ILogger<CryptoOnChainTools> logger,
        DexScreenerClient dexScreenerClient,
        GoPlusSecurityClient goPlusClient,
        IAlertCenterService alertCenterService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dexScreenerClient = dexScreenerClient ?? throw new ArgumentNullException(nameof(dexScreenerClient));
        _goPlusClient = goPlusClient ?? throw new ArgumentNullException(nameof(goPlusClient));
        _alertCenterService = alertCenterService ?? throw new ArgumentNullException(nameof(alertCenterService));
    }

    /// <summary>
    /// 按关键字搜索链上热门交易对（DexScreener）。
    /// </summary>
    [Description("搜索链上 DEX 热门交易对，可按代币符号（如PEPE）或合约地址搜索，返回价格、流动性、涨跌幅等信息。")]
    public async Task<List<HotTokenPair>> SearchHotTokenPairsAsync(
        [Description("搜索关键字（代币符号、名称或合约地址）")] string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("正在搜索链上热门交易对: {Query}", query);
            var pairs = await _dexScreenerClient.SearchPairsAsync(query, cancellationToken);
            var result = pairs
                .Where(p => p.BaseToken is not null)
                .OrderByDescending(p => p.Volume?.H24 ?? 0)
                .Take(10)
                .Select(MapToHotTokenPair)
                .ToList();

            _logger.LogInformation("搜索到 {Count} 个链上交易对: {Query}", result.Count, query);
            return result;
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "搜索链上交易对时发生错误: {Query}", query);
            throw new FriendlyException($"搜索链上交易对时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 按符号或名称解析代币的链与合约地址，供后续安全审计/交易对查询使用。
    /// </summary>
    [Description("根据代币符号或名称解析其所在链与合约地址（DEX 视角）。调用安全审计或交易对查询前若不知道合约地址，必须先用本工具解析；返回按流动性排序的候选列表，一般取第一项作为主池。")]
    public async Task<List<HotTokenPair>> ResolveTokenIdentityAsync(
        [Description("代币符号或名称（如 PEPE、Arbitrum）")] string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("正在解析代币链上身份: {Query}", query);
            var pairs = await _dexScreenerClient.SearchPairsAsync(query, cancellationToken);
            var result = pairs
                .Where(p => p.BaseToken is not null)
                .OrderByDescending(p => p.Liquidity?.Usd ?? 0)
                .Take(10)
                .Select(MapToHotTokenPair)
                .ToList();

            _logger.LogInformation("解析到 {Count} 个候选交易对: {Query}", result.Count, query);
            return result;
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "解析代币链上身份时发生错误: {Query}", query);
            throw new FriendlyException($"解析代币链上身份时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取指定代币合约的全部 DEX 交易对（DexScreener）。
    /// </summary>
    [Description("获取指定代币在指定链上的所有 DEX 交易对信息（价格、流动性、成交量、涨跌幅）。")]
    public async Task<List<HotTokenPair>> GetTokenPairsAsync(
        [Description("链 ID（ethereum、bsc、base、solana 等规范名，也兼容 eth/1、binance-smart-chain、polygon-pos 等写法，自动归一化）")] string chainId,
        [Description("代币合约地址")] string tokenAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("正在获取链上交易对: {ChainId} {TokenAddress}", chainId, tokenAddress);
            var pairs = await _dexScreenerClient.GetTokenPairsAsync(chainId, tokenAddress, cancellationToken);
            var result = pairs
                .OrderByDescending(p => p.Liquidity?.Usd ?? 0)
                .Take(10)
                .Select(MapToHotTokenPair)
                .ToList();

            _logger.LogInformation("获取到 {Count} 个链上交易对: {ChainId} {TokenAddress}", result.Count, chainId, tokenAddress);
            return result;
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取链上交易对时发生错误: {ChainId} {TokenAddress}", chainId, tokenAddress);
            throw new FriendlyException($"获取链上交易对时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 代币安全审计与蜜罐检测（GoPlus）。
    /// </summary>
    [Description("对代币合约进行安全审计与蜜罐检测，返回蜜罐标记、买卖税率、增发/黑名单/暂停交易等风险项与综合风险等级。分析不熟悉的代币前必须调用。")]
    public async Task<TokenSecurityReport> AuditTokenSecurityAsync(
        [Description("链 ID（ethereum、bsc、base、solana 等规范名，也兼容 eth/1、binance-smart-chain、polygon-pos 等写法，自动归一化）")] string chainId,
        [Description("代币合约地址")] string tokenAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("正在进行代币安全审计: {ChainId} {TokenAddress}", chainId, tokenAddress);
            var security = await _goPlusClient.GetTokenSecurityAsync(chainId, tokenAddress, cancellationToken);
            if (security is null)
                throw new FriendlyException("未找到该代币的安全审计数据，请确认合约地址与链 ID 是否正确");

            var report = MapToSecurityReport(ChainRegistry.Normalize(chainId), tokenAddress, security);
            await RaiseSecurityAlertIfDangerousAsync(report, cancellationToken);
            _logger.LogInformation(
                "代币安全审计完成: {Symbol} 风险等级 {RiskLevel} 蜜罐 {IsHoneypot}",
                report.Symbol, report.RiskLevel, report.IsHoneypot);
            return report;
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "代币安全审计时发生错误: {ChainId} {TokenAddress}", chainId, tokenAddress);
            throw new FriendlyException($"代币安全审计时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 蜜罐/高危代币经统一告警中心发布确认级告警（RequireConfirmation），
    /// 失败仅记日志，不影响审计结果返回。
    /// </summary>
    private async Task RaiseSecurityAlertIfDangerousAsync(TokenSecurityReport report, CancellationToken cancellationToken)
    {
        if (report.RiskLevel != "danger")
            return;

        try
        {
            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = string.IsNullOrWhiteSpace(report.Symbol) ? report.TokenAddress : report.Symbol,
                Level = AlertLevel.Critical,
                Source = AlertSource.Risk,
                Title = $"高危代币安全告警: {report.Symbol}",
                Content = $"链 {report.ChainId} 合约 {report.TokenAddress} 检出高危风险项：\n" +
                          string.Join("\n", report.RiskReasons) +
                          $"\n综合风险等级: {report.RiskLevel}。{report.SecurityAdvice}",
                TradingImpact = AlertTradingImpact.RequireConfirmation
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "发布代币安全告警失败: {Symbol} {TokenAddress}", report.Symbol, report.TokenAddress);
        }
    }

    /// <summary>
    /// 将 DexScreener 交易对映射为工具返回模型。
    /// </summary>
    private static HotTokenPair MapToHotTokenPair(DexScreenerPair pair)
    {
        DateTime? createdAt = pair.PairCreatedAt is > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(pair.PairCreatedAt.Value).UtcDateTime
            : null;

        return new HotTokenPair
        {
            ChainId = pair.ChainId ?? string.Empty,
            Symbol = pair.BaseToken?.Symbol ?? string.Empty,
            TokenName = pair.BaseToken?.Name ?? string.Empty,
            TokenAddress = pair.BaseToken?.Address ?? string.Empty,
            PriceUsd = decimal.TryParse(pair.PriceUsd, NumberStyles.Float, CultureInfo.InvariantCulture, out var price) ? price : null,
            Change24hPercent = pair.PriceChange?.H24,
            Volume24hUsd = pair.Volume?.H24,
            LiquidityUsd = pair.Liquidity?.Usd,
            MarketCapUsd = pair.MarketCap ?? pair.Fdv,
            PairCreatedAt = createdAt,
            DexId = pair.DexId ?? string.Empty
        };
    }

    /// <summary>
    /// 将 GoPlus 原始审计字段映射为结构化安全报告，并汇总综合风险等级。
    /// </summary>
    private static TokenSecurityReport MapToSecurityReport(string chainId, string tokenAddress, GoPlusTokenSecurity security)
    {
        var riskReasons = new List<string>();

        var isHoneypot = security.IsHoneypot == "1";
        if (isHoneypot) riskReasons.Add("蜜罐代币：买入后无法卖出");
        if (security.CannotSellAll == "1") riskReasons.Add("无法卖出全部持仓");
        if (security.SlippageModifiable == "1") riskReasons.Add("合约可修改交易滑点");
        if (security.IsSlippageModified == "1") riskReasons.Add("合约已修改过交易滑点");
        if (security.IsMintable == "1") riskReasons.Add("合约可增发代币");
        if (security.IsOwnerCanChangeBalance == "1") riskReasons.Add("所有者可任意修改余额");
        if (security.SelfDestruct == "1") riskReasons.Add("合约可自毁");
        if (security.IsBlackList == "1") riskReasons.Add("合约可拉黑地址禁止交易");
        if (security.TransferPausable == "1") riskReasons.Add("所有者可暂停全部转账");
        if (security.HiddenOwner == "1") riskReasons.Add("隐藏所有者权限");
        if (security.IsOpenSource != "1") riskReasons.Add("合约未开源，无法审计");
        if (security.HolderCount != null && int.TryParse(security.HolderCount, out var holders) && holders < 10)
            riskReasons.Add($"持有者过少（{holders} 个），疑似假币");
        if (security.TradingCooldown == "1") riskReasons.Add("存在交易冷却时间限制");
        if (security.PersonalSlippageModifiable == "1") riskReasons.Add("可对单个地址设置个性化滑点");
        if (security.IsAirdropScam == "1") riskReasons.Add("存在空投诈骗风险");
        if (security.FakeToken == "1") riskReasons.Add("疑似假冒代币");

        var riskLevel = riskReasons.Count switch
        {
            0 => "safe",
            _ when isHoneypot || security.IsOwnerCanChangeBalance == "1" || security.FakeToken == "1" => "danger",
            _ when riskReasons.Count >= 3 => "danger",
            _ => "warning"
        };

        var advice = riskLevel switch
        {
            "safe" => "未检测到明显风险项，但仍需关注流动性规模与持仓集中度。",
            "warning" => "存在中等风险项，参与前需谨慎评估，建议小仓位并设置止损。",
            _ => "高风险代币，强烈建议回避；AI 不会为该代币生成买入建议。"
        };

        return new TokenSecurityReport
        {
            Symbol = security.TokenSymbol ?? string.Empty,
            TokenAddress = tokenAddress,
            ChainId = chainId,
            RiskLevel = riskLevel,
            RiskReasons = riskReasons,
            IsHoneypot = isHoneypot,
            BuyTaxPercent = ParsePercent(security.BuyTax),
            SellTaxPercent = ParsePercent(security.SellTax),
            IsMintable = security.IsMintable == "1",
            IsTradableRestricted = security.TransferPausable == "1" || security.IsBlackList == "1",
            IsFeeChangable = security.SlippageModifiable == "1",
            IsBlacklistable = security.IsBlackList == "1",
            IsWhitelistable = security.IsWhiteList == "1",
            IsOwnershipRenounced = security.CannotBeModified == "1",
            IsOpenSource = security.IsOpenSource == "1",
            IsProxyContract = security.IsProxy == "1",
            HasEnoughHolders = security.HolderCount == null ||
                (int.TryParse(security.HolderCount, out var holderCount) && holderCount >= 10),
            IsAntiWhaleModified = security.AntiWhaleModifiable == "1",
            SecurityAdvice = advice
        };
    }

    /// <summary>
    /// GoPlus 返回的税率字符串（如 "0.05" 表示 5%）转百分比。
    /// </summary>
    private static decimal? ParsePercent(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result * 100
            : null;

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(ResolveTokenIdentityAsync);
        yield return AIFunctionFactory.Create(SearchHotTokenPairsAsync);
        yield return AIFunctionFactory.Create(GetTokenPairsAsync);
        yield return AIFunctionFactory.Create(AuditTokenSecurityAsync);
    }
}