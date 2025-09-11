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
/// CLIP图像嵌入服务（多模态RAG的核心组件）
/// 
/// 【学习要点】：
/// 1. 多模态AI：结合图像理解和文本理解的AI模型
/// 2. CLIP模型：OpenAI开发的跨模态模型，可以将图像和文本映射到同一语义空间
/// 3. 降级机制：生产环境的可靠性保障，主方案失败时自动切换到备用方案
/// 4. ONNX运行时：跨平台的机器学习推理引擎，用于部署深度学习模型
/// 
/// 【架构设计】：
/// - 双功能设计：图像嵌入向量生成 + 图像描述文字生成（Caption）
/// - 分层降级：CLIP模型 → 哈希向量，多模态聊天 → 占位符文字
/// - 资源管理：实现IDisposable自动释放ONNX会话资源
/// - 延迟初始化：首次调用时才加载模型，提升启动性能
/// 
/// 【技术栈】：
/// - Microsoft.ML.OnnxRuntime：ONNX模型推理
/// - SkiaSharp：跨平台图像处理库
/// - Microsoft.SemanticKernel：多模态聊天能力
/// - Microsoft.Extensions.AI：向量嵌入标准接口
/// </summary>
public class ClipImageEmbeddingService : IImageEmbeddingService, IDisposable
{
    // 【配置常量】目标嵌入向量维度，与文本嵌入保持一致以便混合检索
    private const int TargetDim = 1024;

    // 【依赖注入】核心服务组件
    private readonly ILogger<ClipImageEmbeddingService> _logger;          // 结构化日志记录
    private readonly IChatCompletionService? _chat;                       // 多模态聊天服务（可选）
    private readonly string? _modelPath;                                  // CLIP ONNX模型文件路径

    // 【状态管理】ONNX推理会话和初始化标志
    private InferenceSession? _session;                                   // ONNX运行时推理会话
    private bool _initAttempted;                                          // 避免重复初始化的标志

    /// <summary>
    /// 构造函数：使用依赖注入获取服务，支持环境变量配置模型路径
    /// 
    /// 【学习要点】：
    /// - 依赖注入模式：通过IServiceProvider获取可选服务，遵循单一职责原则
    /// - 配置优先级：环境变量 > 默认路径，便于不同环境部署
    /// - 延迟加载：构造时不加载模型，首次使用时才初始化
    /// </summary>
    public ClipImageEmbeddingService(ILogger<ClipImageEmbeddingService> logger, IServiceProvider sp)
    {
        _logger = logger;
        // 尝试获取聊天服务（多模态Caption功能，可选）
        _chat = sp.GetService<IChatCompletionService>();

        // 模型路径配置：优先环境变量，后备默认路径
        _modelPath = Environment.GetEnvironmentVariable("CLIP_IMAGE_ONNX")
                     ?? Path.Combine(AppContext.BaseDirectory, "models", "clip-image.onnx");
    }

    /// <summary>
    /// 生成图像的嵌入向量（RAG系统的核心功能）
    /// 
    /// 【学习要点】：
    /// - 多层降级策略：CLIP模型推理 → 哈希向量，确保系统稳定性
    /// - 异常处理：捕获所有异常并降级，避免影响整体流程
    /// - 向量标准化：确保所有向量都是单位向量，便于余弦相似度计算
    /// - 维度统一：所有向量统一到TargetDim维度，支持混合检索
    /// 
    /// 【技术细节】：
    /// - ONNX推理：使用预训练CLIP模型进行图像编码
    /// - 张量操作：图像预处理为标准输入格式
    /// - 内存管理：using语句确保资源及时释放
    /// </summary>
    public async Task<Embedding<float>> GenerateAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        try
        {
            // 【步骤1】确保ONNX会话已初始化
            EnsureSession();
            if (_session is not null)
            {
                // 【步骤2】解析模型的输入输出结构
                var (imageInput, imageOutput) = ResolveVisionIO();
                if (imageInput == null || imageOutput == null)
                {
                    _logger.LogWarning("Cannot resolve model input/output structure, falling back to hash");
                    return new Embedding<float>(HashToVector(imageBytes, TargetDim));
                }

                // 【步骤3~6】在后台线程执行预处理 + 推理 + 后处理，支持取消
                var vec = await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    // 图像预处理：字节数组 → 标准化张量
                    var tensor = PreprocessToTensor(imageBytes);

                    // 构建模型所需的所有输入
                    var inputs = CreateModelInputs(imageInput, tensor);

                    // 执行ONNX推理并获取特征向量
                    using var results = _session.Run(inputs, new[] { imageOutput });
                    var output = results.First().AsEnumerable<float>().ToArray();

                    // 向量后处理：标准化并调整维度
                    return NormalizeAndResize(output, TargetDim);
                }, ct).ConfigureAwait(false);

                return new Embedding<float>(vec);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 【降级处理】CLIP推理失败时记录日志并降级
            _logger.LogDebug(ex, "CLIP embedding failed, fallback to hash vector");
        }

        // 【降级策略】生成基于哈希的伪向量
        return new Embedding<float>(HashToVector(imageBytes, TargetDim));
    }

    /// <summary>
    /// 解析ONNX模型的输入输出节点名称
    /// 
    /// 【作用】：自动检测模型的图像输入和输出节点名称
    /// 【返回值】：
    /// - imageInput: 图像输入节点名（如 "pixel_values", "image"）  
    /// - imageOutput: 图像特征输出节点名（如 "image_embeds", "pooler_output"）
    /// </summary>
    private (string? imageInput, string? imageOutput) ResolveVisionIO()
    {
        if (_session == null) return (null, null);

        try
        {
            // 常见的输入输出节点名称
            var inputCandidates = new[] { "pixel_values", "image", "input", "images" };
            var outputCandidates = new[] { "image_embeds", "pooler_output", "last_hidden_state", "embeddings", "output" };

            // 查找匹配的节点名称
            var imageInput = inputCandidates.FirstOrDefault(name => _session.InputMetadata.ContainsKey(name))
                           ?? _session.InputMetadata.Keys.FirstOrDefault(); // 降级：使用第一个输入

            var imageOutput = outputCandidates.FirstOrDefault(name => _session.OutputMetadata.ContainsKey(name))
                            ?? _session.OutputMetadata.Keys.FirstOrDefault(); // 降级：使用第一个输出

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
    /// 创建模型所需的所有输入（处理多输入模型）
    /// 
    /// 【设计原因】：
    /// 某些CLIP模型可能需要多个输入（如文本+图像的联合模型）
    /// 此方法确保提供所有必需的输入，对于不需要的输入提供合适的默认值
    /// </summary>
    private List<NamedOnnxValue> CreateModelInputs(string imageInputName, DenseTensor<float> imageTensor)
    {
        var inputs = new List<NamedOnnxValue>();

        // 【添加图像输入】
        inputs.Add(NamedOnnxValue.CreateFromTensor(imageInputName, imageTensor));

        // 【处理可能需要的其他输入】
        foreach (var inputMeta in _session!.InputMetadata)
        {
            if (inputMeta.Key == imageInputName) continue; // 已添加图像输入

            // 【常见的文本输入处理】
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
            // 【attention_mask处理】
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
    /// 生成图像描述文字（多模态理解的重要补充）
    /// 
    /// 【学习要点】：
    /// - 多模态提示：结合文本指令和图像内容的复合输入
    /// - 内容安全：限制描述长度和客观性，避免模型幻觉
    /// - 优雅降级：LLM不可用时提供占位符而非失败
    /// - 异步处理：支持取消令牌，避免长时间阻塞
    /// 
    /// 【业务价值】：
    /// - 可搜索性：为图像提供文本描述，支持文本检索
    /// - 可解释性：用户可以理解图像内容而非只看向量
    /// - 混合检索：文本描述可与图像向量结合提升检索准确性
    /// </summary>
    public async Task<string> CaptionAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        // 【主策略】使用多模态聊天模型生成描述
        if (_chat is not null)
        {
            try
            {
                // 【构建多模态对话】包含文本指令和图像内容
                var history = new ChatHistory("Your job is describing images.");
                history.AddUserMessage(
                [
                    // 【文本指令】引导模型生成客观、简洁的描述
                    new Microsoft.SemanticKernel.TextContent("用不超过20字客观描述这张图（不得臆测未出现的数字/文字）"),
                    // 【图像内容】作为模型输入的图像数据
                    new ImageContent(imageBytes, "image/png"),
                ]);

                // 【模型推理】调用多模态LLM生成描述
                var resp = await _chat.GetChatMessageContentAsync(history, cancellationToken: ct);
                var text = resp?.Content?.Trim();

                // 【后处理】验证和截断描述文字
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // 限制最大长度，避免过长描述
                    if (text.Length > 60) text = text[..60];
                    return text!;
                }
            }
            catch (Exception ex)
            {
                // 【降级处理】多模态推理失败时记录并降级
                _logger.LogDebug(ex, "Multimodal caption generation failed, fallback placeholder");
            }
        }

        // 【降级策略】返回占位符而非失败
        return "(图像内容待解析)";
    }

    /// <summary>
    /// 延迟初始化ONNX推理会话（性能优化的重要模式）
    /// 
    /// 【学习要点】：
    /// - 延迟加载：避免启动时加载大型模型影响性能
    /// - 单次初始化：使用标志位确保只尝试一次，避免重复失败
    /// - 资源检查：验证模型文件存在性，提供清晰的错误信息
    /// - 异常隔离：初始化失败不影响降级功能的正常工作
    /// 
    /// 【设计模式】：
    /// - 防护模式：通过标志位避免重复昂贵操作
    /// - 资源管理：ONNX会话将在Dispose时正确释放
    /// </summary>
    private void EnsureSession()
    {
        // 【防护条件】避免重复初始化尝试
        if (_initAttempted) return;
        _initAttempted = true;

        try
        {
            // 【资源检查】验证模型文件可用性
            if (!string.IsNullOrWhiteSpace(_modelPath) && File.Exists(_modelPath))
            {
                // 【ONNX初始化】创建推理会话
                _session = new InferenceSession(_modelPath);

                // 【模型信息检查】记录模型的输入输出信息，便于调试
                LogModelInfo();

                _logger.LogInformation("Loaded CLIP image ONNX model: {Path}", _modelPath);
            }
            else
            {
                // 【配置警告】模型不存在时的友好提示
                _logger.LogWarning("CLIP model not found at {Path}, using hash fallback", _modelPath);
            }
        }
        catch (Exception ex)
        {
            // 【初始化失败】记录错误但不抛出异常，保证降级功能可用
            _logger.LogWarning(ex, "Failed to init CLIP model session; fallback to hash embedding");
        }
    }

    /// <summary>
    /// 记录ONNX模型的输入输出信息，便于调试和验证
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
    /// 图像预处理：将原始字节转换为CLIP模型所需的标准化张量（简化版本）
    /// 
    /// 【作用】：图像解码 → 缩放到224×224 → 转换为CHW张量格式
    /// 【简化】：移除复杂的ImageNet标准化，让模型自行处理或使用更简单的归一化
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
            original.ScalePixels(resized, SKSamplingOptions.Default);

            // 修复：正确的数组大小，包含batch维度
            var tensorData = new float[1 * channels * size * size];
            var pixels = resized.Pixels;

            // CHW格式处理：Channel-Height-Width
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var pixel = pixels[y * size + x];
                    var r = pixel.Red / 255f;
                    var g = pixel.Green / 255f;
                    var b = pixel.Blue / 255f;

                    var baseIndex = y * size + x;
                    // CHW格式：[batch, channel, height, width]
                    tensorData[0 * size * size + baseIndex] = r;  // R通道
                    tensorData[1 * size * size + baseIndex] = g;  // G通道  
                    tensorData[2 * size * size + baseIndex] = b;  // B通道
                }
            }

            return new DenseTensor<float>(tensorData, new[] { 1, channels, size, size });
        }
        catch (Exception)
        {
            // 错误恢复：返回零张量，保持正确的维度
            return new DenseTensor<float>(new float[1 * channels * size * size], new[] { 1, channels, size, size });
        }
    }

    /// <summary>
    /// 向量归一化和维度调整（确保向量检索的数学正确性）
    /// 
    /// 【学习要点】：
    /// - 向量归一化：L2范数归一化确保余弦相似度计算正确
    /// - 维度对齐：不同模型输出维度可能不同，需要统一
    /// - 零向量处理：避免除零错误，保证数值稳定性
    /// - 循环填充：简单有效的维度扩展策略
    /// 
    /// 【数学原理】：
    /// - L2范数：||v|| = sqrt(v1? + v2? + ... + vn?)
    /// - 归一化：v_norm = v / ||v||，使得 ||v_norm|| = 1
    /// - 余弦相似度：cos(θ) = (a·b) / (||a|| × ||b||)，归一化后简化为 a·b
    /// </summary>
    /// <param name="src">原始向量数据</param>
    /// <param name="dim">目标维度</param>
    /// <returns>归一化并调整维度的向量</returns>
    private static float[] NormalizeAndResize(float[] src, int dim)
    {
        // 【边界检查】处理空向量情况
        if (src.Length == 0) return new float[dim];

        // 【L2范数计算】计算向量的欧几里得长度
        double norm = Math.Sqrt(src.Sum(v => v * v));
        if (norm == 0) norm = 1;  // 【零向量保护】避免除零错误

        // 【向量归一化】每个分量除以向量长度
        var normalized = src.Select(v => (float)(v / norm)).ToArray();

        // 【维度匹配】如果维度已经正确，直接返回
        if (normalized.Length == dim) return normalized;

        // 【维度调整】循环填充到目标维度
        var dst = new float[dim];
        for (int i = 0; i < dim; i++)
            dst[i] = normalized[i % normalized.Length];  // 【循环索引】重复使用原向量元素

        return dst;
    }

    /// <summary>
    /// 哈希降级向量生成（最后的兜底方案）
    /// 
    /// 【学习要点】：
    /// - 降级策略：当AI模型不可用时的可靠备选方案
    /// - 哈希算法：SHA256提供良好的数据分布特性
    /// - 伪随机性：哈希输出具有良好的统计特性，适合作为向量
    /// - 兜底机制：确保系统在任何情况下都能提供基本功能
    /// 
    /// 【设计思路】：
    /// - 确定性：相同输入始终产生相同向量，保证检索一致性
    /// - 分布均匀：SHA256确保向量分量在[0,1]范围内均匀分布
    /// - 简单可靠：不依赖外部模型，永远不会失败
    /// - 语义无关：纯粹基于内容哈希，无语义理解能力
    /// </summary>
    /// <param name="bytes">原始图像字节数据</param>
    /// <param name="dim">目标向量维度</param>
    /// <returns>基于哈希的伪随机向量</returns>
    private static float[] HashToVector(byte[] bytes, int dim)
    {
        // 【哈希计算】使用SHA256算法计算内容哈希
        using var sha = SHA256.Create();
        var h = sha.ComputeHash(bytes);

        // 【向量构造】将哈希字节转换为浮点向量
        var vec = new float[dim];
        for (int i = 0; i < dim; i++)
            vec[i] = h[i % h.Length] / 255f;  // 【归一化】字节值[0,255]转换为[0,1]

        return vec;
    }

    /// <summary>
    /// 资源清理：正确释放ONNX推理会话
    /// 
    /// 【学习要点】：
    /// - 资源管理：ONNX会话持有非托管资源，必须显式释放
    /// - IDisposable模式：.NET资源管理的标准方式
    /// - 内存泄漏预防：机器学习模型通常占用大量内存
    /// - 生命周期管理：服务容器会在适当时机调用Dispose
    /// 
    /// 【最佳实践】：
    /// - 及时释放：避免长期占用GPU/CPU内存
    /// - 防护性编程：检查null避免重复释放错误
    /// - 托管资源：让.NET垃圾回收器处理托管对象
    /// </summary>
    public void Dispose()
    {
        // 【资源释放】释放ONNX推理会话的非托管资源
        _session?.Dispose();
        // 注意：其他字段（_logger, _chatCompletion等）是托管资源，GC会自动处理
    }
}
