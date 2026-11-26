using System.Text.Json;
using BetterWinTab.Models;
using BetterWinTab.Services;

CheckDomainConditions();
CheckFolderMembership();
Console.WriteLine("PASS: domain rules and browser tab membership in Smart Folders.");

static void CheckDomainConditions()
{
    CheckMatch(true, RuleComparison.Equals, "  GITHUB.COM.  ", "https://github.com:8443/login");
    CheckMatch(false, RuleComparison.Equals, "github.com", "https://docs.github.com");
    CheckMatch(true, RuleComparison.Contains, "github.com", "https://github.com");
    CheckMatch(true, RuleComparison.Contains, "github.com", "https://docs.github.com");
    CheckMatch(false, RuleComparison.Contains, "github.com", "https://notgithub.com");
    CheckMatch(false, RuleComparison.Contains, "github.com", "https://github.com.evil.test");
    CheckMatch(false, RuleComparison.Contains, "github.com", "https://other.test/github.com");
    CheckMatch(false, RuleComparison.Contains, "github.com", "https://github.com@other.test");
    CheckMatch(false, RuleComparison.Contains, "", "https://github.com");
    CheckMatch(false, RuleComparison.Contains, "github.com", "about:blank");
    CheckMatch(false, RuleComparison.Contains, "github.com", "not a URL");
    CheckMatch(false, RuleComparison.Equals, "github.com", "https://github.com", isBrowserTab: false);
    CheckMatch(true, RuleComparison.StartsWith, "docs", "https://docs.github.com");
    CheckMatch(false, RuleComparison.StartsWith, "doc", "https://docs.github.com");
    CheckMatch(true, RuleComparison.EndsWith, "github.com", "https://docs.github.com");
    CheckMatch(false, RuleComparison.EndsWith, "github.com", "https://notgithub.com");
    CheckMatch(true, RuleComparison.Regex, @"^docs\.github\.com$", "https://docs.github.com/");
    CheckMatch(false, RuleComparison.Regex, ".*", "about:blank");
    CheckMatch(true, RuleComparison.Equals, "münchen.de", "https://xn--mnchen-3ya.de");
    CheckMatch(true, RuleComparison.Equals, "127.0.0.1", "http://127.0.0.1:8321");
    CheckMatch(false, RuleComparison.Contains, "0.1", "http://127.0.0.1:8321");
    CheckMatch(true, RuleComparison.Equals, "[::1]", "http://[::1]:8321");
}

static void CheckFolderMembership()
{
    var folders = new FolderService();
    var domainRule = new FolderRuleCondition
    {
        Field = RuleField.Domain,
        Comparison = RuleComparison.Contains,
        Value = "github.com"
    };
    var editorRule = new FolderRuleCondition { Field = RuleField.ProcessName, Value = "Code" };
    var browserRule = new FolderRuleCondition { Field = RuleField.ProcessName, Value = "chrome" };
    var projectFolder = folders.CreateSmartRulesFolder("Project", new FolderRuleGroup
    {
        Operator = RuleOperator.OR,
        Conditions = [editorRule, domainRule]
    });
    var browserProjectFolder = folders.CreateSmartRulesFolder("Project in Chrome", new FolderRuleGroup
    {
        Operator = RuleOperator.AND,
        Conditions = [domainRule, browserRule]
    });
    var processFolder = folders.CreateSmartProcessFolder("Chrome windows", "chrome");
    var existingRuleFolder = folders.CreateSmartRulesFolder("Existing rule", new FolderRuleGroup
    {
        Conditions = [browserRule]
    });
    var manualFolder = folders.CreateManualFolder("Manual");
    var notesFolder = folders.CreateNotesFolder();

    var editor = new WindowInfo { Handle = 1, ProcessName = "Code" };
    var browserWindow = new WindowInfo { Handle = 2, ProcessName = "chrome" };
    var projectTab = new WindowInfo
    {
        Handle = browserWindow.Handle,
        ProcessName = "chrome",
        IsBrowserTab = true,
        BrowserName = "Chrome",
        BrowserTabId = "1",
        Url = "https://docs.github.com"
    };
    var otherTab = new WindowInfo
    {
        Handle = browserWindow.Handle,
        ProcessName = "chrome",
        IsBrowserTab = true,
        BrowserName = "Chrome",
        BrowserTabId = "2",
        Url = "https://github.com.evil.test"
    };
    manualFolder.ManualWindowHandles.Add(browserWindow.Handle);

    folders.RefreshAllFolders([editor, browserWindow], [projectTab, otherTab]);
    CheckMembers(folders.Folders.Single(folder => folder.Type == FolderType.All),
        editor, browserWindow, projectTab, otherTab);
    CheckMembers(projectFolder, editor, projectTab);
    CheckMembers(browserProjectFolder, projectTab);
    CheckMembers(processFolder, browserWindow);
    CheckMembers(existingRuleFolder, browserWindow);
    CheckMembers(manualFolder, browserWindow);
    CheckMembers(notesFolder);

    var settings = new AppSettings();
    folders.SaveToSettings(settings);
    var reloadedSettings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
    var reloadedFolders = new FolderService();
    reloadedFolders.LoadFromSettings(reloadedSettings);
    reloadedFolders.RefreshAllFolders([editor, browserWindow], [projectTab, otherTab]);
    var reloadedProjectFolder = reloadedFolders.Folders.Single(folder => folder.Id == projectFolder.Id);
    CheckMembers(reloadedProjectFolder, editor, projectTab);

    reloadedFolders.RefreshAllFolders([editor, browserWindow], []);
    CheckMembers(reloadedProjectFolder, editor);
}

static void CheckMatch(bool expected, RuleComparison comparison, string value, string url, bool isBrowserTab = true)
{
    var browserItem = new WindowInfo { IsBrowserTab = isBrowserTab, Url = url };
    var rule = new FolderRuleCondition { Field = RuleField.Domain, Comparison = comparison, Value = value };
    if (rule.Matches(browserItem) != expected)
        throw new InvalidOperationException($"Domain {comparison} {value} on {url}: expected {expected}.");
}

static void CheckMembers(WindowFolder folder, params WindowInfo[] expected)
{
    if (folder.Windows.Count != expected.Length || !expected.All(folder.Windows.Contains))
        throw new InvalidOperationException($"{folder.Name} has unexpected members: {string.Join(", ", folder.Windows)}");
}

namespace BetterWinTab.Models
{
    // FolderService uses only the saved folders from the app-wide settings type.
    public class AppSettings
    {
        public List<WindowFolder> Folders { get; set; } = new();
    }
}
