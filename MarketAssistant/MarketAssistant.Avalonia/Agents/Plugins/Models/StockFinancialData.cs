using System.Text.Json.Serialization;

namespace MarketAssistant.Plugins.Models;

/// <summary>
/// 股票财务指标实体类
/// </summary>
public class StockFinancialData
{
    /// <summary>
    /// 报告日期yyyy-MM-dd
    /// </summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    /// <summary>
    /// 摊薄每股收益(元)
    /// </summary>
    [JsonPropertyName("tbmg")]
    public string Tbmg { get; set; } = "";

    /// <summary>
    /// 加权每股收益(元)
    /// </summary>
    [JsonPropertyName("jqmg")]
    public string Jqmg { get; set; } = "";

    /// <summary>
    /// 每股收益_调整后(元)
    /// </summary>
    [JsonPropertyName("mgsy")]
    public string Mgsy { get; set; } = "";

    /// <summary>
    /// 扣除非经常性损益后的每股收益(元)
    /// </summary>
    [JsonPropertyName("kfmg")]
    public string Kfmg { get; set; } = "";

    /// <summary>
    /// 每股净资产_调整前(元)
    /// </summary>
    [JsonPropertyName("mgjz")]
    public string Mgjz { get; set; } = "";

    /// <summary>
    /// 每股净资产_调整后(元)
    /// </summary>
    [JsonPropertyName("mgjzad")]
    public string Mgjzad { get; set; } = "";

    /// <summary>
    /// 每股经营性现金流(元)
    /// </summary>
    [JsonPropertyName("mgjy")]
    public string Mgjy { get; set; } = "";

    /// <summary>
    /// 每股资本公积金(元)
    /// </summary>
    [JsonPropertyName("mggjj")]
    public string Mggjj { get; set; } = "";

    /// <summary>
    /// 每股未分配利润(元)
    /// </summary>
    [JsonPropertyName("mgwly")]
    public string Mgwly { get; set; } = "";

    /// <summary>
    /// 总资产利润率(%)
    /// </summary>
    [JsonPropertyName("zclr")]
    public string Zclr { get; set; } = "";

    /// <summary>
    /// 主营业务利润率(%)
    /// </summary>
    [JsonPropertyName("zylr")]
    public string Zylr { get; set; } = "";

    /// <summary>
    /// 总资产净利润率(%)
    /// </summary>
    [JsonPropertyName("zzlr")]
    public string Zzlr { get; set; } = "";

    /// <summary>
    /// 成本费用利润率(%)
    /// </summary>
    [JsonPropertyName("cblr")]
    public string Cblr { get; set; } = "";

    /// <summary>
    /// 营业利润率(%)
    /// </summary>
    [JsonPropertyName("yylr")]
    public string Yylr { get; set; } = "";

    /// <summary>
    /// 主营业务成本率(%)
    /// </summary>
    [JsonPropertyName("zycb")]
    public string Zycb { get; set; } = "";

    /// <summary>
    /// 销售净利率(%)
    /// </summary>
    [JsonPropertyName("xsjl")]
    public string Xsjl { get; set; } = "";

    /// <summary>
    /// 股本报酬率(%)
    /// </summary>
    [JsonPropertyName("gbbc")]
    public string Gbbc { get; set; } = "";

    /// <summary>
    /// 净资产报酬率(%)
    /// </summary>
    [JsonPropertyName("jzbc")]
    public string Jzbc { get; set; } = "";

    /// <summary>
    /// 资产报酬率(%)
    /// </summary>
    [JsonPropertyName("zcbc")]
    public string Zcbc { get; set; } = "";

    /// <summary>
    /// 销售毛利率(%)
    /// </summary>
    [JsonPropertyName("xsml")]
    public string Xsml { get; set; } = "";

    /// <summary>
    /// 三项费用比重
    /// </summary>
    [JsonPropertyName("xxbz")]
    public string Xxbz { get; set; } = "";

    /// <summary>
    /// 非主营比重
    /// </summary>
    [JsonPropertyName("fzy")]
    public string Fzy { get; set; } = "";

    /// <summary>
    /// 主营利润比重
    /// </summary>
    [JsonPropertyName("zybz")]
    public string Zybz { get; set; } = "";

    /// <summary>
    /// 股息发放率(%)
    /// </summary>
    [JsonPropertyName("gxff")]
    public string Gxff { get; set; } = "";

    /// <summary>
    /// 投资收益率(%)
    /// </summary>
    [JsonPropertyName("tzsy")]
    public string Tzsy { get; set; } = "";

    /// <summary>
    /// 主营业务利润(元)
    /// </summary>
    [JsonPropertyName("zyyw")]
    public string Zyyw { get; set; } = "";

    /// <summary>
    /// 净资产收益率(%)
    /// </summary>
    [JsonPropertyName("jzsy")]
    public string Jzsy { get; set; } = "";

    /// <summary>
    /// 加权净资产收益率(%)
    /// </summary>
    [JsonPropertyName("jqjz")]
    public string Jqjz { get; set; } = "";

    /// <summary>
    /// 扣除非经常性损益后的净利润(元)
    /// </summary>
    [JsonPropertyName("kflr")]
    public string Kflr { get; set; } = "";

    /// <summary>
    /// 主营业务收入增长率(%)
    /// </summary>
    [JsonPropertyName("zysr")]
    public string Zysr { get; set; } = "";

    /// <summary>
    /// 净利润增长率(%)
    /// </summary>
    [JsonPropertyName("jlzz")]
    public string Jlzz { get; set; } = "";

    /// <summary>
    /// 净资产增长率(%)
    /// </summary>
    [JsonPropertyName("jzzz")]
    public string Jzzz { get; set; } = "";

    /// <summary>
    /// 总资产增长率(%)
    /// </summary>
    [JsonPropertyName("zzzz")]
    public string Zzzz { get; set; } = "";

    /// <summary>
    /// 应收账款周转率(次)
    /// </summary>
    [JsonPropertyName("yszz")]
    public string Yszz { get; set; } = "";

    /// <summary>
    /// 应收账款周转天数(天)
    /// </summary>
    [JsonPropertyName("yszzt")]
    public string Yszzt { get; set; } = "";

    /// <summary>
    /// 存货周转天数(天)
    /// </summary>
    [JsonPropertyName("chzz")]
    public string Chzz { get; set; } = "";

    /// <summary>
    /// 存货周转率(次)
    /// </summary>
    [JsonPropertyName("chzzl")]
    public string Chzzl { get; set; } = "";

    /// <summary>
    /// 固定资产周转率(次)
    /// </summary>
    [JsonPropertyName("gzzz")]
    public string Gzzz { get; set; } = "";

    /// <summary>
    /// 总资产周转率(次)
    /// </summary>
    [JsonPropertyName("zzzzl")]
    public string Zzzzl { get; set; } = "";

    /// <summary>
    /// 总资产周转天数(天)
    /// </summary>
    [JsonPropertyName("zzzzt")]
    public string Zzzzt { get; set; } = "";

    /// <summary>
    /// 流动资产周转率(次)
    /// </summary>
    [JsonPropertyName("ldzz")]
    public string Ldzz { get; set; } = "";

    /// <summary>
    /// 流动资产周转天数(天)
    /// </summary>
    [JsonPropertyName("ldzzt")]
    public string Ldzzt { get; set; } = "";

    /// <summary>
    /// 股东权益周转率(次)
    /// </summary>
    [JsonPropertyName("gdzz")]
    public string Gdzz { get; set; } = "";

    /// <summary>
    /// 流动比率
    /// </summary>
    [JsonPropertyName("ldbl")]
    public string Ldbl { get; set; } = "";

    /// <summary>
    /// 速动比率
    /// </summary>
    [JsonPropertyName("sdbl")]
    public string Sdbl { get; set; } = "";

    /// <summary>
    /// 现金比率(%)
    /// </summary>
    [JsonPropertyName("xjbl")]
    public string Xjbl { get; set; } = "";

    /// <summary>
    /// 利息支付倍数
    /// </summary>
    [JsonPropertyName("lxzf")]
    public string Lxzf { get; set; } = "";

    /// <summary>
    /// 长期债务与营运资金比率(%)
    /// </summary>
    [JsonPropertyName("zjbl")]
    public string Zjbl { get; set; } = "";

    /// <summary>
    /// 股东权益比率(%)
    /// </summary>
    [JsonPropertyName("gdqy")]
    public string Gdqy { get; set; } = "";

    /// <summary>
    /// 长期负债比率(%)
    /// </summary>
    [JsonPropertyName("cqfz")]
    public string Cqfz { get; set; } = "";

    /// <summary>
    /// 股东权益与固定资产比率(%)
    /// </summary>
    [JsonPropertyName("gdgd")]
    public string Gdgd { get; set; } = "";

    /// <summary>
    /// 负债与所有者权益比率(%)
    /// </summary>
    [JsonPropertyName("fzqy")]
    public string Fzqy { get; set; } = "";

    /// <summary>
    /// 长期资产与长期资金比率(%)
    /// </summary>
    [JsonPropertyName("zczjbl")]
    public string Zczjbl { get; set; } = "";

    /// <summary>
    /// 资本化比率(%)
    /// </summary>
    [JsonPropertyName("zblv")]
    public string Zblv { get; set; } = "";

    /// <summary>
    /// 固定资产净值率(%)
    /// </summary>
    [JsonPropertyName("gdzcjz")]
    public string Gdzcjz { get; set; } = "";

    /// <summary>
    /// 资本固定化比率(%)
    /// </summary>
    [JsonPropertyName("zbgdh")]
    public string Zbgdh { get; set; } = "";

    /// <summary>
    /// 产权比率(%)
    /// </summary>
    [JsonPropertyName("cqbl")]
    public string Cqbl { get; set; } = "";

    /// <summary>
    /// 清算价值比率(%)
    /// </summary>
    [JsonPropertyName("qxjzb")]
    public string Qxjzb { get; set; } = "";

    /// <summary>
    /// 固定资产比重(%)
    /// </summary>
    [JsonPropertyName("gdzcbz")]
    public string Gdzcbz { get; set; } = "";

    /// <summary>
    /// 资产负债率(%)
    /// </summary>
    [JsonPropertyName("zcfzl")]
    public string Zcfzl { get; set; } = "";

    /// <summary>
    /// 总资产(元)
    /// </summary>
    [JsonPropertyName("zzc")]
    public string Zzc { get; set; } = "";

    /// <summary>
    /// 经营现金净流量对销售收入比率(%)
    /// </summary>
    [JsonPropertyName("jyxj")]
    public string Jyxj { get; set; } = "";

    /// <summary>
    /// 资产的经营现金流量回报率(%)
    /// </summary>
    [JsonPropertyName("zcjyxj")]
    public string Zcjyxj { get; set; } = "";

    /// <summary>
    /// 经营现金净流量与净利润的比率(%)
    /// </summary>
    [JsonPropertyName("jylrb")]
    public string Jylrb { get; set; } = "";

    /// <summary>
    /// 经营现金净流量对负债比率(%)
    /// </summary>
    [JsonPropertyName("jyfzl")]
    public string Jyfzl { get; set; } = "";

    /// <summary>
    /// 现金流量比率(%)
    /// </summary>
    [JsonPropertyName("xjlbl")]
    public string Xjlbl { get; set; } = "";

    /// <summary>
    /// 短期股票投资(元)
    /// </summary>
    [JsonPropertyName("dqgptz")]
    public string Dqgptz { get; set; } = "";

    /// <summary>
    /// 短期债券投资(元)
    /// </summary>
    [JsonPropertyName("dqzctz")]
    public string Dqzctz { get; set; } = "";

    /// <summary>
    /// 短期其它经营性投资(元)
    /// </summary>
    [JsonPropertyName("dqjytz")]
    public string Dqjytz { get; set; } = "";

    /// <summary>
    /// 长期股票投资(元)
    /// </summary>
    [JsonPropertyName("qcgptz")]
    public string Qcgptz { get; set; } = "";

    /// <summary>
    /// 长期债券投资(元)
    /// </summary>
    [JsonPropertyName("cqzqtz")]
    public string Cqzqtz { get; set; } = "";

    /// <summary>
    /// 长期其它经营性投资(元)
    /// </summary>
    [JsonPropertyName("cqjyxtz")]
    public string Cqjyxtz { get; set; } = "";

    /// <summary>
    /// 1年以内应收帐款(元)
    /// </summary>
    [JsonPropertyName("yszk1")]
    public string Yszk1 { get; set; } = "";

    /// <summary>
    /// 1-2年以内应收帐款(元)
    /// </summary>
    [JsonPropertyName("yszk12")]
    public string Yszk12 { get; set; } = "";

    /// <summary>
    /// 2-3年以内应收帐款(元)
    /// </summary>
    [JsonPropertyName("yszk23")]
    public string Yszk23 { get; set; } = "";

    /// <summary>
    /// 3年以内应收帐款(元)
    /// </summary>
    [JsonPropertyName("yszk3")]
    public string Yszk3 { get; set; } = "";

    /// <summary>
    /// 1年以内预付货款(元)
    /// </summary>
    [JsonPropertyName("yfhk1")]
    public string Yfhk1 { get; set; } = "";

    /// <summary>
    /// 1-2年以内预付货款(元)
    /// </summary>
    [JsonPropertyName("yfhk12")]
    public string Yfhk12 { get; set; } = "";

    /// <summary>
    /// 2-3年以内预付货款(元)
    /// </summary>
    [JsonPropertyName("yfhk23")]
    public string Yfhk23 { get; set; } = "";

    /// <summary>
    /// 3年以内预付货款(元)
    /// </summary>
    [JsonPropertyName("yfhk3")]
    public string Yfhk3 { get; set; } = "";

    /// <summary>
    /// 1年以内其它应收款(元)
    /// </summary>
    [JsonPropertyName("ysk1")]
    public string Ysk1 { get; set; } = "";

    /// <summary>
    /// 1-2年以内其它应收款(元)
    /// </summary>
    [JsonPropertyName("ysk12")]
    public string Ysk12 { get; set; } = "";

    /// <summary>
    /// 2-3年以内其它应收款(元)
    /// </summary>
    [JsonPropertyName("ysk23")]
    public string Ysk23 { get; set; } = "";

    /// <summary>
    /// 3年以内其它应收款(元)
    /// </summary>
    [JsonPropertyName("ysk3")]
    public string Ysk3 { get; set; } = "";
}