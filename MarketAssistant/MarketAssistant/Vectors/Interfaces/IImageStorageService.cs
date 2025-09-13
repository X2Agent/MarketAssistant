namespace MarketAssistant.Vectors.Interfaces;

/// <summary>
/// Simple image storage service with document-centric organization.
/// Handles both saving new images and resolving paths to existing images.
/// </summary>
public interface IImageStorageService
{
    /// <summary>
    /// Save image bytes in a document-specific directory and return the stored path.
    /// </summary>
    /// <param name="imageBytes">The image data to store</param>
    /// <param name="fileName">The desired filename for the image (auto-generated if null/empty)</param>
    /// <param name="documentPath">The path of the document this image belongs to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The absolute path where the image was stored</returns>
    Task<string> SaveImageAsync(byte[] imageBytes, string? fileName, string documentPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve an image path to an absolute path, supporting various formats:
    /// - Absolute paths (returned as-is)
    /// - file:// protocol URLs
    /// - Relative paths (resolved relative to document directory or document's image directory)
    /// </summary>
    /// <param name="imagePath">The image path to resolve</param>
    /// <param name="documentPath">The document context for resolution</param>
    /// <returns>The absolute path to the image</returns>
    string ResolveImagePath(string imagePath, string documentPath);
}
