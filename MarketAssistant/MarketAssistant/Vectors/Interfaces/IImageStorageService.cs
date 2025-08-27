namespace MarketAssistant.Vectors.Interfaces;

public interface IImageStorageService
{
    /// <summary>
    /// Persist image bytes and return a relative uri (for example: "images/{hash}.png").
    /// Implementations decide how/where to store.
    /// </summary>
    Task<string> SaveImageAsync(byte[] imageBytes, string fileNameHint, CancellationToken ct = default);

    /// <summary>
    /// Ensure the storage container/directory exists (no-op for some implementations).
    /// </summary>
    void EnsureStorageAvailable();
}
