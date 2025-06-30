namespace MarketAssistant.Services
{
    /// <summary>
    /// 应用程序退出管理服务接口
    /// </summary>
    public interface IApplicationExitService
    {
        /// <summary>
        /// 处理窗口关闭请求
        /// </summary>
        /// <param name="window">要关闭的窗口</param>
        /// <returns>是否允许关闭窗口</returns>
        Task<bool> HandleWindowCloseRequestAsync(Window window);

        /// <summary>
        /// 最小化到托盘
        /// </summary>
        /// <param name="window">要最小化的窗口</param>
        void MinimizeToTray(Window window);

        /// <summary>
        /// 从托盘恢复主窗口
        /// </summary>
        void RestoreFromTray();

        /// <summary>
        /// 退出应用程序
        /// </summary>
        void ExitApplication();

        /// <summary>
        /// 检查是否为主窗口
        /// </summary>
        /// <param name="window">窗口实例</param>
        /// <returns>是否为主窗口</returns>
        bool IsMainWindow(Window window);
    }

    /// <summary>
    /// 窗口关闭选择
    /// </summary>
    public enum WindowCloseChoice
    {
        /// <summary>
        /// 取消关闭
        /// </summary>
        Cancel,
        
        /// <summary>
        /// 最小化到托盘
        /// </summary>
        MinimizeToTray,
        
        /// <summary>
        /// 退出应用程序
        /// </summary>
        ExitApplication
    }
}