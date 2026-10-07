namespace BetterWinTab.Services;

/// <summary>
/// Provides fuzzy (approximate) string matching similar to fzf.
/// Supports character skipping, out-of-order bonus penalties, and
/// consecutive-character bonuses for a natural search feel.
/// </summary>
public static class FuzzyMatcher
{
    private const string WordSeparators = " -_./\\|:()[]";

    /// <summary>
    /// Returns a score ≥ 0 if <paramref name="query"/> fuzzy-matches <paramref name="text"/>,
    /// or -1 if there is no match. Higher scores = better match.
    ///
    /// A plain "characters appear in order" subsequence test is far too loose: with long
    /// window/tab titles almost any query matches somewhere, which hides the app/folder
    /// launcher results. Only two kinds of fuzzy alignments are accepted:
    ///   • compact — the matched characters lie within a short span (typos such as "crome" → "Chrome");
    ///   • word-initials — every jump lands at the start of a word ("vscode" → "Visual Studio Code").
    /// </summary>
    public static int Score(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.IsNullOrEmpty(text) || text.Length < query.Length) return -1;

        int maxSpan = query.Length + Math.Max(2, query.Length / 2);
        int maxSegments = Math.Max(2, (query.Length + 1) / 2);
        char firstQueryChar = char.ToLowerInvariant(query[0]);
        Span<int> positions = query.Length <= 128 ? stackalloc int[query.Length] : new int[query.Length];

        int best = -1;
        for (int start = 0; start <= text.Length - query.Length; start++)
        {
            if (char.ToLowerInvariant(text[start]) != firstQueryChar)
                continue;

            if (TryAlignCompact(text, query, start, maxSpan, positions))
                best = Math.Max(best, ScoreAlignment(text, query, positions));

            if (IsWordStart(text, start) && TryAlignWordStarts(text, query, start, maxSegments, positions))
                best = Math.Max(best, ScoreAlignment(text, query, positions));
        }

        return best < 0 ? -1 : Math.Max(0, best);
    }

    private static bool IsWordStart(string text, int index)
        => index == 0
           || WordSeparators.Contains(text[index - 1])
           || (char.IsUpper(text[index]) && char.IsLower(text[index - 1]));

    private static bool TryAlignCompact(string text, string query, int start, int maxSpan, Span<int> positions)
    {
        positions[0] = start;
        int ti = start + 1;
        int limit = Math.Min(text.Length, start + maxSpan);
        for (int qi = 1; qi < query.Length; qi++)
        {
            char qc = char.ToLowerInvariant(query[qi]);
            while (ti < limit && char.ToLowerInvariant(text[ti]) != qc)
                ti++;
            if (ti >= limit)
                return false;
            positions[qi] = ti++;
        }
        return true;
    }

    private static bool TryAlignWordStarts(string text, string query, int start, int maxSegments, Span<int> positions)
    {
        positions[0] = start;
        int segments = 1;
        int pos = start;
        for (int qi = 1; qi < query.Length; qi++)
        {
            char qc = char.ToLowerInvariant(query[qi]);
            if (pos + 1 < text.Length && char.ToLowerInvariant(text[pos + 1]) == qc)
            {
                pos++;
            }
            else
            {
                int next = -1;
                for (int ti = pos + 2; ti < text.Length; ti++)
                {
                    if (char.ToLowerInvariant(text[ti]) == qc && IsWordStart(text, ti))
                    {
                        next = ti;
                        break;
                    }
                }
                if (next < 0 || ++segments > maxSegments)
                    return false;
                pos = next;
            }
            positions[qi] = pos;
        }
        return true;
    }

    private static int ScoreAlignment(string text, string query, ReadOnlySpan<int> positions)
    {
        int score = 0;
        int consecutive = 0;
        int previous = -2;

        for (int qi = 0; qi < positions.Length; qi++)
        {
            int ti = positions[qi];
            consecutive = ti == previous + 1 ? consecutive + 1 : 1;

            // Consecutive match bonus (rewards typing contiguous substrings)
            score += 10 + (consecutive * 5);

            if (IsWordStart(text, ti))
                score += 20;

            if (text[ti] == query[qi])
                score += 2;

            previous = ti;
        }

        // Bonus: the earlier the first match, the better
        score += Math.Max(0, 50 - positions[0] * 3);

        // Penalty for very long texts (prefer shorter matches)
        score -= (text.Length - query.Length) / 4;

        return score;
    }

    /// <summary>
    /// Returns true if <paramref name="query"/> fuzzy-matches <paramref name="text"/>.
    /// </summary>
    public static bool IsMatch(string text, string query)
        => Score(text, query) >= 0;

    /// <summary>
    /// Returns true if <paramref name="query"/> is an exact substring (Contains) match.
    /// Falls back to fuzzy only if the exact match fails.
    /// </summary>
    public static (bool matched, int score) MatchWithFallback(string text, string query)
    {
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            return (true, 1000 + (100 - text.Length)); // Exact substring always wins

        int s = Score(text, query);
        return s >= 0 ? (true, s) : (false, -1);
    }

    /// <summary>
    /// Multi-word token matching: each space-separated word in <paramref name="query"/>
    /// must independently fuzzy-match at least one of the provided <paramref name="fields"/>.
    /// Returns the sum of best-per-word scores, or -1 if any word has no match.
    /// 
    /// Example: query "google crome" with fields ["Google Gemini - Brave", "brave", "Chrome_WidgetWin_1"]
    ///   → "google" matches title field, "crome" matches className field → overall match.
    /// </summary>
    public static (bool matched, int score) MultiWordMatch(string[] fields, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int total = 0;
        foreach (var word in words)
        {
            int best = -1;
            foreach (var field in fields)
            {
                var (m, s) = MatchWithFallback(field, word);
                if (m && s > best)
                    best = s;
            }
            if (best < 0)
                return (false, -1); // This word matched nothing
            total += best;
        }
        return (true, total);
    }
}
