namespace MarketAssistant.Services
{
    /// <summary>
    /// 窗口服务接口
    /// </summary>
    public interface IWindowsService
    {
        /// <summary>
        /// 显示一个新窗口
        /// </summary>
        /// <param name="page">要显示的页面</param>
        /// <param name="parentWindow">父窗口（可选）</param>
        /// <returns>创建的窗口实例</returns>
        Task<Window> ShowWindowAsync(Page page, Window parentWindow = null);

        /// <summary>
        /// 根据页面类型激活已存在的窗口
        /// </summary>
        /// <param name="pageType">页面类型</param>
        /// <returns>如果找到并激活了窗口返回true，否则返回false</returns>
        bool ActivateWindowByPageType(Type pageType);

        /// <summary>
        /// 根据页面类型关闭窗口
        /// </summary>
        /// <param name="pageType">页面类型</param>
        /// <returns>关闭的窗口数量</returns>
        int CloseWindowsByPageType(Type pageType);
    }
}