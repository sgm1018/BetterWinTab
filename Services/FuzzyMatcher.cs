namespace BetterWinTab.Services;

/// <summary>
/// Provides fuzzy (approximate) string matching similar to fzf.
/// Supports character skipping, out-of-order bonus penalties, and
/// consecutive-character bonuses for a natural search feel.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// Returns a score ≥ 0 if <paramref name="query"/> fuzzy-matches <paramref name="text"/>,
    /// or -1 if there is no match. Higher scores = better match.
    /// </summary>
    public static int Score(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.IsNullOrEmpty(text)) return -1;

        int qi = 0;          // query index
        int score = 0;
        int consecutive = 0;
        bool prevMatched = false;
        int firstMatchIndex = -1;

        for (int ti = 0; ti < text.Length && qi < query.Length; ti++)
        {
            char tc = char.ToLowerInvariant(text[ti]);
            char qc = char.ToLowerInvariant(query[qi]);

            if (tc == qc)
            {
                if (firstMatchIndex < 0) firstMatchIndex = ti;

                // Consecutive match bonus (rewards typing contiguous substrings)
                consecutive++;
                score += 10 + (consecutive * 5);

                // Bonus for matching at word boundaries (after space, dash, underscore, or start)
                if (ti == 0 || " -_./\\".Contains(text[ti - 1]))
                    score += 20;

                // Bonus for case-exact match
                if (text[ti] == query[qi])
                    score += 2;

                prevMatched = true;
                qi++;
            }
            else
            {
                if (prevMatched)
                    consecutive = 0;
                prevMatched = false;
            }
        }

        // All query chars must have been consumed
        if (qi < query.Length)
            return -1;

        // Bonus: the earlier the first match, the better
        if (firstMatchIndex >= 0)
            score += Math.Max(0, 50 - firstMatchIndex * 3);

        // Penalty for very long texts (prefer shorter matches)
        score -= (text.Length - query.Length) / 4;

        return Math.Max(0, score);
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
