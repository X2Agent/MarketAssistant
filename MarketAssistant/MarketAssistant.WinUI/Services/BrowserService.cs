using MarketAssistant.Infrastructure;
using Microsoft.Win32;

namespace MarketAssistant.WinUI.Services;

internal class BrowserService : IBrowserService
{
    /// <summary>
    /// 检查Windows系统上安装的浏览器
    /// </summary>
    public string CheckBrowser()
    {
        // 首先检查Edge浏览器
        var edgePath = CheckEdge();
        if (!string.IsNullOrEmpty(edgePath))
        {
            return edgePath;
        }

        // 然后检查Chrome浏览器
        var chromePath = CheckChrome();
        if (!string.IsNullOrEmpty(chromePath))
        {
            return chromePath;
        }

        return string.Empty;
    }

    /// <summary>
    /// 检查Windows系统上安装的Edge浏览器
    /// </summary>
    private static string CheckEdge()
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
                    var edgePath = Path.Combine(path, "msedge.exe");
                    if (File.Exists(edgePath))
                    {
                        return edgePath;
                    }
                }
            }

            // 尝试在WOW6432Node中查找（适用于64位系统上的32位程序）
            using var keyWow = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
            if (keyWow != null)
            {
                var path = keyWow.GetValue("Path") as string;
                if (!string.IsNullOrEmpty(path))
                {
                    var edgePath = Path.Combine(path, "msedge.exe");
                    if (File.Exists(edgePath))
                    {
                        return edgePath;
                    }
                }
            }

            // 尝试在用户目录中查找Edge
            var userEdgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(userEdgePath))
            {
                return userEdgePath;
            }
        }
        catch
        {
            // 忽略注册表访问错误
        }

        return string.Empty;
    }

    /// <summary>
    /// 检查Windows系统上安装的Chrome浏览器
    /// </summary>
    private static string CheckChrome()
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
                    return path;
                }
            }

            // 尝试在WOW6432Node中查找（适用于64位系统上的32位程序）
            using var keyWow = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (keyWow != null)
            {
                var path = keyWow.GetValue(null) as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }

            // 尝试在用户目录中查找Chrome
            var userChromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(userChromePath))
            {
                return userChromePath;
            }
        }
        catch
        {
            // 忽略注册表访问错误
        }

        return string.Empty;
    }
}
