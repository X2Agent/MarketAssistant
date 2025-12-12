using AdaptiveCards;
using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

public class NewsCardParser : BaseAdaptiveCardParser<NewsEventAnalysisResult>
{
    protected override string[] RequiredKeys => new[] { "EventAnalysis", "ImpactEvaluation", "InvestmentGuidance" };

    protected override bool IsValid(NewsEventAnalysisResult model)
    {
        return model.EventAnalysis != null && !string.IsNullOrEmpty(model.EventAnalysis.EventSummary);
    }

    public override AdaptiveCard Parse(NewsEventAnalysisResult model)
    {
        var summaryEvent = !string.IsNullOrEmpty(model.EventAnalysis?.EventSummary)
            ? (model.EventAnalysis.EventSummary.Length > 30 ? model.EventAnalysis.EventSummary.Substring(0, 30) + "..." : model.EventAnalysis.EventSummary)
            : "未知事件";
        var summaryNature = model.EventAnalysis?.EventNature != null ? GetEnumDescription(model.EventAnalysis.EventNature) : "未知";

        var card = new AdaptiveCard("1.5")
        {
            FallbackText = $"新闻事件分析：[{summaryNature}] {summaryEvent}，请查看完整报告。",
            Speak = "新闻事件分析已生成。"
        };
        AddHeader(card.Body, "📰 新闻事件分析", AdaptiveTextColor.Accent);

        // 1. Event & Impact in 2 columns
        var topCols = new AdaptiveColumnSet();
        var leftCol = new AdaptiveColumn { Width = "50%" };
        var rightCol = new AdaptiveColumn { Width = "50%" };
        bool hasLeft = false, hasRight = false;

        if (model.EventAnalysis != null)
        {
            hasLeft = true;
            var type = GetEnumDescription(model.EventAnalysis.EventType);
            var nature = GetEnumDescription(model.EventAnalysis.EventNature);
            var color = nature.Contains("利好") ? AdaptiveTextColor.Good : (nature.Contains("利空") ? AdaptiveTextColor.Attention : AdaptiveTextColor.Default);

            // 1. 标题加粗
            leftCol.Items.Add(new AdaptiveTextBlock
            {
                Text = $"[{type}] {model.EventAnalysis.EventSummary}",
                Weight = AdaptiveTextWeight.Bolder,
                Wrap = true,
                Size = AdaptiveTextSize.Medium
            });

            // 2. 重要性评分看板
            AddScoreHeader(leftCol.Items, "重要性", model.EventAnalysis.ImportanceScore.ToString("F1"));

            // 3. 详细指标
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("事件性质", nature));
            facts.Facts.Add(new AdaptiveFact("可信度", model.EventAnalysis.CredibilityScore.ToString("F1")));
            facts.Facts.Add(new AdaptiveFact("信息来源", GetEnumDescription(model.EventAnalysis.InformationSource)));
            leftCol.Items.Add(facts);
        }

        if (model.ImpactEvaluation != null)
        {
            hasRight = true;
            // 1. 影响范围看板 (如果影响范围太长，可以考虑用持续时间或者其他短词)
            // 这里我们用“基本面影响”作为右侧看板，因为它通常是“正面/负面”比较短
            var impact = GetEnumDescription(model.ImpactEvaluation.FundamentalImpact);
            AddScoreHeader(rightCol.Items, "基本面影响", impact);

            // 2. 逻辑描述 (加粗前置)
            if (!string.IsNullOrEmpty(model.ImpactEvaluation.FundamentalImpactLogic))
            {
                rightCol.Items.Add(new AdaptiveTextBlock { Text = model.ImpactEvaluation.FundamentalImpactLogic, Wrap = true, Weight = AdaptiveTextWeight.Bolder });
            }

            // 3. 详细指标
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("基本面影响分", model.ImpactEvaluation.FundamentalImpactScore.ToString("F1")));
            facts.Facts.Add(new AdaptiveFact("情绪影响", GetEnumDescription(model.ImpactEvaluation.SentimentImpact)));
            facts.Facts.Add(new AdaptiveFact("情绪强度分", model.ImpactEvaluation.SentimentIntensityScore.ToString("F1")));
            facts.Facts.Add(new AdaptiveFact("影响范围", GetEnumDescription(model.ImpactEvaluation.ImpactScope)));
            facts.Facts.Add(new AdaptiveFact("持续时间", GetEnumDescription(model.ImpactEvaluation.ImpactDuration)));
            rightCol.Items.Add(facts);

            if (!string.IsNullOrEmpty(model.ImpactEvaluation.SentimentChangeExpectation))
            {
                rightCol.Items.Add(new AdaptiveTextBlock { Text = $"情绪预期: {model.ImpactEvaluation.SentimentChangeExpectation}", Wrap = true, Size = AdaptiveTextSize.Small });
            }

            if (!string.IsNullOrEmpty(model.ImpactEvaluation.CapitalScaleEstimate))
            {
                rightCol.Items.Add(new AdaptiveTextBlock { Text = $"资金预估: {model.ImpactEvaluation.CapitalScaleEstimate}", Wrap = true, Size = AdaptiveTextSize.Small });
            }
        }

        if (hasLeft) topCols.Columns.Add(leftCol);
        if (hasRight) topCols.Columns.Add(rightCol);
        if (topCols.Columns.Count > 0) card.Body.Add(topCols);

        if (model.InvestmentGuidance != null)
        {
            AddSectionHeader(card.Body, "投资启示");
            var strategy = GetEnumDescription(model.InvestmentGuidance.ResponseStrategy);
            var color = strategy.Contains("买入") || strategy.Contains("做多") ? AdaptiveTextColor.Good : (strategy.Contains("卖出") || strategy.Contains("做空") ? AdaptiveTextColor.Attention : AdaptiveTextColor.Default);

            var container = new AdaptiveContainer { Style = AdaptiveContainerStyle.Emphasis, Spacing = AdaptiveSpacing.Small };

            // 策略看板
            container.Items.Add(new AdaptiveTextBlock
            {
                Text = $"策略: {strategy}",
                Weight = AdaptiveTextWeight.Bolder,
                Size = AdaptiveTextSize.Large,
                Color = color
            });

            if (!string.IsNullOrEmpty(model.InvestmentGuidance.CoreInvestmentLogic))
            {
                container.Items.Add(new AdaptiveTextBlock { Text = model.InvestmentGuidance.CoreInvestmentLogic, Wrap = true });
            }

            if (!string.IsNullOrEmpty(model.InvestmentGuidance.SpecificActionAdvice))
            {
                container.Items.Add(new AdaptiveTextBlock { Text = $"建议: {model.InvestmentGuidance.SpecificActionAdvice}", Wrap = true });
            }

            if (!string.IsNullOrEmpty(model.InvestmentGuidance.KeyRiskAlert))
            {
                container.Items.Add(new AdaptiveTextBlock { Text = $"⚠️ {model.InvestmentGuidance.KeyRiskAlert}", Wrap = true, Color = AdaptiveTextColor.Attention, Weight = AdaptiveTextWeight.Bolder });
            }

            if (model.InvestmentGuidance.FocusPoints != null && model.InvestmentGuidance.FocusPoints.Count > 0)
            {
                container.Items.Add(new AdaptiveTextBlock { Text = "关注重点:", Weight = AdaptiveTextWeight.Bolder, Size = AdaptiveTextSize.Small });
                foreach (var point in model.InvestmentGuidance.FocusPoints)
                {
                    container.Items.Add(new AdaptiveTextBlock { Text = $"• {point}", Wrap = true, Size = AdaptiveTextSize.Small });
                }
            }
            card.Body.Add(container);
        }

        return card;
    }
}
