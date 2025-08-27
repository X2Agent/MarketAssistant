using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarketAssistant.Vectors.Interfaces;
using MarketAssistant.Vectors.Tokenization;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// 使用本地 ONNX Cross-Encoder 模型进行重排的实现（无 HTTP）。
/// 需要环境变量：
///   CROSS_ENCODER_ONNX  -> 模型路径 (.onnx)
///   CROSS_ENCODER_VOCAB -> BERT WordPiece vocab.txt 路径
/// 可选：CROSS_ENCODER_MAXLEN (默认 256)
/// 模型假设输入：input_ids(int64), attention_mask(int64), token_type_ids(int64)  输出：logits 或 (logits) 单浮点或 shape [1,1]
/// </summary>
public sealed class OnnxCrossEncoderRerankerService : IRerankerService, IDisposable
{
    private readonly ILogger<OnnxCrossEncoderRerankerService> _logger;
    private readonly InferenceSession? _session;
    private readonly IWordPieceTokenizer _tokenizer;
    private readonly int _maxLen;
    private readonly int _clsId;
    private readonly int _sepId;
    private readonly int _unkId;
    private readonly object _sync = new();

    public bool IsReady => _session != null && _tokenizer.HasVocab;

    public OnnxCrossEncoderRerankerService(ILogger<OnnxCrossEncoderRerankerService> logger)
    {
        _logger = logger;
        var modelPath = Environment.GetEnvironmentVariable("CROSS_ENCODER_ONNX");
        var vocabPath = Environment.GetEnvironmentVariable("CROSS_ENCODER_VOCAB");
        _maxLen = int.TryParse(Environment.GetEnvironmentVariable("CROSS_ENCODER_MAXLEN"), out var ml) ? Math.Clamp(ml, 32, 512) : 256;
        _tokenizer = new WordPieceTokenizer(vocabPath);
        try
        {
            _unkId = _tokenizer.UNK;
            _clsId = _tokenizer.CLS;
            _sepId = _tokenizer.SEP;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Load vocab failed: {Path}", vocabPath);
        }
        try
        {
            if (!string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath))
            {
                _session = new InferenceSession(modelPath);
                _logger.LogInformation("Loaded CrossEncoder ONNX model: {Path}", modelPath);
            }
            else
            {
                _logger.LogWarning("CrossEncoder ONNX model missing: {Path}", modelPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Init ONNX session failed");
        }
    }

    public async Task<IReadOnlyList<TextSearchResult>> RerankAsync(string query, IEnumerable<TextSearchResult> items, CancellationToken cancellationToken = default)
    {
        var list = items.ToList();
        if (list.Count == 0) return list;

        // 如果服务不可用，抛出异常让上层处理降级
        if (!IsReady)
        {
            throw new InvalidOperationException("ONNX Cross-Encoder reranker is not ready. Model or vocabulary not loaded.");
        }

        try
        {
            var batchSize = list.Count;
            var pool = System.Buffers.ArrayPool<long>.Shared;
            var total = batchSize * _maxLen;
            var idsBuf = pool.Rent(total);
            var tokenTypeBuf = pool.Rent(total);
            var attBuf = pool.Rent(total);
            try
            {
                for (int i = 0; i < batchSize; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var passage = NormalizePassage(list[i]);
                    var tokens = BuildInput(query, passage, out var tokenType, out var attention);
                    var baseIdx = i * _maxLen;
                    for (int j = 0; j < _maxLen; j++)
                    {
                        idsBuf[baseIdx + j] = tokens[j];
                        tokenTypeBuf[baseIdx + j] = tokenType[j];
                        attBuf[baseIdx + j] = attention[j];
                    }
                }

                var inputIds = new DenseTensor<long>(idsBuf, new ReadOnlySpan<int>(new[] { batchSize, _maxLen }));
                var tokenTypeIds = new DenseTensor<long>(tokenTypeBuf, new ReadOnlySpan<int>(new[] { batchSize, _maxLen }));
                var attMask = new DenseTensor<long>(attBuf, new ReadOnlySpan<int>(new[] { batchSize, _maxLen }));

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                    NamedOnnxValue.CreateFromTensor("attention_mask", attMask),
                    NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
                };

                double[] scores;
                lock (_sync)
                {
                    if (_session == null) throw new InvalidOperationException("ONNX session not initialized");
                    using var results = _session.Run(inputs);
                    NamedOnnxValue? scoreVal = null;
                    foreach (var r in results)
                    {
                        if (r.Name.Contains("logit", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("score", StringComparison.OrdinalIgnoreCase) || r.Name == "logits") { scoreVal = r; break; }
                    }
                    if (scoreVal == null) throw new InvalidOperationException("ONNX model output not found");
                    if (scoreVal.Value is DenseTensor<float> dt)
                    {
                        scores = new double[batchSize];
                        if (dt.Rank == 2 && dt.Dimensions[0] == batchSize && dt.Dimensions[1] >= 1)
                        {
                            for (int b = 0; b < batchSize; b++) scores[b] = dt[b, 0];
                        }
                        else if (dt.Length >= batchSize)
                        {
                            var arr = dt.ToArray();
                            for (int b = 0; b < batchSize; b++) scores[b] = arr[b];
                        }
                        else throw new InvalidOperationException("Unexpected score tensor shape");
                    }
                    else if (scoreVal.Value is IEnumerable<float> fEnum)
                    {
                        var arr = fEnum.ToArray(); scores = arr.Select(f => (double)f).ToArray();
                    }
                    else if (scoreVal.Value is float[] fa)
                    {
                        scores = fa.Select(f => (double)f).ToArray();
                    }
                    else throw new InvalidOperationException("Unsupported score tensor type");
                }

                var min = scores.Min(); var max = scores.Max();
                var norm = max - min > 1e-6 ? scores.Select(s => (s - min) / (max - min)).ToArray() : scores;
                return list.Zip(norm, (item, s) => (item, s)).OrderByDescending(p => p.s).Select(p => p.item).ToList();
            }
            finally
            {
                pool.Return(idsBuf);
                pool.Return(tokenTypeBuf);
                pool.Return(attBuf);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Onnx cross-encoder rerank failed");
            throw; // 让上层装饰器处理降级
        }
    }

    private double InferPair(string query, string passage)
    {
        if (_session == null) return 0;
        var tokens = BuildInput(query, passage, out var tokenType, out var attention);
        var inputIds = new DenseTensor<long>(new[] { 1, _maxLen });
        var tokenTypeIds = new DenseTensor<long>(new[] { 1, _maxLen });
        var attMask = new DenseTensor<long>(new[] { 1, _maxLen });
        for (int i = 0; i < _maxLen; i++)
        {
            inputIds[0, i] = tokens[i];
            tokenTypeIds[0, i] = tokenType[i];
            attMask[0, i] = attention[i];
        }
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attMask),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
        };
        lock (_sync)
        {
            using var results = _session.Run(inputs);
            foreach (var r in results)
            {
                if (r.Name.Contains("logit", StringComparison.OrdinalIgnoreCase) || r.Name.Contains("score", StringComparison.OrdinalIgnoreCase) || r.Name == "logits")
                {
                    if (r.Value is IEnumerable<float> fEnum)
                    {
                        return fEnum.First();
                    }
                    if (r.Value is float[] fa && fa.Length > 0) return fa[0];
                    if (r.Value is DenseTensor<float> dt && dt.Length > 0) return dt.GetValue(0);
                }
            }
        }
        return 0d;
    }

    private long[] BuildInput(string query, string passage, out long[] tokenType, out long[] attention)
    {
        var qTokens = WordPiece(query).Select(i => (long)i);
        var pTokens = WordPiece(passage).Select(i => (long)i);
        var ids = new List<long>(_maxLen) { _clsId };
        ids.AddRange(qTokens);
        ids.Add(_sepId);
        var segSplit = ids.Count;
        ids.AddRange(pTokens);
        ids.Add(_sepId);
        if (ids.Count > _maxLen)
        {
            ids = ids.Take(_maxLen).ToList();
            if (ids[^1] != _sepId) ids[^1] = _sepId; // ensure ending sep
        }
        while (ids.Count < _maxLen) ids.Add(0);
        tokenType = new long[_maxLen];
        for (int i = segSplit; i < _maxLen; i++) tokenType[i] = 1; // second segment
        attention = new long[_maxLen];
        for (int i = 0; i < _maxLen; i++) attention[i] = ids[i] == 0 ? 0 : 1;
        return ids.Select(i => (long)i).ToArray();
    }

    private IEnumerable<int> WordPiece(string text)
    {
        return _tokenizer.HasVocab ? _tokenizer.Tokenize(text) : Enumerable.Empty<int>();
    }

    private static string NormalizePassage(TextSearchResult r)
    {
        var txt = (r.Value ?? string.Empty).Trim();
        var name = (r.Name ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(name) && !txt.StartsWith(name, StringComparison.Ordinal) && name.Length < 120)
            txt = name + "\n" + txt;
        if (txt.Length > 1800) txt = txt[..1800];
        return txt;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
