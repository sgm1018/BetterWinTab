namespace BetterWinTab.Models;

/// <summary>
/// A composite rule engine for Smart Folders.
/// Supports AND/OR operators with conditions on process name, title, class name, and browser tab domain.
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
        if (Field == RuleField.Domain)
            return MatchesDomain(window);

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

    private bool MatchesDomain(WindowInfo window)
    {
        if (!window.IsBrowserTab || string.IsNullOrWhiteSpace(Value) ||
            !Uri.TryCreate(window.Url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return false;

        var host = uri.IdnHost.TrimEnd('.');
        if (Comparison == RuleComparison.Regex)
            return TryRegexMatch(host, Value);

        var domain = Value.Trim().TrimEnd('.');
        var domainType = Uri.CheckHostName(domain);
        if (domainType == UriHostNameType.Unknown)
            return false;

        if (domainType == UriHostNameType.Dns)
        {
            try { domain = new System.Globalization.IdnMapping().GetAscii(domain); }
            catch (ArgumentException) { return false; }
        }
        else if (domainType == UriHostNameType.IPv6)
        {
            domain = domain.Trim('[', ']');
        }

        const StringComparison ignoreCase = StringComparison.OrdinalIgnoreCase;
        var exactMatch = host.Equals(domain, ignoreCase);
        var canMatchLabels = uri.HostNameType == UriHostNameType.Dns && domainType == UriHostNameType.Dns;
        // Domain containment means a parent domain, with a full label boundary.
        return Comparison switch
        {
            RuleComparison.Equals => exactMatch,
            RuleComparison.Contains or RuleComparison.EndsWith =>
                exactMatch || (canMatchLabels && host.EndsWith($".{domain}", ignoreCase)),
            RuleComparison.StartsWith =>
                exactMatch || (canMatchLabels && host.StartsWith($"{domain}.", ignoreCase)),
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
    ClassName,
    Domain
}

public enum RuleComparison
{
    Equals,
    Contains,
    StartsWith,
    EndsWith,
    Regex
}
