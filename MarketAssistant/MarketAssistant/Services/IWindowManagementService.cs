namespace MarketAssistant.Services
{
    /// <summary>
    /// 窗口管理服务接口，提供平台特定的窗口操作
    /// </summary>
    public interface IWindowManagementService
    {
        /// <summary>
        /// 隐藏窗口
        /// </summary>
        /// <param name="window">要隐藏的窗口</param>
        void HideWindow(Window window);

        /// <summary>
        /// 显示并激活窗口
        /// </summary>
        /// <param name="window">要显示的窗口</param>
        void ShowAndActivateWindow(Window window);

        /// <summary>
        /// 设置窗口关闭处理
        /// </summary>
        /// <param name="window">窗口</param>
        /// <param name="serviceProvider">服务提供者</param>
        void SetupWindowCloseHandling(Window window, IServiceProvider serviceProvider);
    }
}