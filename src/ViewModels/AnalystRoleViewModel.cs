using CommunityToolkit.Mvvm.ComponentModel;

namespace MarketAssistant.ViewModels;

public partial class AnalystRoleViewModel : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isRequired;
}
