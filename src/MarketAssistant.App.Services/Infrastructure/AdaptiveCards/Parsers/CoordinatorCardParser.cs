using AdaptiveCards;
using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

public class CoordinatorCardParser : BaseAdaptiveCardParser<CoordinatorResult>
{
    protected override string[] RequiredKeys => new[] { "OverallScore", "InvestmentRating", "OperationSuggestions" };

    protected override bool IsValid(CoordinatorResult model)
    {
        return model.OperationSuggestions != null && model.OperationSuggestions.Count > 0;
    }

    public override AdaptiveCard Parse(CoordinatorResult model)
    {
        var summaryRating = GetEnumDescription(model.InvestmentRating);
        var summaryScore = model.OverallScore.ToString("F1");

        var card = new AdaptiveCard("1.5")
        {
            FallbackText = $"综合分析报告：{summaryRating} (评分 {summaryScore})，请查看完整报告。",
            Speak = "综合分析报告已生成。"
        };
        AddHeader(card.Body, "📑 综合分析报告", AdaptiveTextColor.Accent);

        // Investment Rating & Score
        var rating = GetEnumDescription(model.InvestmentRating);
        var score = model.OverallScore.ToString("F1");
        var color = rating.Contains("买入") ? AdaptiveTextColor.Good : (rating.Contains("卖出") ? AdaptiveTextColor.Attention : AdaptiveTextColor.Warning);

        // 1. 综合评分看板
        var scoreContainer = new AdaptiveContainer { Style = AdaptiveContainerStyle.Emphasis, Spacing = AdaptiveSpacing.Medium };
        var scoreCols = new AdaptiveColumnSet();

        // 左侧：大号评分 + 评级
        scoreCols.Columns.Add(new AdaptiveColumn
        {
            Width = "auto",
            Items = {
                new AdaptiveTextBlock { Text = score, Size = AdaptiveTextSize.ExtraLarge, Weight = AdaptiveTextWeight.Bolder, Color = AdaptiveTextColor.Accent },
                new AdaptiveTextBlock { Text = rating, Weight = AdaptiveTextWeight.Bolder, Color = color, Size = AdaptiveTextSize.Large }
            }
        });

        // 右侧：摘要描述
        if (!string.IsNullOrEmpty(model.Summary))
        {
            scoreCols.Columns.Add(new AdaptiveColumn
            {
                Width = "stretch",
                VerticalContentAlignment = AdaptiveVerticalContentAlignment.Center,
                Items = { new AdaptiveTextBlock { Text = model.Summary, Wrap = true, Weight = AdaptiveTextWeight.Bolder } }
            });
        }
        scoreContainer.Items.Add(scoreCols);
        card.Body.Add(scoreContainer);

        // Target Price & Time Horizon (FactSet)
        var facts = new AdaptiveFactSet();
        facts.Facts.Add(new AdaptiveFact("目标价格", model.TargetPrice));
        if (!string.IsNullOrEmpty(model.PriceChangeExpectation))
        {
            facts.Facts.Add(new AdaptiveFact("预期涨幅", model.PriceChangeExpectation));
        }
        facts.Facts.Add(new AdaptiveFact("投资周期", model.TimeHorizonDescription));
        facts.Facts.Add(new AdaptiveFact("置信度", model.ConfidencePercentage.ToString("F0") + "%"));
        facts.Facts.Add(new AdaptiveFact("风险等级", GetEnumDescription(model.RiskLevel)));
        card.Body.Add(facts);

        // Dimension Scores
        if (model.DimensionScores != null)
        {
            AddSectionHeader(card.Body, "维度评分");
            var scoreFacts = new AdaptiveFactSet();
            scoreFacts.Facts.Add(new AdaptiveFact("基本面", model.DimensionScores.Fundamental.ToString("F1")));
            scoreFacts.Facts.Add(new AdaptiveFact("技术面", model.DimensionScores.Technical.ToString("F1")));
            scoreFacts.Facts.Add(new AdaptiveFact("财务面", model.DimensionScores.Financial.ToString("F1")));
            scoreFacts.Facts.Add(new AdaptiveFact("市场情绪", model.DimensionScores.Sentiment.ToString("F1")));
            scoreFacts.Facts.Add(new AdaptiveFact("新闻事件", model.DimensionScores.News.ToString("F1")));
            card.Body.Add(scoreFacts);
        }

        // Highlights & Risks in 2 columns
        var listCols = new AdaptiveColumnSet();
        var leftCol = new AdaptiveColumn { Width = "50%" };
        var rightCol = new AdaptiveColumn { Width = "50%" };
        bool hasLeft = false, hasRight = false;

        if (model.InvestmentHighlights != null && model.InvestmentHighlights.Count > 0)
        {
            hasLeft = true;
            AddListSection(leftCol.Items, model.InvestmentHighlights, "投资亮点");
        }

        if (model.RiskFactors != null && model.RiskFactors.Count > 0)
        {
            hasRight = true;
            AddListSection(rightCol.Items, model.RiskFactors, "风险因素");
        }

        if (hasLeft) listCols.Columns.Add(leftCol);
        if (hasRight) listCols.Columns.Add(rightCol);
        if (listCols.Columns.Count > 0) card.Body.Add(listCols);

        // Operation Suggestions
        if (model.OperationSuggestions != null && model.OperationSuggestions.Count > 0)
        {
            AddSectionHeader(card.Body, "操作建议");
            var suggestionContainer = new AdaptiveContainer { Style = AdaptiveContainerStyle.Emphasis, Spacing = AdaptiveSpacing.Small };
            foreach (var item in model.OperationSuggestions)
            {
                suggestionContainer.Items.Add(new AdaptiveTextBlock { Text = "• " + item, Wrap = true, Weight = AdaptiveTextWeight.Bolder });
            }
            card.Body.Add(suggestionContainer);
        }

        // Consensus & Disagreement
        if (!string.IsNullOrEmpty(model.ConsensusAnalysis))
        {
            AddSectionHeader(card.Body, "核心共识");
            card.Body.Add(new AdaptiveTextBlock { Text = model.ConsensusAnalysis, Wrap = true });
        }

        if (!string.IsNullOrEmpty(model.DisagreementAnalysis))
        {
            AddSectionHeader(card.Body, "分歧与判断");
            card.Body.Add(new AdaptiveTextBlock { Text = model.DisagreementAnalysis, Wrap = true });
        }

        // Key Indicators
        if (model.KeyIndicators != null && model.KeyIndicators.Count > 0)
        {
            AddSectionHeader(card.Body, "关键指标");
            var indicatorFacts = new AdaptiveFactSet();
            foreach (var indicator in model.KeyIndicators.Take(5)) // Limit to top 5 to avoid too long card
            {
                indicatorFacts.Facts.Add(new AdaptiveFact(indicator.Name, $"{indicator.Value} ({indicator.Signal})"));
            }
            card.Body.Add(indicatorFacts);
        }

        return card;
    }
}
