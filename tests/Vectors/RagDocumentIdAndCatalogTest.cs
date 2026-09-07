using MarketAssistant.Rag;
using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Rag.Services;

namespace TestMarketAssistant.Vectors;

/// <summary>
/// P1-01：稳定 DocumentId、统一 Key 方案与文档清单 SQLite 持久化。
/// </summary>
[TestClass]
public class RagDocumentIdAndCatalogTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Compute_SameFileDifferentPathForms_ShouldReturnSameId()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var dir = Path.GetDirectoryName(tempFile)!;
            var name = Path.GetFileName(tempFile);

            var id1 = RagDocumentId.Compute(tempFile);
            var id2 = RagDocumentId.Compute(Path.Combine(dir, name));
            var id3 = RagDocumentId.Compute(Path.Combine(dir, ".", name));

            Assert.AreEqual(id1, id2, "绝对路径与等价组合路径应产生相同 DocumentId");
            Assert.AreEqual(id1, id3, "路径中的 '.' 段应被归一化");
            Assert.AreEqual(32, id1.Length, "DocumentId 应为 32 位十六进制哈希");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Compute_DifferentFiles_ShouldReturnDifferentIds()
    {
        var id1 = RagDocumentId.Compute(@"C:\data\a.pdf");
        var id2 = RagDocumentId.Compute(@"C:\data\b.pdf");
        Assert.AreNotEqual(id1, id2);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SqliteCatalog_ReplaceGetRemove_ShouldRoundTrip()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"rag-catalog-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            IRagDocumentCatalog catalog = new SqliteRagDocumentCatalog(dbPath);

            var keys = await catalog.GetKeysAsync("knowledge", "doc-1");
            Assert.HasCount(0, keys, "无记录时应返回空列表");

            await catalog.ReplaceAsync(new RagDocumentCatalogEntry(
                "knowledge", "doc-1", @"C:\docs\a.pdf", "hash-a",
                ["k1", "k2", "k3"], "test-model", 1024, DateTimeOffset.UtcNow));

            keys = await catalog.GetKeysAsync("knowledge", "doc-1");
            CollectionAssert.AreEquivalent(new[] { "k1", "k2", "k3" }, keys.ToList());

            // 覆盖写入
            await catalog.ReplaceAsync(new RagDocumentCatalogEntry(
                "knowledge", "doc-1", @"C:\docs\a.pdf", "hash-a2",
                ["k4"], "test-model", 1024, DateTimeOffset.UtcNow));
            keys = await catalog.GetKeysAsync("knowledge", "doc-1");
            CollectionAssert.AreEquivalent(new[] { "k4" }, keys.ToList(), "覆盖后应只剩新 Key");

            // 集合隔离
            var otherKeys = await catalog.GetKeysAsync("other-collection", "doc-1");
            Assert.HasCount(0, otherKeys);

            // 删除
            await catalog.RemoveAsync("knowledge", "doc-1");
            keys = await catalog.GetKeysAsync("knowledge", "doc-1");
            Assert.HasCount(0, keys);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MapBlock_ShouldUseUnifiedKeyFormat()
    {
        var cleaning = new TextCleaningService();
        var chunking = new TextChunkingService();
        var mapper = new DocumentBlockMapper(cleaning, chunking);

        var (paragraphs, _, _) = mapper.MapBlock(
            new HeadingBlock { Text = "投资分析", Level = 2, Order = 0 },
            @"C:\docs\a.pdf", "doc123", 0, null);

        var paragraph = paragraphs.Single();
        StringAssert.StartsWith(paragraph.Key, "doc123:", "Key 应以 DocumentId 开头");
        StringAssert.StartsWith(paragraph.Key, "doc123:1:000000:", "Key 应包含块类型与 Order 段");
    }
}