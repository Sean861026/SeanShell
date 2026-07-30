using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class AudioEndpointTextFormatterTests
{
    [TestMethod]
    public void FormatsAvailableVolume()
    {
        var text = AudioEndpointTextFormatter.Format(
            new AudioEndpointSnapshot(true, 42, false));

        Assert.AreEqual("Sound — 42%", text.Summary);
        Assert.AreEqual("Sound 42 percent.", text.AccessibleSummary);
    }

    [TestMethod]
    public void FormatsMutedVolumeAccessibly()
    {
        var text = AudioEndpointTextFormatter.Format(
            new AudioEndpointSnapshot(true, 18, true));

        Assert.AreEqual("Sound — Muted", text.Summary);
        Assert.AreEqual(
            "Sound muted at 18 percent.",
            text.AccessibleSummary);
    }

    [TestMethod]
    public void FormatsUnavailableEndpoint()
    {
        var text = AudioEndpointTextFormatter.Format(
            new AudioEndpointSnapshot(false, null, false));

        Assert.AreEqual("Sound — Status unavailable", text.Summary);
        Assert.AreEqual(
            "Sound status unavailable.",
            text.AccessibleSummary);
    }

    [TestMethod]
    public void ClampsInvalidVolumeForDisplay()
    {
        var text = AudioEndpointTextFormatter.Format(
            new AudioEndpointSnapshot(true, 140, false));

        Assert.AreEqual("Sound — 100%", text.Summary);
    }
}
