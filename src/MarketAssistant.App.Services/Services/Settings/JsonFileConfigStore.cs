using System.Collections.Concurrent;
using System.Text.Json;

namespace MarketAssistant.Services.Settings;

/// <summary>
/// JSON 配置文件存储基类：收敛「按路径加锁 + 原子写回」的通用实现。
/// </summary>
/// <remarks>
/// <para>
/// 用户设置与 MCP 服务器配置各自持久化一个 JSON 文件，此前的两份实现逐行重复了
/// 三件事：① 按规范化路径获取进程内文件锁（Windows 下路径大小写不敏感）；
/// ② 保存前确保目录存在；③ 先写 <c>.tmp</c> 再 <see cref="File.Move(string, string, bool)"/>
/// 整体替换，避免进程中断留下半个 JSON。
/// </para>
/// <para>
/// 原子写尤其值得收敛：两份实现都踩过「直接写目标文件，崩溃后配置损坏」的坑，
/// 但补丁只打在各自文件里，第三份配置文件出现时不会自动获得该保护。
/// </para>
/// <para>
/// <b>子类负责：</b>读取/反序列化策略、Secret 迁移、异常语义与加锁范围。
/// 基类刻意不吞异常——设置损坏时应由子类决定回退默认值还是向上传播。
/// </para>
/// </remarks>
/// <typeparam name="T">配置根类型。</typeparam>
public abstract class JsonFileConfigStore<T>
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>按配置文件路径维度的进程内锁表，保证同路径多实例互斥。</summary>
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    protected JsonFileConfigStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // 规范化路径：同一文件的相对/绝对/不同大小写形式必须落到同一把锁上
        FilePath = Path.GetFullPath(filePath);
        FileLock = FileLocks.GetOrAdd(FilePath, static _ => new object());
    }

    /// <summary>规范化后的配置文件路径。</summary>
    protected string FilePath { get; }

    /// <summary>本实例对应的文件锁。调用方持有它再调用 <see cref="SaveAtomic"/>。</summary>
    protected object FileLock { get; }

    /// <summary>
    /// 读取配置文件的原始 JSON 文本；文件不存在时返回 <c>null</c>。
    /// </summary>
    protected string? ReadRawJson() => File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;

    /// <summary>反序列化配置 JSON。格式非法时抛出 <see cref="JsonException"/>，由子类决定处置。</summary>
    protected static T? ParseJson(string json) => JsonSerializer.Deserialize<T>(json);

    /// <summary>
    /// 原子写回配置：确保目录存在 → 写临时文件 → 整体替换目标文件。
    /// </summary>
    /// <remarks>
    /// 调用方须已持有 <see cref="FileLock"/>，避免序列化与文件替换同其它写入交错。
    /// 本方法不捕获异常：写入失败必须让调用方看见，不能伪装成「已保存」。
    /// </remarks>
    protected void SaveAtomic(T value)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(value, WriteOptions);
        var tempFilePath = FilePath + ".tmp";
        File.WriteAllText(tempFilePath, json);
        File.Move(tempFilePath, FilePath, overwrite: true);
    }
}
