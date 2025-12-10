using AdaptiveCards;
using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

public class TechnicalCardParser : BaseAdaptiveCardParser<TechnicalAnalysisResult>
{
    protected override string[] RequiredKeys => new[] { "PatternTrend", "PriceLevels", "Indicators" };

    protected override bool IsValid(TechnicalAnalysisResult model)
    {
        return model.PatternTrend != null;
    }

    public override AdaptiveCard Parse(TechnicalAnalysisResult model)
    {
        var summaryTrend = model.PatternTrend?.CurrentTrend != null ? GetEnumDescription(model.PatternTrend.CurrentTrend) : "未知";
        var summaryScore = model.PatternTrend?.TrendStrengthScore.ToString("F1") ?? "N/A";

        var card = new AdaptiveCard("1.5")
        {
            FallbackText = $"技术分析报告：当前趋势{summaryTrend} (强度 {summaryScore})，请查看完整报告。",
            Speak = "技术分析报告已生成。"
        };
        AddHeader(card.Body, "📈 技术分析报告", AdaptiveTextColor.Accent);

        // 1. Pattern & Price in 2 columns
        var topCols = new AdaptiveColumnSet();
        var leftCol = new AdaptiveColumn { Width = "50%" };
        var rightCol = new AdaptiveColumn { Width = "50%" };
        bool hasLeft = false, hasRight = false;

        if (model.PatternTrend != null)
        {
            hasLeft = true;
            var trend = GetEnumDescription(model.PatternTrend.CurrentTrend);
            var score = model.PatternTrend.TrendStrengthScore.ToString("F1");

            // 1. 趋势评分看板
            AddScoreHeader(leftCol.Items, $"趋势: {trend}", score);

            // 2. 关键形态描述 (加粗前置)
            if (!string.IsNullOrEmpty(model.PatternTrend.KeyPatterns))
            {
                leftCol.Items.Add(new AdaptiveTextBlock { Text = model.PatternTrend.KeyPatterns, Wrap = true, Weight = AdaptiveTextWeight.Bolder });
            }

            // 3. 详细指标
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("时间框架", GetEnumDescription(model.PatternTrend.TimeFrame)));
            facts.Facts.Add(new AdaptiveFact("周期一致性", model.PatternTrend.TimeFrameConsistencyScore.ToString("F1")));
            facts.Facts.Add(new AdaptiveFact("形态可靠性", model.PatternTrend.PatternReliabilityScore.ToString("F1")));
            leftCol.Items.Add(facts);
        }

        if (model.PriceLevels != null)
        {
            hasRight = true;
            // 1. 突破概率看板
            var breakoutScore = model.PriceLevels.BreakoutProbabilityScore.ToString("F1");
            var breakoutDir = GetEnumDescription(model.PriceLevels.BreakoutDirection);
            AddScoreHeader(rightCol.Items, $"突破: {breakoutDir}", breakoutScore);

            // 2. 价格区间 (使用 FactSet 保持整齐)
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("当前价格", model.PriceLevels.CurrentPrice.ToString("F2")));

            if (model.PriceLevels.SupportLevels != null && model.PriceLevels.SupportLevels.Count > 0)
            {
                facts.Facts.Add(new AdaptiveFact("支撑位", string.Join(", ", model.PriceLevels.SupportLevels.Select(x => x.ToString("F2")))));
            }

            if (model.PriceLevels.ResistanceLevels != null && model.PriceLevels.ResistanceLevels.Count > 0)
            {
                facts.Facts.Add(new AdaptiveFact("阻力位", string.Join(", ", model.PriceLevels.ResistanceLevels.Select(x => x.ToString("F2")))));
            }
            rightCol.Items.Add(facts);
        }

        if (hasLeft) topCols.Columns.Add(leftCol);
        if (hasRight) topCols.Columns.Add(rightCol);
        if (topCols.Columns.Count > 0) card.Body.Add(topCols);

        if (model.Indicators != null)
        {
            AddSectionHeader(card.Body, "技术指标");
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("成交量", GetEnumDescription(model.Indicators.VolumeStatus)));
            facts.Facts.Add(new AdaptiveFact("量价关系", GetEnumDescription(model.Indicators.PriceVolumeRelationship)));
            facts.Facts.Add(new AdaptiveFact("指标一致性", GetEnumDescription(model.Indicators.IndicatorConsistency)));
            card.Body.Add(facts);

            if (!string.IsNullOrEmpty(model.Indicators.TrendIndicatorSignals))
            {
                card.Body.Add(new AdaptiveTextBlock { Text = $"趋势信号: {model.Indicators.TrendIndicatorSignals}", Wrap = true });
            }
            if (!string.IsNullOrEmpty(model.Indicators.MomentumIndicatorSignals))
            {
                card.Body.Add(new AdaptiveTextBlock { Text = $"动量信号: {model.Indicators.MomentumIndicatorSignals}", Wrap = true });
            }
            if (!string.IsNullOrEmpty(model.Indicators.IndicatorSynergyDescription))
            {
                card.Body.Add(new AdaptiveTextBlock { Text = $"指标协同: {model.Indicators.IndicatorSynergyDescription}", Wrap = true, Size = AdaptiveTextSize.Small });
            }
        }

        if (model.Strategy != null)
        {
            AddSectionHeader(card.Body, "交易策略");
            var direction = GetEnumDescription(model.Strategy.OperationDirection);
            var color = direction.Contains("买入") || direction.Contains("做多") ? AdaptiveTextColor.Good : (direction.Contains("卖出") || direction.Contains("做空") ? AdaptiveTextColor.Attention : AdaptiveTextColor.Default);

            // 策略看板：方向 + 目标价
            var strategyContainer = new AdaptiveContainer { Style = AdaptiveContainerStyle.Emphasis, Spacing = AdaptiveSpacing.Small };
            strategyContainer.Items.Add(new AdaptiveTextBlock
            {
                Text = $"建议: {direction}",
                Weight = AdaptiveTextWeight.Bolder,
                Size = AdaptiveTextSize.Large,
                Color = color
            });

            strategyContainer.Items.Add(new AdaptiveTextBlock
            {
                Text = $"评级: {GetEnumDescription(model.Strategy.TechnicalRating)}",
                Weight = AdaptiveTextWeight.Bolder,
                Size = AdaptiveTextSize.Medium
            });

            var facts = new AdaptiveFactSet();
            if (model.Strategy.TargetPriceLow.HasValue && model.Strategy.TargetPriceHigh.HasValue)
            {
                facts.Facts.Add(new AdaptiveFact("目标价", $"{model.Strategy.TargetPriceLow.Value:F2} - {model.Strategy.TargetPriceHigh.Value:F2}"));
            }

            if (model.Strategy.StopLossPrice.HasValue)
            {
                facts.Facts.Add(new AdaptiveFact("止损位", model.Strategy.StopLossPrice.Value.ToString("F2")));
            }

            facts.Facts.Add(new AdaptiveFact("持仓周期", GetEnumDescription(model.Strategy.HoldingPeriod)));
            facts.Facts.Add(new AdaptiveFact("风险等级", GetEnumDescription(model.Strategy.RiskLevel)));

            strategyContainer.Items.Add(facts);
            card.Body.Add(strategyContainer);
        }

        return card;
    }
}
