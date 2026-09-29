using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class InputLayoutSwitchControllerTests
{
    private static readonly InputWindowIdentity Target = new(100, 200, 300);

    [TestMethod]
    public void CaptureFiltersShellWindowsAndInvalidLayouts()
    {
        var backend = new FakeBackend
        {
            Target = Target with { ProcessId = 999 },
            Layouts = [new(0, "invalid"), new(1, "English"), new(1, "duplicate")],
        };
        var snapshot = new InputLayoutSwitchController(backend, 999).Capture(100);

        Assert.IsNull(snapshot.Target);
        Assert.AreEqual((nint)0, snapshot.ActiveLayout);
        Assert.HasCount(1, snapshot.Layouts);
        Assert.AreEqual((nint)1, snapshot.Layouts[0].Handle);
    }

    [TestMethod]
    public void CaptureBoundsLayoutCount()
    {
        var backend = new FakeBackend
        {
            Layouts = Enumerable.Range(1, 100)
                .Select(static handle => new InputLayoutOption(handle, "layout"))
                .ToArray(),
        };
        var snapshot = new InputLayoutSwitchController(backend, 999).Capture(100);

        Assert.HasCount(64, snapshot.Layouts);
        Assert.AreEqual(Target, snapshot.Target);
        Assert.AreEqual((nint)1, snapshot.ActiveLayout);
    }

    [TestMethod]
    public void RequestIsNotConfirmationUntilObserved()
    {
        var backend = new FakeBackend();
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.AreEqual(InputLayoutSwitchResult.RequestPosted, controller.Request(Target, 2));
        Assert.AreEqual(1, backend.PostCount);
        Assert.AreEqual(Target, backend.LastTarget);
        Assert.AreEqual((nint)2, backend.LastLayout);
        Assert.AreEqual(InputLayoutSwitchResult.NotConfirmed, controller.Confirm(Target, 2));
        backend.ActiveLayout = 2;
        Assert.AreEqual(InputLayoutSwitchResult.Confirmed, controller.Confirm(Target, 2));
    }

    [TestMethod]
    public void AlreadyActiveDoesNotPost()
    {
        var backend = new FakeBackend();
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.AreEqual(InputLayoutSwitchResult.AlreadyActive, controller.Request(Target, 1));
        Assert.AreEqual(0, backend.PostCount);
    }

    [TestMethod]
    public void MissingTargetDoesNotPost()
    {
        var backend = new FakeBackend();
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.AreEqual(InputLayoutSwitchResult.NoTarget, controller.Request(null, 2));
        Assert.AreEqual(InputLayoutSwitchResult.NoTarget,
            controller.Request(Target with { ProcessId = 999 }, 2));
        Assert.AreEqual(0, backend.PostCount);
    }

    [TestMethod]
    [DataRow(0, 200, 300)]
    [DataRow(100, 0, 300)]
    [DataRow(100, 200, 0)]
    public void InvalidIdentityNeverPosts(int handle, int thread, int process)
    {
        var backend = new FakeBackend();
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.AreEqual(InputLayoutSwitchResult.NoTarget,
            controller.Request(new(handle, (uint)thread, (uint)process), 2));
        Assert.AreEqual(0, backend.PostCount);
    }

    [TestMethod]
    public void ClosedWindowDoesNotPost()
    {
        var backend = new FakeBackend { Target = null };
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.AreEqual(InputLayoutSwitchResult.TargetChanged, controller.Request(Target, 2));
        Assert.AreEqual(InputLayoutSwitchResult.TargetChanged, controller.Confirm(Target, 2));
        Assert.AreEqual(0, backend.PostCount);
        Assert.IsFalse(controller.IsCurrentTarget(Target));
    }

    [TestMethod]
    [DataRow(201, 300)]
    [DataRow(200, 301)]
    public void ReusedHandleWithDifferentOwnerDoesNotPost(int thread, int process)
    {
        var backend = new FakeBackend
        {
            Target = Target with { ThreadId = (uint)thread, ProcessId = (uint)process },
        };
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.AreEqual(InputLayoutSwitchResult.TargetChanged, controller.Request(Target, 2));
        Assert.AreEqual(InputLayoutSwitchResult.TargetChanged, controller.Confirm(Target, 2));
        Assert.AreEqual(0, backend.PostCount);
        Assert.IsFalse(controller.IsCurrentTarget(Target));
    }

    [TestMethod]
    public void UnrestoredForegroundNeverPosts()
    {
        var backend = new FakeBackend { Foreground = false };
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.AreEqual(InputLayoutSwitchResult.FocusNotRestored, controller.Request(Target, 2));
        Assert.AreEqual(0, backend.PostCount);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    public void UnloadedLayoutNeverPosts(int layout)
    {
        var backend = new FakeBackend();
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.AreEqual(InputLayoutSwitchResult.LayoutUnavailable, controller.Request(Target, layout));
        Assert.AreEqual(0, backend.PostCount);
    }

    [TestMethod]
    public void DeniedMessageIsNotReportedAsSuccess()
    {
        var backend = new FakeBackend { AcceptMessage = false };
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.AreEqual(InputLayoutSwitchResult.RequestFailed, controller.Request(Target, 2));
        Assert.AreEqual(1, backend.PostCount);
    }

    [TestMethod]
    public void ZeroLayoutCannotBeConfirmed()
    {
        var backend = new FakeBackend { ActiveLayout = 0 };
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.AreEqual(InputLayoutSwitchResult.NotConfirmed, controller.Confirm(Target, 0));
    }

    [TestMethod]
    public void OnlyCurrentExternalWindowCanBeRestored()
    {
        var backend = new FakeBackend();
        var controller = new InputLayoutSwitchController(backend, 999);

        Assert.IsTrue(controller.IsCurrentTarget(Target));
        Assert.IsFalse(controller.IsCurrentTarget(null));
        Assert.IsFalse(controller.IsCurrentTarget(Target with { ProcessId = 999 }));
    }

    private sealed class FakeBackend : IInputLayoutBackend
    {
        public InputWindowIdentity? Target { get; set; } = InputLayoutSwitchControllerTests.Target;
        public IReadOnlyList<InputLayoutOption> Layouts { get; set; } =
            [new(1, "English"), new(2, "Chinese")];
        public nint ActiveLayout { get; set; } = 1;
        public bool Foreground { get; set; } = true;
        public bool AcceptMessage { get; set; } = true;
        public int PostCount { get; private set; }
        public InputWindowIdentity? LastTarget { get; private set; }
        public nint LastLayout { get; private set; }

        public InputWindowIdentity? CaptureTarget(nint handle) =>
            Target?.Handle == handle ? Target : null;
        public IReadOnlyList<InputLayoutOption> CaptureLayouts() => Layouts;
        public nint ReadActiveLayout(InputWindowIdentity target) => ActiveLayout;
        public bool IsForeground(InputWindowIdentity target) => Foreground;

        public bool TryPostChange(InputWindowIdentity target, nint layout)
        {
            PostCount++;
            LastTarget = target;
            LastLayout = layout;
            return AcceptMessage;
        }
    }
}
