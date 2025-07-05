using System.Text.Json.Serialization;

namespace MarketAssistant.Plugins.Models;

/// <summary>
/// 股票公司信息实体类
/// </summary>
public class StockCompanyInfo
{
    // ================= 基础信息 =================
    /// <summary>
    /// 公司名称
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 公司英文名称
    /// </summary>
    [JsonPropertyName("ename")]
    public string EName { get; set; } = "";

    /// <summary>
    /// 上市市场 (例如: 沪市主板/深市创业板)
    /// </summary>
    [JsonPropertyName("market")]
    public string Market { get; set; } = "";

    // ================= 上市信息 =================
    /// <summary>
    /// 概念及板块 (多个概念逗号分隔)
    /// </summary>
    [JsonPropertyName("idea")]
    public string Concept { get; set; } = "";

    /// <summary>
    /// 上市日期 (格式 yyyy-MM-dd)
    /// </summary>
    [JsonPropertyName("ldate")]
    public string ListingDate { get; set; } = "";

    /// <summary>
    /// 发行价格 (元)
    /// </summary>
    [JsonPropertyName("sprice")]
    public string IssuePrice { get; set; } = "";

    // ================= 发行信息 =================
    /// <summary>
    /// 主承销商
    /// </summary>
    [JsonPropertyName("principal")]
    public string Underwriter { get; set; } = "";

    /// <summary>
    /// 成立日期 (格式 yyyy-MM-dd)
    /// </summary>
    [JsonPropertyName("rdate")]
    public string EstablishmentDate { get; set; } = "";

    /// <summary>
    /// 注册资本
    /// </summary>
    [JsonPropertyName("rprice")]
    public string RegisteredCapital { get; set; } = "";

    // ================= 机构信息 =================
    /// <summary>
    /// 机构类型
    /// </summary>
    [JsonPropertyName("instype")]
    public string InstitutionType { get; set; } = "";

    /// <summary>
    /// 组织形式
    /// </summary>
    [JsonPropertyName("organ")]
    public string Organization { get; set; } = "";

    // ================= 联系方式 =================
    /// <summary>
    /// 董事会秘书
    /// </summary>
    [JsonPropertyName("secre")]
    public string Secretary { get; set; } = "";

    /// <summary>
    /// 公司电话
    /// </summary>
    [JsonPropertyName("phone")]
    public string CompanyPhone { get; set; } = "";

    /// <summary>
    /// 董秘电话
    /// </summary>
    [JsonPropertyName("sphone")]
    public string SecretaryPhone { get; set; } = "";

    // ================= 其他信息 =================
    /// <summary>
    /// 公司传真
    /// </summary>
    [JsonPropertyName("fax")]
    public string Fax { get; set; } = "";

    /// <summary>
    /// 董秘传真
    /// </summary>
    [JsonPropertyName("sfax")]
    public string SecretaryFax { get; set; } = "";

    /// <summary>
    /// 公司邮箱
    /// </summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    /// <summary>
    /// 董秘邮箱
    /// </summary>
    [JsonPropertyName("semail")]
    public string SecretaryEmail { get; set; } = "";

    // ================= 网站信息 =================
    /// <summary>
    /// 公司官网
    /// </summary>
    [JsonPropertyName("site")]
    public string Website { get; set; } = "";

    /// <summary>
    /// 邮政编码
    /// </summary>
    [JsonPropertyName("post")]
    public string PostalCode { get; set; } = "";

    /// <summary>
    /// 信息披露网站
    /// </summary>
    [JsonPropertyName("infosite")]
    public string InfoWebsite { get; set; } = "";

    // ================= 历史信息 =================
    /// <summary>
    /// 证券简称更名历史
    /// </summary>
    [JsonPropertyName("oname")]
    public string NameHistory { get; set; } = "";

    // ================= 地址信息 =================
    /// <summary>
    /// 注册地址
    /// </summary>
    [JsonPropertyName("addr")]
    public string RegisteredAddress { get; set; } = "";

    /// <summary>
    /// 办公地址
    /// </summary>
    [JsonPropertyName("oaddr")]
    public string OfficeAddress { get; set; } = "";

    // ================= 描述信息 =================
    /// <summary>
    /// 公司简介
    /// </summary>
    [JsonPropertyName("desc")]
    public string Description { get; set; } = "";

    /// <summary>
    /// 经营范围
    /// </summary>
    [JsonPropertyName("bscope")]
    public string BusinessScope { get; set; } = "";

    // ================= 发行细节 =================
    /// <summary>
    /// 承销方式
    /// </summary>
    [JsonPropertyName("printype")]
    public string UnderwritingType { get; set; } = "";

    /// <summary>
    /// 上市推荐人
    /// </summary>
    [JsonPropertyName("referrer")]
    public string ListingReferrer { get; set; } = "";

    /// <summary>
    /// 发行方式
    /// </summary>
    [JsonPropertyName("putype")]
    public string IssueType { get; set; } = "";

    // ================= 财务指标 =================
    /// <summary>
    /// 发行市盈率 (按发行后总股本)
    /// </summary>
    [JsonPropertyName("pe")]
    public string PublishPE { get; set; } = "";

    /// <summary>
    /// 首发前总股本 (万股)
    /// </summary>
    [JsonPropertyName("firgu")]
    public string PreIssueShares { get; set; } = "";

    /// <summary>
    /// 首发后总股本 (万股)
    /// </summary>
    [JsonPropertyName("lastgu")]
    public string PostIssueShares { get; set; } = "";

    /// <summary>
    /// 实际发行量 (万股)
    /// </summary>
    [JsonPropertyName("realgu")]
    public string ActualIssueShares { get; set; } = "";

    // ================= 募集资金 =================
    /// <summary>
    /// 预计募集资金 (万元)
    /// </summary>
    [JsonPropertyName("planm")]
    public string PlannedFunds { get; set; } = "";

    /// <summary>
    /// 实际募集资金 (万元)
    /// </summary>
    [JsonPropertyName("realm")]
    public string ActualFunds { get; set; } = "";

    /// <summary>
    /// 发行费用总额 (万元)
    /// </summary>
    [JsonPropertyName("pubfee")]
    public string TotalIssueCost { get; set; } = "";

    /// <summary>
    /// 募集资金净额 (万元)
    /// </summary>
    [JsonPropertyName("collect")]
    public string NetFunds { get; set; } = "";

    /// <summary>
    /// 承销费用 (万元)
    /// </summary>
    [JsonPropertyName("signfee")]
    public string UnderwritingFee { get; set; } = "";

    /// <summary>
    /// 招股公告日 (格式 yyyy-MM-dd)
    /// </summary>
    [JsonPropertyName("pdate")]
    public string ProspectusDate { get; set; } = "";
}
