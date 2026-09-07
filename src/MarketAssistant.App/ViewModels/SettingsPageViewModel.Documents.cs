using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 设置页 ViewModel 的文档向量化与文件选择部分。
/// 向量化核心逻辑已下沉到 <see cref="DocumentVectorizationService"/>，本部分仅保留 UI 状态与用户交互反馈。
/// </summary>
public partial class SettingsPageViewModel
{
    public bool IsKnowledgeDirectoryValid => !string.IsNullOrEmpty(UserSetting.KnowledgeFileDirectory) && Directory.Exists(UserSetting.KnowledgeFileDirectory);

    [ObservableProperty]
    private bool _isVectorizing;

    // 向量化进度（0-100）
    [ObservableProperty]
    private int _vectorizingProgress;

    [ObservableProperty]
    private string _vectorizingProgressText = "";

    /// <summary>
    /// 选择知识库目录
    /// </summary>
    [RelayCommand]
    private async Task SelectKnowledgeDirectory()
    {
        if (_storageProvider == null) return;

        await SafeExecuteAsync(async () =>
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择知识库目录",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                UserSetting.KnowledgeFileDirectory = folders[0].Path.LocalPath;
            }
        }, "选择知识库目录");
    }

    /// <summary>
    /// 选择日志路径
    /// </summary>
    [RelayCommand]
    private async Task SelectLogPath()
    {
        if (_storageProvider == null) return;

        await SafeExecuteAsync(async () =>
        {
            var folders = await _storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择日志路径",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                UserSetting.LogPath = Path.Combine(folders[0].Path.LocalPath, "logs");
            }
        }, "选择日志路径");
    }

    [RelayCommand]
    private async Task VectorizeDocuments()
    {
        if (!IsKnowledgeDirectoryValid)
        {
            _notificationService.ShowWarning("知识库目录无效，请先选择有效的目录");
            Logger?.LogWarning("知识库目录无效，无法进行向量化");
            return;
        }

        if (!_documentVectorizationService.TryBeginVectorization())
        {
            _notificationService.ShowWarning("已有一个向量化任务在后台进行中，请等待其完成后再试");
            Logger?.LogWarning("拒绝并发的向量化请求");
            return;
        }

        var cts = new CancellationTokenSource();
        _vectorizationCts = cts;
        try
        {
            IsVectorizing = true;
            VectorizingProgress = 0;
            VectorizingProgressText = "准备中...";

            Logger?.LogInformation("开始向量化知识库目录: {Directory}", UserSetting.KnowledgeFileDirectory);

            var progress = new Progress<(int Percent, string Text)>(p =>
            {
                VectorizingProgress = p.Percent;
                VectorizingProgressText = p.Text;
            });

            var result = await _documentVectorizationService.VectorizeDirectoryAsync(
                UserSetting.KnowledgeFileDirectory,
                UserSetting.VectorCollectionName,
                progress,
                cts.Token);

            if (result is null)
            {
                _notificationService.ShowWarning($"未找到支持的文档（支持：{string.Join(", ", DocumentVectorizationService.SupportedExtensions)}）");
                Logger?.LogWarning("知识库目录中没有找到支持的文档");
                return;
            }

            // 显示完成消息（三态：完全成功 / 部分成功 / 失败）
            VectorizingProgress = 100;
            if (result.AllSucceeded)
            {
                VectorizingProgressText = $"✅ 全部完成！共 {result.SuccessCount} 个文件";
                _notificationService.ShowSuccess($"✅ 所有文档向量化完成！\n成功处理 {result.SuccessCount} 个文件");
                Logger?.LogInformation("向量化完成：成功 {Success}/{Total} 个", result.SuccessCount, result.TotalCount);
            }
            else
            {
                var summaryText = $"⚠️ 完成（存在失败）: {result.SuccessCount} 成功, {result.PartialCount} 部分成功, {result.FailedCount} 失败";
                VectorizingProgressText = summaryText;

                var failedList = string.Join("\n- ", result.FailedFiles.Take(5));
                if (result.FailedFiles.Count > 5)
                {
                    failedList += $"\n... 还有 {result.FailedFiles.Count - 5} 个";
                }

                var partialList = string.Join("\n- ", result.PartialFiles.Take(5));
                if (result.PartialFiles.Count > 5)
                {
                    partialList += $"\n... 还有 {result.PartialFiles.Count - 5} 个";
                }

                _notificationService.ShowWarning(
                    $"向量化完成：\n✓ 完全成功 {result.SuccessCount} 个\n△ 部分成功 {result.PartialCount} 个\n✗ 失败 {result.FailedCount} 个" +
                    (result.PartialFiles.Count > 0 ? $"\n\n部分成功（存在失败块）：\n- {partialList}" : string.Empty) +
                    (result.FailedFiles.Count > 0 ? $"\n\n失败文件：\n- {failedList}" : string.Empty));

                Logger?.LogWarning("向量化完成：成功 {Success} 个，部分成功 {Partial} 个，失败 {Failed} 个，总计 {Total} 个",
                    result.SuccessCount, result.PartialCount, result.FailedCount, result.TotalCount);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // 取消发生在文件间隙或准备阶段
            VectorizingProgressText = "向量化已取消";
            _notificationService.ShowWarning("向量化已取消。已完成的部分保持有效。");
            Logger?.LogWarning("向量化被用户取消");
        }
        catch (Exception ex)
        {
            VectorizingProgressText = "向量化失败";
            Logger?.LogError(ex, "向量化过程发生严重错误");
            _notificationService.ShowError(ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, "向量化"));
        }
        finally
        {
            IsVectorizing = false;
            if (ReferenceEquals(_vectorizationCts, cts))
                _vectorizationCts = null;
            cts.Dispose();
            _documentVectorizationService.EndVectorization();
        }
    }
}
