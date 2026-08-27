using System.Text;

namespace MarketAssistant.Services.Agents.MarketAnalysis.Artifacts;

/// <summary>
/// 分析师产物存储（P1-07）：按 Run 隔离保存每位分析师的完整产物，
/// 协调阶段只传摘要，全文通过工具按需读取。
/// </summary>
public interface IAnalystArtifactStore
{
    /// <summary>写入/覆盖指定 Run 下某分析师的产物全文。</summary>
    Task SaveAsync(Guid runId, string analystName, string content, CancellationToken cancellationToken = default);

    /// <summary>读取指定 Run 下某分析师的产物全文；不存在时返回 null。</summary>
    Task<string?> GetAsync(Guid runId, string analystName, CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于文件系统的产物存储实现：%APPDATA%/MarketAssistant/analyst-artifacts/{runId}/{analystName}.md
/// </summary>
public sealed class FileAnalystArtifactStore : IAnalystArtifactStore
{
    private readonly string _rootDirectory;

    public FileAnalystArtifactStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = rootDirectory;
    }

    public async Task SaveAsync(Guid runId, string analystName, string content, CancellationToken cancellationToken = default)
    {
        var path = GetPath(runId, analystName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content ?? string.Empty, Encoding.UTF8, cancellationToken);
    }

    public async Task<string?> GetAsync(Guid runId, string analystName, CancellationToken cancellationToken = default)
    {
        var path = GetPath(runId, analystName);
        if (!File.Exists(path))
            return null;

        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
    }

    private string GetPath(Guid runId, string analystName)
    {
        // analystName 为 MAF Agent ASCII Name，仅允许安全字符
        var safeName = string.Concat(analystName.Where(char.IsLetterOrDigit));
        if (string.IsNullOrEmpty(safeName))
            throw new ArgumentException("分析师名称不能为空", nameof(analystName));

        return Path.Combine(_rootDirectory, runId.ToString("N"), $"{safeName}.md");
    }
}