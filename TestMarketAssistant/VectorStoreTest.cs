using MarketAssistant.Vectors;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace TestMarketAssistant;

[TestClass]
public class VectorStoreTest : BaseKernelTest
{
    private DataUploader dataUploader;

    [TestInitialize]
    public void Initialize()
    {
        var kernel = CreateKernelWithChatCompletion();
        var embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        var vectorStore = kernel.GetRequiredService<VectorStore>();
        dataUploader = new DataUploader(vectorStore, embeddingGenerator);
    }

    [TestMethod]
    public async Task TestDocxDocumentEmbeddingAsync()
    {
        // 创建模拟的文档段落数据，而不是依赖本地文件
        var textParagraphs = new List<TextParagraph>
        {
            new TextParagraph
            {
                Key = Guid.NewGuid().ToString(),
                DocumentUri = "test://document1.docx",
                ParagraphId = "paragraph_1",
                Text = "这是一个测试文档的第一段落，用于测试文档嵌入功能。"
            },
            new TextParagraph
            {
                Key = Guid.NewGuid().ToString(),
                DocumentUri = "test://document1.docx",
                ParagraphId = "paragraph_2",
                Text = "这是第二个段落，包含了更多的测试内容，用于验证向量存储的功能。"
            },
            new TextParagraph
            {
                Key = Guid.NewGuid().ToString(),
                DocumentUri = "test://document1.docx",
                ParagraphId = "paragraph_3",
                Text = "最后一个段落用于完成测试数据的构建，确保测试的完整性。"
            }
        };

        await dataUploader.GenerateEmbeddingsAndUploadAsync(
            "documentation",
            textParagraphs);
    }

    [TestMethod]
    public async Task TestPdfEmbeddingAsync()
    {
        // 创建模拟的PDF段落数据，而不是依赖本地文件
        var textParagraphs = new List<TextParagraph>
        {
            new TextParagraph
            {
                Key = Guid.NewGuid().ToString(),
                DocumentUri = "test://document.pdf",
                ParagraphId = "page_1_paragraph_1",
                Text = "成为高手的第一个要点：持续学习和实践是成功的关键。"
            },
            new TextParagraph
            {
                Key = Guid.NewGuid().ToString(),
                DocumentUri = "test://document.pdf",
                ParagraphId = "page_1_paragraph_2",
                Text = "第二个要点：建立系统性的知识体系，避免碎片化学习。"
            },
            new TextParagraph
            {
                Key = Guid.NewGuid().ToString(),
                DocumentUri = "test://document.pdf",
                ParagraphId = "page_2_paragraph_1",
                Text = "第三个要点：培养批判性思维，不断质疑和验证所学知识。"
            },
            new TextParagraph
            {
                Key = Guid.NewGuid().ToString(),
                DocumentUri = "test://document.pdf",
                ParagraphId = "page_2_paragraph_2",
                Text = "第四个要点：注重实践应用，将理论知识转化为实际能力。"
            }
        };

        await dataUploader.GenerateEmbeddingsAndUploadAsync(
            "pdf",
            textParagraphs);
    }
}
