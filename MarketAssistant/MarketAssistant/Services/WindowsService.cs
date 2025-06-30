using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MarketAssistant.Services
{
    /// <summary>
    /// 跨平台窗口服务实现
    /// </summary>
    public class WindowsService : IWindowsService
    {
        private readonly ILogger<WindowsService> _logger;

        // 窗口跟踪字典：页面类型 -> 窗口实例
        private readonly ConcurrentDictionary<string, Window> _openWindows = new();

        // 父子窗口关联字典：子窗口 -> 父窗口
        private readonly ConcurrentDictionary<Window, Window> _parentChildRelations = new();

        public WindowsService(ILogger<WindowsService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 显示一个新窗口，如果同类型窗口已存在则激活已有窗口
        /// </summary>
        /// <param name="page">要显示的页面</param>
        /// <param name="parentWindow">父窗口（可选）</param>
        /// <returns>创建的窗口实例</returns>
        public async Task<Window> ShowWindowAsync(Page page, Window parentWindow = null)
        {
            var pageTypeKey = page.GetType().FullName ?? "";

            // 检查是否已有同类型窗口
            if (_openWindows.TryGetValue(pageTypeKey!, out var existingWindow))
            {
                // 尝试激活现有窗口
                if (ActivateWindow(existingWindow))
                {
                    return existingWindow;
                }
                else
                {
                    // 如果激活失败，说明窗口可能已被销毁，从跟踪中移除
                    _openWindows.TryRemove(pageTypeKey, out _);
                }
            }

            // 创建新窗口
            var window = new Window
            {
                Page = page
            };

            // 添加到窗口跟踪
            _openWindows[pageTypeKey] = window;

            // 建立父子关系
            if (parentWindow != null)
            {
                _parentChildRelations[window] = parentWindow;
            }

            // 注册窗口销毁事件
            window.Destroying += (sender, args) =>
            {
                // 从跟踪中移除
                _openWindows.TryRemove(pageTypeKey, out _);
                _parentChildRelations.TryRemove(window, out _);

                // 如果有子窗口，也一并关闭
                var childWindows = _parentChildRelations.Where(kvp => kvp.Value == window).Select(kvp => kvp.Key).ToList();
                foreach (var childWindow in childWindows)
                {
                    try
                    {
                        CloseWindow(childWindow);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "关闭子窗口时出错");
                    }
                }
            };

            // 显示窗口 - 使用MAUI标准API
            Application.Current.OpenWindow(window);

            // 等待窗口Handler初始化
            await Task.Delay(100);

            return window;
        }

        /// <summary>
        /// 激活指定类型的窗口
        /// </summary>
        /// <param name="pageType">页面类型</param>
        /// <returns>是否成功激活</returns>
        public bool ActivateWindowByPageType(Type pageType)
        {
            var pageTypeKey = pageType.FullName;
            if (_openWindows.TryGetValue(pageTypeKey, out var window))
            {
                return ActivateWindow(window);
            }
            return false;
        }

        /// <summary>
        /// 关闭指定类型的所有窗口
        /// </summary>
        /// <param name="pageType">页面类型</param>
        /// <returns>关闭的窗口数量</returns>
        public int CloseWindowsByPageType(Type pageType)
        {
            var pageTypeKey = pageType.FullName;
            if (_openWindows.TryGetValue(pageTypeKey, out var window))
            {
                CloseWindow(window);
                return 1;
            }
            return 0;
        }

        /// <summary>
        /// 激活窗口（使用MAUI标准API）
        /// </summary>
        /// <param name="window">要激活的窗口</param>
        /// <returns>是否成功激活</returns>
        private bool ActivateWindow(Window window)
        {
            try
            {
                // 检查窗口是否有效
                if (window == null)
                {
                    _logger.LogWarning("窗口为空，无法激活");
                    return false;
                }

                // 使用MAUI标准API激活窗口
                Application.Current.ActivateWindow(window);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "激活窗口失败");
                return false;
            }
        }

        /// <summary>
        /// 关闭窗口
        /// </summary>
        /// <param name="window">要关闭的窗口</param>
        private void CloseWindow(Window window)
        {
            try
            {
                Application.Current.CloseWindow(window);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭窗口失败");
            }
        }
    }
}