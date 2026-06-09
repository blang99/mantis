using Eto.Drawing;
using Eto.Forms;

namespace Mantis.Plugin.UI;

public static class MantisPanelHost
{
    private static MantisService? _service;
    private static ChatPanel? _panel;
    private static Form? _form;

    // ── MANTIS Dark Theme Colors ──
    public static readonly Color BgDeep = Color.FromArgb(14, 22, 16);
    public static readonly Color BgCard = Color.FromArgb(22, 32, 25);
    public static readonly Color BgSurface = Color.FromArgb(28, 40, 32);
    public static readonly Color BgInput = Color.FromArgb(18, 28, 20);
    public static readonly Color BorderColor = Color.FromArgb(40, 60, 45);
    public static readonly Color MantisGreen = Color.FromArgb(92, 219, 122);
    public static readonly Color MantisDim = Color.FromArgb(58, 155, 85);
    public static readonly Color TextPrimary = Color.FromArgb(220, 235, 225);
    public static readonly Color TextSecondary = Color.FromArgb(140, 170, 150);
    public static readonly Color TextDim = Color.FromArgb(80, 110, 90);
    public static readonly Color Amber = Color.FromArgb(232, 184, 77);
    public static readonly Color ErrorRed = Color.FromArgb(220, 80, 80);

    public static void Register(MantisService service)
    {
        _service = service;
        // Install brand fonts (Orbitron, Space Grotesk, JetBrains Mono)
        // on first run so the UI matches the marketing site typography.
        FontInstaller.EnsureInstalled();
    }

    /// <summary>
    /// Return the one shared <see cref="MantisService"/>, creating and
    /// initializing it on first use. Both entry points — the Grasshopper float
    /// (via <see cref="MantisLoader"/>) and the Rhino dock panel (via
    /// <see cref="MantisDockPanel"/>) — funnel through here so there is a single
    /// service, conversation, and component catalog. Ensures Grasshopper is
    /// loaded first so the catalog scan succeeds even when opened from a Rhino
    /// tab with the Grasshopper editor closed.
    /// </summary>
    public static MantisService GetOrCreateService()
    {
        if (_service != null) return _service;

        // Deliberately does NOT load Grasshopper or scan the component catalog here —
        // that is deferred to MantisService.EnsureReady() on the first build/ask. So
        // opening the MANTIS panel is instant and never forces Grasshopper to load
        // (which could surface unrelated plugins' load-time warnings, slow the open,
        // or fail before the user has even typed anything).
        FontInstaller.EnsureInstalled();

        _service = new MantisService();
        return _service;
    }

    public static void Show()
    {
        var service = GetOrCreateService();

        // If the form already exists and hasn't been closed, just bring it forward
        if (_form != null)
        {
            try
            {
                _form.BringToFront();
                return;
            }
            catch
            {
                // Form was disposed — recreate
                _form = null;
                _panel = null;
            }
        }

        _panel = new ChatPanel(service);

        _form = new Form
        {
            Title = "MANTIS",
            Content = _panel,
            // Larger default so all UI fits without resizing
            Size = new Size(680, 920),
            // Higher minimum so the user can't shrink it into unreadability
            MinimumSize = new Size(520, 720),
            BackgroundColor = BgDeep,
            Topmost = true,                    // Keep on top as a tool panel
            ShowInTaskbar = true,
            Resizable = true
        };

        // Position on the right side of the primary screen
        var screen = Screen.PrimaryScreen;
        if (screen != null)
        {
            var workArea = screen.WorkingArea;
            _form.Location = new Point(
                (int)(workArea.Right - _form.Width - 20),
                (int)(workArea.Top + 40)
            );
        }

        // When the user closes the form, just hide it — don't destroy the panel
        _form.Closing += (s, e) =>
        {
            // Allow the form to close but null the reference so Show() recreates it
            _form = null;
            _panel = null;
        };

        _form.Show();
    }

    public static void Hide()
    {
        _form?.Close();
    }

    public static bool IsVisible => _form != null;
    public static MantisService? Service => _service;
}
