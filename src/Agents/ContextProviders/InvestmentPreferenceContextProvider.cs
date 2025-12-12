using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.ContextProviders;

public class InvestmentPreferenceContextProvider : AIContextProvider
{
    private readonly InvestmentPreference _preference;
    private readonly ILogger? _logger;

    public InvestmentPreferenceContextProvider(InvestmentPreference preference, ILogger? logger = null)
    {
        _preference = preference;
        _logger = logger;
    }

    public override ValueTask<AIContext> InvokingAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 用户投资偏好");
        sb.AppendLine($"风险承受能力: {_preference.RiskTolerance.GetDescription()}");
        sb.AppendLine($"投资期限: {_preference.InvestmentHorizon.GetDescription()}");

        var content = sb.ToString();

        _logger?.LogDebug("Injecting investment preferences into context: {Preferences}", content.Replace("\n", ", "));

        return new ValueTask<AIContext>(new AIContext
        {
            Messages = [new ChatMessage(ChatRole.System, content) { AdditionalProperties = new AdditionalPropertiesDictionary() { ["IsInvestmentPreferenceProviderOutput"] = true } }]
        });
    }
}
