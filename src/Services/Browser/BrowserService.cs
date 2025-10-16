using System.Runtime.InteropServices;

namespace MarketAssistant.Services.Browser;

/// <summary>
/// 跨平台浏览器服务实现
/// </summary>
public class BrowserService : IBrowserService
{
    /// <summary>
    /// 检查系统上安装的浏览器
    /// </summary>
    public string CheckBrowser()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return CheckBrowserWindows();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return CheckBrowserMac();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return CheckBrowserLinux();
        }

        return string.Empty;
    }

    /// <summary>
    /// 检查Windows系统上安装的浏览器
    /// </summary>
    private static string CheckBrowserWindows()
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
    /// 检查Mac系统上安装的浏览器
    /// </summary>
    private static string CheckBrowserMac()
    {
        // 首先检查Chrome浏览器
        var chromePath = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
        if (File.Exists(chromePath))
        {
            return chromePath;
        }

        // 然后检查Safari浏览器
        var safariPath = "/Applications/Safari.app/Contents/MacOS/Safari";
        if (File.Exists(safariPath))
        {
            return safariPath;
        }

        // 检查Firefox
        var firefoxPath = "/Applications/Firefox.app/Contents/MacOS/firefox";
        if (File.Exists(firefoxPath))
        {
            return firefoxPath;
        }

        return string.Empty;
    }

    /// <summary>
    /// 检查Linux系统上安装的浏览器
    /// </summary>
    private static string CheckBrowserLinux()
    {
        // 按优先级检查常见浏览器
        string[] browsers = {
            "/usr/bin/google-chrome",
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser",
            "/usr/bin/firefox",
            "/snap/bin/chromium",
            "/snap/bin/firefox"
        };

        foreach (var browserPath in browsers)
        {
            if (File.Exists(browserPath))
            {
                return browserPath;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 检查Windows系统上安装的Edge浏览器
    /// </summary>
    private static string CheckEdge()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return string.Empty;
        }

        try
        {
            // 尝试在注册表中查找Edge
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
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
            using var keyWow = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
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
            var userEdgePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(userEdgePath))
            {
                return userEdgePath;
            }

            // 尝试在Program Files中查找
            var programFilesEdge = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(programFilesEdge))
            {
                return programFilesEdge;
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
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return string.Empty;
        }

        try
        {
            // 尝试在注册表中查找Chrome
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (key != null)
            {
                var path = key.GetValue(null) as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }

            // 尝试在WOW6432Node中查找（适用于64位系统上的32位程序）
            using var keyWow = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            if (keyWow != null)
            {
                var path = keyWow.GetValue(null) as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }

            // 尝试在用户目录中查找Chrome
            var userChromePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(userChromePath))
            {
                return userChromePath;
            }

            // 尝试在Program Files中查找
            var programFilesChrome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Google", "Chrome", "Application", "chrome.exe");
            if (File.Exists(programFilesChrome))
            {
                return programFilesChrome;
            }
        }
        catch
        {
            // 忽略注册表访问错误
        }

        return string.Empty;
    }
}

