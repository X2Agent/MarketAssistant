using System.Text.Json;
using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Agents.Tools.Models.Crypto;
using MarketAssistant.DataProviders.Web3;

namespace TestMarketAssistant.Tools;

/// <summary>
/// 代币安全审计报告映射（GoPlus 原始字段 → TokenSecurityReport）：
/// 覆盖风险等级判定、知名可信代币对中心化权限项的豁免，以及硬性风险（蜜罐/仿冒/恶意地址）不被豁免。
/// 夹具取自 GoPlus 真实响应的字段形态（USDT 的 owner_change_balance、is_blacklisted 等全为 "1"，
/// 若不豁免会被判为高危并触发 Critical 告警）。
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class CryptoOnChainSecurityReportTest
{
    private const string UsdtPrivileges = """
        "is_open_source":"1","is_proxy":"0","holder_count":"15892705","owner_address":"0xc6cde7c39eb2f0f0095f41570af89efc2c1ea828",
        "can_take_back_ownership":"0","is_mintable":"1","is_blacklisted":"1","owner_change_balance":"1",
        "slippage_modifiable":"1","transfer_pausable":"1","is_honeypot":"0","token_symbol":"USDT"
        """;

    private static GoPlusTokenSecurity Evm(string json) =>
        JsonSerializer.Deserialize<GoPlusTokenSecurity>(json, JsonSerializerOptions.Web)!;

    private static GoPlusSolanaTokenSecurity Solana(string json) =>
        JsonSerializer.Deserialize<GoPlusSolanaTokenSecurity>(json, JsonSerializerOptions.Web)!;

    private static TokenSecurityReport MapEvm(string json) =>
        CryptoOnChainTools.MapToSecurityReport("ethereum", "0xabc", Evm(json));

    private static TokenSecurityReport MapSolana(string json) =>
        CryptoOnChainTools.MapSolanaSecurityReport("MintAddress", Solana(json));

    #region EVM

    [TestMethod]
    public void Evm_TrustedTokenWithCentralizedPrivileges_IsSafe()
    {
        var report = MapEvm("{" + UsdtPrivileges + ",\"trust_list\":\"1\"}");

        Assert.AreEqual("safe", report.RiskLevel);
        Assert.IsEmpty(report.RiskReasons);
        // 豁免只作用于风险评分，权限事实仍如实呈现给模型
        Assert.IsTrue(report.IsMintable);
        Assert.IsTrue(report.IsBlacklistable);
        Assert.IsTrue(report.IsTradableRestricted);
        Assert.IsFalse(report.IsOwnershipRenounced);
        Assert.IsTrue(report.HasEnoughHolders);
        StringAssert.Contains(report.SecurityAdvice, "知名可信代币");
    }

    [TestMethod]
    public void Evm_SamePrivilegesWithoutTrust_IsDanger()
    {
        var report = MapEvm("{" + UsdtPrivileges + "}");

        Assert.AreEqual("danger", report.RiskLevel);
        CollectionAssert.Contains(report.RiskReasons, "所有者可任意修改余额");
        Assert.IsGreaterThanOrEqualTo(5, report.RiskReasons.Count);
    }

    [TestMethod]
    public void Evm_ListedOnMajorCexWithoutTrustList_IsAlsoExempt()
    {
        var report = MapEvm("{" + UsdtPrivileges + ",\"is_in_cex\":{\"listed\":\"1\",\"cex_list\":[\"Binance\"]}}");

        Assert.AreEqual("safe", report.RiskLevel);
        Assert.IsEmpty(report.RiskReasons);
    }

    [TestMethod]
    public void Evm_HoneypotOnTrustedToken_IsStillDanger()
    {
        var report = MapEvm("""{"is_open_source":"1","holder_count":"5000","trust_list":"1","is_honeypot":"1"}""");

        Assert.AreEqual("danger", report.RiskLevel);
        Assert.IsTrue(report.IsHoneypot);
        CollectionAssert.Contains(report.RiskReasons, "蜜罐代币：买入后无法卖出");
    }

    [TestMethod]
    public void Evm_FakeToken_IsDangerAndNamesTheRealToken()
    {
        var report = MapEvm("""
            {"is_open_source":"1","holder_count":"5000",
             "fake_token":{"true_token_address":"0xaf88d065e77c8cc2239327c5edb3a432268e5831","value":1}}
            """);

        Assert.AreEqual("danger", report.RiskLevel);
        Assert.IsTrue(report.RiskReasons.Any(reason => reason.Contains("0xaf88d065e77c8cc2239327c5edb3a432268e5831")));
    }

    [TestMethod]
    public void Evm_TwoCapabilityRisksOnly_IsWarning()
    {
        var report = MapEvm("""{"is_open_source":"1","holder_count":"100000","is_mintable":"1","is_blacklisted":"1"}""");

        Assert.AreEqual("warning", report.RiskLevel);
        Assert.HasCount(2, report.RiskReasons);
    }

    [TestMethod]
    public void Evm_OtherPotentialRisks_AreReported()
    {
        var report = MapEvm("""
            {"is_open_source":"1","holder_count":"100000",
             "other_potential_risks":"Owner can set different transaction taxes for each user, which can trigger serious losses."}
            """);

        Assert.AreEqual("warning", report.RiskLevel);
        StringAssert.Contains(report.RiskReasons.Single(), "Owner can set different transaction taxes");
    }

    [TestMethod]
    [DataRow("""{"owner_address":"","can_take_back_ownership":"0"}""", true, DisplayName = "空串所有者且不可收回：已放弃")]
    [DataRow("""{"can_take_back_ownership":"0"}""", false, DisplayName = "所有者字段缺失：未知，不视为已放弃")]
    [DataRow("""{"owner_address":"","can_take_back_ownership":"1"}""", false, DisplayName = "可收回所有权：未放弃")]
    [DataRow("""{"owner_address":"0xc6cde7c39eb2f0f0095f41570af89efc2c1ea828"}""", false, DisplayName = "仍有所有者：未放弃")]
    public void Evm_OwnershipRenounced_FollowsOwnerAddressSemantics(string json, bool expected)
    {
        Assert.AreEqual(expected, MapEvm(json).IsOwnershipRenounced);
    }

    #endregion

    #region Solana

    [TestMethod]
    public void Solana_TrustedTokenWithAuthorities_IsSafe()
    {
        var report = MapSolana("""
            {"trusted_token":1,"holder_count":"9654398","metadata":{"symbol":"USDC"},
             "mintable":{"authority":[{"address":"BJE5","malicious_address":0}],"status":"1"},
             "freezable":{"authority":[{"address":"7dGb","malicious_address":0}],"status":"1"},
             "closable":{"authority":[],"status":"0"},"balance_mutable_authority":{"authority":[],"status":"0"},
             "transfer_fee_upgradable":{"authority":[],"status":"0"},"transfer_hook":[],
             "non_transferable":"0","default_account_state":"1"}
            """);

        Assert.AreEqual("safe", report.RiskLevel);
        Assert.IsEmpty(report.RiskReasons);
        Assert.AreEqual("USDC", report.Symbol);
        Assert.AreEqual("solana", report.ChainId);
        Assert.IsTrue(report.IsMintable);
        Assert.IsTrue(report.IsBlacklistable);
        Assert.IsFalse(report.IsOwnershipRenounced);
        StringAssert.Contains(report.SecurityAdvice, "知名可信代币");
    }

    [TestMethod]
    public void Solana_UntrustedMintableAndFreezable_IsWarning()
    {
        var report = MapSolana("""
            {"trusted_token":0,"holder_count":"5000",
             "mintable":{"authority":[{"address":"A","malicious_address":0}],"status":"1"},
             "freezable":{"authority":[{"address":"B","malicious_address":0}],"status":"1"}}
            """);

        Assert.AreEqual("warning", report.RiskLevel);
        Assert.HasCount(2, report.RiskReasons);
    }

    [TestMethod]
    public void Solana_ThreeCapabilities_IsDanger()
    {
        var report = MapSolana("""
            {"trusted_token":0,"holder_count":"5000",
             "mintable":{"authority":[],"status":"1"},"freezable":{"authority":[],"status":"1"},
             "closable":{"authority":[],"status":"1"}}
            """);

        Assert.AreEqual("danger", report.RiskLevel);
        Assert.HasCount(3, report.RiskReasons);
    }

    [TestMethod]
    public void Solana_BalanceMutableAuthorityOnUntrustedToken_IsDanger()
    {
        var report = MapSolana("""
            {"trusted_token":0,"holder_count":"5000","balance_mutable_authority":{"authority":[],"status":"1"}}
            """);

        Assert.AreEqual("danger", report.RiskLevel);
        Assert.HasCount(1, report.RiskReasons);
    }

    [TestMethod]
    public void Solana_MaliciousAuthorityAddress_IsDangerEvenOnTrustedToken()
    {
        var report = MapSolana("""
            {"trusted_token":1,"holder_count":"5000",
             "mintable":{"authority":[{"address":"Scam","malicious_address":1}],"status":"1"}}
            """);

        Assert.AreEqual("danger", report.RiskLevel);
        Assert.IsTrue(report.RiskReasons.Any(reason => reason.Contains("恶意地址")));
    }

    [TestMethod]
    public void Solana_NonTransferable_IsDangerAndTreatedAsHoneypot()
    {
        var report = MapSolana("""{"trusted_token":0,"holder_count":"5000","non_transferable":"1"}""");

        Assert.AreEqual("danger", report.RiskLevel);
        Assert.IsTrue(report.IsHoneypot);
        Assert.IsTrue(report.IsTradableRestricted);
    }

    [TestMethod]
    public void Solana_DefaultFrozenAccounts_IsDangerAndTreatedAsHoneypot()
    {
        var report = MapSolana("""{"trusted_token":0,"holder_count":"5000","default_account_state":"2"}""");

        Assert.AreEqual("danger", report.RiskLevel);
        Assert.IsTrue(report.IsHoneypot);
    }

    [TestMethod]
    public void Solana_AllAuthoritiesRevoked_IsSafeAndRenounced()
    {
        var report = MapSolana("""
            {"trusted_token":0,"holder_count":"1020631","metadata":{"symbol":"Bonk"},
             "mintable":{"authority":[],"status":"0"},"freezable":{"authority":[],"status":"0"},
             "closable":{"authority":[],"status":"0"},"balance_mutable_authority":{"authority":[],"status":"0"},
             "transfer_fee_upgradable":{"authority":[],"status":"0"},"transfer_hook":[],
             "non_transferable":"0","default_account_state":"1"}
            """);

        Assert.AreEqual("safe", report.RiskLevel);
        Assert.IsEmpty(report.RiskReasons);
        Assert.IsTrue(report.IsOwnershipRenounced);
        Assert.IsFalse(report.IsMintable);
    }

    [TestMethod]
    public void Solana_TransferHookOnUntrustedToken_IsReported()
    {
        var report = MapSolana("""{"trusted_token":0,"holder_count":"5000","transfer_hook":[{"program_id":"Hook"}]}""");

        Assert.AreEqual("warning", report.RiskLevel);
        Assert.HasCount(1, report.RiskReasons);
    }

    [TestMethod]
    public void Solana_MissingAuthorityFields_AreNotTreatedAsRenounced()
    {
        var report = MapSolana("{}");

        Assert.IsFalse(report.IsOwnershipRenounced);
    }

    #endregion
}
