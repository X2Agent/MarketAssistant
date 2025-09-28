using System.IO;
using MarketAssistant.Applications.Settings;

namespace MarketAssistant.Infrastructure;

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
         try
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
         catch (Exception ex)
         {
             System.Diagnostics.Debug.WriteLine($"获取AppDataDirectory时出错: {ex.Message}");
             
             // 备用方案：使用当前目录
             var fallbackDir = Path.Combine(Directory.GetCurrentDirectory(), "AppData");
             Directory.CreateDirectory(fallbackDir);
             return fallbackDir;
         }
     }
 
     private static string GetCacheDirectory()
     {
         try
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
         catch (Exception ex)
         {
             System.Diagnostics.Debug.WriteLine($"获取CacheDirectory时出错: {ex.Message}");
             
             // 备用方案：使用AppData目录下的Cache子目录
            var fallbackDir = Path.Combine(AppDataDirectory, AppInfo.CacheDirectoryName);
             Directory.CreateDirectory(fallbackDir);
             return fallbackDir;
         }
     }
 
     private static string GetAppPackageDirectory()
     {
         try
         {
             // 对于Avalonia应用，使用应用程序所在目录
             var baseDir = AppDomain.CurrentDomain.BaseDirectory;
             
             // 检查目录是否存在且可访问
             if (Directory.Exists(baseDir))
             {
                 return baseDir;
             }
             
             // 备用方案：使用当前工作目录
             return Directory.GetCurrentDirectory();
         }
         catch (Exception ex)
         {
             System.Diagnostics.Debug.WriteLine($"获取AppPackageDirectory时出错: {ex.Message}");
             return Directory.GetCurrentDirectory();
         }
     }
}
