using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ApplicationIconSnapshotTests
{
    [TestMethod]
    public void AcceptsExactBgraBufferAndCopiesIt()
    {
        var pixels = new byte[2 * 3 * 4];

        var snapshot = new ApplicationIconSnapshot(2, 3, pixels);
        pixels[0] = 42;

        Assert.AreEqual(2, snapshot.Width);
        Assert.AreEqual(3, snapshot.Height);
        Assert.AreEqual(24, snapshot.BgraPixels.Length);
        Assert.AreEqual(0, snapshot.BgraPixels.Span[0]);
    }

    [TestMethod]
    public void RejectsMismatchedBgraBuffer()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new ApplicationIconSnapshot(2, 2, new byte[15]));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(257)]
    public void RejectsUnsafeDimensions(int dimension)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new ApplicationIconSnapshot(dimension, 1, []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new ApplicationIconSnapshot(1, dimension, []));
    }

    [TestMethod]
    public void PreferredDimensionHasBoundedBgraFootprint()
    {
        var byteCount = ApplicationIconSnapshot.PreferredDimension *
            ApplicationIconSnapshot.PreferredDimension *
            4;
        var snapshot = new ApplicationIconSnapshot(
            ApplicationIconSnapshot.PreferredDimension,
            ApplicationIconSnapshot.PreferredDimension,
            new byte[byteCount]);

        Assert.AreEqual(48, snapshot.Width);
        Assert.AreEqual(48, snapshot.Height);
        Assert.AreEqual(9_216, snapshot.BgraPixels.Length);
    }
}
