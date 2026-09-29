using System.Runtime.InteropServices;

namespace SeanShell.InputLayoutProbe;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new InputProbeWindow();
        Application.Run(form);
    }
}

internal sealed class InputProbeWindow : Form
{
    private readonly Label _status = new() { AutoSize = true };
    private readonly TextBox _input = new() { Width = 480 };
    private readonly System.Windows.Forms.Timer _expiry = new() { Interval = 300_000 };
    private readonly nint _originalLayout = GetKeyboardLayout(0);
    private int _changes;

    public InputProbeWindow()
    {
        Text = "SeanShell input layout test (temporary)";
        ClientSize = new Size(560, 210);
        StartPosition = FormStartPosition.CenterScreen;
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
