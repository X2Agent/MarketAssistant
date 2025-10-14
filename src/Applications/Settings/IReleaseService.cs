namespace MarketAssistant.Applications.Settings;

/// <summary>
/// 版本发布服务接口，用于版本检查和更新
/// </summary>
public interface IReleaseService
{
    /// <summary>
    /// 检查是否有新版本
    /// </summary>
    /// <param name="currentVersion">当前版本（支持格式：1.0.0 或 1.0.0.0）</param>
    /// <param name="includePrerelease">是否包含预览版（默认true，适合快速迭代的项目前期）</param>
    /// <returns>版本检查结果</returns>
    /// <exception cref="FriendlyException">当检查更新失败时抛出</exception>
    Task<UpdateCheckResult> CheckForUpdateAsync(string currentVersion, bool includePrerelease = true);

    /// <summary>
    /// 下载更新文件
    /// </summary>
    /// <param name="downloadUrl">下载地址</param>
    /// <param name="savePath">保存路径</param>
    /// <param name="progress">下载进度回调（0.0 到 1.0）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>下载完成的文件路径</returns>
    /// <exception cref="FriendlyException">当下载失败时抛出</exception>
    /// <exception cref="OperationCanceledException">当下载被取消时抛出</exception>
    Task<string> DownloadUpdateAsync(string downloadUrl, string savePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除缓存的版本信息
    /// </summary>
    void ClearCache();
}

