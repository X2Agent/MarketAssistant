using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Controls;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Pages;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;
    private StockSearchPopup _searchPopup;
    private bool _isPopupShowing = false;

    public HomePage(HomeViewModel homeViewModel)
    {
        InitializeComponent();
        _viewModel = homeViewModel;
        BindingContext = _viewModel;

        // 订阅搜索结果变化事件
        _viewModel.Search.PropertyChanged += SearchViewModel_PropertyChanged;

        // 设置SearchBar的TextChanged事件
        searchBar.TextChanged += SearchBar_TextChanged;
    }

    private void SearchViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.Search.IsSearchResultVisible) && _viewModel.Search.IsSearchResultVisible)
        {
            ShowSearchPopup();
        }
    }

    private void SearchBar_TextChanged(object? sender, TextChangedEventArgs e)
    {
        // 当文本为空时，确保关闭弹窗
        if (string.IsNullOrWhiteSpace(e.NewTextValue))
        {
            _viewModel.Search.ClearSearchResults();
        }
    }

    private async void ShowSearchPopup()
    {
        if (_isPopupShowing) return;

        _isPopupShowing = true;

        // 创建搜索结果弹窗
        _searchPopup = new StockSearchPopup(_viewModel.Search.SearchResults, new Command<StockItem>(item => _viewModel.Search.SelectStockCommand.Execute(item)));

        // 配置弹窗选项
        var popupOptions = new PopupOptions
        {
            CanBeDismissedByTappingOutsideOfPopup = true,
        };

        try
        {
            // 使用新的v2 API显示弹窗
            await this.ShowPopupAsync(_searchPopup, popupOptions);
        }
        finally
        {
            // 弹窗关闭后的处理
            _isPopupShowing = false;
            _viewModel.Search.ClearSearchResults();
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // 页面显示时启动定时器
        _viewModel.StartTimer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // 页面隐藏时暂停定时器
        _viewModel.StopTimer();

        // 取消事件订阅，防止内存泄漏
        _viewModel.Search.PropertyChanged -= SearchViewModel_PropertyChanged;
        searchBar.TextChanged -= SearchBar_TextChanged;
    }
}