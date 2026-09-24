using MarketAssistant.Infrastructure.Core;
using MarketAssistant.ViewModels;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.Application;

/// <summary>
/// <see cref="ViewModelBase"/> 页面加载状态机单元测试。
/// 核心契约：失败（Failed）与无数据（Empty）必须严格区分，取消视为未加载（Idle），
/// 已有内容时的刷新不得回落到加载浮层（OverlayState）。
/// </summary>
[TestClass]
public class ViewModelBaseLoadStateTest
{
    private const string OperationName = "加载资产列表";

    private sealed class TestableViewModel(ILogger? logger = null) : ViewModelBase(logger)
    {
        public Task RunLoadAsync(
            Func<CancellationToken, Task<bool>> loader,
            CancellationToken cancellationToken = default)
            => LoadAsync(loader, OperationName, cancellationToken);

        public void RunSetFailed(string errorMessage) => SetFailed(errorMessage);

        public void RunSetEmpty(string? emptyMessage = null) => SetEmpty(emptyMessage);

        public void RunResetLoadState() => ResetLoadState();

        public void RunSetRetryCommand(Func<Task> retryAction) => SetRetryCommand(retryAction);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InitialState_ShouldBeIdleWithoutError()
    {
        var vm = new TestableViewModel();

        Assert.AreEqual(LoadState.Idle, vm.State);
        Assert.AreEqual(LoadState.Idle, vm.OverlayState);
        Assert.IsFalse(vm.IsLoading);
        Assert.IsFalse(vm.HasError);
        Assert.IsFalse(vm.IsEmpty);
        Assert.IsFalse(vm.IsReady);
        Assert.IsFalse(vm.IsInitialLoading);
        Assert.IsFalse(vm.HasLoadedContent);
        Assert.AreEqual(string.Empty, vm.ErrorMessage);
        Assert.IsNull(vm.RetryCommand);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAsync_WhenLoaderReturnsTrue_ShouldBecomeReady()
    {
        var vm = new TestableViewModel();

        await vm.RunLoadAsync(_ => Task.FromResult(true));

        Assert.AreEqual(LoadState.Ready, vm.State);
        Assert.IsTrue(vm.IsReady);
        Assert.IsFalse(vm.IsLoading);
        Assert.IsFalse(vm.HasError);
        Assert.IsFalse(vm.IsEmpty);
        Assert.IsTrue(vm.HasLoadedContent);
        Assert.IsFalse(vm.IsInitialLoading);
        Assert.AreEqual(string.Empty, vm.ErrorMessage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAsync_WhenLoaderReturnsFalse_ShouldBecomeEmptyWithoutError()
    {
        var vm = new TestableViewModel();

        await vm.RunLoadAsync(_ => Task.FromResult(false));

        Assert.AreEqual(LoadState.Empty, vm.State);
        Assert.IsTrue(vm.IsEmpty);
        Assert.IsFalse(vm.HasError, "数据源可用但无数据不得被当成失败");
        Assert.IsFalse(vm.HasLoadedContent);
        Assert.AreEqual(vm.EmptyMessage, vm.OverlayMessage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAsync_WhenLoaderThrows_ShouldBecomeFailedWithMappedMessage()
    {
        var vm = new TestableViewModel();

        await vm.RunLoadAsync(_ => throw new FriendlyException("数据源不可用"));

        Assert.AreEqual(LoadState.Failed, vm.State);
        Assert.IsTrue(vm.HasError);
        Assert.IsFalse(vm.IsEmpty, "失败不得被当成空态");
        Assert.IsFalse(vm.HasLoadedContent);
        StringAssert.Contains(vm.ErrorMessage, OperationName);
        Assert.AreEqual(vm.ErrorMessage, vm.OverlayMessage, "失败态浮层应展示错误信息而非空态文案");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAsync_WhenCancelled_ShouldReturnToIdleWithoutError()
    {
        var vm = new TestableViewModel();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await vm.RunLoadAsync(_ => throw new OperationCanceledException(cts.Token), cts.Token);

        Assert.AreEqual(LoadState.Idle, vm.State, "用户主动取消视为未加载");
        Assert.IsFalse(vm.HasError);
        Assert.AreEqual(string.Empty, vm.ErrorMessage);
        Assert.IsFalse(vm.IsInitialLoading);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAsync_StaleSupersededInvocation_ShouldNotClobberNewerState()
    {
        // 详情页场景：切换资产后旧加载才延迟返回（取消令牌要等请求自然结束才触发），
        // 旧调用的取消/失败/成功结果均不得覆写新加载已写入的状态
        var vm = new TestableViewModel();

        var staleGate = new TaskCompletionSource();
        var staleLoadTask = vm.RunLoadAsync(async _ =>
        {
            await staleGate.Task;
            throw new OperationCanceledException();
        });

        // 新加载在旧调用仍挂起时启动并完成
        await vm.RunLoadAsync(_ => Task.FromResult(true));
        Assert.AreEqual(LoadState.Ready, vm.State);

        staleGate.SetResult();
        await staleLoadTask;

        Assert.AreEqual(LoadState.Ready, vm.State, "过期调用的取消不得把新加载的 Ready 覆写回 Idle");
        Assert.IsTrue(vm.HasLoadedContent);
        Assert.AreEqual(string.Empty, vm.ErrorMessage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAsync_StaleSupersededFailure_ShouldNotClobberNewerState()
    {
        var vm = new TestableViewModel();

        var staleGate = new TaskCompletionSource();
        var staleLoadTask = vm.RunLoadAsync(async _ =>
        {
            await staleGate.Task;
            throw new FriendlyException("旧调用的失败");
        });

        await vm.RunLoadAsync(_ => Task.FromResult(true));
        Assert.AreEqual(LoadState.Ready, vm.State);

        staleGate.SetResult();
        await staleLoadTask;

        Assert.AreEqual(LoadState.Ready, vm.State, "过期调用的失败不得把新加载的 Ready 覆写回 Failed");
        Assert.IsFalse(vm.HasError);
        Assert.AreEqual(string.Empty, vm.ErrorMessage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAsync_FirstLoad_ShouldRaiseIsInitialLoadingOnlyDuringLoad()
    {
        var vm = new TestableViewModel();
        bool? duringLoad = null;

        await vm.RunLoadAsync(_ =>
        {
            duringLoad = vm.IsInitialLoading;
            return Task.FromResult(true);
        });

        Assert.IsTrue(duringLoad, "首次加载期间应展示骨架屏");
        Assert.IsFalse(vm.IsInitialLoading);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAsync_RefreshAfterContentLoaded_ShouldNotShowLoadingOverlay()
    {
        var vm = new TestableViewModel();
        await vm.RunLoadAsync(_ => Task.FromResult(true));
        Assert.IsTrue(vm.HasLoadedContent);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = vm.RunLoadAsync(_ => gate.Task);

        Assert.AreEqual(LoadState.Loading, vm.State);
        Assert.AreEqual(LoadState.Idle, vm.OverlayState, "已有内容刷新时不得遮挡既有内容");
        Assert.IsFalse(vm.IsInitialLoading, "刷新不属于首次加载");

        gate.SetResult(true);
        await refresh;

        Assert.AreEqual(LoadState.Ready, vm.State);
        Assert.AreEqual(LoadState.Ready, vm.OverlayState);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAsync_AfterFailure_ShouldClearPreviousErrorMessage()
    {
        var vm = new TestableViewModel();
        await vm.RunLoadAsync(_ => throw new FriendlyException("第一次失败"));
        Assert.IsTrue(vm.HasError);

        await vm.RunLoadAsync(_ => Task.FromResult(true));

        Assert.AreEqual(LoadState.Ready, vm.State);
        Assert.AreEqual(string.Empty, vm.ErrorMessage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadAsync_FirstLoadEmptyThenFailure_ShouldDistinguishEmptyFromFailed()
    {
        var vm = new TestableViewModel();

        await vm.RunLoadAsync(_ => Task.FromResult(false));
        var afterEmpty = (vm.IsEmpty, vm.HasError);

        await vm.RunLoadAsync(_ => throw new FriendlyException("数据源不可用"));
        var afterFailure = (vm.IsEmpty, vm.HasError);

        Assert.AreEqual((true, false), afterEmpty);
        Assert.AreEqual((false, true), afterFailure);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SetFailed_ShouldKeepLoadedContentFlag()
    {
        var vm = new TestableViewModel();
        await vm.RunLoadAsync(_ => Task.FromResult(true));

        vm.RunSetFailed("刷新失败");

        Assert.AreEqual(LoadState.Failed, vm.State);
        Assert.IsTrue(vm.HasLoadedContent, "已有数据时失败不应清空内容标记");
        Assert.AreEqual("刷新失败", vm.ErrorMessage);
        Assert.IsFalse(vm.IsInitialLoading);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SetEmpty_ShouldOverrideEmptyMessageAndClearError()
    {
        var vm = new TestableViewModel();
        await vm.RunLoadAsync(_ => throw new FriendlyException("先失败"));

        vm.RunSetEmpty("该资产暂无行情数据");

        Assert.AreEqual(LoadState.Empty, vm.State);
        Assert.AreEqual("该资产暂无行情数据", vm.EmptyMessage);
        Assert.AreEqual("该资产暂无行情数据", vm.OverlayMessage);
        Assert.AreEqual(string.Empty, vm.ErrorMessage);
        Assert.IsFalse(vm.HasError);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ResetLoadState_ShouldReturnToIdle()
    {
        var vm = new TestableViewModel();
        await vm.RunLoadAsync(_ => Task.FromResult(true));

        vm.RunResetLoadState();

        Assert.AreEqual(LoadState.Idle, vm.State);
        Assert.IsFalse(vm.HasLoadedContent);
        Assert.AreEqual(string.Empty, vm.ErrorMessage);
        Assert.IsFalse(vm.IsInitialLoading);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SetRetryCommand_ShouldExposeExecutableCommand()
    {
        var vm = new TestableViewModel();
        var invoked = 0;
        vm.RunSetRetryCommand(() =>
        {
            invoked++;
            return Task.CompletedTask;
        });

        Assert.IsNotNull(vm.RetryCommand);
        Assert.AreEqual(0, invoked);

        await vm.RetryCommand.ExecuteAsync(null);

        Assert.AreEqual(1, invoked);
    }
}
