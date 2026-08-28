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
/// CLIP图像嵌入服务（多模态RAG的核心组件）
/// 
/// 【学习要点】：
/// 1. 多模态AI：处理图像和文本的AI模型
/// 2. CLIP模型：OpenAI发布的多模态模型，可以将图像和文本映射到同一个空间
/// 3. 降级策略：构建系统的可靠性保障，在服务失败时自动切换到备用方案
/// 4. ONNX运行时：跨平台的机器学习推理引擎，用于部署机器学习模型
/// 
/// 【功能概要】：
/// - 双重能力：图像嵌入（向量化） + 图像描述生成（Caption）
/// - 分层降级：CLIP模型 -> 哈希（图像） / 多模态服务 -> 占位符（文本）
/// - 资源管理：实现IDisposable自动释放ONNX会话资源
/// - 延迟初始化：首次调用时才加载模型，优化启动速度
/// 
/// 【技术栈】：
/// - Microsoft.ML.OnnxRuntime：ONNX模型推理
/// - SkiaSharp：跨平台图像处理
/// - Microsoft.SemanticKernel：多模态服务编排
/// - Microsoft.Extensions.AI：AI嵌入标准接口
/// </summary>
public class ClipImageEmbeddingService : IImageEmbeddingService, IDisposable
{
    // 目标嵌入向量维度，引用统一常量
    private const int TargetDim = RagConstants.EmbeddingDimension;

    // ImageNet 标准化参数（CLIP 模型训练时使用）
    private static readonly float[] ImageNetMean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] ImageNetStd = { 0.229f, 0.224f, 0.225f };

    // 【依赖注入】：服务的依赖项
    private readonly ILogger<ClipImageEmbeddingService> _logger;          // 结构化日志记录
    private readonly IChatCompletionService? _chat;                       // 多模态聊天服务（可选）
    private readonly string? _modelPath;                                  // CLIP ONNX模型文件路径

    // 【状态管理】：ONNX推理会话和初始化标志
    private InferenceSession? _session;                                   // ONNX运行时推理会话
    private volatile bool _initAttempted;                                 // 防止重复初始化的标志
    private readonly object _initLock = new();                            // 初始化锁：保证并发下仅创建一个 InferenceSession

    /// <summary>
    /// 构造函数：使用依赖注入获取服务，支持环境变量配置模型路径
    /// 
    /// 【学习要点】：
    /// - 依赖注入模式：通过IServiceProvider获取可选服务，遵循单一职责原则
    /// - 配置优先级：环境变量 > 默认路径，适应不同部署环境
    /// - 延迟加载：构造时不加载模型，首次使用时才初始化
    /// </summary>
    public ClipImageEmbeddingService(ILogger<ClipImageEmbeddingService> logger, IServiceProvider sp)
    {
        _logger = logger;
        // 尝试获取聊天服务（多模态Caption功能），可选。
        // 注意：此处为服务定位器模式，若 IChatCompletionService 为 Scoped 且本服务为 Singleton，
        // 会形成 captive dependency（Scoped 实例被 Singleton 捕获）。当前 IChatCompletionService
        // 注册为 Singleton，风险可控；如改为 Scoped 生命周期需重构为工厂委托注入。
        _chat = sp.GetService<IChatCompletionService>();

        // 模型路径配置：优先环境变量，否则使用本地默认路径
        _modelPath = Environment.GetEnvironmentVariable("CLIP_IMAGE_ONNX")
                     ?? Path.Combine(AppContext.BaseDirectory, "models", "clip-image.onnx");
    }

    /// <summary>
    /// 生成图像嵌入向量（RAG系统的核心功能）
    ///
    /// 【实现细节】：
    /// - ONNX推理：使用预训练CLIP模型进行图像编码
    /// - 预处理：将图像预处理为标准张量格式
    /// - 向量归一化：确保向量在单位超球面上，便于余弦相似度计算
    /// - 失败语义：任何失败都抛出 InvalidOperationException，由调用方降级为 Caption 文本召回；
    ///   不降级为哈希向量，也不产出零向量（P1-03）
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
    /// 解析ONNX模型的输入输出节点名称
    /// 
    /// 【自适应】：自动适配模型的图像输入和输出节点名称
    /// 常见名称：
    /// - imageInput: 图像输入节点名称 ("pixel_values", "image")  
    /// - imageOutput: 图像输出节点名称 ("image_embeds", "pooler_output")
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
    /// 创建模型输入列表（处理多输入模型）
    /// 
    /// 【兼容性】：
    /// 某些CLIP模型可能需要多输入（如文本+图像双塔模型）
    /// 此方法确保提供图像输入，并为不需要的文本输入提供空/默认值
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
    /// 生成图像描述（多模态生成能力，可选）
    /// 
    /// 【学习要点】：
    /// - 多模态提示：结合文本指令和图像数据的复杂提示
    /// - 降级保护：服务不可用或异常时，返回占位符
    /// - 异步处理：支持取消令牌，避免长时阻塞
    /// 
    /// 【业务价值】：
    /// - 增强搜索：为图像提供文本描述，支持文本搜索
    /// - 可访问性：辅助视障用户理解图像内容
    /// - 降级兼容：无文本描述时，图像内容仍可被索引（虽然不准确）
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
    /// 延迟初始化ONNX推理会话（单例/缓存模式）
    /// 
    /// 【学习要点】：
    /// - 延迟加载：避免启动时加载大模型影响性能
    /// - 状态锁：使用标志位确保只尝试一次，避免重复失败
    /// - 资源检查：验证模型文件存在性，提供清晰的错误信息
    /// - 异常吞没：初始化失败不影响降级功能的可用性
    /// 
    /// 【设计模式】：
    /// - 懒加载模式：通过标志位控制初始化
    /// - 资源管理：ONNX会话需在Dispose时正确释放
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
    /// 图像预处理：将原始字节转换为CLIP模型所需的标准张量
    /// 
    /// 【处理流程】：图像解码 -> 缩放到224x224 -> ImageNet标准化 -> CHW张量格式
    /// CLIP 模型训练时使用 ImageNet 均值和方差标准化，不做标准化会导致
    /// 图像嵌入与文本嵌入向量空间错位，跨模态检索失效。
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
    /// 向量归一化和维度调整（确保向量数学正确性）
    /// 
    /// 【学习要点】：
    /// - L2归一化：||v|| = 1，确保余弦相似度计算准确
    /// - 维度对齐：不同模型输出维度可能不同，需要统一
    /// - 鲁棒性：处理零向量和维度不匹配情况
    /// - 零填充/截断：避免循环填充引入周期性模式破坏余弦相似度
    /// 
    /// 【数学原理】：
    /// - L2范数：||v|| = sqrt(v1² + v2² + ... + vn²)
    /// - 归一化：v_norm = v / ||v||
    /// - 余弦相似度：cos(θ) = (a·b) / (||a|| · ||b||)，归一化后为 a·b
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
    /// 资源释放：正确释放ONNX推理会话
    ///
    /// 【学习要点】：
    /// - 资源管理：ONNX会话包含非托管资源，需显式释放
    /// - IDisposable模式：.NET资源管理的标准模式
    /// - 内存泄漏预防：机器学习模型通常占用大量内存
    /// - 最佳实践：在容器生命周期结束时调用Dispose
    /// 
    /// 【实现细节】：
    /// - 显式释放：避免长期占用GPU/CPU内存
    /// - 空值检查：_session可能为null（重复释放安全）
    /// - 托管资源：_logger, _chatCompletion等由DI容器管理，无需手动释放
    /// </summary>
    public void Dispose()
    {
        // 资源释放：释放ONNX会话的非托管资源
        _session?.Dispose();
        // 注意：托管字段（_logger, _chatCompletion等）由DI容器自动管理
    }
}
