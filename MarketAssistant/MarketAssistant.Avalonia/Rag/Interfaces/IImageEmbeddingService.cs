using Microsoft.Extensions.AI;

namespace MarketAssistant.Vectors.Interfaces;

public interface IImageEmbeddingService
{
    /// <summary>
    /// ����ͼ�����������ı�ά�ȶ���������ӳ�䣩��
    /// </summary>
    Task<Embedding<float>> GenerateAsync(byte[] imageBytes, CancellationToken ct = default);

    /// <summary>
    /// ���ɼ�ռλ Caption���������ַ�������
    /// </summary>
    Task<string> CaptionAsync(byte[] imageBytes, CancellationToken ct = default);
}
