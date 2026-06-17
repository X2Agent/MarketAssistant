using AdaptiveCards;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Reflection;

namespace MarketAssistant.Infrastructure.AdaptiveCards.Parsers;

public abstract class BaseAdaptiveCardParser<T> : IAdaptiveCardParser<T>
{
    public abstract AdaptiveCard Parse(T model);

    /// <summary>
    /// 必须包含的JSON属性键（用于快速筛选）
    /// </summary>
    protected abstract string[] RequiredKeys { get; }

    public bool TryParse(string json, out AdaptiveCard? card)
    {
        card = null;

        // 1. 快速预检查：必须包含所有关键属性名
        foreach (var key in RequiredKeys)
        {
            if (!json.Contains($"\"{key}\"", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            options.Converters.Add(new JsonStringEnumConverter());

            var model = JsonSerializer.Deserialize<T>(json, options);
            if (model != null && IsValid(model))
            {
                card = Parse(model);
                return true;
            }
        }
        catch
        {
            // Ignore deserialization errors
        }
        return false;
    }

    protected virtual bool IsValid(T model)
    {
        return true;
    }

    protected void AddHeader(IList<AdaptiveElement> container, string title, AdaptiveTextColor color)
    {
        container.Add(new AdaptiveTextBlock
        {
            Text = title,
            Size = AdaptiveTextSize.Medium,
            Weight = AdaptiveTextWeight.Bolder,
            Color = color
        });
    }

    protected void AddSectionHeader(IList<AdaptiveElement> container, string title)
    {
        container.Add(new AdaptiveTextBlock
        {
            Text = title,
            Weight = AdaptiveTextWeight.Bolder,
            Spacing = AdaptiveSpacing.Medium,
            IsSubtle = true
        });
    }

    protected void AddScoreRow(IList<AdaptiveElement> container, string label, string? score)
    {
        if (score == null) return;
        var colSet = new AdaptiveColumnSet { Spacing = AdaptiveSpacing.Small };
        colSet.Columns.Add(new AdaptiveColumn { Width = "auto", Items = { new AdaptiveTextBlock { Text = label } } });
        colSet.Columns.Add(new AdaptiveColumn { Width = "auto", Items = { new AdaptiveTextBlock { Text = score, Weight = AdaptiveTextWeight.Bolder } } });
        container.Add(colSet);
    }

    protected void AddScoreHeader(IList<AdaptiveElement> container, string label, string score)
    {
        var colSet = new AdaptiveColumnSet { Spacing = AdaptiveSpacing.Small };
        // 左侧：大号分数
        colSet.Columns.Add(new AdaptiveColumn
        {
            Width = "auto",
            Items = { new AdaptiveTextBlock { Text = score, Size = AdaptiveTextSize.ExtraLarge, Weight = AdaptiveTextWeight.Bolder, Color = AdaptiveTextColor.Accent } }
        });
        // 右侧：标签（底部对齐）
        colSet.Columns.Add(new AdaptiveColumn
        {
            Width = "stretch",
            VerticalContentAlignment = AdaptiveVerticalContentAlignment.Bottom,
            Items = { new AdaptiveTextBlock { Text = label, IsSubtle = true, Wrap = true } }
        });
        container.Add(colSet);
    }

    /// <summary>
    /// 统一的“策略/操作建议”看板：Emphasis 容器 + 大号彩色标题 + 正文 + 可选 FactSet。
    /// 各分析师的策略区一律走此构件，避免出现三四种重复布局。
    /// </summary>
    protected void AddStrategyBox(
        IList<AdaptiveElement> container,
        string title,
        AdaptiveTextColor titleColor,
        string? content = null,
        AdaptiveFactSet? facts = null,
        List<string>? bulletPoints = null)
    {
        var box = new AdaptiveContainer
        {
            Style = AdaptiveContainerStyle.Emphasis,
            Spacing = AdaptiveSpacing.Small
        };

        box.Items.Add(new AdaptiveTextBlock
        {
            Text = title,
            Weight = AdaptiveTextWeight.Bolder,
            Size = AdaptiveTextSize.Large,
            Color = titleColor
        });

        if (!string.IsNullOrEmpty(content))
        {
            box.Items.Add(new AdaptiveTextBlock { Text = content, Wrap = true });
        }

        if (bulletPoints != null && bulletPoints.Count > 0)
        {
            foreach (var point in bulletPoints)
            {
                box.Items.Add(new AdaptiveTextBlock { Text = "• " + point, Wrap = true, Weight = AdaptiveTextWeight.Bolder });
            }
        }

        if (facts != null && facts.Facts.Count > 0)
        {
            box.Items.Add(facts);
        }

        container.Add(box);
    }

    /// <summary>
    /// 统一的风险警告容器：Attention 样式 + ⚠️ 标题 + 正文 + 可选列表。
    /// 财务/基本面的容器实现与新闻/情绪的行内 ⚠️ 文本均改走此构件。
    /// </summary>
    protected void AddRiskBox(
        IList<AdaptiveElement> container,
        string title,
        string? content,
        List<string>? bulletPoints = null)
    {
        var box = new AdaptiveContainer
        {
            Style = AdaptiveContainerStyle.Attention,
            Spacing = AdaptiveSpacing.Medium
        };

        box.Items.Add(new AdaptiveTextBlock
        {
            Text = "⚠️ " + title,
            Weight = AdaptiveTextWeight.Bolder,
            Color = AdaptiveTextColor.Attention
        });

        if (!string.IsNullOrEmpty(content))
        {
            box.Items.Add(new AdaptiveTextBlock { Text = content, Wrap = true, Size = AdaptiveTextSize.Small });
        }

        if (bulletPoints != null && bulletPoints.Count > 0)
        {
            foreach (var point in bulletPoints)
            {
                box.Items.Add(new AdaptiveTextBlock { Text = "• " + point, Wrap = true, Size = AdaptiveTextSize.Small });
            }
        }

        container.Add(box);
    }

    /// <summary>
    /// 统一的“标题 + FactSet”section，保证 section 间距一致。
    /// </summary>
    protected void AddFactSection(IList<AdaptiveElement> container, string title, AdaptiveFactSet facts)
    {
        if (facts.Facts.Count == 0) return;
        AddSectionHeader(container, title);
        container.Add(facts);
    }

    protected void AddListSection(IList<AdaptiveElement> container, List<string>? list, string title)
    {
        if (list != null && list.Count > 0)
        {
            AddSectionHeader(container, title);
            foreach (var item in list)
            {
                container.Add(new AdaptiveTextBlock { Text = "• " + item, Wrap = true, Spacing = AdaptiveSpacing.None });
            }
        }
    }

    protected string GetEnumDescription(Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attribute = field?.GetCustomAttribute<DescriptionAttribute>();
        return attribute?.Description ?? value.ToString();
    }
}
