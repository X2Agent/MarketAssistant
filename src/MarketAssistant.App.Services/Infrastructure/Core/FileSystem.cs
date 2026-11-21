using System.IO;
using MarketAssistant.Applications.Settings;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// Avalonia平台的FileSystem实现，提供与MAUI FileSystem相同的API
/// </summary>
public static class FileSystem
{
    private static readonly string _appName = AppInfo.AppName;

    /// <summary>
    /// 应用程序数据目录
    /// </summary>
    public static string AppDataDirectory => GetAppDataDirectory();

    /// <summary>
    /// 缓存目录
    /// </summary>
    public static string CacheDirectory => GetCacheDirectory();

    /// <summary>
    /// 应用程序包目录
    /// </summary>
    public static string AppPackageDirectory => GetAppPackageDirectory();

    private static string GetAppDataDirectory()
    {
        string appDataPath;

        if (OperatingSystem.IsWindows())
        {
            appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support");
        }
        else // Linux和其他Unix系统
        {
            appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        var appDataDir = Path.Combine(appDataPath, _appName);

        // 确保目录存在
        Directory.CreateDirectory(appDataDir);
        return appDataDir;
    }

    private static string GetCacheDirectory()
    {
        string cachePath;

        if (OperatingSystem.IsWindows())
        {
            cachePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }
        else if (OperatingSystem.IsMacOS())
        {
            cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches");
        }
        else // Linux和其他Unix系统
        {
            cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache");
        }

        var cacheDir = Path.Combine(cachePath, _appName, AppInfo.CacheDirectoryName);

        // 确保目录存在
        Directory.CreateDirectory(cacheDir);
        return cacheDir;
    }

    // Avalonia 应用使用应用程序所在目录
    private static string GetAppPackageDirectory() => AppDomain.CurrentDomain.BaseDirectory;
}

