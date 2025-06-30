namespace MarketAssistant.Services
{
    /// <summary>
    /// 系统托盘服务接口
    /// </summary>
    public interface ISystemTrayService
    {
        /// <summary>
        /// 初始化系统托盘
        /// </summary>
        void Initialize();

        /// <summary>
        /// 显示托盘图标
        /// </summary>
        void ShowTrayIcon();

        /// <summary>
        /// 隐藏托盘图标
        /// </summary>
        void HideTrayIcon();

        /// <summary>
        /// 设置托盘图标
        /// </summary>
        /// <param name="icon">图标对象（Windows: Icon, Mac: 忽略）</param>
        void SetTrayIcon(object icon);

        /// <summary>
        /// 设置托盘提示文本
        /// </summary>
        /// <param name="text">提示文本</param>
        void SetTrayTooltip(string text);

        /// <summary>
        /// 显示托盘通知
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="message">消息内容</param>
        void ShowNotification(string title, string message);

        /// <summary>
        /// 托盘图标点击事件
        /// </summary>
        event EventHandler TrayIconClicked;

        /// <summary>
        /// 显示主界面菜单项点击事件
        /// </summary>
        event EventHandler ShowMainWindowRequested;

        /// <summary>
        /// 退出应用菜单项点击事件
        /// </summary>
        event EventHandler ExitApplicationRequested;

        /// <summary>
        /// 释放资源
        /// </summary>
        void Dispose();
    }
}