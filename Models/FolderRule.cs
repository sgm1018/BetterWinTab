namespace BetterWinTab.Models;

/// <summary>
/// A composite rule engine for Smart Folders.
/// Supports AND/OR operators with multiple conditions on processName, title, and className.
/// </summary>
public class FolderRuleGroup
{
    /// <summary>AND = all conditions must match; OR = any condition can match.</summary>
    public RuleOperator Operator { get; set; } = RuleOperator.OR;

    /// <summary>Individual conditions to evaluate.</summary>
    public List<FolderRuleCondition> Conditions { get; set; } = new();

    /// <summary>
    /// Evaluates the rule group against a WindowInfo.
    /// </summary>
    public bool Matches(WindowInfo window)
    {
        if (Conditions.Count == 0) return false;

        return Operator == RuleOperator.AND
            ? Conditions.All(c => c.Matches(window))
            : Conditions.Any(c => c.Matches(window));
    }
}

/// <summary>
/// A single condition that tests a window field against a value.
/// </summary>
public class FolderRuleCondition
{
    /// <summary>Which window property to test.</summary>
    public RuleField Field { get; set; } = RuleField.ProcessName;

    /// <summary>Comparison operator.</summary>
    public RuleComparison Comparison { get; set; } = RuleComparison.Equals;

    /// <summary>The value to compare against.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Evaluates this condition against a WindowInfo.
    /// </summary>
    public bool Matches(WindowInfo window)
    {
        var fieldValue = Field switch
        {
            RuleField.ProcessName => window.ProcessName,
            RuleField.Title => window.Title,
            RuleField.ClassName => window.ClassName,
            _ => string.Empty
        };

        return Comparison switch
        {
            RuleComparison.Equals => fieldValue.Equals(Value, StringComparison.OrdinalIgnoreCase),
            RuleComparison.Contains => fieldValue.Contains(Value, StringComparison.OrdinalIgnoreCase),
            RuleComparison.StartsWith => fieldValue.StartsWith(Value, StringComparison.OrdinalIgnoreCase),
            RuleComparison.EndsWith => fieldValue.EndsWith(Value, StringComparison.OrdinalIgnoreCase),
            RuleComparison.Regex => TryRegexMatch(fieldValue, Value),
            _ => false
        };
    }

    private static bool TryRegexMatch(string input, string pattern)
    {
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                input, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Display string for the Field enum (used in compiled XAML bindings).
    /// </summary>
    public string FieldDisplay => Field.ToString();

    /// <summary>
    /// Display string for the Comparison enum (used in compiled XAML bindings).
    /// </summary>
    public string ComparisonDisplay => Comparison.ToString();

    public override string ToString()
    {
        return $"{Field} {Comparison} \"{Value}\"";
    }
}

public enum RuleOperator
{
    AND,
    OR
}

public enum RuleField
{
    ProcessName,
    Title,
    ClassName
}

public enum RuleComparison
{
    Equals,
    Contains,
    StartsWith,
    EndsWith,
    Regex
}
