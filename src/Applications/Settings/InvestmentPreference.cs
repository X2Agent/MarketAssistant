using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Settings;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RiskToleranceLevel
{
    [Description("保守型")]
    Conservative,
    [Description("平衡型")]
    Balanced,
    [Description("激进型")]
    Aggressive
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InvestmentHorizonType
{
    [Description("短期")]
    ShortTerm,
    [Description("中期")]
    MediumTerm,
    [Description("长期")]
    LongTerm
}

public class InvestmentPreference : INotifyPropertyChanged
{
    private RiskToleranceLevel _riskTolerance = RiskToleranceLevel.Balanced;
    /// <summary>
    /// 风险承受能力
    /// </summary>
    public RiskToleranceLevel RiskTolerance
    {
        get => _riskTolerance;
        set => SetProperty(ref _riskTolerance, value);
    }

    private InvestmentHorizonType _investmentHorizon = InvestmentHorizonType.MediumTerm;
    /// <summary>
    /// 投资期限
    /// </summary>
    public InvestmentHorizonType InvestmentHorizon
    {
        get => _investmentHorizon;
        set => SetProperty(ref _investmentHorizon, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
