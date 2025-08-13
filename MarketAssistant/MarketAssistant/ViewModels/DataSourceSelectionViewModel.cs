using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels
{
    public partial class DataSourceSelectionViewModel : ViewModelBase
    {
        private readonly IUserSettingService _userSettingService;
        private readonly ILogger<DataSourceSelectionViewModel> _logger;

        public DataSourceSelectionViewModel(
            ILogger<DataSourceSelectionViewModel> logger,
            IUserSettingService userSettingService) : base(logger)
        {
            _userSettingService = userSettingService;
            _logger = logger;
            
            // 初始化选择状态
            InitializeSelections();
        }

        /// <summary>
        /// 初始化选择状态
        /// </summary>
        private void InitializeSelections()
        {
            var currentSetting = _userSettingService.CurrentSetting;
            
            // 根据当前设置初始化选择状态
            IsAStocksSelected = currentSetting.SelectedDataSource == MarketDataSource.AStocks;
            IsHKStocksSelected = currentSetting.SelectedDataSource == MarketDataSource.HKStocks;
            IsUSStocksSelected = currentSetting.SelectedDataSource == MarketDataSource.USStocks;
            IsCryptoSelected = currentSetting.SelectedDataSource == MarketDataSource.Crypto;
        }

        /// <summary>
        /// A股是否被选中
        /// </summary>
        [ObservableProperty]
        private bool _isAStocksSelected = true;

        /// <summary>
        /// 港股是否被选中
        /// </summary>
        [ObservableProperty]
        private bool _isHKStocksSelected;

        /// <summary>
        /// 美股是否被选中
        /// </summary>
        [ObservableProperty]
        private bool _isUSStocksSelected;

        /// <summary>
        /// 虚拟币是否被选中
        /// </summary>
        [ObservableProperty]
        private bool _isCryptoSelected;

        /// <summary>
        /// 确认选择命令
        /// </summary>
        [RelayCommand]
        private async Task ConfirmSelectionAsync()
        {
            try
            {
                // 确定选择的数据源
                MarketDataSource selectedDataSource = MarketDataSource.AStocks; // 默认值
                
                if (IsAStocksSelected)
                    selectedDataSource = MarketDataSource.AStocks;
                else if (IsHKStocksSelected)
                    selectedDataSource = MarketDataSource.HKStocks;
                else if (IsUSStocksSelected)
                    selectedDataSource = MarketDataSource.USStocks;
                else if (IsCryptoSelected)
                    selectedDataSource = MarketDataSource.Crypto;

                // 更新用户设置
                var currentSetting = _userSettingService.CurrentSetting;
                currentSetting.SelectedDataSource = selectedDataSource;
                currentSetting.IsFirstLaunch = false; // 标记为非首次启动
                
                _userSettingService.UpdateSettings(currentSetting);
                
                _logger.LogInformation("用户选择了数据源: {DataSource}", selectedDataSource);
                
                // 导航到主页面
                await Shell.Current.GoToAsync("//main");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确认数据源选择时发生错误");
                await Shell.Current.DisplayAlert("错误", "保存设置时发生错误，请重试", "确定");
            }
        }

        /// <summary>
        /// 当某个数据源选择状态改变时调用
        /// </summary>
        /// <param name="propertyName">属性名</param>
        partial void OnIsAStocksSelectedChanged(bool value)
        {
            if (value)
                DeselectOtherDataSources("IsAStocksSelected");
        }

        partial void OnIsHKStocksSelectedChanged(bool value)
        {
            if (value)
                DeselectOtherDataSources("IsHKStocksSelected");
        }

        partial void OnIsUSStocksSelectedChanged(bool value)
        {
            if (value)
                DeselectOtherDataSources("IsUSStocksSelected");
        }

        partial void OnIsCryptoSelectedChanged(bool value)
        {
            if (value)
                DeselectOtherDataSources("IsCryptoSelected");
        }

        /// <summary>
        /// 取消选择其他数据源（单选逻辑）
        /// </summary>
        /// <param name="exceptProperty">要排除的属性名</param>
        private void DeselectOtherDataSources(string exceptProperty)
        {
            if (exceptProperty != "IsAStocksSelected")
                IsAStocksSelected = false;
            if (exceptProperty != "IsHKStocksSelected")
                IsHKStocksSelected = false;
            if (exceptProperty != "IsUSStocksSelected")
                IsUSStocksSelected = false;
            if (exceptProperty != "IsCryptoSelected")
                IsCryptoSelected = false;
        }
    }
}