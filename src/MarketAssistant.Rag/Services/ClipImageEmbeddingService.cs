using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SkiaSharp;
using System.Security.Cryptography;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// CLIP 图像嵌入服务（多模态 RAG 组件）。
/// 双重能力：图像嵌入（ONNX 推理）+ 图像描述生成（Caption，可选多模态服务）。
/// 降级策略：图像嵌入失败抛出异常由调用方降级为 Caption 文本召回；
/// Caption 不可用或失败时返回占位符。ONNX 会话延迟到首次调用时加载。
/// </summary>
public class ClipImageEmbeddingService : IImageEmbeddingService, IDisposable
{
    // 目标嵌入向量维度，引用统一常量
    private const int TargetDim = RagConstants.EmbeddingDimension;

    // ImageNet 标准化参数（CLIP 模型训练时使用）
    private static readonly float[] ImageNetMean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] ImageNetStd = { 0.229f, 0.224f, 0.225f };

    private readonly ILogger<ClipImageEmbeddingService> _logger;
    private readonly IChatCompletionService? _chat;
    private readonly string? _modelPath;

    private InferenceSession? _session;
    private volatile bool _initAttempted;
    private readonly object _initLock = new();

    /// <summary>
    /// 构造函数：模型路径优先取环境变量 CLIP_IMAGE_ONNX，否则用本地默认路径。
    /// Caption 客户端由工厂延迟创建，AI 未配置时为 null（Caption 降级为占位符）。
    /// </summary>
    public ClipImageEmbeddingService(ILogger<ClipImageEmbeddingService> logger, IImageCaptionClientFactory captionClientFactory)
    {
        _logger = logger;
        _chat = captionClientFactory.Create();

        _modelPath = Environment.GetEnvironmentVariable("CLIP_IMAGE_ONNX")
                     ?? Path.Combine(AppContext.BaseDirectory, "models", "clip-image.onnx");
    }

    /// <summary>
    /// 生成图像嵌入向量（ONNX 推理 + 归一化）。
    /// 失败语义：任何失败都抛出 InvalidOperationException，由调用方降级为 Caption 文本召回；
    /// 不降级为哈希向量，也不产出零向量（P1-03）。
    /// </summary>
    public async Task<Embedding<float>> GenerateAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        try
        {
            // 取消优先于一切降级/不可用判断
            ct.ThrowIfCancellationRequested();

            // 步骤1：确保ONNX会话已初始化
            EnsureSession();
            if (_session is not null)
            {
                // 步骤2：解析模型的输入输出结构
                var (imageInput, imageOutput) = ResolveVisionIO();
                if (imageInput == null || imageOutput == null)
                {
                    _logger.LogWarning("Cannot resolve model input/output structure; image embedding unavailable");
                    throw new InvalidOperationException("CLIP 模型输入输出结构无法解析，图像嵌入不可用");
                }

                // 步骤3~6：在后台线程执行预处理 + 推理 + 后处理（支持取消）
                var vec = await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    // 图像预处理：字节数组 -> 标准张量
                    var tensor = PreprocessToTensor(imageBytes);

                    // 创建模型输入（处理多输入情况）
                    var inputs = CreateModelInputs(imageInput, tensor);

                    // 执行ONNX推理并获取输出向量
                    using var results = _session.Run(inputs, new[] { imageOutput });
                    var output = results.First().AsEnumerable<float>().ToArray();

                    // 后处理：归一化和调整维度
                    return NormalizeAndResize(output, TargetDim);
                }, ct).ConfigureAwait(false);

                return new Embedding<float>(vec);
            }

            // P1-03：CLIP 模型不可用时不再降级为哈希向量
            throw new InvalidOperationException("CLIP 模型不可用，图像嵌入未生成");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // P1-03：CLIP 失败不再降级为哈希向量——哈希向量无语义，
            // 不得混入与文本同维度的语义检索空间。调用方应跳过图像向量并依赖 Caption 文本召回。
            _logger.LogWarning(ex, "CLIP 图像嵌入失败，本图不生成图像向量（Caption 文本召回不受影响）");
            throw new InvalidOperationException("CLIP 图像嵌入不可用", ex);
        }
    }

    /// <summary>
    /// 解析 ONNX 模型的输入输出节点名称，自动适配不同 CLIP 导出模型的常见命名。
    /// </summary>
    private (string? imageInput, string? imageOutput) ResolveVisionIO()
    {
        if (_session == null) return (null, null);

        try
        {
            // 候选输入节点名称
            var inputCandidates = new[] { "pixel_values", "image", "input", "images" };
            var outputCandidates = new[] { "image_embeds", "pooler_output", "last_hidden_state", "embeddings", "output" };

            // 查找匹配的节点名称
            var imageInput = inputCandidates.FirstOrDefault(name => _session.InputMetadata.ContainsKey(name))
                           ?? _session.InputMetadata.Keys.FirstOrDefault(); // 兜底：使用第一个输入

            var imageOutput = outputCandidates.FirstOrDefault(name => _session.OutputMetadata.ContainsKey(name))
                            ?? _session.OutputMetadata.Keys.FirstOrDefault(); // 兜底：使用第一个输出

            _logger.LogDebug("Resolved model IO: input={Input}, output={Output}", imageInput, imageOutput);
            return (imageInput, imageOutput);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve model IO structure");
            return (null, null);
        }
    }

    /// <summary>
    /// 创建模型输入列表。部分 CLIP 导出模型带文本双塔输入，
    /// 对非图像输入补空张量，保证单图推理可执行。
    /// </summary>
    private List<NamedOnnxValue> CreateModelInputs(string imageInputName, DenseTensor<float> imageTensor)
    {
        var inputs = new List<NamedOnnxValue>();

        // 添加图像输入
        inputs.Add(NamedOnnxValue.CreateFromTensor(imageInputName, imageTensor));

        // 处理其他可能需要的输入
        foreach (var inputMeta in _session!.InputMetadata)
        {
            if (inputMeta.Key == imageInputName) continue; // 跳过图像输入

            // 处理文本输入（input_ids, text等）
            if (inputMeta.Key.Contains("input_ids") || inputMeta.Key.Contains("text"))
            {
                // 创建空文本输入（表示只处理图像）
                var textShape = inputMeta.Value.Dimensions.ToArray();
                if (textShape.Any(d => d <= 0)) textShape = new[] { 1, 1 }; // 默认形状

                var emptyTextTensor = new DenseTensor<long>(new long[textShape.Aggregate(1, (a, b) => a * b)], textShape);
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputMeta.Key, emptyTextTensor));
                _logger.LogDebug("Added empty text input: {InputName} with shape [{Shape}]",
                    inputMeta.Key, string.Join(", ", textShape));
            }
            // 处理attention_mask输入
            else if (inputMeta.Key.Contains("attention_mask"))
            {
                var maskShape = inputMeta.Value.Dimensions.ToArray();
                if (maskShape.Any(d => d <= 0)) maskShape = new[] { 1, 1 };

                var emptyMaskTensor = new DenseTensor<long>(new long[maskShape.Aggregate(1, (a, b) => a * b)], maskShape);
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputMeta.Key, emptyMaskTensor));
                _logger.LogDebug("Added empty attention mask: {InputName} with shape [{Shape}]",
                    inputMeta.Key, string.Join(", ", maskShape));
            }
        }

        return inputs;
    }

    /// <summary>
    /// 生成图像描述（多模态生成能力，可选）。
    /// 多模态服务不可用或生成失败时降级为占位符，图像仍可被索引。
    /// </summary>
    public async Task<string> CaptionAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        // 检查点：使用多模态聊天服务生成描述
        if (_chat is not null)
        {
            try
            {
                // 构建多模态对话历史（文本指令 + 图像）
                var history = new ChatHistory("Your job is describing images.");
                history.AddUserMessage(
                [
                    // 文本指令：要求客观、简洁地描述
                    new Microsoft.SemanticKernel.TextContent("请用不超过20个字客观描述这张图片的内容。不要出现“这张图片”/“照片”等字样。"),
                    // 图像内容：作为模态输入
                    new ImageContent(imageBytes, "image/png"),
                ]);

                // 调用多模态LLM生成回复
                var resp = await _chat.GetChatMessageContentAsync(history, cancellationToken: ct);
                var text = resp?.Content?.Trim();

                // 结果验证和截断
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // 限制最大长度，避免过长描述
                    if (text.Length > 60) text = text[..60];
                    return text!;
                }
            }
            catch (Exception ex)
            {
                // 异常处理：多模态生成失败时记录日志
                _logger.LogDebug(ex, "Multimodal caption generation failed, fallback placeholder");
            }
        }

        // 降级策略：返回占位符（生成失败）
        return "(图像内容生成失败)";
    }

    /// <summary>
    /// 延迟初始化 ONNX 推理会话。双检查锁保证并发下仅创建一个会话；
    /// 初始化失败不抛出（仅标记已尝试），由 GenerateAsync 因会话不可用而失败，
    /// 调用方降级为 Caption 文本召回。
    /// </summary>
    private void EnsureSession()
    {
        // 快速路径：已初始化过（无论成败）则直接返回
        if (_initAttempted) return;

        // 双检查锁：并发首次调用时仅允许一个线程创建 InferenceSession，
        // 其余线程等待后复用同一会话（或复用"初始化已失败"的结果），不会重复加载模型
        lock (_initLock)
        {
            if (_initAttempted) return;
            try
            {
                // 资源检查：验证模型文件路径
                if (!string.IsNullOrWhiteSpace(_modelPath) && File.Exists(_modelPath))
                {
                    // 创建ONNX运行时推理会话
                    _session = new InferenceSession(_modelPath);

                    // 记录模型信息（输入输出节点）便于调试
                    LogModelInfo();

                    _logger.LogInformation("Loaded CLIP image ONNX model: {Path}", _modelPath);
                }
                else
                {
                    // 警告：配置的模型不存在时发出提示（不降级为哈希向量，图像嵌入将不可用）
                    _logger.LogWarning("CLIP model not found at {Path}, image embedding will be unavailable", _modelPath);
                }
            }
            catch (Exception ex)
            {
                // 初始化失败：记录错误但不抛出，GenerateAsync 会因会话不可用而抛出，
                // 由调用方降级为 Caption 文本召回
                _logger.LogWarning(ex, "Failed to init CLIP model session; image embedding will be unavailable");
            }
            finally
            {
                // 无论成败只尝试一次，避免失败后反复加载模型
                _initAttempted = true;
            }
        }
    }

    /// <summary>
    /// 记录ONNX模型的输入输出信息（用于调试和验证）
    /// </summary>
    private void LogModelInfo()
    {
        if (_session == null) return;

        try
        {
            _logger.LogInformation("ONNX Model Information:");

            // 输入节点信息
            _logger.LogInformation("Inputs:");
            foreach (var input in _session.InputMetadata)
            {
                var shape = string.Join(", ", input.Value.Dimensions);
                _logger.LogInformation("  - {Name}: {Type} [{Shape}]", input.Key, input.Value.ElementType, shape);
            }

            // 输出节点信息
            _logger.LogInformation("Outputs:");
            foreach (var output in _session.OutputMetadata)
            {
                var shape = string.Join(", ", output.Value.Dimensions);
                _logger.LogInformation("  - {Name}: {Type} [{Shape}]", output.Key, output.Value.ElementType, shape);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log model metadata");
        }
    }

    /// <summary>
    /// 图像预处理：解码 → 缩放到 224x224 → ImageNet 标准化 → CHW 张量。
    /// 必须用 CLIP 训练时的均值方差标准化，否则图像与文本嵌入向量空间错位，
    /// 跨模态检索失效。预处理失败必须抛出，静默零张量会污染多模态召回。
    /// </summary>
    private static DenseTensor<float> PreprocessToTensor(byte[] bytes)
    {
        const int size = 224;
        const int channels = 3;

        try
        {
            using var original = SKBitmap.Decode(bytes);
            if (original == null) throw new InvalidOperationException("Failed to decode image");

            using var resized = new SKBitmap(size, size);
            original.ScalePixels(resized, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            // 准备数据数组（注意batch维度）
            var tensorData = new float[1 * channels * size * size];
            var pixels = resized.Pixels;

            // CHW格式：Channel-Height-Width
            // 先归一化到 [0,1]，再应用 ImageNet 标准化：(x - mean) / std
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var pixel = pixels[y * size + x];
                    var r = pixel.Red / 255f;
                    var g = pixel.Green / 255f;
                    var b = pixel.Blue / 255f;

                    var baseIndex = y * size + x;
                    // CHW格式：[batch, channel, height, width]，应用 ImageNet 标准化
                    tensorData[0 * size * size + baseIndex] = (r - ImageNetMean[0]) / ImageNetStd[0];
                    tensorData[1 * size * size + baseIndex] = (g - ImageNetMean[1]) / ImageNetStd[1];
                    tensorData[2 * size * size + baseIndex] = (b - ImageNetMean[2]) / ImageNetStd[2];
                }
            }

            return new DenseTensor<float>(tensorData, new[] { 1, channels, size, size });
        }
        catch (Exception ex)
        {
            // 预处理失败必须抛出：静默返回全零张量会被当作合法输入跑完推理，
            // 产出语义上无意义的零向量入库，污染多模态召回
            throw new InvalidOperationException($"图像预处理失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 向量归一化与维度调整：L2 归一化保证余弦相似度计算准确；
    /// 目标维度不足零填充、超长截断（零填充不引入伪周期模式）。
    /// </summary>
    /// <param name="src">原始输出向量</param>
    /// <param name="dim">目标维度</param>
    /// <returns>归一化且调整维度后的向量</returns>
    private static float[] NormalizeAndResize(float[] src, int dim)
    {
        // 边界检查：空向量处理
        if (src.Length == 0) return new float[dim];

        // 计算L2范数（欧几里得范数）
        double norm = Math.Sqrt(src.Sum(v => v * v));
        if (norm == 0) norm = 1;  // 避免除以零

        // 归一化：每个分量除以范数
        var normalized = src.Select(v => (float)(v / norm)).ToArray();

        // 维度匹配：若维度已正确则直接返回
        if (normalized.Length == dim) return normalized;

        // 维度调整：源向量短则零填充，长则截断
        // 零填充不会引入伪周期模式，保持向量语义完整性
        var dst = new float[dim];
        var copyLen = Math.Min(normalized.Length, dim);
        Array.Copy(normalized, dst, copyLen);

        return dst;
    }

    /// <summary>
    /// 释放 ONNX 推理会话的非托管资源（重复释放安全）。
    /// </summary>
    public void Dispose()
    {
        // 资源释放：释放ONNX会话的非托管资源
        _session?.Dispose();
        // 注意：托管字段（_logger, _chatCompletion等）由DI容器自动管理
    }
}
