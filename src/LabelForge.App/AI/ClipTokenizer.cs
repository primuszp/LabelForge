using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace LabelForge.App.AI;

public sealed class ClipTokenizer
{
    public const int ContextLength = 32;
    private readonly Dictionary<byte, char> byteEncoder;
    private readonly Dictionary<string, int> encoder;
    private readonly Dictionary<(string, string), int> ranks;
    private readonly Dictionary<string, string> cache = [];
    private readonly Regex pattern = new(@"'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly int sot;
    private readonly int eot;

    public ClipTokenizer(string mergesPath)
    {
        byteEncoder = BytesToUnicode();
        var merges = File.ReadLines(mergesPath, Encoding.UTF8).Where(line => line.Length > 0)
            .Select(line => line.Split(' ')).Select(parts => (parts[0], parts[1])).ToList();
        var vocab = new List<string>(byteEncoder.Values.Select(value => value.ToString()));
        vocab.AddRange(byteEncoder.Values.Select(value => value + "</w>"));
        vocab.AddRange(merges.Select(merge => merge.Item1 + merge.Item2));
        vocab.Add("<start_of_text>");
        vocab.Add("<end_of_text>");
        encoder = vocab.Select((value, index) => (value, index)).ToDictionary(item => item.value, item => item.index);
        ranks = merges.Select((value, index) => (value, index)).ToDictionary(item => item.value, item => item.index);
        sot = encoder["<start_of_text>"];
        eot = encoder["<end_of_text>"];
    }

    public long[] Tokenize(string text)
    {
        var ids = new List<long> { sot };
        ids.AddRange(Encode(text).Select(id => (long)id));
        ids.Add(eot);
        if (ids.Count > ContextLength) { ids = ids.Take(ContextLength).ToList(); ids[^1] = eot; }
        while (ids.Count < ContextLength) ids.Add(0);
        return ids.ToArray();
    }

    private IEnumerable<int> Encode(string text)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim().ToLowerInvariant();
        foreach (Match match in pattern.Matches(text))
        {
            var encoded = new StringBuilder();
            foreach (var value in Encoding.UTF8.GetBytes(match.Value)) encoded.Append(byteEncoder[value]);
            foreach (var token in Bpe(encoded.ToString()).Split(' ')) yield return encoder[token];
        }
    }

    private string Bpe(string token)
    {
        if (cache.TryGetValue(token, out var result)) return result;
        var word = token.Select((value, index) => index == token.Length - 1 ? value + "</w>" : value.ToString()).ToList();
        if (word.Count == 1) return cache[token] = token + "</w>";
        while (true)
        {
            (string, string)? best = null; var bestRank = int.MaxValue;
            for (var i = 0; i < word.Count - 1; i++)
                if (ranks.TryGetValue((word[i], word[i + 1]), out var rank) && rank < bestRank)
                { best = (word[i], word[i + 1]); bestRank = rank; }
            if (best is null) break;
            var merged = new List<string>();
            for (var i = 0; i < word.Count;)
            {
                if (i < word.Count - 1 && word[i] == best.Value.Item1 && word[i + 1] == best.Value.Item2)
                { merged.Add(word[i] + word[i + 1]); i += 2; }
                else merged.Add(word[i++]);
            }
            word = merged;
        }
        return cache[token] = string.Join(" ", word);
    }

    private static Dictionary<byte, char> BytesToUnicode()
    {
        var bytes = Enumerable.Range('!', '~' - '!' + 1).Concat(Enumerable.Range(0xA1, 0xAC - 0xA1 + 1))
            .Concat(Enumerable.Range(0xAE, 0xFF - 0xAE + 1)).ToList();
        var chars = new List<int>(bytes); var next = 0;
        for (var value = 0; value < 256; value++)
            if (!bytes.Contains(value)) { bytes.Add(value); chars.Add(256 + next++); }
        return bytes.Select((value, index) => (value, index)).ToDictionary(item => (byte)item.value, item => (char)chars[item.index]);
    }
}
