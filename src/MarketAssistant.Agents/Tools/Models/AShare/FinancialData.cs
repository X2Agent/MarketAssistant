using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

/// <summary>
/// 财务指标实体
/// </summary>
public class FinancialData
{
    /// <summary>
    /// 报告日期 yyyy-MM-dd
    /// </summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    /// <summary>
    /// 摊薄每股收益(元)
    /// </summary>
    [JsonPropertyName("tbmg")]
    public decimal? Tbmg { get; set; }

    /// <summary>
    /// 加权每股收益(元)
    /// </summary>
    [JsonPropertyName("jqmg")]
    public decimal? Jqmg { get; set; }

    /// <summary>
    /// 每股收益_调整后(元)
    /// </summary>
    [JsonPropertyName("mgsy")]
    public decimal? Mgsy { get; set; }

    /// <summary>
    /// 扣除非经常性损益后的每股收益(元)
    /// </summary>
    [JsonPropertyName("kfmg")]
    public decimal? Kfmg { get; set; }

    /// <summary>
    /// 每股净资产_调整后(元)
    /// </summary>
    [JsonPropertyName("mgjz")]
    public decimal? Mgjz { get; set; }

    /// <summary>
    /// 每股净资产_调整后(元)
    /// </summary>
    [JsonPropertyName("mgjzad")]
    public decimal? Mgjzad { get; set; }

    /// <summary>
    /// 每股经营性现金流(元)
    /// </summary>
    [JsonPropertyName("mgjy")]
    public decimal? Mgjy { get; set; }

    /// <summary>
    /// 每股资本公积金(元)
    /// </summary>
    [JsonPropertyName("mggjj")]
    public decimal? Mggjj { get; set; }

    /// <summary>
    /// 每股未分配利润(元)
    /// </summary>
    [JsonPropertyName("mgwly")]
    public decimal? Mgwly { get; set; }

    /// <summary>
    /// 总资产利润率(%)
    /// </summary>
    [JsonPropertyName("zclr")]
    public decimal? Zclr { get; set; }

    /// <summary>
    /// 主营业务利润率(%)
    /// </summary>
    [JsonPropertyName("zylr")]
    public decimal? Zylr { get; set; }

    /// <summary>
    /// 总资产净利润率(%)
    /// </summary>
    [JsonPropertyName("zzlr")]
    public decimal? Zzlr { get; set; }

    /// <summary>
    /// 成本费用利润率(%)
    /// </summary>
    [JsonPropertyName("cblr")]
    public decimal? Cblr { get; set; }

    /// <summary>
    /// 营业利润率(%)
    /// </summary>
    [JsonPropertyName("yylr")]
    public decimal? Yylr { get; set; }

    /// <summary>
    /// 主营业务成本率(%)
    /// </summary>
    [JsonPropertyName("zycb")]
    public decimal? Zycb { get; set; }

    /// <summary>
    /// 销售净利率(%)
    /// </summary>
    [JsonPropertyName("xsjl")]
    public decimal? Xsjl { get; set; }

    /// <summary>
    /// 股本报酬率(%)
    /// </summary>
    [JsonPropertyName("gbbc")]
    public decimal? Gbbc { get; set; }

    /// <summary>
    /// 净资产报酬率(%)
    /// </summary>
    [JsonPropertyName("jzbc")]
    public decimal? Jzbc { get; set; }

    /// <summary>
    /// 资产报酬率(%)
    /// </summary>
    [JsonPropertyName("zcbc")]
    public decimal? Zcbc { get; set; }

    /// <summary>
    /// 销售毛利率(%)
    /// </summary>
    [JsonPropertyName("xsml")]
    public decimal? Xsml { get; set; }

    /// <summary>
    /// 三项费用比重
    /// </summary>
    [JsonPropertyName("xxbz")]
    public decimal? Xxbz { get; set; }

    /// <summary>
    /// 非主营比重
    /// </summary>
    [JsonPropertyName("fzy")]
    public decimal? Fzy { get; set; }

    /// <summary>
    /// 主营利润比重
    /// </summary>
    [JsonPropertyName("zybz")]
    public decimal? Zybz { get; set; }

    /// <summary>
    /// 股息发放率(%)
    /// </summary>
    [JsonPropertyName("gxff")]
    public decimal? Gxff { get; set; }

    /// <summary>
    /// 投资收益率(%)
    /// </summary>
    [JsonPropertyName("tzsy")]
    public decimal? Tzsy { get; set; }

    /// <summary>
    /// 主营业务利润(元)
    /// </summary>
    [JsonPropertyName("zyyw")]
    public decimal? Zyyw { get; set; }

    /// <summary>
    /// 净资产收益率(%)
    /// </summary>
    [JsonPropertyName("jzsy")]
    public decimal? Jzsy { get; set; }

    /// <summary>
    /// 加权净资产收益率(%)
    /// </summary>
    [JsonPropertyName("jqjz")]
    public decimal? Jqjz { get; set; }

    /// <summary>
    /// 扣除非经常性损益后的净利润(元)
    /// </summary>
    [JsonPropertyName("kflr")]
    public decimal? Kflr { get; set; }

    /// <summary>
    /// 主营业务收入增长率(%)
    /// </summary>
    [JsonPropertyName("zysr")]
    public decimal? Zysr { get; set; }

    /// <summary>
    /// 净利润增长率(%)
    /// </summary>
    [JsonPropertyName("jlzz")]
    public decimal? Jlzz { get; set; }

    /// <summary>
    /// 净资产增长率(%)
    /// </summary>
    [JsonPropertyName("jzzz")]
    public decimal? Jzzz { get; set; }

    /// <summary>
    /// 总资产增长率(%)
    /// </summary>
    [JsonPropertyName("zzzz")]
    public decimal? Zzzz { get; set; }

    /// <summary>
    /// 应收账款周转率(次)
    /// </summary>
    [JsonPropertyName("yszz")]
    public decimal? Yszz { get; set; }

    /// <summary>
    /// 应收账款周转天数(天)
    /// </summary>
    [JsonPropertyName("yszzt")]
    public decimal? Yszzt { get; set; }

    /// <summary>
    /// 存货周转天数(天)
    /// </summary>
    [JsonPropertyName("chzz")]
    public decimal? Chzz { get; set; }

    /// <summary>
    /// 存货周转率(次)
    /// </summary>
    [JsonPropertyName("chzzl")]
    public decimal? Chzzl { get; set; }

    /// <summary>
    /// 固定资产周转率(次)
    /// </summary>
    [JsonPropertyName("gzzz")]
    public decimal? Gzzz { get; set; }

    /// <summary>
    /// 总资产周转率(次)
    /// </summary>
    [JsonPropertyName("zzzzl")]
    public decimal? Zzzzl { get; set; }

    /// <summary>
    /// 总资产周转天数(天)
    /// </summary>
    [JsonPropertyName("zzzzt")]
    public decimal? Zzzzt { get; set; }

    /// <summary>
    /// 流动资产周转率(次)
    /// </summary>
    [JsonPropertyName("ldzz")]
    public decimal? Ldzz { get; set; }

    /// <summary>
    /// 流动资产周转天数(天)
    /// </summary>
    [JsonPropertyName("ldzzt")]
    public decimal? Ldzzt { get; set; }

    /// <summary>
    /// 股东权益周转率(次)
    /// </summary>
    [JsonPropertyName("gdzz")]
    public decimal? Gdzz { get; set; }

    /// <summary>
    /// 流动比率
    /// </summary>
    [JsonPropertyName("ldbl")]
    public decimal? Ldbl { get; set; }

    /// <summary>
    /// 速动比率
    /// </summary>
    [JsonPropertyName("sdbl")]
    public decimal? Sdbl { get; set; }

    /// <summary>
    /// 现金比率(%)
    /// </summary>
    [JsonPropertyName("xjbl")]
    public decimal? Xjbl { get; set; }

    /// <summary>
    /// 利息支付倍数
    /// </summary>
    [JsonPropertyName("lxzf")]
    public decimal? Lxzf { get; set; }

    /// <summary>
    /// 长期债务与营运资金比率(%)
    /// </summary>
    [JsonPropertyName("zjbl")]
    public decimal? Zjbl { get; set; }

    /// <summary>
    /// 股东权益比率(%)
    /// </summary>
    [JsonPropertyName("gdqy")]
    public decimal? Gdqy { get; set; }

    /// <summary>
    /// 长期负债比率(%)
    /// </summary>
    [JsonPropertyName("cqfz")]
    public decimal? Cqfz { get; set; }

    /// <summary>
    /// 股东权益与固定资产比率(%)
    /// </summary>
    [JsonPropertyName("gdgd")]
    public decimal? Gdgd { get; set; }

    /// <summary>
    /// 负债与所有者权益比率(%)
    /// </summary>
    [JsonPropertyName("fzqy")]
    public decimal? Fzqy { get; set; }

    /// <summary>
    /// 长期资产与长期资金比率(%)
    /// </summary>
    [JsonPropertyName("zczjbl")]
    public decimal? Zczjbl { get; set; }

    /// <summary>
    /// 资本化比率(%)
    /// </summary>
    [JsonPropertyName("zblv")]
    public decimal? Zblv { get; set; }

    /// <summary>
    /// 固定资产净值率(%)
    /// </summary>
    [JsonPropertyName("gdzcjz")]
    public decimal? Gdzcjz { get; set; }

    /// <summary>
    /// 资本固定化比率(%)
    /// </summary>
    [JsonPropertyName("zbgdh")]
    public decimal? Zbgdh { get; set; }

    /// <summary>
    /// 产权比率(%)
    /// </summary>
    [JsonPropertyName("cqbl")]
    public decimal? Cqbl { get; set; }

    /// <summary>
    /// 清算价值比率(%)
    /// </summary>
    [JsonPropertyName("qxjzb")]
    public decimal? Qxjzb { get; set; }

    /// <summary>
    /// 固定资产比重(%)
    /// </summary>
    [JsonPropertyName("gdzcbz")]
    public decimal? Gdzcbz { get; set; }

    /// <summary>
    /// 资产负债率(%)
    /// </summary>
    [JsonPropertyName("zcfzl")]
    public decimal? Zcfzl { get; set; }

    /// <summary>
    /// 总资产(元)
    /// </summary>
    [JsonPropertyName("zzc")]
    public decimal? Zzc { get; set; }

    /// <summary>
    /// 经营现金净流量对销售收入比率(%)
    /// </summary>
    [JsonPropertyName("jyxj")]
    public decimal? Jyxj { get; set; }

    /// <summary>
    /// 资产的经营现金流量回报率(%)
    /// </summary>
    [JsonPropertyName("zcjyxj")]
    public decimal? Zcjyxj { get; set; }

    /// <summary>
    /// 经营现金净流量与净利润的比率(%)
    /// </summary>
    [JsonPropertyName("jylrb")]
    public decimal? Jylrb { get; set; }

    /// <summary>
    /// 经营现金净流量对负债比率(%)
    /// </summary>
    [JsonPropertyName("jyfzl")]
    public decimal? Jyfzl { get; set; }

    /// <summary>
    /// 现金流量比率(%)
    /// </summary>
    [JsonPropertyName("xjlbl")]
    public decimal? Xjlbl { get; set; }

    /// <summary>
    /// 短期股票投资(元)
    /// </summary>
    [JsonPropertyName("dqgptz")]
    public decimal? Dqgptz { get; set; }

    /// <summary>
    /// 短期债券投资(元)
    /// </summary>
    [JsonPropertyName("dqzctz")]
    public decimal? Dqzctz { get; set; }

    /// <summary>
    /// 短期其它经营性投资(元)
    /// </summary>
    [JsonPropertyName("dqjytz")]
    public decimal? Dqjytz { get; set; }

    /// <summary>
    /// 长期股票投资(元)
    /// </summary>
    [JsonPropertyName("qcgptz")]
    public decimal? Qcgptz { get; set; }

    /// <summary>
    /// 长期债券投资(元)
    /// </summary>
    [JsonPropertyName("cqzqtz")]
    public decimal? Cqzqtz { get; set; }

    /// <summary>
    /// 长期其它经营性投资(元)
    /// </summary>
    [JsonPropertyName("cqjyxtz")]
    public decimal? Cqjyxtz { get; set; }

    /// <summary>
    /// 1年以内应收帐款(元)
    /// </summary>
    [JsonPropertyName("yszk1")]
    public decimal? Yszk1 { get; set; }

    /// <summary>
    /// 1-2年以内应收帐款(元)
    /// </summary>
    [JsonPropertyName("yszk12")]
    public decimal? Yszk12 { get; set; }

    /// <summary>
    /// 2-3年以内应收帐款(元)
    /// </summary>
    [JsonPropertyName("yszk23")]
    public decimal? Yszk23 { get; set; }

    /// <summary>
    /// 3年以内应收帐款(元)
    /// </summary>
    [JsonPropertyName("yszk3")]
    public decimal? Yszk3 { get; set; }

    /// <summary>
    /// 1年以内预付货款(元)
    /// </summary>
    [JsonPropertyName("yfhk1")]
    public decimal? Yfhk1 { get; set; }

    /// <summary>
    /// 1-2年以内预付货款(元)
    /// </summary>
    [JsonPropertyName("yfhk12")]
    public decimal? Yfhk12 { get; set; }

    /// <summary>
    /// 2-3年以内预付货款(元)
    /// </summary>
    [JsonPropertyName("yfhk23")]
    public decimal? Yfhk23 { get; set; }

    /// <summary>
    /// 3年以内预付货款(元)
    /// </summary>
    [JsonPropertyName("yfhk3")]
    public decimal? Yfhk3 { get; set; }

    /// <summary>
    /// 1年以内其它应收款(元)
    /// </summary>
    [JsonPropertyName("ysk1")]
    public decimal? Ysk1 { get; set; }

    /// <summary>
    /// 1-2年以内其它应收款(元)
    /// </summary>
    [JsonPropertyName("ysk12")]
    public decimal? Ysk12 { get; set; }

    /// <summary>
    /// 2-3年以内其它应收款(元)
    /// </summary>
    [JsonPropertyName("ysk23")]
    public decimal? Ysk23 { get; set; }

    /// <summary>
    /// 3年以内其它应收款(元)
    /// </summary>
    [JsonPropertyName("ysk3")]
    public decimal? Ysk3 { get; set; }
}
