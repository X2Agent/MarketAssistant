using AdaptiveCards;
using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

public class CryptoMetricsCardParser : BaseAdaptiveCardParser<CryptoMetricsAnalysisResult>
{
    /// <summary>波动风险评分高于此值时追加风险提示</summary>
    private const float HighVolatilityRiskScore = 7;

    protected override string[] RequiredKeys => ["MarketCap", "Liquidity", "VolatilityRisk", "MarketStructure"];

    public override AdaptiveCard Parse(CryptoMetricsAnalysisResult model)
    {
        var card = new AdaptiveCard("1.5")
        {
            FallbackText = "项目指标分析：市值规模、流动性、波动风险与市场结构评估，请查看完整报告。",
            Speak = "项目指标分析已生成。"
        };
        AddHeader(card.Body, "项目指标分析", AdaptiveTextColor.Accent, "avares://MarketAssistant/Assets/Images/icon_grid.svg");

        var drawdownNote = string.IsNullOrWhiteSpace(model.VolatilityRisk.MaxDrawdown)
            ? null
            : $"最大回撤：{model.VolatilityRisk.MaxDrawdown}";

        // 四项指标两两并排：评分看板 + 说明文字
        (string Label, float? Score, string? Note)[] metrics =
        [
            ("市值规模", model.MarketCap.MarketCapScore, model.MarketCap.CirculatingAssessment),
            ("流动性", model.Liquidity.LiquidityScore, model.Liquidity.VolumeDistributionNotes),
            ("波动风险", model.VolatilityRisk.RiskScore, drawdownNote),
            ("市场结构", model.MarketStructure.StructureScore, model.MarketStructure.KeyObservations)
        ];
        foreach (var pair in metrics.Chunk(2))
        {
            var row = new AdaptiveColumnSet();
            foreach (var metric in pair)
            {
                AddMetricColumn(row, metric);
            }

            if (row.Columns.Count > 0)
            {
                card.Body.Add(row);
            }
        }

        if (model.VolatilityRisk.RiskScore > HighVolatilityRiskScore)
        {
            AddRiskBox(card.Body, "风险提示", "波动风险评分偏高，注意控制仓位与杠杆");
        }

        return card;
    }

    /// <summary>模型约定无数据时评分与说明都为 null，该指标整列省略。</summary>
    private void AddMetricColumn(AdaptiveColumnSet row, (string Label, float? Score, string? Note) metric)
    {
        if (metric.Score is null && string.IsNullOrWhiteSpace(metric.Note))
        {
            return;
        }

        var column = new AdaptiveColumn { Width = "50%" };
        if (metric.Score is { } score)
        {
            AddScoreHeader(column.Items, metric.Label, score.ToString("F1"));
        }
        else
        {
            AddSectionHeader(column.Items, metric.Label);
        }

        if (!string.IsNullOrWhiteSpace(metric.Note))
        {
            column.Items.Add(new AdaptiveTextBlock { Text = metric.Note, Wrap = true, Size = AdaptiveTextSize.Small });
        }

        row.Columns.Add(column);
    }
}
