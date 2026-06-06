using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LabelForge.App.AI;

/// <summary>
/// Minimal CLIP-compatible BPE tokenizer.
///
/// Vocabulary and merge files are loaded from the models folder:
///   vocab.json  – token → id mapping
///   merges.txt  – BPE merge pairs
///
/// If vocab files are not found, falls back to ASCII character-level encoding
/// (works for simple single-word prompts in English).
///
/// Reference: https://github.com/openai/CLIP/blob/main/clip/simple_tokenizer.py
/// </summary>
public static class ClipTokenizer
{
    private const int BOS = 49406;   // <|startoftext|>
    private const int EOS = 49407;   // <|endoftext|>
    private const int MaxTokens = 77;

    private static Dictionary<string, int>? vocab;
    private static Dictionary<(string, string), int>? bpeMerges;
    private static readonly object loadLock = new();

    // ── Public API ────────────────────────────────────────────────────────

    public static long[] Tokenize(string text)
    {
        EnsureLoaded();

        var tokens = new List<long> { BOS };

        if (vocab is not null && bpeMerges is not null)
        {
            // Proper BPE tokenization
            foreach (var word in TokenizeWords(text))
            {
                var wordTokens = BpeEncode(word);
                tokens.AddRange(wordTokens.Select(t => (long)t));
                if (tokens.Count >= MaxTokens - 1) break;
            }
        }
        else
        {
            // Fallback: character-level ASCII encoding
            foreach (char c in text.ToLower().Trim())
            {
                tokens.Add(Math.Min((int)c + 256, 49405));
                if (tokens.Count >= MaxTokens - 1) break;
            }
        }

        tokens.Add(EOS);

        // Pad to MaxTokens
        while (tokens.Count < MaxTokens)
            tokens.Add(0L);

        return tokens.Take(MaxTokens).ToArray();
    }

    // ── Loading ───────────────────────────────────────────────────────────

    private static void EnsureLoaded()
    {
        if (vocab is not null) return;
        lock (loadLock)
        {
            if (vocab is not null) return;
            TryLoad();
        }
    }

    private static void TryLoad()
    {
        var folder = PresetModels.ModelsFolder;
        var vocabPath  = Path.Combine(folder, "vocab.json");
        var mergesPath = Path.Combine(folder, "merges.txt");

        if (!File.Exists(vocabPath) || !File.Exists(mergesPath)) return;

        try
        {
            // Load vocab
            var json = File.ReadAllText(vocabPath);
            vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new();

            // Load BPE merges
            bpeMerges = new Dictionary<(string, string), int>();
            var lines = File.ReadAllLines(mergesPath);
            int rank = 0;
            foreach (var line in lines.Skip(1)) // skip header
            {
                var parts = line.Split(' ');
                if (parts.Length == 2)
                    bpeMerges[(parts[0], parts[1])] = rank++;
            }
        }
        catch
        {
            vocab      = null;
            bpeMerges  = null;
        }
    }

    // ── BPE ───────────────────────────────────────────────────────────────

    // Word tokeniser: splits on whitespace and punctuation, adds </w> suffix
    private static readonly Regex WordPattern =
        new(@"'s|'t|'re|'ve|'m|'ll|'d|\p{L}+|\p{N}+|[^\s\p{L}\p{N}]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static IEnumerable<string> TokenizeWords(string text) =>
        WordPattern.Matches(text.ToLower())
                   .Select(m => m.Value.Trim())
                   .Where(w => !string.IsNullOrEmpty(w))
                   .Select(w => w + "</w>");

    private static List<int> BpeEncode(string word)
    {
        if (vocab is null || bpeMerges is null) return [];

        // Start: individual characters + </w> marker already on the last char
        var chars = word.ToCharArray()
                        .Select(c => c.ToString())
                        .ToList();

        if (chars.Count == 0) return [];

        // Try to look up the whole word first (common short words)
        if (vocab.TryGetValue(word, out int wordId))
            return [wordId];

        var symbols = chars;

        // BPE merge loop
        while (symbols.Count > 1)
        {
            int bestRank = int.MaxValue;
            int bestIdx  = -1;

            for (int i = 0; i < symbols.Count - 1; i++)
            {
                if (bpeMerges.TryGetValue((symbols[i], symbols[i + 1]), out int rank)
                    && rank < bestRank)
                {
                    bestRank = rank;
                    bestIdx  = i;
                }
            }

            if (bestIdx < 0) break;

            var merged = symbols[bestIdx] + symbols[bestIdx + 1];
            symbols = [..symbols[..bestIdx], merged, ..symbols[(bestIdx + 2)..]];
        }

        return symbols
            .Select(s => vocab.TryGetValue(s, out int id) ? id : 0)
            .Where(id => id > 0)
            .ToList();
    }
}
