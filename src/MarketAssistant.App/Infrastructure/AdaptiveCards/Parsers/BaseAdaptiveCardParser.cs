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
