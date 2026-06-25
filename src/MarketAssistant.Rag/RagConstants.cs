namespace MarketAssistant.Rag;

/// <summary>
/// RAG 模块共享常量，避免魔法数字分散在各服务中。
/// </summary>
public static class RagConstants
{
    /// <summary>
    /// 向量嵌入维度（文本与图像统一维度，与 <see cref="TextParagraph"/> 注解、CLIP 服务一致）。
    /// 更换嵌入模型时只需修改此值并重建向量库。
    /// </summary>
    public const int EmbeddingDimension = 1024;
}
