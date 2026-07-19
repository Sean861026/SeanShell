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
}
