using System.Collections.Concurrent;
using System.Text.Json;
using MarketAssistant.Applications.Settings;

namespace MarketAssistant.Infrastructure.Configuration;

/// <summary>
/// Avalonia平台的Preferences实现，提供与MAUI Preferences相同的API
/// </summary>
public static class Preferences
{
    private static readonly ConcurrentDictionary<string, object> _preferences = new();
    private static readonly string _preferencesFilePath;
    private static readonly object _lock = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    static Preferences()
    {
        // 使用用户配置目录存储preferences
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDataDir = Path.Combine(appDataPath, AppInfo.AppName);
        
        // 确保目录存在
        Directory.CreateDirectory(appDataDir);
        
        _preferencesFilePath = Path.Combine(appDataDir, AppInfo.PreferencesFileName);
        LoadPreferences();
    }

    /// <summary>
    /// 获取Preferences的默认实例
    /// </summary>
    public static IPreferences Default { get; } = new PreferencesImpl();

    /// <summary>
    /// 从文件加载preferences
    /// </summary>
    private static void LoadPreferences()
    {
        try
        {
            if (File.Exists(_preferencesFilePath))
            {
                var json = File.ReadAllText(_preferencesFilePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        _preferences[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载Preferences时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 保存preferences到文件
    /// </summary>
    private static void SavePreferences()
    {
        try
        {
            lock (_lock)
            {
                var dict = _preferences.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var json = JsonSerializer.Serialize(dict, _jsonOptions);
                File.WriteAllText(_preferencesFilePath, json);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存Preferences时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// Preferences实现类
    /// </summary>
    private class PreferencesImpl : IPreferences
    {
        public void Clear()
        {
            _preferences.Clear();
            SavePreferences();
        }

        public void Clear(string key)
        {
            _preferences.TryRemove(key, out _);
            SavePreferences();
        }

        public bool ContainsKey(string key)
        {
            return _preferences.ContainsKey(key);
        }

        public T Get<T>(string key, T defaultValue)
        {
            if (_preferences.TryGetValue(key, out var value))
            {
                try
                {
                    // 直接类型匹配
                    if (value is T directValue)
                    {
                        return directValue;
                    }
                    
                    // 处理JsonElement
                    if (value is JsonElement jsonElement)
                    {
                        return jsonElement.Deserialize<T>(_jsonOptions) ?? defaultValue;
                    }
                    
                    // 处理字符串到枚举的转换
                    if (typeof(T).IsEnum && value is string stringValue)
                    {
                        return (T)Enum.Parse(typeof(T), stringValue);
                    }
                    
                    // 处理基本类型转换
                    if (value is IConvertible)
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    
                    // 尝试JSON反序列化
                    if (value is string jsonString)
                    {
                        return JsonSerializer.Deserialize<T>(jsonString, _jsonOptions) ?? defaultValue;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Preferences类型转换失败: {ex.Message}");
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        public void Remove(string key)
        {
            _preferences.TryRemove(key, out _);
            SavePreferences();
        }

        public void Set<T>(string key, T value)
        {
            if (value == null)
            {
                Remove(key);
                return;
            }
            
            // 对于复杂类型，使用JSON序列化
            if (!IsSimpleType(typeof(T)))
            {
                var json = JsonSerializer.Serialize(value, _jsonOptions);
                _preferences[key] = json;
            }
            else
            {
                _preferences[key] = value;
            }
            
            SavePreferences();
        }
        
        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || 
                   type.IsEnum || 
                   type == typeof(string) || 
                   type == typeof(decimal) || 
                   type == typeof(DateTime) || 
                   type == typeof(DateTimeOffset) || 
                   type == typeof(TimeSpan) || 
                   type == typeof(Guid);
        }
    }
}

/// <summary>
/// Preferences接口，与MAUI Preferences接口保持一致
/// </summary>
public interface IPreferences
{
    void Clear();
    void Clear(string key);
    bool ContainsKey(string key);
    T Get<T>(string key, T defaultValue);
    void Remove(string key);
    void Set<T>(string key, T value);
}

