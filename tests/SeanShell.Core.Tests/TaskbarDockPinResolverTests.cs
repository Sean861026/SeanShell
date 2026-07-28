using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarDockPinResolverTests
{
    [TestMethod]
    public void FindPinnedApplicationUsesAnyWindowInGroup()
    {
        var pinned = new[]
        {
            CreateCommand("app:vscode", "Visual Studio Code", "Code"),
            CreateCommand("app:terminal", "Terminal", "WindowsTerminal"),
        };
        var windows = new[]
        {
            CreateWindow("Code", "First"),
            CreateWindow("Code", "Second"),
        };

        var result = TaskbarDockPinResolver.FindPinnedApplication(pinned, windows);

        Assert.IsNotNull(result);
        Assert.AreEqual("app:vscode", result.Id);
    }

    [TestMethod]
    public void FindPinCandidatesKeepsDistinctMatchingShortcutsInIndexOrder()
    {
        var applications = new[]
        {
            CreateCommand("app:code", "Code", "Code.exe"),
            CreateCommand("app:code-insiders", "Code Insiders", "Code"),
            CreateCommand("app:code", "Duplicate", "CODE"),
            CreateCommand("app:terminal", "Terminal", "WindowsTerminal"),
        };

        var result = TaskbarDockPinResolver.FindPinCandidates(
            applications,
            [CreateWindow("code", "Repository")]);

        Assert.HasCount(2, result);
        Assert.AreEqual("app:code", result[0].Id);
        Assert.AreEqual("app:code-insiders", result[1].Id);
    }

    [TestMethod]
    public void FindPinCandidatesRejectsCommandsWithoutExplicitProcessIdentity()
    {
        var applications = new[]
        {
            CreateCommand("app:unknown", "Unknown", null),
            CreateCommand("app:other", "Other", "other"),
        };

        var result = TaskbarDockPinResolver.FindPinCandidates(
            applications,
            [CreateWindow("target", "Window")]);

        Assert.IsEmpty(result);
    }

    private static ShellCommand CreateCommand(
        string id,
        string title,
        string? processName) =>
        new(id, title, null, _ => ValueTask.CompletedTask)
        {
            ApplicationProcessName = processName,
        };

    private static DesktopWindowSnapshot CreateWindow(
        string processName,
        string title) =>
        new(1, 2, processName, title, false, 3);
}
