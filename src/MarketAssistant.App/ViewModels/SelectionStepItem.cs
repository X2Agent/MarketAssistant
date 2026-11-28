using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Applications.InvestmentSelection.Models;

namespace MarketAssistant.ViewModels;

public enum SelectionStepState
{
    Pending,
    Running,
    Completed
}

/// <summary>
/// 运行态进度卡里的一个步骤；完成时的结果摘要由工作流给出（如“已生成 5 个筛选条件”）。
/// </summary>
public partial class SelectionStepItem(InvestmentSelectionStep step, string title) : ObservableObject
{
    public InvestmentSelectionStep Step { get; } = step;

    public string Title { get; } = title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsRunning), nameof(IsCompleted), nameof(StatusText))]
    private SelectionStepState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _detail;

    public bool IsPending => State == SelectionStepState.Pending;

    public bool IsRunning => State == SelectionStepState.Running;

    public bool IsCompleted => State == SelectionStepState.Completed;

    public string StatusText => State switch
    {
        SelectionStepState.Pending => "等待中",
        SelectionStepState.Running => "进行中",
        _ => Detail ?? "已完成"
    };
}
