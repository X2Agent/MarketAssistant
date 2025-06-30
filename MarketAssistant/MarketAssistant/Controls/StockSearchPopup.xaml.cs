using MarketAssistant.Applications.Stocks.Models;
using System.Collections.ObjectModel;

namespace MarketAssistant.Controls;

public partial class StockSearchPopup : ContentView
{
    public ObservableCollection<StockItem> StockResults { get; set; }
    public Command<StockItem> SelectStockCommand { get; set; }

    public StockSearchPopup(ObservableCollection<StockItem> stockResults, Command<StockItem> selectStockCommand)
    {
        InitializeComponent();

        StockResults = stockResults;
        SelectStockCommand = selectStockCommand;

        // 设置弹出窗口的数据上下文为自身，以便绑定命令和数据
        BindingContext = this;
    }

    // 当用户选择一个股票项时调用
    public void OnStockSelected(StockItem stockItem)
    {
        // 执行选择命令
        SelectStockCommand.Execute(stockItem);
    }
}