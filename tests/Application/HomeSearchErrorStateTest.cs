using MarketAssistant.Applications;
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
/// HomeSearchViewModel 失败态单元测试：搜索失败必须与"没有匹配结果"可区分，
/// 并在查询清空/关闭弹层时被复位。
/// </summary>
[TestClass]
public class HomeSearchErrorStateTest
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
            marketContext,
            NullLogger<HomeSearchViewModel>.Instance);
    }

    private static void SeedFailedState(HomeSearchViewModel vm)
    {
        vm.SearchResults.Add(new AssetItem { Name = "残留结果", Code = "SH600519" });
        vm.SelectedResult = vm.SearchResults[0];
        vm.IsSearchResultVisible = true;
        vm.SearchErrorMessage = "搜索资产失败：网络连接超时";
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SearchErrorMessage_ShouldDriveHasSearchErrorAndRaiseNotification()
    {
        var vm = CreateViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        Assert.IsFalse(vm.HasSearchError);

        vm.SearchErrorMessage = "搜索资产失败：网络连接超时";

        Assert.IsTrue(vm.HasSearchError);
        Assert.Contains(nameof(HomeSearchViewModel.HasSearchError), changed);

        changed.Clear();
        vm.SearchErrorMessage = string.Empty;

        Assert.IsFalse(vm.HasSearchError);
        Assert.Contains(nameof(HomeSearchViewModel.HasSearchError), changed);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void OnSearchQueryChanged_WithWhitespace_ShouldClearErrorResultsAndPopup()
    {
        var vm = CreateViewModel();
        SeedFailedState(vm);

        vm.SearchQuery = "   ";

        Assert.AreEqual(string.Empty, vm.SearchErrorMessage);
        Assert.IsFalse(vm.HasSearchError);
        Assert.IsEmpty(vm.SearchResults);
        Assert.IsNull(vm.SelectedResult);
        Assert.IsFalse(vm.IsSearchResultVisible);
        Assert.IsFalse(vm.IsSearching);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void OnSearchQueryChanged_BackToEmpty_ShouldClearErrorState()
    {
        var vm = CreateViewModel();
        vm.SearchQuery = " ";
        SeedFailedState(vm);

        vm.SearchQuery = string.Empty;

        Assert.IsFalse(vm.HasSearchError);
        Assert.AreEqual(string.Empty, vm.SearchErrorMessage);
        Assert.IsFalse(vm.IsSearchResultVisible);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ClearSearch_AfterFailure_ShouldResetErrorState()
    {
        var vm = CreateViewModel();
        SeedFailedState(vm);

        vm.ClearSearch();

        Assert.IsFalse(vm.HasSearchError);
        Assert.AreEqual(string.Empty, vm.SearchErrorMessage);
        Assert.AreEqual(string.Empty, vm.SearchQuery);
        Assert.IsEmpty(vm.SearchResults);
        Assert.IsNull(vm.SelectedResult);
        Assert.IsFalse(vm.IsSearchResultVisible);
        Assert.IsFalse(vm.IsSearching);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NavigateToAsset_ShouldHidePopupAndRaiseSelection()
    {
        var vm = CreateViewModel();
        SeedFailedState(vm);
        var target = new AssetItem { Name = "贵州茅台", Code = "SH600519" };
        AssetItem? selected = null;
        vm.AssetSelected += (_, asset) => selected = asset;

        vm.NavigateToAssetCommand.Execute(target);

        Assert.AreSame(target, selected);
        Assert.IsFalse(vm.IsSearchResultVisible, "选中资产后应关闭搜索结果弹层");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NavigateToAsset_WithNull_ShouldDoNothing()
    {
        var vm = CreateViewModel();
        SeedFailedState(vm);
        var raised = false;
        vm.AssetSelected += (_, _) => raised = true;

        vm.NavigateToAssetCommand.Execute(null);

        Assert.IsFalse(raised);
        Assert.IsTrue(vm.IsSearchResultVisible);
    }
}
