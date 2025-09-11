using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// Document-centric local file system image storage service.
/// Organizes images in document-specific directories for better isolation and management.
/// </summary>
public sealed class LocalImageStorageService : IImageStorageService
{
    private readonly ILogger<LocalImageStorageService> _logger;
    
    // Configuration constants
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB
    private const string DefaultExtension = ".png";
    
    // Pre-compiled regex for filename cleanup
    private static readonly Regex FileNameCleanupRegex = new(@"[<>:""/\\|?*\x00-\x1f]", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg"
    };

    public LocalImageStorageService(ILogger<LocalImageStorageService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalImageStorageService>.Instance;
    }

    public async Task<string> SaveImageAsync(byte[] imageBytes, string? fileName, string documentPath, CancellationToken cancellationToken = default)
    {
        ValidateInputs(imageBytes, fileName ?? string.Empty, documentPath);
        
        // Generate GUID-based filename if not provided
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{Guid.NewGuid()}.png";
        }
        
        // Ensure document's image directory exists
        EnsureDocumentStorageAvailable(documentPath);
        
        var imageDir = GetDocumentImageDirectory(documentPath);
        var safeFileName = GenerateSafeFileName(fileName);
        var fullPath = Path.Combine(imageDir, safeFileName);

        try
        {
            _logger.LogDebug("Saving image to: {FullPath}, Size: {Size} bytes", fullPath, imageBytes.Length);
            
            // Directly overwrite if file exists
            await File.WriteAllBytesAsync(fullPath, imageBytes, cancellationToken);
            
            _logger.LogInformation("Successfully saved image: {FileName} for document: {DocumentPath}", 
                safeFileName, documentPath);
            
            return fullPath;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to save image {FileName} for document {DocumentPath}", 
                safeFileName, documentPath);
            throw new InvalidOperationException($"Failed to save image: {ex.Message}", ex);
        }
    }

    public string ResolveImagePath(string imagePath, string documentPath)
    {
        if (string.IsNullOrEmpty(imagePath))
            return string.Empty;

        // Handle file:// protocol
        if (imagePath.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            var decodedPath = Uri.UnescapeDataString(imagePath[8..]);
            return decodedPath.Replace('/', Path.DirectorySeparatorChar);
        }

        // Handle absolute paths
        if (Path.IsPathRooted(imagePath))
        {
            return imagePath;
        }

        // Handle relative paths with multiple resolution strategies
        var documentDir = Path.GetDirectoryName(documentPath) ?? throw new ArgumentException("Invalid document path", nameof(documentPath));
        
        // 1. Check relative to document directory
        var relativeToDoc = Path.Combine(documentDir, imagePath);
        if (File.Exists(relativeToDoc))
        {
            return relativeToDoc;
        }

        // 2. Check relative to document's image directory
        var documentImageDir = GetDocumentImageDirectory(documentPath);
        var relativeToImageDir = Path.Combine(documentImageDir, imagePath);
        if (File.Exists(relativeToImageDir))
        {
            return relativeToImageDir;
        }

        // 3. Return potential path (for new files)
        return relativeToDoc;
    }

    private string GetDocumentImageDirectory(string documentPath)
    {
        var documentDir = Path.GetDirectoryName(documentPath) 
            ?? throw new ArgumentException("Invalid document path", nameof(documentPath));
        var documentName = Path.GetFileNameWithoutExtension(documentPath);
        
        // Clean document name to ensure it's a valid directory name
        var cleanDocumentName = FileNameCleanupRegex.Replace(documentName, "_");
        if (string.IsNullOrWhiteSpace(cleanDocumentName))
        {
            cleanDocumentName = "document";
        }
        
        return Path.Combine(documentDir, cleanDocumentName);
    }

    private void EnsureDocumentStorageAvailable(string documentPath)
    {
        try
        {
            var imageDir = GetDocumentImageDirectory(documentPath);
            if (!Directory.Exists(imageDir))
            {
                Directory.CreateDirectory(imageDir);
                _logger.LogDebug("Created document images directory: {ImagesDir}", imageDir);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create document images directory for: {DocumentPath}", documentPath);
            throw new InvalidOperationException($"Cannot create document image storage directory: {documentPath}", ex);
        }
    }

    private static void ValidateInputs(byte[] imageBytes, string fileName, string documentPath)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            throw new ArgumentException("Image data cannot be null or empty", nameof(imageBytes));

        if (imageBytes.Length > MaxFileSizeBytes)
            throw new ArgumentException($"Image size {imageBytes.Length} bytes exceeds maximum allowed size {MaxFileSizeBytes} bytes", nameof(imageBytes));

        if (string.IsNullOrWhiteSpace(documentPath))
            throw new ArgumentException("Document path cannot be null or empty", nameof(documentPath));
    }

    private static string GenerateSafeFileName(string fileName)
    {
        // Clean illegal characters from filename
        var cleanName = FileNameCleanupRegex.Replace(fileName.Trim(), "_");
        
        // Prevent path traversal attacks
        cleanName = Path.GetFileName(cleanName);
        
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            return Guid.NewGuid().ToString("N") + DefaultExtension;
        }

        var extension = Path.GetExtension(cleanName);
        var baseName = Path.GetFileNameWithoutExtension(cleanName);
        
        // Validate extension
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            extension = DefaultExtension;
        }
        
        // Ensure base name is not empty
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = Guid.NewGuid().ToString("N");
        }

        // Limit filename length
        if (baseName.Length > 100)
        {
            baseName = baseName[..100];
        }

        return baseName + extension;
    }
}
