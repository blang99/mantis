using Eto.Drawing;
using Eto.Forms;
using Grasshopper;
using Grasshopper.Kernel;
using Mantis.Plugin.AI;
using Mantis.Plugin.ErrorHealing;
using Mantis.Plugin.ScriptBuilder;

namespace Mantis.Plugin.UI;

public class ChatPanel : Panel
{
    private readonly MantisService _service;

    // ── Main chat area ──
    private readonly Scrollable _messageScroll;
    private readonly StackLayout _messageList;
    private readonly TextArea _inputBox;
    private readonly Button _sendButton;
    private readonly Label _placeholderLabel;

    // ── Top-bar buttons (consistent styling) ──
    private readonly Button _menuButton;     // toggles history sidebar
    private readonly Button _modelsButton;   // toggles model panel
    private readonly Button _explainButton;  // explains the canvas selection
    private readonly Button _webButton;      // opens landing page
    private readonly Label _statusLabel;

    // ── Bottom status bar ──
    private readonly Label _bottomModelLabel;
    private readonly Label _bottomProviderBadge;
    private readonly CheckBox _exploreToggle;
    private readonly CheckBox _iterateToggle;

    // ── Sidebar (chat history) ──
    private readonly Splitter _outerSplitter;
    private readonly Splitter _innerSplitter;
    private readonly StackLayout _sidebarList;
    private readonly Scrollable _sidebarScroll;
    private readonly Panel _sidebarPanel;
    private bool _sidebarVisible = true;
    private const int SidebarWidth = 220;

    // ── Models panel (slide-in from right) ──
    private readonly ModelPickerPanel _modelPanel;
    private readonly Panel _modelPanelWrapper;
    private bool _modelPanelVisible;
    private const int ModelPanelWidth = 340;

    // Below this window width there isn't room for the history sidebar AND the
    // model panel AND a usable chat column (220 + 340 + ~300) — so opening one
    // auto-collapses the other.
    private const int BothPanelsMinWidth = 860;

    // ── Build progress (live build-trace) ──
    private readonly StackLayout _buildProgressBar;
    private readonly List<string> _buildSteps = new();
    private Label? _buildPulseDot;     // pulsing ● status indicator
    private Label? _buildHeaderLabel;  // "BUILDING · n placed"
    private Label? _buildTraceLabel;   // "Circle › Extrude › Cap Holes"
    private UITimer? _buildPulseTimer;
    private bool _buildPulseOn;

    // ── Typing indicator ("MANTIS is thinking" animated dots) ──
    //   Bridges the dead-air gap between Send and the first component appearing
    //   on the canvas — the AI's response has to fully arrive before the build
    //   starts, so this keeps the chat alive during the wait.
    private StackLayoutItem? _typingItem;
    private Label? _typingDotsLabel;
    private UITimer? _typingTimer;
    private int _typingFrame;

    // ── Error-healing banner (driven by ErrorMonitor.SolutionEnd) ──
    private readonly StackLayout _errorBanner;
    private Label? _errorBannerLabel;
    private Button? _errorFixButton;
    private bool _errorMonitoringStarted;
    private GH_Document? _monitoredDoc;

    // ── Persistent-service event handlers ──
    //   These subscribe to the long-lived MantisService / ErrorMonitor, which
    //   outlive this panel (the host recreates the panel but reuses the
    //   service). Stored so OnUnLoad can detach them and avoid stacking
    //   handlers — without this, reopening the panel would fire duplicate
    //   error banners against a dead UI.
    private Action<MultiSolutionResponse>? _onSolutionsReadyHandler;
    private Action<List<ComponentError>>? _onErrorsDetectedHandler;
    private Action? _onErrorsClearedHandler;

    // ── State ──
    private CancellationTokenSource? _cts;
    private string _activeProvider = "";

    // ── Responsive content sizing ──
    //   Eto.Forms Labels on macOS (NSTextField) only wrap to an *explicit*
    //   Width — never to a parent layout constraint. So every wrapping text
    //   control is registered here with the horizontal `inset` it sits behind
    //   (sum of paddings/margins/siblings between it and the content edge).
    //   On each resize we recompute ONE content width and re-apply. `capped`
    //   controls (the welcome block) clamp to MaxContentWidth for a readable
    //   line length; chat bubbles fill the full available width.
    private readonly Dictionary<Control, (int inset, bool capped)> _responsive = new();
    private const int MaxContentWidth = 600;
    private const int MinContentWidth = 160;
    private const int ScrollbarGutter = 16;

    // ── Chat history ──
    private readonly List<ChatConversation> _conversations = new();
    private int _activeConversationIndex = -1;

    // ── Theme ──
    private static Color Bg => MantisPanelHost.BgDeep;
    private static Color BgCard => MantisPanelHost.BgCard;
    private static Color BgSurface => MantisPanelHost.BgSurface;
    private static Color BgInput => MantisPanelHost.BgInput;
    private static Color Border => MantisPanelHost.BorderColor;
    private static Color Accent => MantisPanelHost.MantisGreen;
    private static Color AccentDim => MantisPanelHost.MantisDim;
    private static Color Text1 => MantisPanelHost.TextPrimary;
    private static Color Text2 => MantisPanelHost.TextSecondary;
    private static Color TextD => MantisPanelHost.TextDim;
    private static Color Amber => MantisPanelHost.Amber;
    private static Color ErrRed => MantisPanelHost.ErrorRed;

    private static readonly Color UserBubbleBg = Color.FromArgb(28, 50, 35);
    private static readonly Color AiBubbleBg = Color.FromArgb(22, 32, 26);
    private static readonly Color ErrorBubbleBg = Color.FromArgb(42, 18, 18);
    private static readonly Color SystemBubbleBg = Color.FromArgb(18, 26, 22);

    private static readonly Color SidebarBg = Color.FromArgb(12, 20, 14);
    private static readonly Color SidebarItemBg = Color.FromArgb(18, 28, 20);
    private static readonly Color SidebarItemActiveBg = Color.FromArgb(26, 42, 30);
    private static readonly Color SidebarItemHoverBg = Color.FromArgb(24, 36, 27);

    private static readonly Color FreeBadgeBg = Color.FromArgb(28, 60, 38);
    private static readonly Color FreeBadgeText = Color.FromArgb(140, 240, 170);
    private static readonly Color PaidBadgeBg = Color.FromArgb(60, 48, 22);
    private static readonly Color PaidBadgeText = Color.FromArgb(255, 200, 100);

    private static readonly Color SuggestionBg = Color.FromArgb(24, 38, 28);
    private static readonly Color SuggestionHoverBg = Color.FromArgb(32, 50, 38);

    // ── Font sizes (consistent + readable) ──
    private const float FontBody = 14f;
    private const float FontBodySmall = 13f;
    private const float FontHeader = 17f;
    private const float FontButton = 12f;
    private const float FontLabel = 12f;
    private const float FontMeta = 11f;
    private const float FontMono = 12f;
    private const float FontInput = 14f;
    private const float FontBadge = 10f;
    private const float FontWelcomeTitle = 22f;
    private const float FontWelcomeSubtitle = 14f;
    private const float FontSuggestion = 13f;

    // ── Brand fonts (match marketing/landing-page.html) ──
    //   Display: Orbitron — geometric/futuristic for MANTIS wordmark
    //   Body:    Space Grotesk — humanist sans-serif for body text
    //   Mono:    JetBrains Mono — for code, model IDs, metadata
    // Eto.Forms takes a single family name. If not installed, the system
    // falls back to the default sans-serif automatically. To guarantee the
    // marketing look, install these fonts (free from fonts.google.com).
    private const string FontDisplay = "Orbitron";
    private const string FontBodyFamily = "Space Grotesk";
    private const string FontMonoFamily = "JetBrains Mono";

    // ── Suggested starter prompts (clickable) ──
    private static readonly string[] SuggestedPrompts =
    {
        "Create a circle with adjustable radius and extrude it 20 units up",
        "Build a parametric tower with tapering floors",
        "Make a Voronoi facade panel from a surface",
        "Generate a hexagonal grid on a surface",
        "Create a waffle structure with adjustable spacing"
    };

    // ── Mantis web/landing page URL ──
    private const string MantisWebUrl = "https://mantis.design";

    // ═══════════════════════════════════════════
    //  DATA MODELS
    // ═══════════════════════════════════════════

    private class ChatConversation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "New Chat";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<ChatBubbleData> Messages { get; set; } = new();
    }

    private class ChatBubbleData
    {
        public string Sender { get; set; } = "";
        public string Type { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    // ═══════════════════════════════════════════
    //  CONSTRUCTOR
    // ═══════════════════════════════════════════

    public ChatPanel(MantisService service)
    {
        _service = service;
        _activeProvider = _service.ProviderManager.ActiveProviderName;
        BackgroundColor = Bg;

        // Message area
        _messageList = new StackLayout
        {
            Spacing = 0,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = Bg,
            Padding = new Padding(0, 10)
        };
        _messageScroll = new Scrollable
        {
            Content = _messageList,
            BackgroundColor = Bg,
            Border = BorderType.None,
            ExpandContentWidth = true
        };

        // Build progress
        _buildProgressBar = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Padding = new Padding(14, 10),
            BackgroundColor = Color.FromArgb(18, 28, 22),
            Visible = false
        };

        // Error-healing banner (hidden until ErrorMonitor reports errors)
        _errorBanner = BuildErrorBanner();

        // Input
        _inputBox = new TextArea
        {
            Font = new Font(FontBodyFamily, FontInput),
            BackgroundColor = BgInput,
            TextColor = Text1,
            Wrap = true,
            Height = 90,
            Text = ""
        };
        _inputBox.KeyDown += OnInputKeyDown;
        _inputBox.TextChanged += OnInputTextChanged;

        _placeholderLabel = new Label
        {
            Text = "Describe what you want to build...",
            Font = new Font(FontBodyFamily, FontBody),
            TextColor = TextD,
            Visible = true
        };

        _sendButton = new Button
        {
            Text = "Send",
            Width = 100,
            Height = 48,
            BackgroundColor = Accent,
            TextColor = Bg,
            Font = new Font(FontBodyFamily, FontBody, FontStyle.Bold)
        };
        _sendButton.Click += OnSendClick;

        // ── Top-bar buttons (all same styling) ──
        _menuButton = MakeTopBarButton("History", "Toggle chat history sidebar");
        _menuButton.Click += OnSidebarToggle;

        _modelsButton = MakeTopBarButton("Models", "Choose AI model & API key");
        _modelsButton.Click += OnModelsToggle;

        _explainButton = MakeTopBarButton("Explain", "Explain the component(s) selected on the canvas");
        _explainButton.Click += OnExplainClick;

        _webButton = MakeTopBarButton("Web", "Open mantis.design");
        _webButton.Click += OnOpenWeb;

        _statusLabel = new Label
        {
            Text = "Ready",
            TextColor = Accent,
            Font = new Font(FontMonoFamily, FontMeta),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Bottom status bar
        _bottomProviderBadge = new Label
        {
            Font = new Font(FontMonoFamily, FontBadge, FontStyle.Bold),
            VerticalAlignment = VerticalAlignment.Center
        };
        _bottomModelLabel = new Label
        {
            Font = new Font(FontBodyFamily, FontMeta),
            TextColor = Text2,
            VerticalAlignment = VerticalAlignment.Center,
            Wrap = WrapMode.None
        };

        _exploreToggle = new CheckBox
        {
            Text = "Multi-Solution",
            ToolTip = "Generate 2-3 alternative approaches",
            TextColor = Text2,
            Font = new Font(FontBodyFamily, FontMeta)
        };
        _iterateToggle = new CheckBox
        {
            Text = "Iterate Canvas",
            ToolTip = "Force build-on-top of the current canvas. Follow-ups already " +
                      "extend Mantis-generated graphs automatically — use this only to " +
                      "build onto components Mantis did not create.",
            TextColor = Text2,
            Font = new Font(FontBodyFamily, FontMeta)
        };

        // Sidebar
        _sidebarList = new StackLayout
        {
            Spacing = 3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Padding(8, 8),
            BackgroundColor = SidebarBg
        };
        _sidebarScroll = new Scrollable
        {
            Content = _sidebarList,
            BackgroundColor = SidebarBg,
            Border = BorderType.None,
            ExpandContentWidth = true
        };
        _sidebarPanel = new Panel
        {
            Content = BuildSidebarContent(),
            BackgroundColor = SidebarBg,
            Width = SidebarWidth
        };

        // Models panel
        _modelPanel = new ModelPickerPanel(_service);
        _modelPanel.OnClose += () => SetModelPanelVisible(false);
        _modelPanelWrapper = new Panel
        {
            Content = _modelPanel,
            Width = ModelPanelWidth,
            BackgroundColor = Bg
        };

        _outerSplitter = new Splitter
        {
            Orientation = Orientation.Horizontal,
            FixedPanel = SplitterFixedPanel.Panel1,
            Panel1MinimumSize = 0,
            BackgroundColor = Bg
        };
        _innerSplitter = new Splitter
        {
            Orientation = Orientation.Horizontal,
            FixedPanel = SplitterFixedPanel.Panel2,
            Panel2MinimumSize = 0,
            BackgroundColor = Bg
        };

        StartNewConversation();
        WireServiceEvents();
        BuildLayout();
        RefreshBottomStatusBar();

        // When the chat scroll area resizes, update all responsive labels so
        // their text wraps to the available width. This is what fixes the
        // "text gets cut off when window is narrow" bug on macOS — Eto.Forms
        // Labels only wrap to an explicit Width, not to layout constraints.
        _messageScroll.SizeChanged += (_, _) => UpdateResponsiveLabelWidths();
        SizeChanged += (_, _) => UpdateResponsiveLabelWidths();
    }

    /// <summary>
    /// Full width available for content inside the message scroll area, minus
    /// an allowance for the vertical scrollbar. Falls back to the panel width
    /// before the scroll area has been laid out.
    /// </summary>
    private int FullContentWidth()
    {
        var avail = _messageScroll.Width > 0 ? _messageScroll.Width : Width;
        return Math.Max(MinContentWidth, avail - ScrollbarGutter);
    }

    /// <summary>Content width clamped for a readable line length (welcome block).</summary>
    private int CappedContentWidth() => Math.Min(MaxContentWidth, FullContentWidth());

    /// <summary>
    /// Register a wrapping control so its Width tracks the content area on every
    /// resize. `inset` = the horizontal space between the content edge and this
    /// control (parent paddings + sibling widths + gaps). `capped` clamps the
    /// basis to MaxContentWidth for a comfortable reading measure.
    /// </summary>
    private void TrackResponsive(Control control, int inset, bool capped = false)
    {
        _responsive[control] = (inset, capped);
        ApplyWidth(control);
    }

    private void UpdateResponsiveLabelWidths()
    {
        foreach (var control in _responsive.Keys)
            ApplyWidth(control);
    }

    private void ApplyWidth(Control control)
    {
        if (!_responsive.TryGetValue(control, out var info)) return;
        var basis = info.capped ? CappedContentWidth() : FullContentWidth();
        var target = Math.Max(80, basis - info.inset);
        if (control.Width != target) control.Width = target;
    }

    // ═══════════════════════════════════════════
    //  TOP-BAR BUTTON FACTORY (consistent styling)
    // ═══════════════════════════════════════════

    private Button MakeTopBarButton(string text, string tooltip)
    {
        return new Button
        {
            Text = text,
            Width = 72,
            Height = 34,
            BackgroundColor = BgSurface,
            TextColor = Text1,
            Font = new Font(FontBodyFamily, FontButton, FontStyle.Bold),
            ToolTip = tooltip
        };
    }

    // ═══════════════════════════════════════════
    //  LOGO
    // ═══════════════════════════════════════════

    /// <summary>
    /// MANTIS "The Constellation" logo — exactly matches the SVG on
    /// mantis.design landing page: 6 nodes (top, 2 mid, 2 bottom, center)
    /// connected by lines forming a stylized mantis head silhouette and a
    /// node-graph topology (referencing Grasshopper).
    /// SVG source: marketing/landing-page.html nav-logo-mark
    /// </summary>
    private class MantisLogo : Drawable
    {
        public MantisLogo()
        {
            Size = new Size(34, 34);
            BackgroundColor = Colors.Transparent;
            Paint += OnPaint;
        }

        private void OnPaint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var accent = MantisPanelHost.MantisGreen;
            // Logo design uses 36x36 viewBox — scale to our drawable size
            var scale = Size.Width / 36f;
            float S(float n) => n * scale;

            // Connecting lines (drawn first, so dots overlay them)
            var linePen = new Pen(Color.FromArgb(accent.Rb, accent.Gb, accent.Bb, 102), 1.2f * scale)
            {
                LineCap = PenLineCap.Round
            };

            // Lines: outer nodes → center node (18,18)
            g.DrawLine(linePen, S(18), S(5), S(18), S(18));   // top → center
            g.DrawLine(linePen, S(5), S(15), S(18), S(18));   // upper-left → center
            g.DrawLine(linePen, S(31), S(15), S(18), S(18));  // upper-right → center
            g.DrawLine(linePen, S(10), S(28), S(18), S(18));  // lower-left → center
            g.DrawLine(linePen, S(26), S(28), S(18), S(18));  // lower-right → center

            // Outer nodes — solid mantis green at 70% opacity
            var nodeBrush = new SolidBrush(Color.FromArgb(accent.Rb, accent.Gb, accent.Bb, 178));
            DrawNode(g, nodeBrush, S(5), S(15), S(2.5f));
            DrawNode(g, nodeBrush, S(31), S(15), S(2.5f));
            DrawNode(g, nodeBrush, S(10), S(28), S(2.5f));
            DrawNode(g, nodeBrush, S(26), S(28), S(2.5f));

            // Top node — solid full opacity (the "head")
            var solidBrush = new SolidBrush(accent);
            DrawNode(g, solidBrush, S(18), S(5), S(3.5f));

            // Center node — solid full opacity, larger
            DrawNode(g, solidBrush, S(18), S(18), S(4f));
        }

        private static void DrawNode(Graphics g, SolidBrush brush, float cx, float cy, float r)
        {
            g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
        }
    }

    // ═══════════════════════════════════════════
    //  SIDEBAR (Chat History)
    // ═══════════════════════════════════════════

    private Panel BuildSidebarContent()
    {
        var titleLabel = new Label
        {
            Text = "Chat History",
            Font = new Font(FontBodyFamily, FontLabel, FontStyle.Bold),
            TextColor = Text2,
            VerticalAlignment = VerticalAlignment.Center
        };

        // "+ New Chat" button INSIDE the sidebar, under the title
        var newChatBtn = new Button
        {
            Text = "+ New Chat",
            Height = 34,
            BackgroundColor = Accent,
            TextColor = Bg,
            Font = new Font(FontBodyFamily, FontButton, FontStyle.Bold),
            ToolTip = "Start a new conversation"
        };
        newChatBtn.Click += OnNewChat;

        var header = new StackLayout
        {
            Spacing = 8,
            Padding = new Padding(10, 12, 10, 10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = SidebarBg,
            Items =
            {
                titleLabel,
                newChatBtn
            }
        };

        var sep = new Panel { BackgroundColor = Border, Height = 1 };

        return new StackLayout
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = SidebarBg,
            Items =
            {
                header,
                sep,
                new StackLayoutItem(_sidebarScroll, expand: true)
            }
        };
    }

    private void RefreshSidebar()
    {
        _sidebarList.Items.Clear();

        for (int i = _conversations.Count - 1; i >= 0; i--)
        {
            var conv = _conversations[i];
            var idx = i;
            var isActive = (i == _activeConversationIndex);

            var titleLabel = new Label
            {
                Text = TruncateTitle(conv.Title, 26),
                Font = new Font(FontBodyFamily, FontBodySmall, isActive ? FontStyle.Bold : FontStyle.None),
                TextColor = isActive ? Accent : Text1,
                Wrap = WrapMode.None
            };

            var timeLabel = new Label
            {
                Text = FormatTimestamp(conv.CreatedAt),
                Font = new Font(FontBodyFamily, FontMeta),
                TextColor = TextD
            };

            var userMsgCount = conv.Messages.Count(m => m.Type == "user");
            var msgCountLabel = new Label
            {
                Text = $"{userMsgCount} msg{(userMsgCount != 1 ? "s" : "")}",
                Font = new Font(FontBodyFamily, FontMeta),
                TextColor = TextD
            };

            var bottomRow = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Items = { timeLabel, msgCountLabel }
            };

            var item = new StackLayout
            {
                Spacing = 4,
                Padding = new Padding(12, 10),
                BackgroundColor = isActive ? SidebarItemActiveBg : SidebarItemBg,
                Items = { titleLabel, bottomRow }
            };

            var clickPanel = new Panel { Content = item, Cursor = Cursors.Pointer };
            if (!isActive) AddHover(clickPanel, item, SidebarItemBg, SidebarItemHoverBg);
            clickPanel.MouseDown += (_, _) => LoadConversation(idx);
            _sidebarList.Items.Add(new StackLayoutItem(clickPanel));
        }
    }

    private void OnSidebarToggle(object? sender, EventArgs e) =>
        SetSidebarVisible(!_sidebarVisible);

    private void SetSidebarVisible(bool visible)
    {
        _sidebarVisible = visible;
        if (visible)
        {
            // Not enough room for both panels — collapse the model panel first.
            if (_modelPanelVisible && Width > 0 && Width < BothPanelsMinWidth)
                SetModelPanelVisible(false);

            _outerSplitter.Panel1 = _sidebarPanel;
            _outerSplitter.Position = SidebarWidth;
            _menuButton.BackgroundColor = ColorMix(BgSurface, Accent, 0.3f);
        }
        else
        {
            _outerSplitter.Panel1 = null;
            _outerSplitter.Position = 0;
            _menuButton.BackgroundColor = BgSurface;
        }
    }

    private static Color ColorMix(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.Rb * (1 - t) + b.Rb * t),
            (int)(a.Gb * (1 - t) + b.Gb * t),
            (int)(a.Bb * (1 - t) + b.Bb * t));
    }

    /// <summary>
    /// Wire mouse-enter/leave on <paramref name="trigger"/> to swap the
    /// background of <paramref name="target"/> — lightweight hover affordance
    /// for custom clickable rows (suggestion pills, history items).
    /// </summary>
    private static void AddHover(Control trigger, Control target, Color normal, Color hover)
    {
        trigger.MouseEnter += (_, _) => target.BackgroundColor = hover;
        trigger.MouseLeave += (_, _) => target.BackgroundColor = normal;
    }

    // ═══════════════════════════════════════════
    //  CONVERSATION MANAGEMENT
    // ═══════════════════════════════════════════

    private void StartNewConversation()
    {
        var conv = new ChatConversation();
        _conversations.Add(conv);
        _activeConversationIndex = _conversations.Count - 1;
        _messageList.Items.Clear();
        _responsive.Clear();
        AddWelcomeScreen();
        RefreshSidebar();
    }

    private void OnNewChat(object? sender, EventArgs e)
    {
        _service.NewConversation();
        StartNewConversation();
        _statusLabel.Text = "Ready";
        _statusLabel.TextColor = Accent;
    }

    private void LoadConversation(int index)
    {
        if (index < 0 || index >= _conversations.Count) return;
        if (index == _activeConversationIndex) return;

        _activeConversationIndex = index;
        _service.NewConversation();

        _messageList.Items.Clear();
        _responsive.Clear();
        var conv = _conversations[index];

        // If a conversation has no messages yet, show the welcome screen.
        // Otherwise replay the bubbles.
        if (conv.Messages.Count == 0)
        {
            AddWelcomeScreen();
        }
        else
        {
            foreach (var msg in conv.Messages)
                AddMessageBubble(msg.Sender, msg.Type, msg.Text, msg.Timestamp, store: false);
        }

        RefreshSidebar();
        _statusLabel.Text = "Ready";
        _statusLabel.TextColor = Accent;
    }

    private ChatConversation? ActiveConversation =>
        _activeConversationIndex >= 0 && _activeConversationIndex < _conversations.Count
            ? _conversations[_activeConversationIndex]
            : null;

    // ═══════════════════════════════════════════
    //  MODELS PANEL TOGGLE
    // ═══════════════════════════════════════════

    private void OnModelsToggle(object? sender, EventArgs e) =>
        SetModelPanelVisible(!_modelPanelVisible);

    private void SetModelPanelVisible(bool visible)
    {
        _modelPanelVisible = visible;
        if (visible)
        {
            // Not enough room for both panels — collapse the history sidebar first.
            if (_sidebarVisible && Width > 0 && Width < BothPanelsMinWidth)
                SetSidebarVisible(false);

            _innerSplitter.Panel2 = _modelPanelWrapper;
            // Position the splitter so chat takes most of the space
            try { _innerSplitter.Position = Math.Max(0, Width - ModelPanelWidth); } catch { }
            _modelsButton.BackgroundColor = ColorMix(BgSurface, Accent, 0.3f);
        }
        else
        {
            _innerSplitter.Panel2 = null;
            _modelsButton.BackgroundColor = BgSurface;
        }
        // Always refresh bottom bar — provider may have changed in the panel
        RefreshBottomStatusBar();
    }

    // ═══════════════════════════════════════════
    //  WEB BUTTON
    // ═══════════════════════════════════════════

    private void OnOpenWeb(object? sender, EventArgs e)
    {
        try
        {
            // Open in the default browser
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = MantisWebUrl,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // Fallback for macOS
            try { System.Diagnostics.Process.Start("open", MantisWebUrl); }
            catch
            {
                AddMessageBubble("MANTIS", "ai",
                    $"Could not open browser. Visit manually: {MantisWebUrl}");
            }
        }
    }

    // ═══════════════════════════════════════════
    //  LAYOUT ASSEMBLY
    // ═══════════════════════════════════════════

    private void BuildLayout()
    {
        // ── Top bar: LOGO + MANTIS on LEFT, buttons on RIGHT ──
        var logo = new MantisLogo();

        // MANTIS wordmark uses Orbitron (display font, matches marketing site).
        // Letters spaced wider to match the 0.2em tracking on landing-page.html.
        var titleLabel = new Label
        {
            Text = "M A N T I S",
            Font = new Font(FontDisplay, FontHeader, FontStyle.Bold),
            TextColor = Accent,
            VerticalAlignment = VerticalAlignment.Center
        };

        var leftCluster = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                logo,
                titleLabel
            }
        };

        var rightCluster = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _menuButton,
                _modelsButton,
                _explainButton,
                _webButton
            }
        };

        var topBar = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Padding(14, 12, 14, 12),
            BackgroundColor = BgCard,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                leftCluster,
                new StackLayoutItem(null, expand: true),
                _statusLabel,
                new Panel { Width = 12 },
                rightCluster
            }
        };

        // ── Input + bottom status ──
        var bottomStatusBar = BuildBottomStatusBar();
        var inputContainer = BuildInputArea();

        // ── Chat column ──
        var chatColumn = new StackLayout
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = Bg,
            Items =
            {
                new StackLayoutItem(_messageScroll, expand: true),
                _buildProgressBar,
                _errorBanner,
                new Panel { BackgroundColor = Border, Height = 1 },
                bottomStatusBar,
                new Panel { BackgroundColor = Border, Height = 1 },
                inputContainer
            }
        };

        // ── Inner splitter: Chat | Model panel (hidden by default) ──
        _innerSplitter.Panel1 = chatColumn;
        _innerSplitter.Panel2 = null;

        // ── Outer splitter: Sidebar | (Inner splitter) ──
        _outerSplitter.Panel1 = _sidebarPanel;
        _outerSplitter.Panel2 = _innerSplitter;
        _outerSplitter.Position = SidebarWidth;

        // Sidebar starts visible — reflect that in the toggle button's tint.
        _menuButton.BackgroundColor = ColorMix(BgSurface, Accent, 0.3f);

        // Final assembly: top bar + outer splitter
        Content = new StackLayout
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = Bg,
            Items =
            {
                topBar,
                new Panel { BackgroundColor = Border, Height = 1 },
                new StackLayoutItem(_outerSplitter, expand: true)
            }
        };
    }

    private StackLayout BuildBottomStatusBar()
    {
        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Padding(14, 8, 14, 8),
            BackgroundColor = BgCard,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _bottomProviderBadge,
                _bottomModelLabel,
                new StackLayoutItem(null, expand: true),
                _exploreToggle,
                _iterateToggle
            }
        };
    }

    private void RefreshBottomStatusBar()
    {
        var providerName = _service.ProviderManager.ActiveProviderName;
        var model = _service.ProviderManager.Active.AvailableModels
            .FirstOrDefault(m => m.Id == _service.ProviderManager.Active.SelectedModel);

        var isFree = model?.IsFree == true || providerName == "Ollama";
        _bottomProviderBadge.Text = $" {providerName.ToUpper()}  {(isFree ? "FREE" : "PAID")} ";
        _bottomProviderBadge.BackgroundColor = isFree ? FreeBadgeBg : PaidBadgeBg;
        _bottomProviderBadge.TextColor = isFree ? FreeBadgeText : PaidBadgeText;

        _bottomModelLabel.Text = model != null
            ? $"  {model.DisplayName}"
            : "  No model — click 'Models' to choose";
    }

    private StackLayout BuildInputArea()
    {
        var hintLabel = new Label
        {
            Text = "Enter to send  ·  Shift+Enter for new line",
            Font = new Font(FontBodyFamily, FontMeta),
            TextColor = TextD
        };

        var sendCol = new StackLayout
        {
            Spacing = 4,
            VerticalContentAlignment = VerticalAlignment.Bottom,
            Items = { _sendButton }
        };

        return new StackLayout
        {
            Spacing = 8,
            Padding = new Padding(14, 12, 14, 12),
            BackgroundColor = BgCard,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                _placeholderLabel,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Items =
                    {
                        new StackLayoutItem(_inputBox, expand: true),
                        sendCol
                    }
                },
                hintLabel
            }
        };
    }

    // ═══════════════════════════════════════════
    //  WELCOME SCREEN (with suggested prompts)
    // ═══════════════════════════════════════════

    private void AddWelcomeScreen()
    {
        // Inset = card left pad (24) + right pad (24) = 48, inside the capped
        // content column (the scrollbar gutter is handled by the width fns).
        const int cardInset = 48;

        var title = new Label
        {
            Text = "WELCOME TO MANTIS",
            Font = new Font(FontDisplay, FontWelcomeTitle, FontStyle.Bold),
            TextColor = Accent,
            TextAlignment = TextAlignment.Center,
            Wrap = WrapMode.Word
        };
        TrackResponsive(title, cardInset, capped: true);

        var subtitle = new Label
        {
            Text = "Your AI computational design assistant",
            Font = new Font(FontBodyFamily, FontWelcomeSubtitle),
            TextColor = Text2,
            TextAlignment = TextAlignment.Center,
            Wrap = WrapMode.Word
        };
        TrackResponsive(subtitle, cardInset, capped: true);

        var description = new Label
        {
            Text = "Describe what you want to build and I'll generate native Grasshopper components live on your canvas.",
            Font = new Font(FontBodyFamily, FontBody),
            TextColor = Text1,
            TextAlignment = TextAlignment.Center,
            Wrap = WrapMode.Word
        };
        TrackResponsive(description, cardInset, capped: true);

        var tryLabel = new Label
        {
            Text = "Try one of these to get started:",
            Font = new Font(FontBodyFamily, FontBody, FontStyle.Bold),
            TextColor = Text2,
            TextAlignment = TextAlignment.Left,
            Wrap = WrapMode.Word
        };
        TrackResponsive(tryLabel, cardInset, capped: true);

        // Build clickable suggestion pills (responsive)
        var suggestionsList = new StackLayout
        {
            Spacing = 8,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { tryLabel }
        };
        foreach (var prompt in SuggestedPrompts)
            suggestionsList.Items.Add(BuildSuggestionPill(prompt));

        var providerHint = new Label
        {
            Text = "Current model: " + _service.ProviderManager.Active.SelectedModel +
                   "  (click 'Models' to change)",
            Font = new Font(FontMonoFamily, FontMeta),
            TextColor = TextD,
            TextAlignment = TextAlignment.Center,
            Wrap = WrapMode.Word
        };
        TrackResponsive(providerHint, cardInset, capped: true);

        var welcomeCard = new StackLayout
        {
            Spacing = 14,
            Padding = new Padding(24, 32, 24, 24),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = Bg,
            Items =
            {
                title,
                subtitle,
                new Panel { Height = 4 },
                description,
                new Panel { Height = 8 },
                suggestionsList,
                new Panel { Height = 4 },
                providerHint
            }
        };
        // The card itself is the capped content column; its children inset from it.
        TrackResponsive(welcomeCard, 0, capped: true);

        // Center the capped welcome block horizontally so centered text reads
        // as centered in the window (not left-anchored in a stretched card).
        var welcomeRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Items =
            {
                new StackLayoutItem(null, expand: true),
                new StackLayoutItem(welcomeCard),
                new StackLayoutItem(null, expand: true)
            }
        };
        _messageList.Items.Add(new StackLayoutItem(welcomeRow));

        // Force initial layout — must run after the welcome card is added
        Application.Instance.AsyncInvoke(UpdateResponsiveLabelWidths);
    }

    /// <summary>
    /// Clickable suggestion pill. The prompt Label is registered with
    /// TrackResponsive so its width is constrained on every layout change —
    /// without this, Eto.Forms Labels on macOS don't wrap to a parent layout
    /// constraint and would overflow horizontally.
    /// </summary>
    private Control BuildSuggestionPill(string prompt)
    {
        var arrow = new Label
        {
            Text = ">",
            Font = new Font(FontBodyFamily, FontSuggestion, FontStyle.Bold),
            TextColor = Accent,
            VerticalAlignment = VerticalAlignment.Top
        };

        var promptLabel = new Label
        {
            Text = prompt,
            Font = new Font(FontBodyFamily, FontSuggestion),
            TextColor = Text1,
            Wrap = WrapMode.Word
        };
        // Inset from the capped column: card pad 48 + pill pad 28 + gap 10 + arrow ~12 = 98
        TrackResponsive(promptLabel, 98, capped: true);

        var table = new TableLayout
        {
            Spacing = new Size(10, 0),
            Padding = new Padding(14, 12),
            BackgroundColor = SuggestionBg,
            Rows =
            {
                new TableRow(
                    new TableCell(arrow, scaleWidth: false),
                    new TableCell(promptLabel, scaleWidth: true)
                )
            }
        };

        var click = new Panel { Content = table, Cursor = Cursors.Pointer };
        AddHover(click, table, SuggestionBg, SuggestionHoverBg);
        var capturedPrompt = prompt;
        click.MouseDown += (_, _) =>
        {
            _inputBox.Text = capturedPrompt;
            UpdatePlaceholder();
            _inputBox.Focus();
        };
        return click;
    }

    // ═══════════════════════════════════════════
    //  MESSAGE BUBBLES
    // ═══════════════════════════════════════════

    private void AddMessageBubble(string sender, string type, string text,
        DateTime? timestamp = null, bool store = true)
    {
        var ts = timestamp ?? DateTime.Now;

        // Clear the welcome screen on first real message
        if (store && _messageList.Items.Count > 0 &&
            ActiveConversation?.Messages.Count == 0)
        {
            _messageList.Items.Clear();
            _responsive.Clear();
        }

        if (store && ActiveConversation != null)
        {
            ActiveConversation.Messages.Add(new ChatBubbleData
            {
                Sender = sender,
                Type = type,
                Text = text,
                Timestamp = ts
            });

            if (type == "user" && ActiveConversation.Title == "New Chat")
            {
                ActiveConversation.Title = TruncateTitle(text, 30);
                RefreshSidebar();
            }
        }

        var bubble = CreateBubble(sender, type, text, ts);

        if (_messageList.Items.Count > 0)
            _messageList.Items.Add(new StackLayoutItem(new Panel { Height = 8, BackgroundColor = Bg }));

        _messageList.Items.Add(new StackLayoutItem(bubble));

        Application.Instance.AsyncInvoke(() =>
        {
            _messageScroll.ScrollPosition = new Point(0, int.MaxValue / 2);
        });
    }

    private Panel CreateBubble(string sender, string type, string text, DateTime ts)
    {
        Color bubbleBg, senderColor, textColor;
        string label;

        switch (type)
        {
            case "user":
                bubbleBg = UserBubbleBg;
                senderColor = Accent;
                textColor = Text1;
                label = "You";
                break;
            case "error":
                bubbleBg = ErrorBubbleBg;
                senderColor = ErrRed;
                textColor = Color.FromArgb(235, 175, 175);
                label = "Error";
                break;
            case "system":
                bubbleBg = SystemBubbleBg;
                senderColor = TextD;
                textColor = Text2;
                label = "System";
                break;
            default:
                bubbleBg = AiBubbleBg;
                senderColor = Accent;
                textColor = Text1;
                label = "MANTIS";
                break;
        }

        var senderLabel = new Label
        {
            Text = label,
            Font = new Font(FontBodyFamily, FontLabel, FontStyle.Bold),
            TextColor = senderColor
        };

        var timeLabel = new Label
        {
            Text = ts.ToString("HH:mm"),
            Font = new Font(FontBodyFamily, FontMeta),
            TextColor = TextD
        };

        var headerRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Padding(0, 0, 0, 6),
            Items =
            {
                senderLabel,
                new StackLayoutItem(null, expand: true),
                timeLabel
            }
        };

        var bodyLabel = new Label
        {
            Text = text,
            Font = type == "system" ? new Font(FontBodyFamily, FontBodySmall) : new Font(FontBodyFamily, FontBody),
            TextColor = textColor,
            Wrap = WrapMode.Word
        };
        // Bubbles fill the full width. Inset: margins 20 + stripe 4 + inner pad 32 = 56
        TrackResponsive(bodyLabel, 56);

        var innerContent = new TableLayout
        {
            Spacing = new Size(0, 2),
            Padding = new Padding(16, 12, 16, 12),
            BackgroundColor = bubbleBg,
            Rows =
            {
                new TableRow(new TableCell(headerRow, scaleWidth: true)),
                new TableRow(new TableCell(bodyLabel, scaleWidth: true))
            }
        };

        Color stripeColor = type switch
        {
            "user" => Accent,
            "error" => ErrRed,
            "system" => TextD,
            _ => AccentDim
        };

        var accentStripe = new Panel
        {
            BackgroundColor = stripeColor,
            Width = 4
        };

        var bubbleRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                accentStripe,
                new StackLayoutItem(innerContent, expand: true)
            }
        };

        return new Panel
        {
            Content = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Items =
                {
                    new StackLayoutItem(new Panel { Width = 10, BackgroundColor = Bg }),
                    new StackLayoutItem(bubbleRow, expand: true),
                    new StackLayoutItem(new Panel { Width = 10, BackgroundColor = Bg })
                }
            }
        };
    }

    // ═══════════════════════════════════════════
    //  BUILD PROGRESS
    // ═══════════════════════════════════════════

    private void ShowBuildProgress()
    {
        _buildSteps.Clear();
        EnsureBuildTraceBuilt();

        _buildPulseOn = true;
        _buildPulseDot!.TextColor = Accent;
        _buildHeaderLabel!.Text = "BUILDING";
        _buildHeaderLabel.TextColor = Accent;
        _buildTraceLabel!.Text = "placing components on canvas…";
        _buildTraceLabel.TextColor = TextD;
        _buildProgressBar.Visible = true;

        StartBuildPulse();
    }

    /// <summary>
    /// Build the stable child structure of the build-trace bar exactly once.
    /// Updating labels in place (rather than rebuilding the Items each step)
    /// keeps the pulse-dot reference valid and avoids layout thrash.
    /// </summary>
    private void EnsureBuildTraceBuilt()
    {
        if (_buildTraceLabel != null) return;

        _buildPulseDot = new Label
        {
            Text = "●",
            Font = new Font(FontMonoFamily, FontMono),
            TextColor = Accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        _buildHeaderLabel = new Label
        {
            Text = "BUILDING",
            Font = new Font(FontMonoFamily, FontMono, FontStyle.Bold),
            TextColor = Accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        _buildTraceLabel = new Label
        {
            Text = "",
            Font = new Font(FontMonoFamily, FontMono),
            TextColor = Text2,
            Wrap = WrapMode.None,
            VerticalAlignment = VerticalAlignment.Center
        };

        _buildProgressBar.Items.Clear();
        _buildProgressBar.Items.Add(_buildPulseDot);
        _buildProgressBar.Items.Add(_buildHeaderLabel);
        _buildProgressBar.Items.Add(new Label
        {
            Text = "·",
            Font = new Font(FontMonoFamily, FontMono),
            TextColor = TextD,
            VerticalAlignment = VerticalAlignment.Center
        });
        _buildProgressBar.Items.Add(new StackLayoutItem(_buildTraceLabel, expand: true));
    }

    private void AddBuildStep(string name)
    {
        _buildSteps.Add(name);
        if (_buildTraceLabel == null || _buildHeaderLabel == null) return;

        _buildHeaderLabel.Text = $"BUILDING · {_buildSteps.Count} placed";

        // Show the most recent 3 components as a left-to-right flow: a › b › c
        var startIdx = Math.Max(0, _buildSteps.Count - 3);
        var recent = _buildSteps.GetRange(startIdx, _buildSteps.Count - startIdx);
        var prefix = startIdx > 0 ? "… › " : "";
        _buildTraceLabel.Text = prefix + string.Join("  ›  ", recent);
        _buildTraceLabel.TextColor = Accent;
    }

    /// <summary>Switch the trace bar into its "done" state (called on build complete).</summary>
    private void CompleteBuildProgress()
    {
        StopBuildPulse();
        if (_buildPulseDot != null) _buildPulseDot.TextColor = Accent;
        if (_buildHeaderLabel != null)
        {
            _buildHeaderLabel.Text = _buildSteps.Count > 0
                ? $"BUILD COMPLETE · {_buildSteps.Count} components"
                : "BUILD COMPLETE";
            _buildHeaderLabel.TextColor = Accent;
        }
    }

    private void StartBuildPulse()
    {
        StopBuildPulse();
        _buildPulseTimer = new UITimer { Interval = 0.5 };
        _buildPulseTimer.Elapsed += (_, _) =>
        {
            _buildPulseOn = !_buildPulseOn;
            if (_buildPulseDot != null)
                _buildPulseDot.TextColor = _buildPulseOn ? Accent : AccentDim;
        };
        _buildPulseTimer.Start();
    }

    private void StopBuildPulse()
    {
        _buildPulseTimer?.Stop();
        _buildPulseTimer = null;
    }

    private void HideBuildProgress()
    {
        StopBuildPulse();
        _buildProgressBar.Visible = false;
    }

    // ═══════════════════════════════════════════
    //  TYPING INDICATOR  (bridges the LLM wait)
    // ═══════════════════════════════════════════

    private void ShowTypingIndicator()
    {
        if (_typingItem != null) return;  // already showing

        _typingDotsLabel = new Label
        {
            Text = "MANTIS is thinking ●",
            Font = new Font(FontBodyFamily, FontBody),
            TextColor = AccentDim
        };

        var inner = new TableLayout
        {
            Padding = new Padding(16, 12, 16, 12),
            BackgroundColor = AiBubbleBg,
            Rows = { new TableRow(new TableCell(_typingDotsLabel, scaleWidth: true)) }
        };
        var stripe = new Panel { BackgroundColor = AccentDim, Width = 4 };
        var bubbleRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { stripe, new StackLayoutItem(inner, expand: true) }
        };
        var bubble = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Items =
            {
                new StackLayoutItem(new Panel { Width = 10, BackgroundColor = Bg }),
                new StackLayoutItem(bubbleRow, expand: true),
                new StackLayoutItem(new Panel { Width = 10, BackgroundColor = Bg })
            }
        };

        // Wrap with its own top spacer so removing this single item also removes
        // the gap — no fragile index math against the message list.
        var wrapper = new StackLayout
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(new Panel { Height = 8, BackgroundColor = Bg }),
                new StackLayoutItem(bubble)
            }
        };

        _typingItem = new StackLayoutItem(wrapper);
        _messageList.Items.Add(_typingItem);

        _typingFrame = 0;
        _typingTimer = new UITimer { Interval = 0.4 };
        _typingTimer.Elapsed += (_, _) =>
        {
            _typingFrame = (_typingFrame + 1) % 4;
            var dots = _typingFrame switch
            {
                0 => "●",
                1 => "● ●",
                2 => "● ● ●",
                _ => "·"
            };
            if (_typingDotsLabel != null)
                _typingDotsLabel.Text = $"MANTIS is thinking  {dots}";
        };
        _typingTimer.Start();

        Application.Instance.AsyncInvoke(() =>
            _messageScroll.ScrollPosition = new Point(0, int.MaxValue / 2));
    }

    private void HideTypingIndicator()
    {
        _typingTimer?.Stop();
        _typingTimer = null;
        _typingDotsLabel = null;

        if (_typingItem != null)
        {
            _messageList.Items.Remove(_typingItem);
            _typingItem = null;
        }
    }

    protected override void OnUnLoad(EventArgs e)
    {
        base.OnUnLoad(e);
        // Stop any running timers so they don't fire against detached controls
        // after the panel is removed from its window.
        _typingTimer?.Stop();
        _buildPulseTimer?.Stop();

        // Detach from the long-lived service/monitor so a recreated panel
        // doesn't stack duplicate handlers firing against this dead instance.
        if (_onSolutionsReadyHandler != null)
            _service.OnSolutionsReady -= _onSolutionsReadyHandler;
        if (_onErrorsDetectedHandler != null)
            _service.ErrorMonitor.OnErrorsDetected -= _onErrorsDetectedHandler;
        if (_onErrorsClearedHandler != null)
            _service.ErrorMonitor.OnErrorsCleared -= _onErrorsClearedHandler;
    }

    // ═══════════════════════════════════════════
    //  INPUT HANDLING
    // ═══════════════════════════════════════════

    private void UpdatePlaceholder() =>
        _placeholderLabel.Visible = string.IsNullOrEmpty(_inputBox.Text);

    private void OnInputTextChanged(object? sender, EventArgs e) => UpdatePlaceholder();

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Keys.Enter && !e.Modifiers.HasFlag(Keys.Shift))
        {
            e.Handled = true;
            _ = SendMessage();
        }
    }

    private void OnSendClick(object? sender, EventArgs e) => _ = SendMessage();

    // ═══════════════════════════════════════════
    //  EVENT WIRING
    // ═══════════════════════════════════════════

    private void WireServiceEvents()
    {
        _service.OnStatus += msg =>
            Application.Instance.Invoke(() =>
            {
                _statusLabel.Text = msg;
                _statusLabel.TextColor = Accent;
            });

        _service.OnAdvice += msg =>
            Application.Instance.Invoke(() =>
            {
                HideTypingIndicator();
                AddMessageBubble("MANTIS", "ai", msg);
            });

        _service.OnError += msg =>
            Application.Instance.Invoke(() =>
            {
                HideTypingIndicator();
                AddMessageBubble("ERROR", "error", msg);
            });

        _service.OnComponentPlaced += (id, name) =>
            Application.Instance.Invoke(() =>
            {
                // First component to land — swap the "thinking" bubble for the
                // live build-trace bar.
                HideTypingIndicator();
                if (!_buildProgressBar.Visible) ShowBuildProgress();
                AddBuildStep(name);
                _statusLabel.Text = $"Placed: {name}";
            });

        _service.OnBuildComplete += () =>
            Application.Instance.Invoke(() =>
            {
                HideTypingIndicator();
                CompleteBuildProgress();
                _statusLabel.Text = "Build complete";
                _statusLabel.TextColor = Accent;
                Task.Delay(3000).ContinueWith(_ =>
                    Application.Instance.Invoke(() => HideBuildProgress()));
            });

        // ── Multi-solution: render comparison tabs when alternatives arrive ──
        _onSolutionsReadyHandler = multi =>
            Application.Instance.Invoke(() => ShowSolutionTabs(multi));
        _service.OnSolutionsReady += _onSolutionsReadyHandler;

        // ── Error-healing: banner reflects the live error count. These fire
        //    from GH's SolutionEnd (possibly off the UI thread) so marshal. ──
        _onErrorsDetectedHandler = errors =>
            Application.Instance.AsyncInvoke(() => ShowErrorBanner(errors.Count));
        _service.ErrorMonitor.OnErrorsDetected += _onErrorsDetectedHandler;

        _onErrorsClearedHandler = () =>
            Application.Instance.AsyncInvoke(HideErrorBanner);
        _service.ErrorMonitor.OnErrorsCleared += _onErrorsClearedHandler;
    }

    // ═══════════════════════════════════════════
    //  SEND
    // ═══════════════════════════════════════════

    private async Task SendMessage()
    {
        var prompt = _inputBox.Text.Trim();
        if (string.IsNullOrEmpty(prompt)) return;

        if (!_service.HasApiKey && _activeProvider != "Ollama")
        {
            AddMessageBubble("MANTIS", "ai",
                $"No API key configured for {_activeProvider}.\n\n" +
                "Click the 'Models' button above to configure your API key, " +
                "or switch to Ollama for free local inference.");
            SetModelPanelVisible(true);
            return;
        }

        _inputBox.Text = "";
        UpdatePlaceholder();
        AddMessageBubble("You", "user", prompt);
        SetBusy(true);
        ShowTypingIndicator();

        if (_service.ProviderManager.Active is OllamaClient ollama)
        {
            var warning = ollama.GetModelQualityWarning();
            if (warning != null)
                AddMessageBubble("MANTIS", "system", warning);
        }

        var doc = EnsureGrasshopperDocument();
        if (doc == null)
        {
            AddMessageBubble("ERROR", "error",
                "No Grasshopper document found.\n" +
                "Open Grasshopper first, then try again.");
            SetBusy(false);
            HideTypingIndicator();
            HideBuildProgress();
            return;
        }

        // Begin watching this document for component errors so the healing
        // banner can offer one-click fixes after the build solves.
        EnsureErrorMonitoring(doc);

        _cts = new CancellationTokenSource();
        try
        {
            if (_iterateToggle.Checked == true)
                await _service.IterateAsync(prompt, doc, _cts.Token);
            else if (_exploreToggle.Checked == true)
                await _service.GenerateMultiSolutionAsync(prompt, doc, _cts.Token);
            else
                await _service.GenerateAsync(prompt, doc, streaming: true, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AddMessageBubble("MANTIS", "ai", "Generation cancelled.");
        }
        catch (InvalidOperationException ex)
        {
            AddMessageBubble("ERROR", "error", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            AddMessageBubble("ERROR", "error", $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            AddMessageBubble("ERROR", "error", $"Unexpected error: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            HideTypingIndicator();  // safety net — no-op if already hidden
            _cts = null;
        }
    }

    private static GH_Document? EnsureGrasshopperDocument()
    {
        var activeDoc = Instances.ActiveCanvas?.Document;
        if (activeDoc != null) return activeDoc;

        var server = Instances.DocumentServer;
        if (server != null && server.DocumentCount > 0)
        {
            var existing = server[0];
            TryAssignDocumentToCanvas(existing);
            return existing;
        }

        try
        {
            var newDoc = new GH_Document();
            server?.AddDocument(newDoc);
            TryAssignDocumentToCanvas(newDoc);
            return newDoc;
        }
        catch { return null; }
    }

    private static void TryAssignDocumentToCanvas(GH_Document doc)
    {
        try
        {
            var canvasProp = typeof(Instances).GetProperty(
                "ActiveCanvas",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            if (canvasProp == null) return;
            var canvasObj = canvasProp.GetValue(null);
            if (canvasObj == null) return;
            var docProp = canvasObj.GetType().GetProperty("Document");
            if (docProp?.CanWrite == true)
                docProp.SetValue(canvasObj, doc);
        }
        catch { }
    }

    private void SetBusy(bool busy)
    {
        _sendButton.Enabled = !busy;
        _inputBox.ReadOnly = busy;
        _modelsButton.Enabled = !busy;
        _explainButton.Enabled = !busy;
        if (busy)
        {
            _statusLabel.Text = "Generating...";
            _statusLabel.TextColor = Amber;
            _sendButton.Text = "...";
        }
        else
        {
            _statusLabel.TextColor = Accent;
            _sendButton.Text = "Send";
        }
    }

    // ═══════════════════════════════════════════
    //  ERROR-HEALING BANNER
    // ═══════════════════════════════════════════

    private StackLayout BuildErrorBanner()
    {
        var warnIcon = new Label
        {
            Text = "⚠",
            Font = new Font(FontBodyFamily, FontBody, FontStyle.Bold),
            TextColor = Amber,
            VerticalAlignment = VerticalAlignment.Center
        };

        _errorBannerLabel = new Label
        {
            Text = "",
            Font = new Font(FontBodyFamily, FontBodySmall, FontStyle.Bold),
            TextColor = Amber,
            Wrap = WrapMode.None,
            VerticalAlignment = VerticalAlignment.Center
        };

        _errorFixButton = new Button
        {
            Text = "Fix with AI",
            Width = 110,
            Height = 28,
            BackgroundColor = Amber,
            TextColor = Bg,
            Font = new Font(FontBodyFamily, FontButton, FontStyle.Bold),
            ToolTip = "Send the errors to the AI and apply its fix"
        };
        _errorFixButton.Click += OnFixErrorsClick;

        var dismiss = new Button
        {
            Text = "Dismiss",
            Width = 78,
            Height = 28,
            BackgroundColor = BgSurface,
            TextColor = Text2,
            Font = new Font(FontBodyFamily, FontButton)
        };
        dismiss.Click += (_, _) => HideErrorBanner();

        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Padding(14, 8),
            BackgroundColor = Color.FromArgb(40, 30, 16),
            VerticalContentAlignment = VerticalAlignment.Center,
            Visible = false,
            Items =
            {
                warnIcon,
                new StackLayoutItem(_errorBannerLabel, expand: true),
                _errorFixButton,
                dismiss
            }
        };
    }

    private void ShowErrorBanner(int count)
    {
        if (_errorBannerLabel != null)
            _errorBannerLabel.Text = count == 1
                ? "1 component has an error"
                : $"{count} components have errors";
        _errorBanner.Visible = true;
    }

    private void HideErrorBanner() => _errorBanner.Visible = false;

    private void EnsureErrorMonitoring(GH_Document doc)
    {
        if (_errorMonitoringStarted && ReferenceEquals(_monitoredDoc, doc)) return;
        _service.StartErrorMonitoring(doc);   // detaches any previous document
        _errorMonitoringStarted = true;
        _monitoredDoc = doc;
    }

    private async void OnFixErrorsClick(object? sender, EventArgs e) => await HealErrors();

    private async Task HealErrors()
    {
        var doc = EnsureGrasshopperDocument();
        if (doc == null)
        {
            AddMessageBubble("ERROR", "error",
                "No Grasshopper document found. Open Grasshopper first.");
            return;
        }

        if (_errorFixButton != null) _errorFixButton.Enabled = false;
        SetBusy(true);
        ShowTypingIndicator();
        _cts = new CancellationTokenSource();
        try
        {
            await _service.HealErrorsAsync(doc, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AddMessageBubble("MANTIS", "ai", "Healing cancelled.");
        }
        catch (Exception ex)
        {
            AddMessageBubble("ERROR", "error", $"Healing failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            HideTypingIndicator();
            if (_errorFixButton != null) _errorFixButton.Enabled = true;
            _cts = null;
        }
    }

    // ═══════════════════════════════════════════
    //  EXPLAIN MODE  (explain the canvas selection)
    // ═══════════════════════════════════════════

    private async void OnExplainClick(object? sender, EventArgs e) => await ExplainSelection();

    private async Task ExplainSelection()
    {
        var doc = Instances.ActiveCanvas?.Document;
        if (doc == null)
        {
            AddMessageBubble("MANTIS", "system",
                "Open Grasshopper and select a component, then click Explain.");
            return;
        }

        var selected = doc.SelectedObjects();
        if (selected == null || selected.Count == 0)
        {
            AddMessageBubble("MANTIS", "system",
                "Select one component on the canvas to explain it — or select several " +
                "to explain the whole chain — then click Explain.");
            return;
        }

        var target = selected[0];
        var isChain = selected.Count > 1;
        AddMessageBubble("You", "user", isChain
            ? $"Explain the chain starting at \"{target.Name}\" ({selected.Count} components selected)"
            : $"Explain the \"{target.Name}\" component");

        SetBusy(true);
        ShowTypingIndicator();
        _cts = new CancellationTokenSource();
        try
        {
            var explanation = isChain
                ? await _service.ExplainChainAsync(target, _cts.Token)
                : await _service.ExplainComponentAsync(target, _cts.Token);
            HideTypingIndicator();
            AddMessageBubble("MANTIS", "ai", explanation);
        }
        catch (OperationCanceledException)
        {
            HideTypingIndicator();
            AddMessageBubble("MANTIS", "ai", "Explanation cancelled.");
        }
        catch (Exception ex)
        {
            HideTypingIndicator();
            AddMessageBubble("ERROR", "error", $"Explain failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            HideTypingIndicator();
            _cts = null;
        }
    }

    // ═══════════════════════════════════════════
    //  MULTI-SOLUTION COMPARISON TABS
    // ═══════════════════════════════════════════

    /// <summary>
    /// Render the alternative approaches as a comparison card in the chat.
    /// Each option shows its name, description, component/connection count and
    /// an "Apply" button that builds it live on the canvas.
    /// </summary>
    private void ShowSolutionTabs(MultiSolutionResponse multi)
    {
        if (multi.Solutions.Count == 0) return;

        // Insets are measured from the content edge (FullContentWidth already
        // nets out the scrollbar gutter): outer 10+10 side panels = 20; cards
        // add 14+14 padding = 28 more.
        const int headerInset = 20;
        const int cardLabelInset = 48;

        var header = new Label
        {
            Text = $"{multi.Solutions.Count} approaches — pick one to build:",
            Font = new Font(FontBodyFamily, FontBody, FontStyle.Bold),
            TextColor = Accent,
            Wrap = WrapMode.Word
        };
        TrackResponsive(header, headerInset);

        var column = new StackLayout
        {
            Spacing = 10,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { header }
        };

        for (int i = 0; i < multi.Solutions.Count; i++)
        {
            var sol = multi.Solutions[i];
            var index = i;
            var optionName = string.IsNullOrWhiteSpace(sol.SolutionName)
                ? $"Option {i + 1}"
                : sol.SolutionName;

            var nameLabel = new Label
            {
                Text = $"{i + 1}.  {optionName}",
                Font = new Font(FontBodyFamily, FontBody, FontStyle.Bold),
                TextColor = Text1,
                Wrap = WrapMode.Word
            };
            TrackResponsive(nameLabel, cardLabelInset);

            var descLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(sol.SolutionDescription)
                    ? "(no description provided)"
                    : sol.SolutionDescription,
                Font = new Font(FontBodyFamily, FontBodySmall),
                TextColor = Text2,
                Wrap = WrapMode.Word
            };
            TrackResponsive(descLabel, cardLabelInset);

            var metaLabel = new Label
            {
                Text = $"{sol.Components.Count} components · {sol.Connections.Count} connections",
                Font = new Font(FontMonoFamily, FontMeta),
                TextColor = TextD,
                VerticalAlignment = VerticalAlignment.Center
            };

            var applyBtn = new Button
            {
                Text = "Apply",
                Width = 88,
                Height = 30,
                BackgroundColor = Accent,
                TextColor = Bg,
                Font = new Font(FontBodyFamily, FontButton, FontStyle.Bold),
                ToolTip = $"Build \"{optionName}\" on the canvas"
            };
            applyBtn.Click += async (_, _) => await ApplySolution(index);

            var metaRow = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    new StackLayoutItem(metaLabel, expand: true),
                    applyBtn
                }
            };

            var card = new StackLayout
            {
                Spacing = 6,
                Padding = new Padding(14, 12),
                BackgroundColor = BgCard,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items = { nameLabel, descLabel, metaRow }
            };
            column.Items.Add(card);
        }

        var wrapped = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Items =
            {
                new StackLayoutItem(new Panel { Width = 10, BackgroundColor = Bg }),
                new StackLayoutItem(column, expand: true),
                new StackLayoutItem(new Panel { Width = 10, BackgroundColor = Bg })
            }
        };

        if (_messageList.Items.Count > 0)
            _messageList.Items.Add(new StackLayoutItem(new Panel { Height = 8, BackgroundColor = Bg }));
        _messageList.Items.Add(new StackLayoutItem(wrapped));

        Application.Instance.AsyncInvoke(() =>
        {
            UpdateResponsiveLabelWidths();
            _messageScroll.ScrollPosition = new Point(0, int.MaxValue / 2);
        });
    }

    private async Task ApplySolution(int index)
    {
        var doc = EnsureGrasshopperDocument();
        if (doc == null)
        {
            AddMessageBubble("ERROR", "error",
                "No Grasshopper document found. Open Grasshopper first.");
            return;
        }

        EnsureErrorMonitoring(doc);
        SetBusy(true);
        ShowTypingIndicator();
        _cts = new CancellationTokenSource();
        try
        {
            await _service.ApplySolutionAsync(index, doc, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            AddMessageBubble("MANTIS", "ai", "Apply cancelled.");
        }
        catch (Exception ex)
        {
            AddMessageBubble("ERROR", "error", $"Apply failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            HideTypingIndicator();
            _cts = null;
        }
    }

    // ═══════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════

    private static string TruncateTitle(string text, int maxLen)
    {
        var firstLine = text.Split('\n')[0].Trim();
        if (firstLine.Length <= maxLen) return firstLine;
        return firstLine.Substring(0, maxLen - 1) + "...";
    }

    private static string FormatTimestamp(DateTime dt)
    {
        var now = DateTime.Now;
        if (dt.Date == now.Date) return dt.ToString("HH:mm");
        if (dt.Date == now.Date.AddDays(-1)) return "Yesterday";
        return dt.ToString("MMM d");
    }
}
