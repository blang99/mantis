using Eto.Drawing;
using Eto.Forms;
using Mantis.Plugin.AI;

namespace Mantis.Plugin.UI;

/// <summary>
/// Inline side panel for picking AI provider + model + API key.
/// Slides into the main layout as a third splitter panel — no modal window,
/// no resizing required. Designed to work even at narrow widths (~320px).
/// </summary>
public class ModelPickerPanel : Panel
{
    private readonly MantisService _service;
    private string _selectedProvider;
    private string _selectedModel;

    private readonly StackLayout _providerList;
    private readonly StackLayout _modelList;
    private readonly PasswordBox _apiKeyBox;
    private readonly Label _apiKeyStatus;
    private readonly Label _providerStatus;
    private readonly Button _saveKeyButton;
    private readonly Scrollable _outerScroll;

    public event Action? OnClose;

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

    private static readonly Color FreeBadgeBg = Color.FromArgb(28, 60, 38);
    private static readonly Color FreeBadgeText = Color.FromArgb(140, 240, 170);
    private static readonly Color PaidBadgeBg = Color.FromArgb(60, 48, 22);
    private static readonly Color PaidBadgeText = Color.FromArgb(255, 200, 100);
    private static readonly Color ModelRowBg = Color.FromArgb(22, 32, 26);
    private static readonly Color ModelRowSelectedBg = Color.FromArgb(34, 60, 42);

    private const float FontTitle = 15f;
    private const float FontSection = 11f;
    private const float FontModel = 13f;
    private const float FontModelDesc = 11f;
    private const float FontModelId = 10f;
    private const float FontBadge = 9f;
    private const float FontHint = 11f;

    public ModelPickerPanel(MantisService service)
    {
        _service = service;
        _selectedProvider = service.ProviderManager.ActiveProviderName;
        _selectedModel = service.ProviderManager.Active.SelectedModel;

        BackgroundColor = Bg;

        _providerList = new StackLayout
        {
            Spacing = 4,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = Bg
        };

        _modelList = new StackLayout
        {
            Spacing = 4,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = Bg
        };

        _apiKeyBox = new PasswordBox
        {
            BackgroundColor = BgInput,
            TextColor = Text1,
            Font = new Font("sans-serif", FontModel)
        };

        _apiKeyStatus = new Label
        {
            Font = new Font("JetBrains Mono, Menlo, monospace", FontHint),
            TextColor = AccentDim,
            Wrap = WrapMode.Word
        };

        _providerStatus = new Label
        {
            Font = new Font("sans-serif", FontHint),
            TextColor = Text2,
            Wrap = WrapMode.Word
        };

        _saveKeyButton = new Button
        {
            Text = "Save",
            Width = 80,
            Height = 32,
            BackgroundColor = Accent,
            TextColor = Bg,
            Font = new Font("sans-serif", FontHint, FontStyle.Bold)
        };
        _saveKeyButton.Click += OnSaveKey;

        _outerScroll = new Scrollable
        {
            Content = BuildContent(),
            BackgroundColor = Bg,
            Border = BorderType.None,
            ExpandContentWidth = true
        };
        Content = _outerScroll;

        RefreshAll();
    }

    // ═══════════════════════════════════════════
    //  LAYOUT
    // ═══════════════════════════════════════════

    private Control BuildContent()
    {
        // Header with close button
        var closeButton = new Button
        {
            Text = "Close",
            Width = 60,
            Height = 28,
            BackgroundColor = BgSurface,
            TextColor = Text2,
            Font = new Font("sans-serif", FontHint)
        };
        closeButton.Click += (_, _) => OnClose?.Invoke();

        var titleRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Padding = new Padding(14, 12, 12, 12),
            BackgroundColor = BgCard,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                new Label
                {
                    Text = "AI Model",
                    Font = new Font("sans-serif", FontTitle, FontStyle.Bold),
                    TextColor = Accent,
                    VerticalAlignment = VerticalAlignment.Center
                },
                new StackLayoutItem(null, expand: true),
                closeButton
            }
        };

        var sep1 = new Panel { BackgroundColor = Border, Height = 1 };

        // Provider section
        var providerSection = new StackLayout
        {
            Spacing = 8,
            Padding = new Padding(14, 14, 14, 10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = Bg,
            Items =
            {
                new Label
                {
                    Text = "PROVIDER",
                    Font = new Font("JetBrains Mono, Menlo, monospace", FontSection, FontStyle.Bold),
                    TextColor = TextD
                },
                _providerList,
                _providerStatus
            }
        };

        // Model section
        var modelSection = new StackLayout
        {
            Spacing = 8,
            Padding = new Padding(14, 10, 14, 10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = Bg,
            Items =
            {
                new Label
                {
                    Text = "MODEL",
                    Font = new Font("JetBrains Mono, Menlo, monospace", FontSection, FontStyle.Bold),
                    TextColor = TextD
                },
                _modelList
            }
        };

        // API key section
        var keySection = new StackLayout
        {
            Spacing = 8,
            Padding = new Padding(14, 10, 14, 14),
            BackgroundColor = BgCard,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label
                {
                    Text = "API KEY",
                    Font = new Font("JetBrains Mono, Menlo, monospace", FontSection, FontStyle.Bold),
                    TextColor = TextD
                },
                _apiKeyStatus,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Items =
                    {
                        new StackLayoutItem(_apiKeyBox, expand: true),
                        _saveKeyButton
                    }
                },
                BuildHelpLinks()
            }
        };

        return new StackLayout
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = Bg,
            Items =
            {
                titleRow,
                sep1,
                providerSection,
                modelSection,
                keySection
            }
        };
    }

    private Control BuildHelpLinks()
    {
        return new StackLayout
        {
            Spacing = 3,
            Padding = new Padding(0, 8, 0, 0),
            Items =
            {
                new Label
                {
                    Text = "Get a free API key:",
                    Font = new Font("sans-serif", FontHint, FontStyle.Bold),
                    TextColor = TextD
                },
                MakeHelpLine("OpenRouter", "openrouter.ai/keys", "most free models"),
                MakeHelpLine("Claude", "console.anthropic.com", ""),
                MakeHelpLine("Gemini", "aistudio.google.com/apikey", ""),
                MakeHelpLine("OpenAI", "platform.openai.com/api-keys", ""),
                MakeHelpLine("Ollama", "ollama.com", "free local")
            }
        };
    }

    private Label MakeHelpLine(string provider, string url, string note)
    {
        var text = string.IsNullOrEmpty(note)
            ? $"  {provider}: {url}"
            : $"  {provider}: {url}  ({note})";
        return new Label
        {
            Text = text,
            Font = new Font("JetBrains Mono, Menlo, monospace", FontHint - 1),
            TextColor = AccentDim,
            Wrap = WrapMode.Word
        };
    }

    // ═══════════════════════════════════════════
    //  REFRESH
    // ═══════════════════════════════════════════

    private void RefreshAll()
    {
        RefreshProviderList();
        RefreshProviderStatus();
        RefreshModelList();
        RefreshApiKeyStatus();
    }

    private void RefreshProviderList()
    {
        _providerList.Items.Clear();

        foreach (var providerName in _service.ProviderManager.ProviderNames)
        {
            var provider = _service.ProviderManager.GetProvider(providerName);
            bool hasFree = provider.AvailableModels.Any(m => m.IsFree);
            bool hasPaid = provider.AvailableModels.Any(m => !m.IsFree);

            string badgeText;
            Color badgeBg, badgeFg;
            if (hasFree && hasPaid) { badgeText = "FREE+PAID"; badgeBg = FreeBadgeBg; badgeFg = FreeBadgeText; }
            else if (hasFree) { badgeText = "FREE"; badgeBg = FreeBadgeBg; badgeFg = FreeBadgeText; }
            else { badgeText = "PAID"; badgeBg = PaidBadgeBg; badgeFg = PaidBadgeText; }

            var nameLabel = new Label
            {
                Text = providerName,
                Font = new Font("sans-serif", FontModel, FontStyle.Bold),
                TextColor = providerName == _selectedProvider ? Accent : Text1,
                VerticalAlignment = VerticalAlignment.Center
            };

            var badge = new Panel
            {
                BackgroundColor = badgeBg,
                Padding = new Padding(6, 3),
                Content = new Label
                {
                    Text = badgeText,
                    Font = new Font("JetBrains Mono, Menlo, monospace", FontBadge, FontStyle.Bold),
                    TextColor = badgeFg
                }
            };

            var checkMark = providerName == _selectedProvider
                ? new Label { Text = "✓", Font = new Font("sans-serif", FontModel, FontStyle.Bold), TextColor = Accent }
                : new Label { Text = " " };

            var row = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Padding = new Padding(12, 9),
                VerticalContentAlignment = VerticalAlignment.Center,
                BackgroundColor = providerName == _selectedProvider ? ModelRowSelectedBg : ModelRowBg,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new StackLayoutItem(nameLabel, expand: true),
                    badge,
                    checkMark
                }
            };

            var click = new Panel { Content = row, Cursor = Cursors.Pointer };
            var p = providerName;
            click.MouseDown += (_, _) => SelectProvider(p);
            _providerList.Items.Add(new StackLayoutItem(click));
        }
    }

    private void RefreshProviderStatus()
    {
        _providerStatus.Text = _selectedProvider switch
        {
            "Ollama" => "Local — no API key, no rate limits. Requires Ollama at localhost:11434.",
            "OpenRouter" => "Cloud aggregator — many free models with generous limits.",
            "Claude" => "Anthropic direct — premium quality. Paid key required.",
            "OpenAI" => "OpenAI direct — GPT-4o, o1, o3. Paid key required.",
            "Gemini" => "Google direct — free tier with 15 RPM rate limit.",
            _ => ""
        };
    }

    private void RefreshModelList()
    {
        _modelList.Items.Clear();
        var provider = _service.ProviderManager.Active;
        var models = provider.AvailableModels;

        if (models.Count == 0)
        {
            _modelList.Items.Add(new Label
            {
                Text = _selectedProvider == "Ollama"
                    ? "No models installed.\nRun: ollama pull llama3"
                    : "No models available.",
                Font = new Font("sans-serif", FontModel),
                TextColor = Amber,
                Wrap = WrapMode.Word
            });
            return;
        }

        var freeModels = models.Where(m => m.IsFree).ToList();
        var paidModels = models.Where(m => !m.IsFree).ToList();

        if (freeModels.Count > 0)
        {
            _modelList.Items.Add(BuildGroupHeader("FREE", Accent));
            foreach (var m in freeModels)
                _modelList.Items.Add(BuildModelRow(m));
        }

        if (paidModels.Count > 0)
        {
            _modelList.Items.Add(new Panel { Height = 6, BackgroundColor = Bg });
            _modelList.Items.Add(BuildGroupHeader("PAID  (API key required)", Amber));
            foreach (var m in paidModels)
                _modelList.Items.Add(BuildModelRow(m));
        }
    }

    private Control BuildGroupHeader(string title, Color color)
    {
        return new Panel
        {
            Padding = new Padding(2, 6, 2, 4),
            Content = new Label
            {
                Text = title,
                Font = new Font("JetBrains Mono, Menlo, monospace", FontHint, FontStyle.Bold),
                TextColor = color
            }
        };
    }

    private Control BuildModelRow(ModelOption m)
    {
        bool isSelected = m.Id == _selectedModel;

        var nameLabel = new Label
        {
            Text = m.DisplayName,
            Font = new Font("sans-serif", FontModel, FontStyle.Bold),
            TextColor = isSelected ? Accent : Text1
        };

        var descLabel = new Label
        {
            Text = m.Description,
            Font = new Font("sans-serif", FontModelDesc),
            TextColor = Text2,
            Wrap = WrapMode.Word
        };

        var idLabel = new Label
        {
            Text = m.Id,
            Font = new Font("JetBrains Mono, Menlo, monospace", FontModelId),
            TextColor = TextD,
            Wrap = WrapMode.Word
        };

        var badge = new Panel
        {
            BackgroundColor = m.IsFree ? FreeBadgeBg : PaidBadgeBg,
            Padding = new Padding(6, 3),
            Content = new Label
            {
                Text = m.IsFree ? "FREE" : "PAID",
                Font = new Font("JetBrains Mono, Menlo, monospace", FontBadge, FontStyle.Bold),
                TextColor = m.IsFree ? FreeBadgeText : PaidBadgeText
            }
        };

        var checkMark = isSelected
            ? new Label { Text = "✓", Font = new Font("sans-serif", FontModel, FontStyle.Bold), TextColor = Accent }
            : new Label { Text = " " };

        var textColumn = new StackLayout
        {
            Spacing = 2,
            Items = { nameLabel, descLabel, idLabel }
        };

        var rightColumn = new StackLayout
        {
            Spacing = 4,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Items = { badge, checkMark }
        };

        var row = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Padding(12, 10),
            VerticalContentAlignment = VerticalAlignment.Top,
            BackgroundColor = isSelected ? ModelRowSelectedBg : ModelRowBg,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(textColumn, expand: true),
                rightColumn
            }
        };

        var click = new Panel { Content = row, Cursor = Cursors.Pointer };
        var id = m.Id;
        click.MouseDown += (_, _) => SelectModel(id);
        return click;
    }

    private void RefreshApiKeyStatus()
    {
        if (_selectedProvider == "Ollama")
        {
            _apiKeyStatus.Text = "No API key needed — Ollama runs locally";
            _apiKeyStatus.TextColor = Accent;
            _apiKeyBox.Enabled = false;
            _saveKeyButton.Enabled = false;
            return;
        }

        _apiKeyBox.Enabled = true;
        _saveKeyButton.Enabled = true;

        var hint = LoadSavedKeyHint(_selectedProvider);
        if (hint != null)
        {
            _apiKeyStatus.Text = $"Saved: {hint}";
            _apiKeyStatus.TextColor = AccentDim;
        }
        else
        {
            _apiKeyStatus.Text = $"No {_selectedProvider} key saved";
            _apiKeyStatus.TextColor = Amber;
        }
    }

    // ═══════════════════════════════════════════
    //  ACTIONS
    // ═══════════════════════════════════════════

    private void SelectProvider(string providerName)
    {
        _selectedProvider = providerName;
        _service.SetProvider(providerName);
        _selectedModel = _service.ProviderManager.Active.SelectedModel;
        RefreshAll();
    }

    private void SelectModel(string modelId)
    {
        _selectedModel = modelId;
        _service.SetModel(modelId);
        RefreshModelList();
    }

    private void OnSaveKey(object? sender, EventArgs e)
    {
        var key = _apiKeyBox.Text?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            _apiKeyStatus.Text = "Please enter a key";
            _apiKeyStatus.TextColor = ErrRed;
            return;
        }

        if (_selectedProvider == "Ollama")
        {
            _apiKeyStatus.Text = "Ollama doesn't need an API key";
            _apiKeyStatus.TextColor = Accent;
            return;
        }

        _service.SetApiKey(_selectedProvider, key);
        SaveApiKeySettings(_selectedProvider, key);
        _apiKeyBox.Text = "";
        _apiKeyStatus.Text = $"✓ Saved for {_selectedProvider}";
        _apiKeyStatus.TextColor = Accent;
    }

    // ═══════════════════════════════════════════
    //  PERSISTENCE
    // ═══════════════════════════════════════════

    private static void SaveApiKeySettings(string provider, string apiKey)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mantis");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.json");

            var settings = new Dictionary<string, object>();
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(existing);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    settings[prop.Name] = prop.Value.ToString()!;
            }

            settings["activeProvider"] = provider;
            settings["savedAt"] = DateTime.UtcNow.ToString("o");

            settings[$"{provider.ToLower()}KeyHint"] = apiKey.Length > 8
                ? apiKey[..4] + new string('*', apiKey.Length - 8) + apiKey[^4..]
                : new string('*', apiKey.Length);

            File.WriteAllText(path,
                System.Text.Json.JsonSerializer.Serialize(settings,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private static string? LoadSavedKeyHint(string provider)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mantis", "settings.json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var key = $"{provider.ToLower()}KeyHint";
            return doc.RootElement.TryGetProperty(key, out var hint) ? hint.GetString() : null;
        }
        catch { return null; }
    }
}
