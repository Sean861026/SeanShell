using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class WorkAreaReservationLayoutTests
{
    [TestMethod]
    public void ReservesFullHeightWhenNoBottomInsetExists()
    {
        var plan = WorkAreaReservationLayout.Calculate(
            new DockBounds(0, 0, 1920, 1080),
            116);

        Assert.AreEqual(116, plan.AdditionalHeight);
        Assert.AreEqual(new DockBounds(0, 964, 1920, 116), plan.ReservedArea);
    }

    [TestMethod]
    public void ReservesFullDockHeightDespiteTransientTaskbarInset()
    {
        var plan = WorkAreaReservationLayout.Calculate(
            new DockBounds(-1920, 0, 1920, 1080),
            116);

        Assert.AreEqual(116, plan.AdditionalHeight);
        Assert.AreEqual(new DockBounds(-1920, 964, 1920, 116), plan.ReservedArea);
    }

    [TestMethod]
    public void ClampsReservationToShortMonitor()
    {
        var plan = WorkAreaReservationLayout.Calculate(
            new DockBounds(0, 0, 100, 60),
            116);

        Assert.AreEqual(60, plan.AdditionalHeight);
        Assert.AreEqual(new DockBounds(0, 0, 100, 60), plan.ReservedArea);
    }
}
