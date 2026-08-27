using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using MarketAssistant.ViewModels.Home;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

/// <summary>
/// HomeSearchViewModel 单元测试：覆盖键盘高亮导航（MoveSelection）与清空逻辑
/// </summary>
[TestClass]
public class HomeSearchViewModelTest
{
    private static HomeSearchViewModel CreateViewModel()
    {
        var services = new ServiceCollection();
        var userSettingService = new Mock<IUserSettingService>();
        userSettingService
            .Setup(s => s.CurrentSetting)
            .Returns(new UserSetting());
        services.AddSingleton(userSettingService.Object);
        var serviceProvider = services.BuildServiceProvider();
        var marketContext = new MarketContext(userSettingService.Object, serviceProvider);

        return new HomeSearchViewModel(
            serviceProvider,
            marketContext,
            NullLogger<HomeSearchViewModel>.Instance);
    }

    private static List<AssetItem> CreateResults(int count)
        => Enumerable.Range(1, count)
            .Select(i => new AssetItem { Name = $"资产{i}", Code = $"CODE{i:000}" })
            .ToList();

    [TestMethod]
    public void MoveSelection_WithNoResults_ShouldReturnFalse()
    {
        var vm = CreateViewModel();

        Assert.IsFalse(vm.MoveSelection(1));
        Assert.IsNull(vm.SelectedResult);
    }

    [TestMethod]
    public void MoveSelection_WithNoHighlight_DownShouldSelectFirstItem()
    {
        var vm = CreateViewModel();
        foreach (var item in CreateResults(3))
        {
            vm.SearchResults.Add(item);
        }

        Assert.IsTrue(vm.MoveSelection(1));
        Assert.AreSame(vm.SearchResults[0], vm.SelectedResult);
    }

    [TestMethod]
    public void MoveSelection_AtLastItem_DownShouldStayAndReturnFalse()
    {
        var vm = CreateViewModel();
        foreach (var item in CreateResults(2))
        {
            vm.SearchResults.Add(item);
        }
        vm.SelectedResult = vm.SearchResults[1];

        Assert.IsFalse(vm.MoveSelection(1));
        Assert.AreSame(vm.SearchResults[1], vm.SelectedResult);
    }

    [TestMethod]
    public void MoveSelection_AtFirstItem_UpShouldStayAndReturnFalse()
    {
        var vm = CreateViewModel();
        foreach (var item in CreateResults(2))
        {
            vm.SearchResults.Add(item);
        }
        vm.SelectedResult = vm.SearchResults[0];

        Assert.IsFalse(vm.MoveSelection(-1));
        Assert.AreSame(vm.SearchResults[0], vm.SelectedResult);
    }

    [TestMethod]
    public void MoveSelection_SequentialDowns_ShouldMoveThroughList()
    {
        var vm = CreateViewModel();
        foreach (var item in CreateResults(3))
        {
            vm.SearchResults.Add(item);
        }

        vm.MoveSelection(1);
        Assert.AreSame(vm.SearchResults[0], vm.SelectedResult);

        vm.MoveSelection(1);
        Assert.AreSame(vm.SearchResults[1], vm.SelectedResult);

        vm.MoveSelection(-1);
        Assert.AreSame(vm.SearchResults[0], vm.SelectedResult);
    }

    [TestMethod]
    public void ClearSearch_ShouldClearQueryResultsAndSelection()
    {
        var vm = CreateViewModel();
        foreach (var item in CreateResults(2))
        {
            vm.SearchResults.Add(item);
        }
        vm.SelectedResult = vm.SearchResults[0];
        vm.SearchQuery = "茅台";
        vm.IsSearchResultVisible = true;

        vm.ClearSearch();

        Assert.AreEqual(string.Empty, vm.SearchQuery);
        Assert.AreEqual(0, vm.SearchResults.Count);
        Assert.IsNull(vm.SelectedResult);
        Assert.IsFalse(vm.IsSearchResultVisible);
        Assert.IsFalse(vm.IsSearching);
    }
}
