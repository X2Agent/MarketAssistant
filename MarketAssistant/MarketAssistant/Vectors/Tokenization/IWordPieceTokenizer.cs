using System.Collections.Generic;

namespace MarketAssistant.Vectors.Tokenization
{
    public interface IWordPieceTokenizer
    {
        IEnumerable<int> Tokenize(string text);
        int CLS { get; }
        int SEP { get; }
        int UNK { get; }
        bool HasVocab { get; }
    }
}
