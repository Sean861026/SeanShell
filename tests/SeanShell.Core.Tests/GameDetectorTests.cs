using SeanShell.Gaming;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class GameDetectorTests
{
    [TestMethod]
    public void IsGame_NormalizesExeExtensionAndCase()
    {
        var detector = new GameDetector(["eldenring.exe"]);

        Assert.IsTrue(detector.IsGame("ELDENRING"));
        Assert.IsTrue(detector.IsGame("eldenring.exe"));
        Assert.IsFalse(detector.IsGame("explorer.exe"));
    }

    [TestMethod]
    public void ParseRulesNormalizesSeparatorsAndRemovesDuplicates()
    {
        var rules = GameDetector.ParseRules("eldenring.exe; Game.EXE\r\neldenring");

        CollectionAssert.AreEqual(new[] { "eldenring", "Game" }, rules.ToArray());
    }
}
