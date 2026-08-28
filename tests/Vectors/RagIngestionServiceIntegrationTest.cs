using MarketAssistant.Rag;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class RagIngestionServiceIntegrationTest : BaseAgentTest
{
    private IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator = null!;
    private VectorStore _vectorStore = null!;
    private IRagIngestionService _ragIngestionService = null!;
    private string _collectionName = null!;
    private string _testDocxFile = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        base.BaseInitialize();

        // 获取服务（嵌入生成器仅在 JINA_API_KEY 已配置时注册，缺失时给出明确失败原因）
        _embeddingGenerator = _serviceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (_embeddingGenerator is null)
        {
            Assert.Fail("JINA_API_KEY 环境变量未配置，无法创建嵌入生成器进行真实场景验证");
        }
        _vectorStore = _serviceProvider.GetRequiredService<VectorStore>();

        // 从 DI 容器获取 RagIngestionService
        _ragIngestionService = _serviceProvider.GetRequiredService<IRagIngestionService>();

        // 创建测试集合
        _collectionName = $"test_ingest_{Guid.NewGuid():N}";
        var testCollection = _vectorStore.GetCollection<string, TextParagraph>(_collectionName);
        await testCollection.EnsureCollectionExistsAsync();

        // 获取测试文件路径
        var testProjectDir = GetTestProjectDirectory();
        _testDocxFile = Path.Combine(testProjectDir, "demo.docx");

        // 确保测试文件存在
        Assert.IsTrue(File.Exists(_testDocxFile), $"测试文件不存在: {_testDocxFile}");
    }

    private static string GetTestProjectDirectory()
    {
        // 从当前执行目录向上查找，直到找到包含demo.docx的目录
        var currentDir = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(currentDir);

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "demo.docx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException("无法找到包含demo.docx的测试项目目录。请确保demo.docx文件存在于TestMarketAssistant项目目录中。");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestIngestFileAsync()
    {
        // 验证IngestFileAsync能否正确执行
        Console.WriteLine($"准备摄取文件: {_testDocxFile}");

        // 获取集合
        var collection = _vectorStore.GetCollection<string, TextParagraph>(_collectionName);

        // 执行文件摄取
        await _ragIngestionService.IngestFileAsync(
            collection,
            _collectionName,
            _testDocxFile,
            _embeddingGenerator);

        Console.WriteLine("文件摄取完成");

        // 验证摄取结果 - 使用直接的向量搜索
        // 生成查询向量
        var queryEmbedding = await _embeddingGenerator.GenerateAsync("文档内容");

        var vectorSearchOptions = new VectorSearchOptions<TextParagraph>
        {
            VectorProperty = r => r.TextEmbedding
        };

        // 使用直接向量搜索（指定最大返回数量）
        var searchResults = collection.SearchAsync(queryEmbedding, 5, vectorSearchOptions);
        var results = new List<TextParagraph>();

        await foreach (var result in searchResults)
        {
            results.Add(result.Record);
        }

        Assert.IsTrue(results.Count > 0, "应该有数据被摄取到向量存储中");

        Console.WriteLine($"摄取成功，找到 {results.Count} 个段落");

        // 打印前几个结果的内容片段
        foreach (var result in results.Take(3))
        {
            var text = result.Text;
            var preview = text.Length > 100 ? text[..100] + "..." : text;
            Console.WriteLine($"段落: {preview}");
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestIngestFileAsync_WithWrongDimension_ShouldFailBeforeUpsert()
    {
        // Arrange - 使用返回非 1024 维向量的嵌入生成器
        var collection = _vectorStore.GetCollection<string, TextParagraph>(_collectionName);
        var wrongDimGenerator = new FixedDimensionEmbeddingGenerator(_embeddingGenerator, 1536);

        // Act
        var result = await _ragIngestionService.IngestFileAsync(
            collection,
            _collectionName,
            _testDocxFile,
            wrongDimGenerator);

        // Assert - 在 Upsert 前失败，不产生脏数据
        Assert.IsTrue(result.IsFailure, "非 1024 维向量不应有任何段落入库");
        Assert.IsTrue(result.Failures.Any(f => f.ErrorCode == "EmbeddingDimensionMismatch"),
            "失败原因应包含维度不匹配");
        var message = result.Failures.First(f => f.ErrorCode == "EmbeddingDimensionMismatch").Message;
        StringAssert.Contains(message, "1024", "错误消息应包含期望维度");
        StringAssert.Contains(message, "1536", "错误消息应包含实际维度");

        Console.WriteLine($"维度校验生效，{result.Failures.Count} 个块被拒绝写入");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestIngestFileAsync_WithFewerEmbeddings_ShouldFailWithoutWriting()
    {
        // Arrange - 返回条数少于文本条数
        var collection = _vectorStore.GetCollection<string, TextParagraph>(_collectionName);
        var shortGenerator = new ShortCountEmbeddingGenerator(_embeddingGenerator);

        // Act
        var result = await _ragIngestionService.IngestFileAsync(
            collection,
            _collectionName,
            _testDocxFile,
            shortGenerator);

        // Assert
        Assert.IsTrue(result.IsFailure, "嵌入数量不足时不应有段落入库");
        Assert.IsTrue(result.Failures.Any(f => f.ErrorCode == "EmbeddingCountMismatch"),
            "失败原因应包含数量不匹配");

        Console.WriteLine($"数量校验生效，{result.Failures.Count} 个块被拒绝写入");
    }

    /// <summary>包装真实生成器但输出固定错误维度的嵌入。</summary>
    private sealed class FixedDimensionEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>> inner, int dimension)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = await inner.GenerateAsync(values, options, cancellationToken);
            return new GeneratedEmbeddings<Embedding<float>>(
                embeddings.Select(e => new Embedding<float>(new float[dimension])));
        }
    }

    /// <summary>丢弃一半嵌入以模拟返回条数不足。</summary>
    private sealed class ShortCountEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>> inner)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var embeddings = await inner.GenerateAsync(values, options, cancellationToken);
            return new GeneratedEmbeddings<Embedding<float>>(embeddings.Take(Math.Max(1, embeddings.Count / 2)));
        }
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        // 测试结束后的清理工作
        await Task.CompletedTask;
    }
}
