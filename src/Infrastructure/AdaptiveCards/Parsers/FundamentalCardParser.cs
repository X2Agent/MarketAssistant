using AdaptiveCards;
using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

public class FundamentalCardParser : BaseAdaptiveCardParser<FundamentalAnalysisResult>
{
    protected override string[] RequiredKeys => new[] { "BasicInfo", "Fundamentals", "Competition" };

    protected override bool IsValid(FundamentalAnalysisResult model)
    {
        return model.BasicInfo != null && !string.IsNullOrEmpty(model.BasicInfo.Symbol);
    }

    public override AdaptiveCard Parse(FundamentalAnalysisResult model)
    {
        var summaryName = model.BasicInfo?.Name ?? "未知股票";
        var summaryRating = model.GrowthValue?.InvestmentRating != null ? GetEnumDescription(model.GrowthValue.InvestmentRating) : "暂无评级";

        var card = new AdaptiveCard("1.5")
        {
            FallbackText = $"基本面分析：{summaryName} - 评级：{summaryRating}，请查看完整报告。",
            Speak = "基本面分析已生成。"
        };
        AddHeader(card.Body, "📊 基本面分析", AdaptiveTextColor.Accent);

        if (model.BasicInfo != null)
        {
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("股票", $"{model.BasicInfo.Name} ({model.BasicInfo.Symbol})"));
            facts.Facts.Add(new AdaptiveFact("当前价格", model.BasicInfo.CurrentPrice.ToString("F2")));
            if (model.BasicInfo.DailyChangePercent != 0)
            {
                facts.Facts.Add(new AdaptiveFact("涨跌幅", $"{model.BasicInfo.DailyChangePercent:F2}%"));
            }
            card.Body.Add(facts);
        }

        // 1. Fundamentals & Competition in 2 columns
        var topCols = new AdaptiveColumnSet();
        var leftCol = new AdaptiveColumn { Width = "50%" };
        var rightCol = new AdaptiveColumn { Width = "50%" };
        bool hasLeft = false, hasRight = false;

        if (model.Fundamentals != null)
        {
            hasLeft = true;
            // 1. 评分大字展示
            AddScoreHeader(leftCol.Items, "业务质量", model.Fundamentals.BusinessQualityScore.ToString("F1"));

            // 2. 核心业务描述
            leftCol.Items.Add(new AdaptiveTextBlock { Text = model.Fundamentals.CoreBusiness ?? "暂无数据", Wrap = true, Weight = AdaptiveTextWeight.Bolder });

            // 3. 详细指标列表 (回归 FactSet 以保证对齐)
            var facts = new AdaptiveFactSet();
            if (!string.IsNullOrEmpty(model.Fundamentals.Industry))
            {
                facts.Facts.Add(new AdaptiveFact("所属行业", model.Fundamentals.Industry));
            }
            facts.Facts.Add(new AdaptiveFact("盈利趋势", GetEnumDescription(model.Fundamentals.ProfitabilityTrend)));
            facts.Facts.Add(new AdaptiveFact("现金流状态", GetEnumDescription(model.Fundamentals.CashFlowStatus)));
            leftCol.Items.Add(facts);

            if (!string.IsNullOrEmpty(model.Fundamentals.ProfitabilityOverview))
            {
                leftCol.Items.Add(new AdaptiveTextBlock { Text = model.Fundamentals.ProfitabilityOverview, Wrap = true, IsSubtle = true, Size = AdaptiveTextSize.Small });
            }
        }

        if (model.Competition != null)
        {
            hasRight = true;
            // 1. 评分大字展示
            AddScoreHeader(rightCol.Items, "竞争力", model.Competition.CompetenceStrengthScore.ToString("F1"));

            // 2. 核心优势描述
            rightCol.Items.Add(new AdaptiveTextBlock { Text = model.Competition.CoreCompetence ?? "N/A", Wrap = true, Weight = AdaptiveTextWeight.Bolder });

            // 3. 详细指标列表
            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("市场地位", GetEnumDescription(model.Competition.MarketPosition)));
            facts.Facts.Add(new AdaptiveFact("生命周期", GetEnumDescription(model.Competition.IndustryLifecycle)));
            facts.Facts.Add(new AdaptiveFact("竞争壁垒", GetEnumDescription(model.Competition.BarrierLevel)));
            rightCol.Items.Add(facts);

            if (!string.IsNullOrEmpty(model.Competition.BarrierDescription))
            {
                rightCol.Items.Add(new AdaptiveTextBlock { Text = $"壁垒: {model.Competition.BarrierDescription}", Wrap = true, IsSubtle = true, Size = AdaptiveTextSize.Small });
            }
        }

        if (hasLeft) topCols.Columns.Add(leftCol);
        if (hasRight) topCols.Columns.Add(rightCol);
        if (topCols.Columns.Count > 0) card.Body.Add(topCols);

        if (model.GrowthValue != null)
        {
            AddSectionHeader(card.Body, "投资评级");
            var rating = GetEnumDescription(model.GrowthValue.InvestmentRating);
            var color = rating.Contains("买入") ? AdaptiveTextColor.Good : AdaptiveTextColor.Default;

            card.Body.Add(new AdaptiveTextBlock
            {
                Text = rating,
                Weight = AdaptiveTextWeight.Bolder,
                Size = AdaptiveTextSize.Medium,
                Color = color
            });

            var facts = new AdaptiveFactSet();
            facts.Facts.Add(new AdaptiveFact("增长持续性", model.GrowthValue.GrowthSustainabilityScore.ToString("F1")));
            card.Body.Add(facts);

            if (!string.IsNullOrEmpty(model.GrowthValue.ValuationDescription))
            {
                card.Body.Add(new AdaptiveTextBlock { Text = $"估值评估: {model.GrowthValue.ValuationDescription}", Wrap = true });
            }

            if (!string.IsNullOrEmpty(model.GrowthValue.ValuationTarget))
            {
                card.Body.Add(new AdaptiveTextBlock { Text = $"目标: {model.GrowthValue.ValuationTarget}", Wrap = true });
            }

            if (!string.IsNullOrEmpty(model.GrowthValue.GrowthDrivers))
            {
                AddSectionHeader(card.Body, "增长驱动");
                card.Body.Add(new AdaptiveTextBlock { Text = model.GrowthValue.GrowthDrivers, Wrap = true });
            }

            if (!string.IsNullOrEmpty(model.GrowthValue.KeyRisk))
            {
                var container = new AdaptiveContainer { Style = AdaptiveContainerStyle.Attention, Spacing = AdaptiveSpacing.Small };
                container.Items.Add(new AdaptiveTextBlock { Text = "⚠️ 关键风险", Weight = AdaptiveTextWeight.Bolder, Color = AdaptiveTextColor.Attention });
                container.Items.Add(new AdaptiveTextBlock { Text = model.GrowthValue.KeyRisk, Wrap = true, Size = AdaptiveTextSize.Small });
                card.Body.Add(container);
            }
        }

        return card;
    }

    private void AddKeyValueRow(IList<AdaptiveElement> container, string key, string value, bool isBold = false)
    {
        var row = new AdaptiveColumnSet { Spacing = AdaptiveSpacing.Small };
        row.Columns.Add(new AdaptiveColumn { Width = "auto", Items = { new AdaptiveTextBlock { Text = key, IsSubtle = true } } });
        row.Columns.Add(new AdaptiveColumn { Width = "stretch", Items = { new AdaptiveTextBlock { Text = value, Weight = isBold ? AdaptiveTextWeight.Bolder : AdaptiveTextWeight.Default, Wrap = true } } });
        container.Add(row);
    }
}