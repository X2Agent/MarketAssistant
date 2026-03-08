using AdaptiveCards;
using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

public class FinancialCardParser : BaseAdaptiveCardParser<FinancialAnalysisResult>
{
    protected override string[] RequiredKeys => new[] { "HealthAssessment", "ProfitQuality", "CashFlow" };

    protected override bool IsValid(FinancialAnalysisResult model)
    {
        return model.HealthAssessment != null;
    }

    public override AdaptiveCard Parse(FinancialAnalysisResult model)
    {
        var summaryScore = model.HealthAssessment?.SolvencyScore.ToString("F1") ?? "N/A";
        var summaryInsight = !string.IsNullOrEmpty(model.HealthAssessment?.CoreInsight)
            ? (model.HealthAssessment.CoreInsight.Length > 20 ? model.HealthAssessment.CoreInsight.Substring(0, 20) + "..." : model.HealthAssessment.CoreInsight)
            : "暂无核心观点";

        var card = new AdaptiveCard("1.5")
        {
            FallbackText = $"财务分析报告：偿债评分 {summaryScore}，观点：{summaryInsight}，请查看完整报告。",
            Speak = "财务分析报告已生成。"
        };
        AddHeader(card.Body, "💰 财务分析报告", AdaptiveTextColor.Good);

        // 使用两列布局展示核心指标
        var metricsCols = new AdaptiveColumnSet();
        var leftCol = new AdaptiveColumn { Width = "50%" };
        var rightCol = new AdaptiveColumn { Width = "50%" };
        bool hasLeft = false, hasRight = false;

        if (model.HealthAssessment != null)
        {
            hasLeft = true;
            var score = model.HealthAssessment.SolvencyScore.ToString("F1");

            // 1. 偿债评分看板
            AddScoreHeader(leftCol.Items, "偿债能力", score);

            // 2. 核心观点 (加粗前置)
            if (!string.IsNullOrEmpty(model.HealthAssessment.CoreInsight))
            {
                leftCol.Items.Add(new AdaptiveTextBlock { Text = model.HealthAssessment.CoreInsight, Wrap = true, Weight = AdaptiveTextWeight.Bolder });
            }

            // 3. 详细指标
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("流动比率", model.HealthAssessment.CurrentRatio?.ToString("F2") ?? "N/A"));
            facts.Facts.Add(new AdaptiveFact("速动比率", model.HealthAssessment.QuickRatio?.ToString("F2") ?? "N/A"));
            facts.Facts.Add(new AdaptiveFact("资产负债率", model.HealthAssessment.DebtRatio?.ToString("F2") + "%"));
            facts.Facts.Add(new AdaptiveFact("负债率趋势", GetEnumDescription(model.HealthAssessment.DebtRatioTrend)));
            leftCol.Items.Add(facts);

            if (!string.IsNullOrEmpty(model.HealthAssessment.SolvencyAssessment))
            {
                leftCol.Items.Add(new AdaptiveTextBlock { Text = model.HealthAssessment.SolvencyAssessment, Wrap = true, Size = AdaptiveTextSize.Small });
            }
        }

        if (model.ProfitQuality != null)
        {
            hasRight = true;
            // 1. 盈利质量看板 (这里没有直接的评分，可以用ROE作为大数字展示)
            var roe = model.ProfitQuality.ROE?.ToString("F2") + "%";
            AddScoreHeader(rightCol.Items, "ROE", roe);

            // 2. 可持续性描述 (加粗前置)
            if (!string.IsNullOrEmpty(model.ProfitQuality.ProfitSustainability))
            {
                rightCol.Items.Add(new AdaptiveTextBlock { Text = model.ProfitQuality.ProfitSustainability, Wrap = true, Weight = AdaptiveTextWeight.Bolder });
            }

            // 3. 详细指标
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("ROA", model.ProfitQuality.ROA?.ToString("F2") + "%"));
            facts.Facts.Add(new AdaptiveFact("毛利率", model.ProfitQuality.GrossMargin?.ToString("F2") + "%"));
            facts.Facts.Add(new AdaptiveFact("净利率", model.ProfitQuality.NetMargin?.ToString("F2") + "%"));
            facts.Facts.Add(new AdaptiveFact("净利趋势", GetEnumDescription(model.ProfitQuality.NetMarginTrend)));
            rightCol.Items.Add(facts);
        }

        if (hasLeft) metricsCols.Columns.Add(leftCol);
        if (hasRight) metricsCols.Columns.Add(rightCol);
        if (metricsCols.Columns.Count > 0) card.Body.Add(metricsCols);

        if (model.CashFlow != null)
        {
            AddSectionHeader(card.Body, "现金流评估");
            var facts = new AdaptiveFactSet();
            if (model.CashFlow.OperatingCashFlow.HasValue)
            {
                facts.Facts.Add(new AdaptiveFact("经营现金流", model.CashFlow.OperatingCashFlow.Value.ToString("N0")));
            }
            facts.Facts.Add(new AdaptiveFact("现金流/净利", model.CashFlow.CashFlowToNetIncomeRatio?.ToString("F2") ?? "N/A"));
            facts.Facts.Add(new AdaptiveFact("自由现金流", GetEnumDescription(model.CashFlow.FreeCashFlowStatus)));
            facts.Facts.Add(new AdaptiveFact("现金转换周期", model.CashFlow.CashConversionCycle?.ToString() ?? "N/A"));
            card.Body.Add(facts);

            if (!string.IsNullOrEmpty(model.CashFlow.EfficiencyDescription))
            {
                card.Body.Add(new AdaptiveTextBlock { Text = model.CashFlow.EfficiencyDescription, Wrap = true });
            }
        }

        if (model.RiskWarning != null)
        {
            var warning = model.RiskWarning.FraudRiskRationale;
            if (!string.IsNullOrEmpty(warning))
            {
                var container = new AdaptiveContainer { Style = AdaptiveContainerStyle.Attention, Spacing = AdaptiveSpacing.Medium };
                container.Items.Add(new AdaptiveTextBlock { Text = "⚠️ 风险预警", Weight = AdaptiveTextWeight.Bolder, Color = AdaptiveTextColor.Attention });
                container.Items.Add(new AdaptiveTextBlock { Text = warning, Wrap = true });

                if (model.RiskWarning.KeyRiskIndicators != null && model.RiskWarning.KeyRiskIndicators.Count > 0)
                {
                    foreach (var indicator in model.RiskWarning.KeyRiskIndicators)
                    {
                        container.Items.Add(new AdaptiveTextBlock { Text = $"• {indicator}", Wrap = true, Size = AdaptiveTextSize.Small });
                    }
                }

                if (model.RiskWarning.MonitoringPoints != null && model.RiskWarning.MonitoringPoints.Count > 0)
                {
                    container.Items.Add(new AdaptiveTextBlock { Text = "建议关注:", Weight = AdaptiveTextWeight.Bolder, Size = AdaptiveTextSize.Small });
                    foreach (var point in model.RiskWarning.MonitoringPoints)
                    {
                        container.Items.Add(new AdaptiveTextBlock { Text = $"• {point}", Wrap = true, Size = AdaptiveTextSize.Small });
                    }
                }

                card.Body.Add(container);
            }
        }

        return card;
    }
}
