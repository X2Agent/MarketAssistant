using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

[Description("上市公司基本信息")]
public class CompanyInfo
{
    [Description("公司名称")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [Description("公司英文名称")]
    [JsonPropertyName("ename")]
    public string EName { get; set; } = "";

    [Description("所属市场（如：主板、创业板、科创板）")]
    [JsonPropertyName("market")]
    public string Market { get; set; } = "";

    [Description("所属概念板块（多个以逗号分隔）")]
    [JsonPropertyName("idea")]
    public string Concept { get; set; } = "";

    [Description("上市日期（yyyy-MM-dd）")]
    [JsonPropertyName("ldate")]
    public string ListingDate { get; set; } = "";

    [Description("发行价格（元）")]
    [JsonPropertyName("sprice")]
    public string IssuePrice { get; set; } = "";

    [Description("主承销商")]
    [JsonPropertyName("principal")]
    public string Underwriter { get; set; } = "";

    [Description("成立日期（yyyy-MM-dd）")]
    [JsonPropertyName("rdate")]
    public string EstablishmentDate { get; set; } = "";

    [Description("注册资本")]
    [JsonPropertyName("rprice")]
    public string RegisteredCapital { get; set; } = "";

    [Description("机构类型")]
    [JsonPropertyName("instype")]
    public string InstitutionType { get; set; } = "";

    [Description("组织形式")]
    [JsonPropertyName("organ")]
    public string Organization { get; set; } = "";

    [Description("董事会秘书")]
    [JsonPropertyName("secre")]
    public string Secretary { get; set; } = "";

    [Description("公司电话")]
    [JsonPropertyName("phone")]
    public string CompanyPhone { get; set; } = "";

    [Description("董秘电话")]
    [JsonPropertyName("sphone")]
    public string SecretaryPhone { get; set; } = "";

    [Description("公司传真")]
    [JsonPropertyName("fax")]
    public string Fax { get; set; } = "";

    [Description("董秘传真")]
    [JsonPropertyName("sfax")]
    public string SecretaryFax { get; set; } = "";

    [Description("公司邮箱")]
    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [Description("董秘邮箱")]
    [JsonPropertyName("semail")]
    public string SecretaryEmail { get; set; } = "";

    [Description("公司网站")]
    [JsonPropertyName("site")]
    public string Website { get; set; } = "";

    [Description("邮政编码")]
    [JsonPropertyName("post")]
    public string PostalCode { get; set; } = "";

    [Description("信息披露网站")]
    [JsonPropertyName("infosite")]
    public string InfoWebsite { get; set; } = "";

    [Description("公司曾用名（历史更名）")]
    [JsonPropertyName("oname")]
    public string NameHistory { get; set; } = "";

    [Description("注册地址")]
    [JsonPropertyName("addr")]
    public string RegisteredAddress { get; set; } = "";

    [Description("办公地址")]
    [JsonPropertyName("oaddr")]
    public string OfficeAddress { get; set; } = "";

    [Description("公司简介")]
    [JsonPropertyName("desc")]
    public string Description { get; set; } = "";

    [Description("经营范围")]
    [JsonPropertyName("bscope")]
    public string BusinessScope { get; set; } = "";

    [Description("承销方式")]
    [JsonPropertyName("printype")]
    public string UnderwritingType { get; set; } = "";

    [Description("上市推荐人")]
    [JsonPropertyName("referrer")]
    public string ListingReferrer { get; set; } = "";

    [Description("发行方式")]
    [JsonPropertyName("putype")]
    public string IssueType { get; set; } = "";

    [Description("发行市盈率")]
    [JsonPropertyName("pe")]
    public string PublishPE { get; set; } = "";

    [Description("发行前总股本（万股）")]
    [JsonPropertyName("firgu")]
    public string PreIssueShares { get; set; } = "";

    [Description("发行后总股本（万股）")]
    [JsonPropertyName("lastgu")]
    public string PostIssueShares { get; set; } = "";

    [Description("实际发行数量（万股）")]
    [JsonPropertyName("realgu")]
    public string ActualIssueShares { get; set; } = "";

    [Description("预计募集资金（万元）")]
    [JsonPropertyName("planm")]
    public string PlannedFunds { get; set; } = "";

    [Description("实际募集资金（万元）")]
    [JsonPropertyName("realm")]
    public string ActualFunds { get; set; } = "";

    [Description("发行总费用（万元）")]
    [JsonPropertyName("pubfee")]
    public string TotalIssueCost { get; set; } = "";

    [Description("募集资金净额（万元）")]
    [JsonPropertyName("collect")]
    public string NetFunds { get; set; } = "";

    [Description("承销保荐费用（万元）")]
    [JsonPropertyName("signfee")]
    public string UnderwritingFee { get; set; } = "";

    [Description("招股书披露日期（yyyy-MM-dd）")]
    [JsonPropertyName("pdate")]
    public string ProspectusDate { get; set; } = "";
}