using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace MarketAssistant.Avalonia.ViewModels
{
    /// <summary>
    /// 收藏页ViewModel
    /// </summary>
    public partial class FavoritesPageViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _title = "收藏";

        public ObservableCollection<FavoriteItemViewModel> FavoriteItems { get; }

        public FavoritesPageViewModel()
        {
            FavoriteItems = new ObservableCollection<FavoriteItemViewModel>();
            LoadFavorites();
        }

        private void LoadFavorites()
        {
            // 示例数据，实际应该从服务加载
            FavoriteItems.Add(new FavoriteItemViewModel("000001", "平安银行", "SZ"));
            FavoriteItems.Add(new FavoriteItemViewModel("600036", "招商银行", "SH"));
        }
    }

    public class FavoriteItemViewModel : ViewModelBase
    {
        public string Code { get; }
        public string Name { get; }
        public string Market { get; }

        public FavoriteItemViewModel(string code, string name, string market)
        {
            Code = code;
            Name = name;
            Market = market;
        }
    }
}
