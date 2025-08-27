using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MarketAssistant.Vectors.Tokenization
{
    public sealed class WordPieceTokenizer : IWordPieceTokenizer
    {
        private readonly Dictionary<string, int> _vocab;

        public int CLS { get; }
        public int SEP { get; }
        public int UNK { get; }

        public bool HasVocab => _vocab.Count > 0;

        public WordPieceTokenizer(string? vocabPath)
        {
            _vocab = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(vocabPath) && File.Exists(vocabPath))
            {
                foreach (var line in File.ReadLines(vocabPath, Encoding.UTF8))
                {
                    var token = line.Trim();
                    if (token.Length == 0) continue;
                    if (!_vocab.ContainsKey(token)) _vocab[token] = _vocab.Count;
                }
            }
            UNK = _vocab.TryGetValue("[UNK]", out var u) ? u : 100;
            CLS = _vocab.TryGetValue("[CLS]", out var c) ? c : 101;
            SEP = _vocab.TryGetValue("[SEP]", out var s) ? s : 102;
        }

        public IEnumerable<int> Tokenize(string text)
        {
            if (_vocab.Count == 0) yield break;
            if (text == null) yield break;
            text = text.ToLowerInvariant();
            text = Regex.Replace(text, @"\s+", " ").Trim();
            foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (_vocab.TryGetValue(token, out var id)) { yield return id; continue; }
                int start = 0; bool failed = false; var pieces = new List<int>();
                while (start < token.Length)
                {
                    int end = token.Length;
                    int curId = -1; string? curStr = null;
                    while (start < end)
                    {
                        var sub = token.Substring(start, end - start);
                        if (start > 0) sub = "##" + sub;
                        if (_vocab.TryGetValue(sub, out var sid)) { curId = sid; curStr = sub; break; }
                        end--;
                    }
                    if (curId == -1) { failed = true; break; }
                    pieces.Add(curId);
                    start += (curStr!.StartsWith("##") ? curStr.Length - 2 : curStr.Length);
                }
                if (failed || pieces.Count == 0) yield return UNK;
                else foreach (var p in pieces) yield return p;
            }
        }
    }
}
