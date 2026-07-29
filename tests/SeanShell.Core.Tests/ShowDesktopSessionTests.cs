using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ShowDesktopSessionTests
{
    [TestMethod]
    public void ToggleMinimizesThenRestoresAllWindows()
    {
        var calls = new List<string>();
        var session = new ShowDesktopSession(new StubController(calls));

        var minimized = session.Toggle();
        var restored = session.Toggle();

        Assert.IsTrue(minimized.Success);
        Assert.IsTrue(restored.Success);
        Assert.IsFalse(session.IsDesktopShown);
        CollectionAssert.AreEqual(
            new[] { "minimize", "restore" },
            calls);
    }

    [TestMethod]
    public void FailureDoesNotChangeSessionState()
    {
        var calls = new List<string>();
        var session = new ShowDesktopSession(
            new StubController(calls) { MinimizeSucceeds = false });

        var result = session.Toggle();

        Assert.IsFalse(result.Success);
        Assert.IsFalse(session.IsDesktopShown);
        CollectionAssert.AreEqual(new[] { "minimize" }, calls);
    }

    [TestMethod]
    public void ResetInvalidatesPendingRestore()
    {
        var calls = new List<string>();
        var session = new ShowDesktopSession(new StubController(calls));
        Assert.IsTrue(session.Toggle().Success);

        session.Reset();
        var result = session.Toggle();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(session.IsDesktopShown);
        CollectionAssert.AreEqual(
            new[] { "minimize", "minimize" },
            calls);
    }

    private sealed class StubController(List<string> calls) :
        IDesktopVisibilityController
    {
        public bool MinimizeSucceeds { get; init; } = true;

        public DesktopVisibilityResult MinimizeAll()
        {
            calls.Add("minimize");
            return new DesktopVisibilityResult(
                MinimizeSucceeds,
                MinimizeSucceeds ? null : "minimize failed");
        }

        public DesktopVisibilityResult UndoMinimizeAll()
        {
            calls.Add("restore");
            return new DesktopVisibilityResult(true);
        }
    }
}
