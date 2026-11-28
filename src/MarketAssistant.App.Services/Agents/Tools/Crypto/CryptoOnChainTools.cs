using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.Crypto;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.DataProviders.Web3;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
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
public sealed class CryptoOnChainTools(
    ILogger<CryptoOnChainTools> logger,
    DexScreenerClient dexScreenerClient,
    GoPlusSecurityClient goPlusClient,
    IAlertCenterService alertCenterService) : IOnChainTools
{
    private readonly ILogger<CryptoOnChainTools> _logger = logger;
    private readonly DexScreenerClient _dexScreenerClient = dexScreenerClient;
    private readonly GoPlusSecurityClient _goPlusClient = goPlusClient;
    private readonly IAlertCenterService _alertCenterService = alertCenterService;

    /// <summary>
    /// 按关键字搜索链上热门交易对（DexScreener）。
    /// </summary>
    [Description("搜索链上 DEX 热门交易对，可按代币符号（如PEPE）或合约地址搜索，返回价格、流动性、涨跌幅等信息。")]
    public async Task<List<HotTokenPair>> SearchHotTokenPairsAsync(
        [Description("搜索关键字（代币符号、名称或合约地址）")] string query,
        CancellationToken cancellationToken = default)
    {
        return await ToolExecutor.ExecuteAsync("搜索链上交易对", query, _logger, async _ =>
        {
            var pairs = await _dexScreenerClient.SearchPairsAsync(query, cancellationToken);
            var result = pairs
                .Where(p => p.BaseToken is not null)
                .OrderByDescending(p => p.Volume?.H24 ?? 0)
                .Take(10)
                .Select(MapToHotTokenPair)
                .ToList();

            _logger.LogInformation("搜索到 {Count} 个链上交易对: {Query}", result.Count, query);
            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// 按符号或名称解析代币的链与合约地址，供后续安全审计/交易对查询使用。
    /// </summary>
    [Description("根据代币符号或名称解析其所在链与合约地址（DEX 视角）。调用安全审计或交易对查询前若不知道合约地址，必须先用本工具解析；返回按流动性排序的候选列表，一般取第一项作为主池。")]
    public async Task<List<HotTokenPair>> ResolveTokenIdentityAsync(
        [Description("代币符号或名称（如 PEPE、Arbitrum）")] string query,
        CancellationToken cancellationToken = default)
    {
        return await ToolExecutor.ExecuteAsync("解析代币链上身份", query, _logger, async _ =>
        {
            var pairs = await _dexScreenerClient.SearchPairsAsync(query, cancellationToken);
            var result = pairs
                .Where(p => p.BaseToken is not null)
                .OrderByDescending(p => p.Liquidity?.Usd ?? 0)
                .Take(10)
                .Select(MapToHotTokenPair)
                .ToList();

            _logger.LogInformation("解析到 {Count} 个候选交易对: {Query}", result.Count, query);
            return result;
        }, cancellationToken);
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
        return await ToolExecutor.ExecuteAsync("获取链上交易对", $"{chainId}:{tokenAddress}", _logger, async _ =>
        {
            var pairs = await _dexScreenerClient.GetTokenPairsAsync(chainId, tokenAddress, cancellationToken);
            var result = pairs
                .OrderByDescending(p => p.Liquidity?.Usd ?? 0)
                .Take(10)
                .Select(MapToHotTokenPair)
                .ToList();

            _logger.LogInformation("获取到 {Count} 个链上交易对: {ChainId} {TokenAddress}", result.Count, chainId, tokenAddress);
            return result;
        }, cancellationToken);
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
        return await ToolExecutor.ExecuteAsync("代币安全审计", $"{chainId}:{tokenAddress}", _logger, async _ =>
        {
            const string NotFoundMessage = "未找到该代币的安全审计数据，请确认合约地址与链 ID 是否正确";

            var canonicalChainId = ChainRegistry.Normalize(chainId);
            TokenSecurityReport report;
            if (canonicalChainId == "solana")
            {
                var solanaSecurity = await _goPlusClient.GetSolanaTokenSecurityAsync(tokenAddress, cancellationToken)
                    ?? throw new FriendlyException(NotFoundMessage);
                report = MapSolanaSecurityReport(tokenAddress, solanaSecurity);
            }
            else
            {
                var security = await _goPlusClient.GetTokenSecurityAsync(canonicalChainId, tokenAddress, cancellationToken)
                    ?? throw new FriendlyException(NotFoundMessage);
                report = MapToSecurityReport(canonicalChainId, tokenAddress, security);
            }

            await RaiseSecurityAlertIfDangerousAsync(report, cancellationToken);
            _logger.LogInformation(
                "代币安全审计完成: {Symbol} 风险等级 {RiskLevel} 蜜罐 {IsHoneypot}",
                report.Symbol, report.RiskLevel, report.IsHoneypot);
            return report;
        }, cancellationToken);
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
    /// 将 GoPlus EVM 原始审计字段映射为结构化安全报告，并汇总综合风险等级。
    /// 知名可信代币（trust_list=1 或已上线主流 CEX，官方称此时其他风险提示通常可忽略）不计增发/拉黑等
    /// 中心化权限类风险项；蜜罐、空投诈骗、仿冒代币等硬性风险始终计入。
    /// </summary>
    internal static TokenSecurityReport MapToSecurityReport(string chainId, string tokenAddress, GoPlusTokenSecurity security)
    {
        var riskReasons = new List<string>();

        var isTrusted = security.TrustList == "1" || security.IsInCex?.Listed == "1";
        var isHoneypot = security.IsHoneypot == "1";
        var isFakeToken = security.FakeToken?.Value == 1;

        if (isHoneypot) riskReasons.Add("蜜罐代币：买入后无法卖出");
        if (security.CannotSellAll == "1") riskReasons.Add("无法卖出全部持仓");
        if (security.CannotBuy == "1") riskReasons.Add("沙箱模拟无法买入（反机器人机制也可能触发）");
        if (security.HoneypotWithSameCreator == "1") riskReasons.Add("创建者曾发布过蜜罐代币");
        if (security.IsAirdropScam == "1") riskReasons.Add("存在空投诈骗风险");
        if (isFakeToken) riskReasons.Add($"疑似仿冒主流代币（真实代币地址：{security.FakeToken!.TrueTokenAddress}）");
        if (security.IsOpenSource != "1") riskReasons.Add("合约未开源，无法审计");
        if (security.HolderCount != null && int.TryParse(security.HolderCount, out var holders) && holders < 10)
            riskReasons.Add($"持有者过少（{holders} 个），疑似假币");
        if (!string.IsNullOrEmpty(security.OtherPotentialRisks)) riskReasons.Add(security.OtherPotentialRisks);

        if (!isTrusted)
        {
            if (security.SlippageModifiable == "1") riskReasons.Add("合约可修改交易滑点");
            if (security.IsMintable == "1") riskReasons.Add("合约可增发代币");
            if (security.OwnerChangeBalance == "1") riskReasons.Add("所有者可任意修改余额");
            if (security.Selfdestruct == "1") riskReasons.Add("合约可自毁");
            if (security.IsBlacklisted == "1") riskReasons.Add("合约可拉黑地址禁止交易");
            if (security.TransferPausable == "1") riskReasons.Add("所有者可暂停全部转账");
            if (security.HiddenOwner == "1") riskReasons.Add("隐藏所有者权限");
            if (security.CanTakeBackOwnership == "1") riskReasons.Add("所有者可收回已放弃的所有权");
            if (security.TradingCooldown == "1") riskReasons.Add("存在交易冷却时间限制");
            if (security.PersonalSlippageModifiable == "1") riskReasons.Add("可对单个地址设置个性化滑点");
        }

        var hasCriticalReason = isHoneypot || isFakeToken || (!isTrusted && security.OwnerChangeBalance == "1");
        var (riskLevel, advice) = AssessRisk(riskReasons, hasCriticalReason, isTrusted);

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
            IsTradableRestricted = security.TransferPausable == "1" || security.IsBlacklisted == "1",
            IsFeeChangable = security.SlippageModifiable == "1",
            IsBlacklistable = security.IsBlacklisted == "1",
            IsWhitelistable = security.IsWhitelisted == "1",
            // owner_address：空串表示合约无所有者，字段缺失表示未知，不能视为已放弃
            IsOwnershipRenounced = security.OwnerAddress is { Length: 0 } && security.CanTakeBackOwnership != "1",
            IsOpenSource = security.IsOpenSource == "1",
            IsProxyContract = security.IsProxy == "1",
            HasEnoughHolders = security.HolderCount == null ||
                (int.TryParse(security.HolderCount, out var holderCount) && holderCount >= 10),
            IsAntiWhaleModified = security.AntiWhaleModifiable == "1",
            SecurityAdvice = advice
        };
    }

    /// <summary>
    /// 将 GoPlus Solana 审计字段映射为结构化安全报告。Solana 无蜜罐/税率字段，
    /// 以权限项（增发/冻结/关闭/改余额/升级手续费）、转账钩子与恶意地址标记评估；
    /// 知名可信代币（trusted_token=1）不计权限类风险项。
    /// </summary>
    internal static TokenSecurityReport MapSolanaSecurityReport(string tokenAddress, GoPlusSolanaTokenSecurity security)
    {
        var riskReasons = new List<string>();

        var isTrusted = security.TrustedToken == 1;
        var isMintable = security.Mintable?.Status == 1;
        var isFreezable = security.Freezable?.Status == 1;
        var isClosable = security.Closable?.Status == 1;
        var canChangeBalance = security.BalanceMutableAuthority?.Status == 1;
        var isFeeUpgradable = security.TransferFeeUpgradable?.Status == 1;
        var hasTransferHook = security.TransferHook is { Count: > 0 };
        var isNonTransferable = security.NonTransferable == 1;
        var isDefaultFrozen = security.DefaultAccountState == 2;
        GoPlusSolanaAuthority?[] authorities =
        [
            security.Mintable, security.Freezable, security.Closable,
            security.BalanceMutableAuthority, security.TransferFeeUpgradable
        ];
        var hasMaliciousAuthority = authorities.Any(authority =>
            authority?.Authority?.Any(holder => holder.MaliciousAddress == 1) == true);

        if (hasMaliciousAuthority) riskReasons.Add("权限持有地址被标记为恶意地址");
        if (isNonTransferable) riskReasons.Add("代币不可转让（non-transferable），买入后无法转出或卖出");
        if (isDefaultFrozen) riskReasons.Add("新建代币账户默认冻结，需冻结权限持有者解冻后才能交易");
        if (security.HolderCount != null && int.TryParse(security.HolderCount, out var holders) && holders < 10)
            riskReasons.Add($"持有者过少（{holders} 个），疑似假币");

        if (!isTrusted)
        {
            if (isMintable) riskReasons.Add("增发权限未放弃，可无限增发代币");
            if (isFreezable) riskReasons.Add("存在冻结权限，可冻结任意持有者账户");
            if (isClosable) riskReasons.Add("存在关闭权限，可关闭代币账户");
            if (canChangeBalance) riskReasons.Add("存在余额修改权限，可任意修改持有者余额");
            if (isFeeUpgradable) riskReasons.Add("转账手续费配置可被升级，可能随时调高税率");
            if (hasTransferHook) riskReasons.Add("启用转账钩子（Transfer Hook），转账会触发额外合约逻辑");
        }

        var hasCriticalReason = hasMaliciousAuthority || isNonTransferable || isDefaultFrozen || (!isTrusted && canChangeBalance);
        var (riskLevel, advice) = AssessRisk(riskReasons, hasCriticalReason, isTrusted);

        return new TokenSecurityReport
        {
            Symbol = security.Metadata?.Symbol ?? string.Empty,
            TokenAddress = tokenAddress,
            ChainId = "solana",
            RiskLevel = riskLevel,
            RiskReasons = riskReasons,
            IsHoneypot = isNonTransferable || isDefaultFrozen,
            IsMintable = isMintable,
            IsTradableRestricted = isFreezable || isNonTransferable || isDefaultFrozen,
            IsFeeChangable = isFeeUpgradable,
            IsBlacklistable = isFreezable,
            // 全部权限项明确为 0 才视为已放弃；字段缺失表示未知
            IsOwnershipRenounced = authorities.All(authority => authority?.Status == 0),
            // SPL Token 为标准开源程序，无需像 EVM 那样逐合约核验源码
            IsOpenSource = true,
            HasEnoughHolders = security.HolderCount == null ||
                (int.TryParse(security.HolderCount, out var holderCount) && holderCount >= 10),
            SecurityAdvice = advice
        };
    }

    /// <summary>
    /// 按风险项汇总综合等级与建议：含致命项或风险项不少于 3 条为 danger，有风险项为 warning，否则 safe。
    /// </summary>
    private static (string RiskLevel, string Advice) AssessRisk(List<string> riskReasons, bool hasCriticalReason, bool isTrusted)
    {
        var riskLevel = riskReasons.Count switch
        {
            0 => "safe",
            _ when hasCriticalReason || riskReasons.Count >= 3 => "danger",
            _ => "warning"
        };

        var advice = riskLevel switch
        {
            "safe" => "未检测到明显风险项，但仍需关注流动性规模与持仓集中度。",
            "warning" => "存在中等风险项，参与前需谨慎评估，建议小仓位并设置止损。",
            _ => "高风险代币，强烈建议回避；AI 不会为该代币生成买入建议。"
        };

        return (riskLevel, isTrusted
            ? advice + "该代币被 GoPlus 标记为知名可信代币，增发/拉黑/冻结等中心化权限属其常规特征，未计入风险项。"
            : advice);
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