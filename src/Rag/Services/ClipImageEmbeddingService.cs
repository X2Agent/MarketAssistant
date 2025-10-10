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
/// CLIPͼ��Ƕ����񣨶�ģ̬RAG�ĺ��������
/// 
/// ��ѧϰҪ�㡿��
/// 1. ��ģ̬AI�����ͼ��������ı������AIģ��
/// 2. CLIPģ�ͣ�OpenAI�����Ŀ�ģ̬ģ�ͣ����Խ�ͼ����ı�ӳ�䵽ͬһ����ռ�
/// 3. �������ƣ����������Ŀɿ��Ա��ϣ�������ʧ��ʱ�Զ��л������÷���
/// 4. ONNX����ʱ����ƽ̨�Ļ���ѧϰ�������棬���ڲ������ѧϰģ��
/// 
/// ���ܹ���ơ���
/// - ˫������ƣ�ͼ��Ƕ���������� + ͼ�������������ɣ�Caption��
/// - �ֲ㽵����CLIPģ�� �� ��ϣ��������ģ̬���� �� ռλ������
/// - ��Դ������ʵ��IDisposable�Զ��ͷ�ONNX�Ự��Դ
/// - �ӳٳ�ʼ�����״ε���ʱ�ż���ģ�ͣ�������������
/// 
/// ������ջ����
/// - Microsoft.ML.OnnxRuntime��ONNXģ������
/// - SkiaSharp����ƽ̨ͼ������
/// - Microsoft.SemanticKernel����ģ̬��������
/// - Microsoft.Extensions.AI������Ƕ���׼�ӿ�
/// </summary>
public class ClipImageEmbeddingService : IImageEmbeddingService, IDisposable
{
    // �����ó�����Ŀ��Ƕ������ά�ȣ����ı�Ƕ�뱣��һ���Ա��ϼ���
    private const int TargetDim = 1024;

    // ������ע�롿���ķ������
    private readonly ILogger<ClipImageEmbeddingService> _logger;          // �ṹ����־��¼
    private readonly IChatCompletionService? _chat;                       // ��ģ̬������񣨿�ѡ��
    private readonly string? _modelPath;                                  // CLIP ONNXģ���ļ�·��

    // ��״̬������ONNX�����Ự�ͳ�ʼ����־
    private InferenceSession? _session;                                   // ONNX����ʱ�����Ự
    private bool _initAttempted;                                          // �����ظ���ʼ���ı�־

    /// <summary>
    /// ���캯����ʹ������ע���ȡ����֧�ֻ�����������ģ��·��
    /// 
    /// ��ѧϰҪ�㡿��
    /// - ����ע��ģʽ��ͨ��IServiceProvider��ȡ��ѡ������ѭ��һְ��ԭ��
    /// - �������ȼ����������� > Ĭ��·�������ڲ�ͬ��������
    /// - �ӳټ��أ�����ʱ������ģ�ͣ��״�ʹ��ʱ�ų�ʼ��
    /// </summary>
    public ClipImageEmbeddingService(ILogger<ClipImageEmbeddingService> logger, IServiceProvider sp)
    {
        _logger = logger;
        // ���Ի�ȡ������񣨶�ģ̬Caption���ܣ���ѡ��
        _chat = sp.GetService<IChatCompletionService>();

        // ģ��·�����ã����Ȼ�����������Ĭ��·��
        _modelPath = Environment.GetEnvironmentVariable("CLIP_IMAGE_ONNX")
                     ?? Path.Combine(AppContext.BaseDirectory, "models", "clip-image.onnx");
    }

    /// <summary>
    /// ����ͼ���Ƕ��������RAGϵͳ�ĺ��Ĺ��ܣ�
    /// 
    /// ��ѧϰҪ�㡿��
    /// - ��㽵�����ԣ�CLIPģ������ �� ��ϣ������ȷ��ϵͳ�ȶ���
    /// - �쳣���������������쳣������������Ӱ����������
    /// - ������׼����ȷ�������������ǵ�λ�����������������ƶȼ���
    /// - ά��ͳһ����������ͳһ��TargetDimά�ȣ�֧�ֻ�ϼ���
    /// 
    /// ������ϸ�ڡ���
    /// - ONNX������ʹ��Ԥѵ��CLIPģ�ͽ���ͼ�����
    /// - ����������ͼ��Ԥ����Ϊ��׼�����ʽ
    /// - �ڴ������using���ȷ����Դ��ʱ�ͷ�
    /// </summary>
    public async Task<Embedding<float>> GenerateAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        try
        {
            // ������1��ȷ��ONNX�Ự�ѳ�ʼ��
            EnsureSession();
            if (_session is not null)
            {
                // ������2������ģ�͵���������ṹ
                var (imageInput, imageOutput) = ResolveVisionIO();
                if (imageInput == null || imageOutput == null)
                {
                    _logger.LogWarning("Cannot resolve model input/output structure, falling back to hash");
                    return new Embedding<float>(HashToVector(imageBytes, TargetDim));
                }

                // ������3~6���ں�̨�߳�ִ��Ԥ���� + ���� + ������֧��ȡ��
                var vec = await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    // ͼ��Ԥ�������ֽ����� �� ��׼������
                    var tensor = PreprocessToTensor(imageBytes);

                    // ����ģ���������������
                    var inputs = CreateModelInputs(imageInput, tensor);

                    // ִ��ONNX��������ȡ��������
                    using var results = _session.Run(inputs, new[] { imageOutput });
                    var output = results.First().AsEnumerable<float>().ToArray();

                    // ������������׼��������ά��
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
            // ������������CLIP����ʧ��ʱ��¼��־������
            _logger.LogDebug(ex, "CLIP embedding failed, fallback to hash vector");
        }

        // ���������ԡ����ɻ��ڹ�ϣ��α����
        return new Embedding<float>(HashToVector(imageBytes, TargetDim));
    }

    /// <summary>
    /// ����ONNXģ�͵���������ڵ�����
    /// 
    /// �����á����Զ����ģ�͵�ͼ�����������ڵ�����
    /// ������ֵ����
    /// - imageInput: ͼ������ڵ������� "pixel_values", "image"��  
    /// - imageOutput: ͼ����������ڵ������� "image_embeds", "pooler_output"��
    /// </summary>
    private (string? imageInput, string? imageOutput) ResolveVisionIO()
    {
        if (_session == null) return (null, null);

        try
        {
            // ��������������ڵ�����
            var inputCandidates = new[] { "pixel_values", "image", "input", "images" };
            var outputCandidates = new[] { "image_embeds", "pooler_output", "last_hidden_state", "embeddings", "output" };

            // ����ƥ��Ľڵ�����
            var imageInput = inputCandidates.FirstOrDefault(name => _session.InputMetadata.ContainsKey(name))
                           ?? _session.InputMetadata.Keys.FirstOrDefault(); // ������ʹ�õ�һ������

            var imageOutput = outputCandidates.FirstOrDefault(name => _session.OutputMetadata.ContainsKey(name))
                            ?? _session.OutputMetadata.Keys.FirstOrDefault(); // ������ʹ�õ�һ�����

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
    /// ����ģ��������������루����������ģ�ͣ�
    /// 
    /// �����ԭ�򡿣�
    /// ĳЩCLIPģ�Ϳ�����Ҫ������루���ı�+ͼ�������ģ�ͣ�
    /// �˷���ȷ���ṩ���б�������룬���ڲ���Ҫ�������ṩ���ʵ�Ĭ��ֵ
    /// </summary>
    private List<NamedOnnxValue> CreateModelInputs(string imageInputName, DenseTensor<float> imageTensor)
    {
        var inputs = new List<NamedOnnxValue>();

        // ������ͼ�����롿
        inputs.Add(NamedOnnxValue.CreateFromTensor(imageInputName, imageTensor));

        // ������������Ҫ���������롿
        foreach (var inputMeta in _session!.InputMetadata)
        {
            if (inputMeta.Key == imageInputName) continue; // ������ͼ������

            // ���������ı����봦����
            if (inputMeta.Key.Contains("input_ids") || inputMeta.Key.Contains("text"))
            {
                // �������ı����루��ʾֻ����ͼ��
                var textShape = inputMeta.Value.Dimensions.ToArray();
                if (textShape.Any(d => d <= 0)) textShape = new[] { 1, 1 }; // Ĭ����״

                var emptyTextTensor = new DenseTensor<long>(new long[textShape.Aggregate(1, (a, b) => a * b)], textShape);
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputMeta.Key, emptyTextTensor));
                _logger.LogDebug("Added empty text input: {InputName} with shape [{Shape}]",
                    inputMeta.Key, string.Join(", ", textShape));
            }
            // ��attention_mask������
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
    /// ����ͼ���������֣���ģ̬�������Ҫ���䣩
    /// 
    /// ��ѧϰҪ�㡿��
    /// - ��ģ̬��ʾ������ı�ָ���ͼ�����ݵĸ�������
    /// - ���ݰ�ȫ�������������ȺͿ͹��ԣ�����ģ�ͻþ�
    /// - ���Ž�����LLM������ʱ�ṩռλ������ʧ��
    /// - �첽������֧��ȡ�����ƣ����ⳤʱ������
    /// 
    /// ��ҵ���ֵ����
    /// - �������ԣ�Ϊͼ���ṩ�ı�������֧���ı�����
    /// - �ɽ����ԣ��û���������ͼ�����ݶ���ֻ������
    /// - ��ϼ������ı���������ͼ�����������������׼ȷ��
    /// </summary>
    public async Task<string> CaptionAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        // �������ԡ�ʹ�ö�ģ̬����ģ����������
        if (_chat is not null)
        {
            try
            {
                // ��������ģ̬�Ի��������ı�ָ���ͼ������
                var history = new ChatHistory("Your job is describing images.");
                history.AddUserMessage(
                [
                    // ���ı�ָ�����ģ�����ɿ͹ۡ���������
                    new Microsoft.SemanticKernel.TextContent("�ò�����20�ֿ͹���������ͼ�������ܲ�δ���ֵ�����/���֣�"),
                    // ��ͼ�����ݡ���Ϊģ�������ͼ������
                    new ImageContent(imageBytes, "image/png"),
                ]);

                // ��ģ�����������ö�ģ̬LLM��������
                var resp = await _chat.GetChatMessageContentAsync(history, cancellationToken: ct);
                var text = resp?.Content?.Trim();

                // ����������֤�ͽض���������
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // ������󳤶ȣ������������
                    if (text.Length > 60) text = text[..60];
                    return text!;
                }
            }
            catch (Exception ex)
            {
                // ��������������ģ̬����ʧ��ʱ��¼������
                _logger.LogDebug(ex, "Multimodal caption generation failed, fallback placeholder");
            }
        }

        // ���������ԡ�����ռλ������ʧ��
        return "(ͼ�����ݴ�����)";
    }

    /// <summary>
    /// �ӳٳ�ʼ��ONNX�����Ự�������Ż�����Ҫģʽ��
    /// 
    /// ��ѧϰҪ�㡿��
    /// - �ӳټ��أ���������ʱ���ش���ģ��Ӱ������
    /// - ���γ�ʼ����ʹ�ñ�־λȷ��ֻ����һ�Σ������ظ�ʧ��
    /// - ��Դ��飺��֤ģ���ļ������ԣ��ṩ�����Ĵ�����Ϣ
    /// - �쳣���룺��ʼ��ʧ�ܲ�Ӱ�콵�����ܵ���������
    /// 
    /// �����ģʽ����
    /// - ����ģʽ��ͨ����־λ�����ظ��������
    /// - ��Դ������ONNX�Ự����Disposeʱ��ȷ�ͷ�
    /// </summary>
    private void EnsureSession()
    {
        // �����������������ظ���ʼ������
        if (_initAttempted) return;
        _initAttempted = true;

        try
        {
            // ����Դ��顿��֤ģ���ļ�������
            if (!string.IsNullOrWhiteSpace(_modelPath) && File.Exists(_modelPath))
            {
                // ��ONNX��ʼ�������������Ự
                _session = new InferenceSession(_modelPath);

                // ��ģ����Ϣ��顿��¼ģ�͵����������Ϣ�����ڵ���
                LogModelInfo();

                _logger.LogInformation("Loaded CLIP image ONNX model: {Path}", _modelPath);
            }
            else
            {
                // �����þ��桿ģ�Ͳ�����ʱ���Ѻ���ʾ
                _logger.LogWarning("CLIP model not found at {Path}, using hash fallback", _modelPath);
            }
        }
        catch (Exception ex)
        {
            // ����ʼ��ʧ�ܡ���¼���󵫲��׳��쳣����֤�������ܿ���
            _logger.LogWarning(ex, "Failed to init CLIP model session; fallback to hash embedding");
        }
    }

    /// <summary>
    /// ��¼ONNXģ�͵����������Ϣ�����ڵ��Ժ���֤
    /// </summary>
    private void LogModelInfo()
    {
        if (_session == null) return;

        try
        {
            _logger.LogInformation("ONNX Model Information:");

            // ����ڵ���Ϣ
            _logger.LogInformation("Inputs:");
            foreach (var input in _session.InputMetadata)
            {
                var shape = string.Join(", ", input.Value.Dimensions);
                _logger.LogInformation("  - {Name}: {Type} [{Shape}]", input.Key, input.Value.ElementType, shape);
            }

            // ����ڵ���Ϣ
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
    /// ͼ��Ԥ��������ԭʼ�ֽ�ת��ΪCLIPģ������ı�׼���������򻯰汾��
    /// 
    /// �����á���ͼ����� �� ���ŵ�224��224 �� ת��ΪCHW������ʽ
    /// ���򻯡����Ƴ����ӵ�ImageNet��׼������ģ�����д�����ʹ�ø��򵥵Ĺ�һ��
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

            // �޸�����ȷ�������С������batchά��
            var tensorData = new float[1 * channels * size * size];
            var pixels = resized.Pixels;

            // CHW��ʽ������Channel-Height-Width
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var pixel = pixels[y * size + x];
                    var r = pixel.Red / 255f;
                    var g = pixel.Green / 255f;
                    var b = pixel.Blue / 255f;

                    var baseIndex = y * size + x;
                    // CHW��ʽ��[batch, channel, height, width]
                    tensorData[0 * size * size + baseIndex] = r;  // Rͨ��
                    tensorData[1 * size * size + baseIndex] = g;  // Gͨ��  
                    tensorData[2 * size * size + baseIndex] = b;  // Bͨ��
                }
            }

            return new DenseTensor<float>(tensorData, new[] { 1, channels, size, size });
        }
        catch (Exception)
        {
            // ����ָ���������������������ȷ��ά��
            return new DenseTensor<float>(new float[1 * channels * size * size], new[] { 1, channels, size, size });
        }
    }

    /// <summary>
    /// ������һ����ά�ȵ�����ȷ��������������ѧ��ȷ�ԣ�
    /// 
    /// ��ѧϰҪ�㡿��
    /// - ������һ����L2������һ��ȷ���������ƶȼ�����ȷ
    /// - ά�ȶ��룺��ͬģ�����ά�ȿ��ܲ�ͬ����Ҫͳһ
    /// - ���������������������󣬱�֤��ֵ�ȶ���
    /// - ѭ����䣺����Ч��ά����չ����
    /// 
    /// ����ѧԭ������
    /// - L2������||v|| = sqrt(v1? + v2? + ... + vn?)
    /// - ��һ����v_norm = v / ||v||��ʹ�� ||v_norm|| = 1
    /// - �������ƶȣ�cos(��) = (a��b) / (||a|| �� ||b||)����һ�����Ϊ a��b
    /// </summary>
    /// <param name="src">ԭʼ��������</param>
    /// <param name="dim">Ŀ��ά��</param>
    /// <returns>��һ��������ά�ȵ�����</returns>
    private static float[] NormalizeAndResize(float[] src, int dim)
    {
        // ���߽��顿�������������
        if (src.Length == 0) return new float[dim];

        // ��L2�������㡿����������ŷ����ó���
        double norm = Math.Sqrt(src.Sum(v => v * v));
        if (norm == 0) norm = 1;  // ������������������������

        // ��������һ����ÿ������������������
        var normalized = src.Select(v => (float)(v / norm)).ToArray();

        // ��ά��ƥ�䡿���ά���Ѿ���ȷ��ֱ�ӷ���
        if (normalized.Length == dim) return normalized;

        // ��ά�ȵ�����ѭ����䵽Ŀ��ά��
        var dst = new float[dim];
        for (int i = 0; i < dim; i++)
            dst[i] = normalized[i % normalized.Length];  // ��ѭ���������ظ�ʹ��ԭ����Ԫ��

        return dst;
    }

    /// <summary>
    /// ��ϣ�����������ɣ����Ķ��׷�����
    /// 
    /// ��ѧϰҪ�㡿��
    /// - �������ԣ���AIģ�Ͳ�����ʱ�Ŀɿ���ѡ����
    /// - ��ϣ�㷨��SHA256�ṩ���õ����ݷֲ�����
    /// - α����ԣ���ϣ����������õ�ͳ�����ԣ��ʺ���Ϊ����
    /// - ���׻��ƣ�ȷ��ϵͳ���κ�����¶����ṩ��������
    /// 
    /// �����˼·����
    /// - ȷ���ԣ���ͬ����ʼ�ղ�����ͬ��������֤����һ����
    /// - �ֲ����ȣ�SHA256ȷ������������[0,1]��Χ�ھ��ȷֲ�
    /// - �򵥿ɿ����������ⲿģ�ͣ���Զ����ʧ��
    /// - �����޹أ�����������ݹ�ϣ����������������
    /// </summary>
    /// <param name="bytes">ԭʼͼ���ֽ�����</param>
    /// <param name="dim">Ŀ������ά��</param>
    /// <returns>���ڹ�ϣ��α�������</returns>
    private static float[] HashToVector(byte[] bytes, int dim)
    {
        // ����ϣ���㡿ʹ��SHA256�㷨�������ݹ�ϣ
        using var sha = SHA256.Create();
        var h = sha.ComputeHash(bytes);

        // ���������졿����ϣ�ֽ�ת��Ϊ��������
        var vec = new float[dim];
        for (int i = 0; i < dim; i++)
            vec[i] = h[i % h.Length] / 255f;  // ����һ�����ֽ�ֵ[0,255]ת��Ϊ[0,1]

        return vec;
    }

    /// <summary>
    /// ��Դ��������ȷ�ͷ�ONNX�����Ự
    /// 
    /// ��ѧϰҪ�㡿��
    /// - ��Դ������ONNX�Ự���з��й���Դ��������ʽ�ͷ�
    /// - IDisposableģʽ��.NET��Դ�����ı�׼��ʽ
    /// - �ڴ�й©Ԥ��������ѧϰģ��ͨ��ռ�ô����ڴ�
    /// - �������ڹ������������������ʵ�ʱ������Dispose
    /// 
    /// �����ʵ������
    /// - ��ʱ�ͷţ����ⳤ��ռ��GPU/CPU�ڴ�
    /// - �����Ա�̣����null�����ظ��ͷŴ���
    /// - �й���Դ����.NET���������������йܶ���
    /// </summary>
    public void Dispose()
    {
        // ����Դ�ͷš��ͷ�ONNX�����Ự�ķ��й���Դ
        _session?.Dispose();
        // ע�⣺�����ֶΣ�_logger, _chatCompletion�ȣ����й���Դ��GC���Զ�����
    }
}
