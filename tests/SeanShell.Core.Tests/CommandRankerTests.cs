using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class CommandRankerTests
{
    [TestMethod]
    public void Score_ExactTitleRanksAbovePrefixAndKeyword()
    {
        var command = CreateCommand("Windows Terminal", ["console", "powershell"]);

        var exact = CommandRanker.Score(command, "Windows Terminal");
        var prefix = CommandRanker.Score(command, "Windows");
        var keyword = CommandRanker.Score(command, "console");

        Assert.IsGreaterThan(prefix, exact);
        Assert.IsGreaterThan(keyword, prefix);
    }

    [TestMethod]
    public void Score_MultipleTokensRequireEveryTokenToMatch()
    {
        var command = CreateCommand("Visual Studio Code", ["editor", "development"]);

        Assert.IsGreaterThan(0, CommandRanker.Score(command, "visual code"));
        Assert.AreEqual(0, CommandRanker.Score(command, "visual browser"));
    }

    [TestMethod]
    public void Score_SubsequenceSupportsShortFuzzyQueries()
    {
        var command = CreateCommand("Task Manager", []);

        Assert.IsGreaterThan(0, CommandRanker.Score(command, "tskmgr"));
    }

    private static ShellCommand CreateCommand(string title, IReadOnlyList<string> keywords) =>
        new($"test:{title}", title, null, _ => ValueTask.CompletedTask)
        {
            Keywords = keywords,
        };
}
