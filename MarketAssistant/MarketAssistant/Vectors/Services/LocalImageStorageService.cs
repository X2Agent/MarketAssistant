using MarketAssistant.Vectors.Interfaces;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// Simple local filesystem image storage. Stores under AppContext.BaseDirectory/images by default.
/// </summary>
public class LocalImageStorageService : IImageStorageService
{
    private readonly string _imagesDir;

    public LocalImageStorageService(string? root = null)
    {
        _imagesDir = Path.Combine(root ?? AppContext.BaseDirectory, "images");
    }

    public void EnsureStorageAvailable()
    {
        Directory.CreateDirectory(_imagesDir);
    }

    public async Task<string> SaveImageAsync(byte[] imageBytes, string fileNameHint, CancellationToken ct = default)
    {
        EnsureStorageAvailable();
        var safeName = string.IsNullOrWhiteSpace(fileNameHint) ? Guid.NewGuid().ToString("N") : fileNameHint;
        // if provided hint contains extension, preserve it; otherwise use .png
        var ext = Path.GetExtension(safeName);
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        var fileName = safeName + (ext.StartsWith('.') ? string.Empty : string.Empty);
        // if hint already has ext, avoid duplicating
        if (!Path.HasExtension(safeName)) fileName = safeName + ext;

        var path = Path.Combine(_imagesDir, fileName);
        await File.WriteAllBytesAsync(path, imageBytes, ct);
        return Path.Combine("images", fileName);
    }
}
