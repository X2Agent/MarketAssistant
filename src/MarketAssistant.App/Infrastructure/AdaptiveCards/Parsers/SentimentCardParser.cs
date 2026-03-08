using AdaptiveCards;
using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

public class SentimentCardParser : BaseAdaptiveCardParser<MarketSentimentAnalysisResult>
{
    protected override string[] RequiredKeys => new[] { "SentimentAssessment", "CapitalFlowAnalysis", "BehaviorAnalysis" };

    protected override bool IsValid(MarketSentimentAnalysisResult model)
    {
        return model.SentimentAssessment != null;
    }

    public override AdaptiveCard Parse(MarketSentimentAnalysisResult model)
    {
        var summaryEmotion = model.SentimentAssessment?.DominantEmotion != null ? GetEnumDescription(model.SentimentAssessment.DominantEmotion) : "未知";
        var summaryScore = model.SentimentAssessment?.EmotionIntensityScore.ToString("F1") ?? "N/A";

        var card = new AdaptiveCard("1.5")
        {
            FallbackText = $"市场情绪分析：主导情绪{summaryEmotion} (强度 {summaryScore})，请查看完整报告。",
            Speak = "市场情绪分析已生成。"
        };
        AddHeader(card.Body, "🌡️ 市场情绪分析", AdaptiveTextColor.Accent);

        // 1. Sentiment & Capital Flow in 2 columns
        var topCols = new AdaptiveColumnSet();
        var leftCol = new AdaptiveColumn { Width = "50%" };
        var rightCol = new AdaptiveColumn { Width = "50%" };
        bool hasLeft = false, hasRight = false;

        if (model.SentimentAssessment != null)
        {
            hasLeft = true;
            var emotion = GetEnumDescription(model.SentimentAssessment.DominantEmotion);
            var score = model.SentimentAssessment.EmotionIntensityScore.ToString("F1");

            // 1. 情绪评分看板
            AddScoreHeader(leftCol.Items, $"主导情绪: {emotion}", score);

            // 2. 信心趋势描述 (加粗前置)
            if (!string.IsNullOrEmpty(model.SentimentAssessment.ConfidenceTrendDescription))
            {
                leftCol.Items.Add(new AdaptiveTextBlock { Text = model.SentimentAssessment.ConfidenceTrendDescription, Wrap = true, Weight = AdaptiveTextWeight.Bolder });
            }

            // 3. 详细指标
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("市场氛围", GetEnumDescription(model.SentimentAssessment.OverallAtmosphere)));
            facts.Facts.Add(new AdaptiveFact("氛围强度", model.SentimentAssessment.AtmosphereIntensityScore.ToString("F1")));
            facts.Facts.Add(new AdaptiveFact("投资者信心", GetEnumDescription(model.SentimentAssessment.InvestorConfidenceLevel)));
            if (!string.IsNullOrEmpty(model.SentimentAssessment.VIXLevel))
            {
                facts.Facts.Add(new AdaptiveFact("VIX/情绪指数", model.SentimentAssessment.VIXLevel));
            }
            leftCol.Items.Add(facts);
        }

        if (model.CapitalFlowAnalysis != null)
        {
            hasRight = true;
            // 1. 资金流向看板 (使用主力净额作为大数字，如果为空则用主力资金方向)
            var flowDir = GetEnumDescription(model.CapitalFlowAnalysis.MainCapitalFlow);
            var flowAmount = model.CapitalFlowAnalysis.MainCapitalAmount.HasValue
                ? (model.CapitalFlowAnalysis.MainCapitalAmount.Value / 100000000m).ToString("F1") + "亿" // 简化显示为亿，使用 decimal 后缀 m
                : flowDir;

            AddScoreHeader(rightCol.Items, "主力资金", flowAmount);

            // 2. 机构持仓描述 (加粗前置)
            if (!string.IsNullOrEmpty(model.CapitalFlowAnalysis.InstitutionPositionChange))
            {
                rightCol.Items.Add(new AdaptiveTextBlock { Text = model.CapitalFlowAnalysis.InstitutionPositionChange, Wrap = true, Weight = AdaptiveTextWeight.Bolder });
            }

            // 3. 详细指标
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("主力动向", flowDir));
            facts.Facts.Add(new AdaptiveFact("机构动向", GetEnumDescription(model.CapitalFlowAnalysis.InstitutionTrend)));
            facts.Facts.Add(new AdaptiveFact("北向资金", GetEnumDescription(model.CapitalFlowAnalysis.NorthboundCapitalFlow)));
            if (!string.IsNullOrEmpty(model.CapitalFlowAnalysis.MarginFinancingChange))
            {
                facts.Facts.Add(new AdaptiveFact("融资变化", model.CapitalFlowAnalysis.MarginFinancingChange));
            }
            if (!string.IsNullOrEmpty(model.CapitalFlowAnalysis.MarginTradingChange))
            {
                facts.Facts.Add(new AdaptiveFact("融券变化", model.CapitalFlowAnalysis.MarginTradingChange));
            }
            rightCol.Items.Add(facts);
        }

        if (hasLeft) topCols.Columns.Add(leftCol);
        if (hasRight) topCols.Columns.Add(rightCol);
        if (topCols.Columns.Count > 0) card.Body.Add(topCols);

        if (model.BehaviorAnalysis != null)
        {
            AddSectionHeader(card.Body, "行为分析");
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("行为偏差", GetEnumDescription(model.BehaviorAnalysis.MainBehaviorBias)));
            facts.Facts.Add(new AdaptiveFact("偏差严重度", model.BehaviorAnalysis.BiasSeverityScore.ToString("F1")));
            facts.Facts.Add(new AdaptiveFact("散户特征", GetEnumDescription(model.BehaviorAnalysis.RetailInvestorCharacteristics)));
            facts.Facts.Add(new AdaptiveFact("散户活跃度", model.BehaviorAnalysis.RetailActivityScore.ToString("F1")));
            facts.Facts.Add(new AdaptiveFact("机构一致性", GetEnumDescription(model.BehaviorAnalysis.InstitutionBehaviorConsistency)));
            facts.Facts.Add(new AdaptiveFact("风险偏好", GetEnumDescription(model.BehaviorAnalysis.RiskPreference)));
            card.Body.Add(facts);
        }

        if (model.ShortTermStrategy != null)
        {
            AddSectionHeader(card.Body, "短期策略");
            var recommendation = GetEnumDescription(model.ShortTermStrategy.OperationRecommendation);
            var color = recommendation.Contains("买入") || recommendation.Contains("做多") ? AdaptiveTextColor.Good : (recommendation.Contains("卖出") || recommendation.Contains("做空") ? AdaptiveTextColor.Attention : AdaptiveTextColor.Default);

            var container = new AdaptiveContainer { Style = AdaptiveContainerStyle.Emphasis, Spacing = AdaptiveSpacing.Small };

            // 策略看板
            container.Items.Add(new AdaptiveTextBlock
            {
                Text = $"建议: {recommendation}",
                Weight = AdaptiveTextWeight.Bolder,
                Size = AdaptiveTextSize.Large,
                Color = color
            });

            if (!string.IsNullOrEmpty(model.ShortTermStrategy.ShortTermOpportunities))
            {
                container.Items.Add(new AdaptiveTextBlock { Text = model.ShortTermStrategy.ShortTermOpportunities, Wrap = true });
            }

            if (!string.IsNullOrEmpty(model.ShortTermStrategy.PsychologicalTrapToAvoid))
            {
                container.Items.Add(new AdaptiveTextBlock { Text = $"⚠️ 心理陷阱: {model.ShortTermStrategy.PsychologicalTrapToAvoid}", Wrap = true, Color = AdaptiveTextColor.Attention, Size = AdaptiveTextSize.Small });
            }

            var facts = new AdaptiveFactSet();
            if (!string.IsNullOrEmpty(model.ShortTermStrategy.TargetPriceRange))
            {
                facts.Facts.Add(new AdaptiveFact("目标区间", model.ShortTermStrategy.TargetPriceRange));
            }
            if (!string.IsNullOrEmpty(model.ShortTermStrategy.StopLossPosition))
            {
                facts.Facts.Add(new AdaptiveFact("止损位置", model.ShortTermStrategy.StopLossPosition));
            }
            if (facts.Facts.Count > 0)
            {
                container.Items.Add(facts);
            }

            card.Body.Add(container);
        }

        return card;
    }
}
