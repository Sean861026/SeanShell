namespace SeanShell.Core;

public interface IDesktopVisibilityController
{
    DesktopVisibilityResult MinimizeAll();

    DesktopVisibilityResult UndoMinimizeAll();
}

public sealed record DesktopVisibilityResult(
    bool Success,
    string? Error = null);

public sealed class ShowDesktopSession(
    IDesktopVisibilityController controller)
{
    public bool IsDesktopShown { get; private set; }

    public DesktopVisibilityResult Toggle()
    {
        var result = IsDesktopShown
            ? controller.UndoMinimizeAll()
            : controller.MinimizeAll();
        if (result.Success)
        {
            IsDesktopShown = !IsDesktopShown;
        }

        return result;
    }

    public void Reset() => IsDesktopShown = false;
}
