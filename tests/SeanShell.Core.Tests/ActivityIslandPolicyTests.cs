using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ActivityIslandPolicyTests
{
    [TestMethod]
    public void OnlyEnabledOrdinaryDesktopCanShowIsland()
    {
        for (var combination = 0; combination < 16; combination++)
        {
            var enabled = (combination & 1) != 0;
            var gaming = (combination & 2) != 0;
            var immersive = (combination & 4) != 0;
            var shuttingDown = (combination & 8) != 0;

            Assert.AreEqual(combination == 1,
                ActivityIslandPolicy.CanShow(enabled, gaming, immersive, shuttingDown),
                $"Unexpected visibility for combination {combination}.");
        }
    }
}
