using System.Runtime.InteropServices;

namespace SeanShell.InputLayoutProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var notebookOnly = args.Length == 1 &&
            string.Equals(args[0], "--notebook-only", StringComparison.OrdinalIgnoreCase);
        if (args.Length != 0 && !notebookOnly)
        {
            Console.Error.WriteLine("Usage: SeanShell.InputLayoutProbe.exe [--notebook-only]");
            return 64;
        }

        ApplicationConfiguration.Initialize();
        if (notebookOnly)
        {
            // A failed startup safety check must propagate to Main, not open a
            // WinForms exception dialog on an arbitrary display.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        }

        try
        {
            // Resolve before constructing any Form. There is deliberately no
            // primary-display fallback when the notebook panel is unavailable.
            var workArea = notebookOnly ? NotebookProbePlacement.ResolveWorkArea() : (Rectangle?)null;
            using var form = new InputProbeWindow(workArea);
            Application.Run(form);
            return 0;
        }
        catch (Exception exception) when (notebookOnly && exception is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Notebook-only probe refused to open: {exception.Message}");
            return 2;
        }
    }
}

internal sealed class InputProbeWindow : Form
{
    private readonly Label _status = new() { AutoSize = true };
    private readonly TextBox _input = new() { Width = 480 };
    private readonly System.Windows.Forms.Timer _expiry = new() { Interval = 300_000 };
    private readonly nint _originalLayout = GetKeyboardLayout(0);
    private readonly bool _notebookOnly;
    private int _changes;

    public InputProbeWindow(Rectangle? notebookWorkArea = null)
    {
        _notebookOnly = notebookWorkArea is not null;
        Text = "SeanShell input layout test (temporary)";
        ClientSize = new Size(560, 210);
        StartPosition = _notebookOnly ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
        if (notebookWorkArea is { } workArea)
        {
            PlaceWithinNotebook(workArea);
        }
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16),
        };
        content.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Temporary input target. No files or settings are saved.",
        });
        content.Controls.Add(_input);
        content.Controls.Add(_status);
        content.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Use Ctrl + Alt + D, then Quick settings > Input methods.\n" +
                "Restore the original layout before closing. Auto-closes after 5 minutes.",
        });
        Controls.Add(content);
        InputLanguageChanged += (_, _) =>
        {
            _changes++;
            UpdateStatus();
        };
        Shown += (_, _) =>
        {
            _input.Focus();
            UpdateStatus();
            _expiry.Start();
        };
        _expiry.Tick += (_, _) => Close();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (value && _notebookOnly && !Visible)
        {
            // Recheck immediately before visibility; a disconnected or ambiguous
            // panel must not make Windows choose another monitor for this probe.
            PlaceWithinNotebook(NotebookProbePlacement.ResolveWorkArea());
        }
        base.SetVisibleCore(value);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_notebookOnly)
        {
            // Load follows initial DPI/layout sizing but precedes first Visible
            // and Shown, so clamp the final outer rectangle before it is exposed.
            PlaceWithinNotebook(NotebookProbePlacement.ResolveWorkArea());
        }
    }

    private void PlaceWithinNotebook(Rectangle workArea)
    {
        Bounds = NotebookProbePlacement.CenterWithin(Size, workArea);
        if (!workArea.Contains(Bounds))
        {
            // Native minimum window sizing must not defeat the bounding check
            // on an unusually small work area.
            throw new InvalidOperationException("The probe window cannot fit inside the notebook work area.");
        }
    }

    private void UpdateStatus() => _status.Text =
        $"Original: {FormatLayout(_originalLayout)}\n" +
        $"Current: {FormatLayout(GetKeyboardLayout(0))}\n" +
        $"Observed input-language changes: {_changes}";

    private static string FormatLayout(nint layout) =>
        unchecked((uint)layout.ToInt64()).ToString("X8");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _expiry.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);
}

internal static class NotebookProbePlacement
{
    private const uint DisplayDeviceActive = 0x00000001;
    private const uint DisplayDeviceMirroringDriver = 0x00000008;
    private const uint GetDeviceInterfaceName = 0x00000001;

    public static Rectangle ResolveWorkArea()
    {
        var matches = new List<Rectangle>();
        foreach (var screen in Screen.AllScreens)
        {
            for (uint index = 0; ; index++)
            {
                var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                if (!EnumDisplayDevices(screen.DeviceName, index, ref device, GetDeviceInterfaceName))
                {
                    break;
                }

                // EDD_GET_DEVICE_INTERFACE_NAME returns the per-monitor device
                // interface, e.g. \\?\DISPLAY#SDC41A3#instance#{interface-guid}.
                var identity = device.DeviceId.Split('#');
                if ((device.StateFlags & DisplayDeviceActive) != 0 &&
                    (device.StateFlags & DisplayDeviceMirroringDriver) == 0 &&
                    string.Equals(identity[0], @"\\?\DISPLAY", StringComparison.OrdinalIgnoreCase) &&
                    identity.Length >= 3 &&
                    string.Equals(identity[1], "SDC41A3", StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(screen.WorkingArea);
                }
            }
        }

        if (matches.Count != 1 || matches[0].Width <= 0 || matches[0].Height <= 0)
        {
            throw new InvalidOperationException(
                $"Expected exactly one active SDC41A3 notebook panel with a usable work area; found {matches.Count}.");
        }

        return matches[0];
    }

    public static Rectangle CenterWithin(Size desiredSize, Rectangle workArea)
    {
        var width = Math.Clamp(desiredSize.Width, 1, workArea.Width);
        var height = Math.Clamp(desiredSize.Height, 1, workArea.Height);
        return new Rectangle(
            workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2,
            width,
            height);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string deviceName, uint deviceIndex, ref DisplayDevice device, uint flags);
}
