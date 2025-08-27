using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SkiaSharp;
using System.Security.Cryptography;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// 图片向量与 Caption 服务（多模态）。
/// - Caption：优先使用支持图像输入的多模态聊天模型，失败回退占位。
/// - 向量：优先使用 CLIP ONNX 模型，失败回退哈希伪向量。
/// </summary>
public class ClipImageEmbeddingService : IImageEmbeddingService, IDisposable
{
    private const int TargetDim = 1024;
    private readonly ILogger<ClipImageEmbeddingService> _logger;
    private readonly IChatCompletionService? _chat;
    private readonly string? _modelPath;
    private InferenceSession? _session;
    private bool _initAttempted;

    public ClipImageEmbeddingService(ILogger<ClipImageEmbeddingService> logger, IServiceProvider sp)
    {
        _logger = logger;
        _chat = sp.GetService<IChatCompletionService>();
        _modelPath = Environment.GetEnvironmentVariable("CLIP_IMAGE_ONNX")
                     ?? Path.Combine(AppContext.BaseDirectory, "models", "clip-image.onnx");
    }

    public Task<Embedding<float>> GenerateAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        try
        {
            EnsureSession();
            if (_session is not null)
            {
                var tensor = PreprocessToTensor(imageBytes);
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("pixel_values", tensor)
                };
                using var results = _session.Run(inputs);
                var output = results.First().AsEnumerable<float>().ToArray();
                var vec = NormalizeAndResize(output, TargetDim);
                return Task.FromResult(new Embedding<float>(vec));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CLIP embedding failed, fallback to hash vector");
        }

        // Fallback to hash-based vector
        return Task.FromResult(new Embedding<float>(HashToVector(imageBytes, TargetDim)));
    }

    public async Task<string> GenerateCaptionAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (_chat is not null)
        {
            try
            {
                var history = new ChatHistory();
                history.AddUserMessage(
                [
                    new Microsoft.SemanticKernel.TextContent("用不超过20字客观描述这张图（不得臆测未出现的数字/文字）"),
                    new ImageContent(imageBytes, "image/png"),
                ]);
                var resp = await _chat.GetChatMessageContentAsync(history, cancellationToken: ct);
                var text = resp?.Content?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (text.Length > 60) text = text[..60];
                    return text!;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Multimodal caption generation failed, fallback placeholder");
            }
        }
        return "(图像内容待解析)";
    }

    private void EnsureSession()
    {
        if (_initAttempted) return;
        _initAttempted = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(_modelPath) && File.Exists(_modelPath))
            {
                _session = new InferenceSession(_modelPath);
                _logger.LogInformation("Loaded CLIP image ONNX model: {Path}", _modelPath);
            }
            else
            {
                _logger.LogWarning("CLIP model not found at {Path}, using hash fallback", _modelPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to init CLIP model session; fallback to hash embedding");
        }
    }

    private static DenseTensor<float> PreprocessToTensor(byte[] bytes)
    {
        try
        {
            // 使用 SkiaSharp 解码和预处理图像
            using var skiaImage = SKImage.FromEncodedData(bytes);
            if (skiaImage == null) throw new InvalidOperationException("Failed to decode image");

            using var surface = SKSurface.Create(new SKImageInfo(224, 224, SKColorType.Rgba8888));
            var canvas = surface.Canvas;

            // 将图像缩放到 224x224
            canvas.DrawImage(skiaImage, new SKRect(0, 0, 224, 224), SKSamplingOptions.Default);

            // 获取像素数据
            using var snapshot = surface.Snapshot();
            using var pixmap = snapshot.PeekPixels();

            // 转换为 CLIP 格式：CHW (3, 224, 224) 且归一化
            var data = new float[3 * 224 * 224];

            // CLIP 使用的 ImageNet 标准化参数
            var mean = new[] { 0.48145466f, 0.4578275f, 0.40821073f };
            var std = new[] { 0.26862954f, 0.26130258f, 0.27577711f };

            unsafe
            {
                var pixelPtr = (uint*)pixmap.GetPixels();

                for (int y = 0; y < 224; y++)
                {
                    for (int x = 0; x < 224; x++)
                    {
                        var pixelIndex = y * 224 + x;
                        var pixel = pixelPtr[pixelIndex];

                        // 提取 RGB 分量 (RGBA 格式)
                        var r = (pixel >> 0) & 0xFF;   // Red
                        var g = (pixel >> 8) & 0xFF;   // Green  
                        var b = (pixel >> 16) & 0xFF;  // Blue

                        // 归一化到 [0, 1] 然后应用 ImageNet 标准化
                        var rNorm = (r / 255.0f - mean[0]) / std[0];
                        var gNorm = (g / 255.0f - mean[1]) / std[1];
                        var bNorm = (b / 255.0f - mean[2]) / std[2];

                        // CHW 格式：Channel 优先
                        data[0 * 224 * 224 + y * 224 + x] = rNorm; // R channel
                        data[1 * 224 * 224 + y * 224 + x] = gNorm; // G channel  
                        data[2 * 224 * 224 + y * 224 + x] = bNorm; // B channel
                    }
                }
            }

            return new DenseTensor<float>(data, new[] { 1, 3, 224, 224 });
        }
        catch (Exception)
        {
            // 解码失败时返回零填充张量
            var fallbackData = new float[1 * 3 * 224 * 224];
            return new DenseTensor<float>(fallbackData, new[] { 1, 3, 224, 224 });
        }
    }

    private static float[] NormalizeAndResize(float[] src, int dim)
    {
        if (src.Length == 0) return new float[dim];
        double norm = Math.Sqrt(src.Sum(v => v * v));
        if (norm == 0) norm = 1;
        var normalized = src.Select(v => (float)(v / norm)).ToArray();
        if (normalized.Length == dim) return normalized;
        var dst = new float[dim];
        for (int i = 0; i < dim; i++) dst[i] = normalized[i % normalized.Length];
        return dst;
    }

    private static float[] HashToVector(byte[] bytes, int dim)
    {
        using var sha = SHA256.Create();
        var h = sha.ComputeHash(bytes);
        var vec = new float[dim];
        for (int i = 0; i < dim; i++) vec[i] = h[i % h.Length] / 255f;
        return vec;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
