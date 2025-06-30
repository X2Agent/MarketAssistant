using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Pages;

public partial class SettingPage : ContentPage, IRecipient<ToastMessage>
{
    public SettingPage(SettingViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel;
        WeakReferenceMessenger.Default.Register(this);
    }

    public async void Receive(ToastMessage message)
    {
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        var toast = Toast.Make(message.Content, ToastDuration.Short, 14);
        await toast.Show(cancellationTokenSource.Token);
    }

    // 页面消失时取消注册
    protected override void OnDisappearing()
    {
        WeakReferenceMessenger.Default.Unregister<ToastMessage>(this);
        base.OnDisappearing();
    }
}