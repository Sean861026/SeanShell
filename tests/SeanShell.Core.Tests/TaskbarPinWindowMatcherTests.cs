using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class TaskbarPinWindowMatcherTests
{
    [DataRow("vivaldi", "vivaldi", true)]
    [DataRow("vivaldi.exe", "VIVALDI", true)]
    [DataRow("Code", "devenv", false)]
    [DataRow(null, "vivaldi", false)]
    [TestMethod]
    public void IsMatchUsesExplicitProcessIdentityOnly(
        string? pinnedProcessName,
        string windowProcessName,
        bool expected)
    {
        var command = CreateCommand(pinnedProcessName);
        var window = new DesktopWindowSnapshot(
            1,
            2,
            windowProcessName,
            "Window",
            false,
            3);

        Assert.AreEqual(expected, TaskbarPinWindowMatcher.IsMatch(command, window));
    }

    private static ShellCommand CreateCommand(string? processName) =>
        new("app:test", "Test", null, _ => ValueTask.CompletedTask)
        {
            ApplicationProcessName = processName,
        };
}
