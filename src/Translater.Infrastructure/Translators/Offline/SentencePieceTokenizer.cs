namespace Translater.Infrastructure.Translators.Offline;

/// <summary>
/// Minimal SentencePiece BPE/Unigram decoder that reads the .spm vocab exported
/// as a plain-text vocab file (one token per line: token\tscore).
/// For MarianMT OPUS models, we use the vocab.json + source.spm approach.
/// This implementation reads the vocabulary and merges from sentencepiece model
/// exported as vocab.txt format.
/// </summary>
public sealed class SentencePieceTokenizer : IDisposable
{
    private readonly Dictionary<string, int> _token2Id;
    private readonly Dictionary<int, string> _id2Token;
    private readonly int _padId;
    private readonly int _eosId;
    private readonly int _unkId;

    // SentencePiece special unicode char for space
    private const char SpaceChar = '\u2581';

    public int PadId => _padId;
    public int EosId => _eosId;
    public int VocabSize => _token2Id.Count;

    private SentencePieceTokenizer(Dictionary<string, int> token2Id, int padId, int eosId, int unkId)
    {
        _token2Id = token2Id;
        _id2Token = token2Id.ToDictionary(kv => kv.Value, kv => kv.Key);
        _padId = padId;
        _eosId = eosId;
        _unkId = unkId;
    }

    /// <summary>
    /// Load from a vocab.txt file (HuggingFace MarianMT format: one token per line).
    /// Token index = line number.
    /// </summary>
    public static SentencePieceTokenizer LoadFromVocab(string vocabPath)
    {
        var token2Id = new Dictionary<string, int>();
        int padId = 0, eosId = 0, unkId = 0;

        var lines = File.ReadAllLines(vocabPath);
        for (int i = 0; i < lines.Length; i++)
        {
            var token = lines[i].Split('\t')[0]; // vocab.txt may have tab-separated scores
            token2Id[token] = i;

            if (token == "<pad>") padId = i;
            else if (token == "</s>") eosId = i;
            else if (token == "<unk>") unkId = i;
        }

        return new SentencePieceTokenizer(token2Id, padId, eosId, unkId);
    }

    /// <summary>
    /// Simple greedy tokenization: longest-match from vocabulary.
    /// For production quality, a proper SentencePiece implementation would be better,
    /// but this works well enough for OPUS-MT models.
    /// </summary>
    public int[] Encode(string text)
    {
        // Normalize: replace spaces with SentencePiece space marker
        var normalized = SpaceChar + text.Replace(" ", SpaceChar.ToString());
        
        var tokens = new List<int>();
        int pos = 0;

        while (pos < normalized.Length)
        {
            int bestLen = 0;
            int bestId = _unkId;

            // Try longest match first (up to 20 chars)
            int maxLen = Math.Min(20, normalized.Length - pos);
            for (int len = maxLen; len >= 1; len--)
            {
                var candidate = normalized.Substring(pos, len);
                if (_token2Id.TryGetValue(candidate, out int id))
                {
                    bestLen = len;
                    bestId = id;
                    break;
                }
            }

            if (bestLen == 0)
            {
                // Single char fallback
                bestLen = 1;
                bestId = _unkId;
            }

            tokens.Add(bestId);
            pos += bestLen;
        }

        // Append EOS token
        tokens.Add(_eosId);
        return tokens.ToArray();
    }

    /// <summary>
    /// Decode token IDs back to text.
    /// </summary>
    public string Decode(IEnumerable<int> ids)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var id in ids)
        {
            if (id == _eosId || id == _padId)
                break;
            if (_id2Token.TryGetValue(id, out var token))
                sb.Append(token);
        }

        // Replace SentencePiece space marker with regular space and trim
        return sb.ToString().Replace(SpaceChar, ' ').Trim();
    }

    public void Dispose() { }
}
