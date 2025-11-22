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
    // 目标嵌入向量维度（文本嵌入保持一致以便比较）
    private const int TargetDim = 1024;

    // 【依赖注入】：服务的依赖项
    private readonly ILogger<ClipImageEmbeddingService> _logger;          // 结构化日志记录
    private readonly IChatCompletionService? _chat;                       // 多模态聊天服务（可选）
    private readonly string? _modelPath;                                  // CLIP ONNX模型文件路径

    // 【状态管理】：ONNX推理会话和初始化标志
    private InferenceSession? _session;                                   // ONNX运行时推理会话
    private bool _initAttempted;                                          // 防止重复初始化的标志

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
        // 尝试获取聊天服务（多模态Caption功能），可选
        _chat = sp.GetService<IChatCompletionService>();

        // 模型路径配置：优先环境变量，否则使用本地默认路径
        _modelPath = Environment.GetEnvironmentVariable("CLIP_IMAGE_ONNX")
                     ?? Path.Combine(AppContext.BaseDirectory, "models", "clip-image.onnx");
    }

    /// <summary>
    /// 生成图像嵌入向量（RAG系统的核心功能）
    /// 
    /// 【学习要点】：
    /// - 异常降级：CLIP模型异常 -> 哈希算法，确保系统稳定性
    /// - 异步编程：使用Task.Run将计算密集型任务移至后台线程，避免阻塞UI
    /// - 向量归一化：确保向量在单位超球面上，便于余弦相似度计算
    /// - 维度统一：统一到TargetDim维度，支持哈希降级
    /// 
    /// 【实现细节】：
    /// - ONNX推理：使用预训练CLIP模型进行图像编码
    /// - 预处理：将图像预处理为标准张量格式
    /// - 内存管理：使用using确保资源及时释放
    /// </summary>
    public async Task<Embedding<float>> GenerateAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        try
        {
            // 步骤1：确保ONNX会话已初始化
            EnsureSession();
            if (_session is not null)
            {
                // 步骤2：解析模型的输入输出结构
                var (imageInput, imageOutput) = ResolveVisionIO();
                if (imageInput == null || imageOutput == null)
                {
                    _logger.LogWarning("Cannot resolve model input/output structure, falling back to hash");
                    return new Embedding<float>(HashToVector(imageBytes, TargetDim));
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
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 异常处理：CLIP推理失败时记录日志并降级
            _logger.LogDebug(ex, "CLIP embedding failed, fallback to hash vector");
        }

        // 降级策略：生成哈希向量作为备用
        return new Embedding<float>(HashToVector(imageBytes, TargetDim));
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
        // 检查点：防止重复初始化尝试
        if (_initAttempted) return;
        _initAttempted = true;

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
                // 警告：配置的模型不存在时发出提示
                _logger.LogWarning("CLIP model not found at {Path}, using hash fallback", _modelPath);
            }
        }
        catch (Exception ex)
        {
            // 初始化失败：记录错误但不抛出异常，保证降级可用
            _logger.LogWarning(ex, "Failed to init CLIP model session; fallback to hash embedding");
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
    /// 图像预处理：将原始字节转换为CLIP模型所需的标准张量（简化版）
    /// 
    /// 【处理流程】：图像解码 -> 缩放到224x224 -> 转换为CHW张量格式
    /// 【简化】：未移除均值和方差（ImageNet标准），此处使用更简单的归一化
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
            original.ScalePixels(resized, SKFilterQuality.Medium);

            // 准备数据数组（注意batch维度）
            var tensorData = new float[1 * channels * size * size];
            var pixels = resized.Pixels;

            // CHW格式：Channel-Height-Width
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
            // 异常降级：返回全零张量确保不崩溃
            return new DenseTensor<float>(new float[1 * channels * size * size], new[] { 1, channels, size, size });
        }
    }

    /// <summary>
    /// 向量归一化和维度调整（确保向量数学正确性）
    /// 
    /// 【学习要点】：
    /// - L2归一化：||v|| = 1，确保余弦相似度计算准确
    /// - 维度对齐：不同模型输出维度可能不同，需要统一
    /// - 鲁棒性：处理零向量和维度不匹配情况
    /// - 循环填充：简单有效的维度扩展策略
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

        // 维度调整：循环填充到目标维度
        var dst = new float[dim];
        for (int i = 0; i < dim; i++)
            dst[i] = normalized[i % normalized.Length];  // 循环复制使用原向量元素

        return dst;
    }

    /// <summary>
    /// 哈希向量生成（降级兜底方案）
    /// 
    /// 【学习要点】：
    /// - 确定性：相同输入始终产生相同输出，保证验证一致性
    /// - 哈希算法：SHA256提供良好的数据分布特性
    /// - 伪随机性：哈希值具有统计随机性，适合作为随机向量
    /// - 降级无关：不依赖外部模型，作为最后的防线
    /// 
    /// 【实现思路】：
    /// - 确定性：相同字节数组产生相同哈希
    /// - 分布均匀：SHA256确保输出在[0,1]范围内均匀分布
    /// - 简单可靠：无外部依赖，永不失败
    /// </summary>
    /// <param name="bytes">原始图像字节数组</param>
    /// <param name="dim">目标向量维度</param>
    /// <returns>基于哈希的伪随机向量</returns>
    private static float[] HashToVector(byte[] bytes, int dim)
    {
        // 计算哈希值：使用SHA256算法
        using var sha = SHA256.Create();
        var h = sha.ComputeHash(bytes);

        // 生成向量：将哈希字节转换为浮点数
        var vec = new float[dim];
        for (int i = 0; i < dim; i++)
            vec[i] = h[i % h.Length] / 255f;  // 将字节值[0,255]转换为[0,1]

        return vec;
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
