using System.Security.Cryptography;
using System.Text;

namespace MarketAssistant.Rag;

/// <summary>
/// 稳定文档标识（P1-01）：
/// <c>Path.GetFullPath → 统一分隔符 → 大小写归一 → SHA-256</c>。
/// 不把明文路径放进 Key，也不用全文当文档身份；
/// 同一文件无论以绝对/相对路径、大小写或分隔符差异引用，DocumentId 保持一致。
/// </summary>
public static class RagDocumentId
{
    public static string Compute(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var normalized = fullPath.Replace('\\', '/');

        // Windows 文件系统大小写不敏感；Linux/macOS 保留大小写
        if (OperatingSystem.IsWindows())
            normalized = normalized.ToLowerInvariant();

        return Sha256Hex(normalized)[..32];
    }

    private static string Sha256Hex(string input)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }
}