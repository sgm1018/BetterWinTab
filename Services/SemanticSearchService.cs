using BetterWinTab.Models;

namespace BetterWinTab.Services;

public sealed class SemanticSearchService
{
    private readonly Dictionary<string, IndexedRecord> _records = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public int Count
    {
        get
        {
            lock (_sync)
                return _records.Count;
        }
    }

    public void Replace(IEnumerable<(SemanticSearchRecord Record, float[] Embedding)> records)
    {
        lock (_sync)
        {
            _records.Clear();
            foreach (var item in records)
                _records[item.Record.Id] = new IndexedRecord(item.Record, Normalize(item.Embedding));
        }
    }

    public void Upsert(SemanticSearchRecord record, float[] embedding)
    {
        lock (_sync)
            _records[record.Id] = new IndexedRecord(record, Normalize(embedding));
    }

    public void Remove(string id)
    {
        lock (_sync)
            _records.Remove(id);
    }

    public IReadOnlyList<(SemanticSearchRecord Record, float Score)> Search(
        IReadOnlyList<float> queryEmbedding,
        int limit = 20)
    {
        var query = Normalize(queryEmbedding);
        lock (_sync)
        {
            return _records.Values
                .Where(item => item.Embedding.Length == query.Length)
                .Select(item => (item.Record, Score: Dot(query, item.Embedding)))
                .OrderByDescending(item => item.Score)
                .Take(Math.Max(0, limit))
                .ToArray();
        }
    }

    private static float[] Normalize(IReadOnlyList<float> vector)
    {
        var length = MathF.Sqrt(vector.Sum(value => value * value));
        if (length <= float.Epsilon)
            return vector.ToArray();

        var normalized = new float[vector.Count];
        for (var index = 0; index < vector.Count; index++)
            normalized[index] = vector[index] / length;
        return normalized;
    }

    private static float Dot(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var score = 0f;
        for (var index = 0; index < left.Count; index++)
            score += left[index] * right[index];
        return score;
    }

    private sealed record IndexedRecord(SemanticSearchRecord Record, float[] Embedding);
}