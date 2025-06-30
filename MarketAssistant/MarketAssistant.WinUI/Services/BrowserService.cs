using MarketAssistant.Infrastructure;
using Microsoft.Win32;

namespace MarketAssistant.WinUI.Services;

internal class BrowserService : IBrowserService
{
    /// <summary>
    /// 检查Windows系统上安装的浏览器
    /// </summary>
    /// <returns>浏览器路径和是否找到浏览器</returns>
    public (string Path, bool Found) CheckBrowser()
    {
        // 首先检查Edge浏览器
        var (edgePath, edgeFound) = CheckEdge();
        if (edgeFound)
        {
            return (edgePath, true);
        }

        // 然后检查Chrome浏览器
        var (chromePath, chromeFound) = CheckChrome();
        if (chromeFound)
        {
            return (chromePath, true);
        }

        return (string.Empty, false);
    }

    /// <summary>
    /// 检查Windows系统上安装的Edge浏览器
    /// </summary>
    /// <returns>Edge路径和是否找到Edge</returns>
    private static (string Path, bool Found) CheckEdge()
    {
        try
        {
            // 尝试在标准路径查找Edge
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
            if (key != null)
            {
                var path = key.GetValue("Path") as string;
                if (!string.IsNullOrEmpty(path))
                {
                    return (Path.Combine(path, "msedge.exe"), true);
                }
            }

            // 尝试在WOW6432Node中查找（适用于64位系统上的32位程序）
            using var keyWow = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
            if (keyWow != null)
            {
                var path = keyWow.GetValue("Path") as string;
                if (!string.IsNullOrEmpty(path))
                {
                    return (Path.Combine(path, "msedge.exe"), true);
                }
            }

            // 尝试在用户目录中查找Edge
            var userEdgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(userEdgePath))
            {
                return (userEdgePath, true);
            }
        }
        catch
        {
            // 忽略注册表访问错误
        }

        return (string.Empty, false);
    }

    /// <summary>
    /// 检查Windows系统上安装的Chrome浏览器
    /// </summary>
    /// <returns>Chrome路径和是否找到Chrome</returns>
    private static (string Path, bool Found) CheckChrome()
    {
        try
        {
            // 尝试在标准路径查找Chrome
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (key != null)
            {
                var path = key.GetValue(null) as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return (path, true);
                }
            }

            // 尝试在WOW6432Node中查找（适用于64位系统上的32位程序）
            using var keyWow = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (keyWow != null)
            {
                var path = keyWow.GetValue(null) as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return (path, true);
                }
            }

            // 尝试在用户目录中查找Chrome
            var userChromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(userChromePath))
            {
                return (userChromePath, true);
            }
        }
        catch
        {
            // 忽略注册表访问错误
        }

        return (string.Empty, false);
    }
}
