using Eto.Drawing;
using Eto.Forms;
using Mantis.Plugin.AI;

namespace Mantis.Plugin.UI;

public class SettingsDialog : Dialog
{
    private readonly MantisService _service;
    private readonly DropDown _providerPicker;
    private readonly PasswordBox _apiKeyBox;
    private readonly Label _apiKeyLabel;
    private readonly Label _apiKeyHint;
    private readonly Panel _apiKeyPanel;

    // Theme shortcuts
    private static Color Bg => MantisPanelHost.BgDeep;
    private static Color BgCard => MantisPanelHost.BgCard;
    private static Color BgSurface => MantisPanelHost.BgSurface;
    private static Color Border => MantisPanelHost.BorderColor;
    private static Color Accent => MantisPanelHost.MantisGreen;
    private static Color AccentDim => MantisPanelHost.MantisDim;
    private static Color Text1 => MantisPanelHost.TextPrimary;
    private static Color Text2 => MantisPanelHost.TextSecondary;
    private static Color TextD => MantisPanelHost.TextDim;

    public SettingsDialog(MantisService service)
    {
        _service = service;
        Title = "MANTIS — Settings";
        MinimumSize = new Size(440, 380);
        Padding = new Padding(0);
        Resizable = false;
        BackgroundColor = Bg;

        // Provider selector
        _providerPicker = new DropDown { BackgroundColor = BgSurface, TextColor = Text1 };
        foreach (var name in _service.ProviderManager.ProviderNames)
            _providerPicker.Items.Add(name);
        _providerPicker.SelectedKey = _service.ProviderManager.ActiveProviderName;
        _providerPicker.SelectedKeyChanged += OnProviderPickerChanged;

        // API key input
        _apiKeyBox = new PasswordBox
        {
            Width = 340,
            BackgroundColor = BgSurface,
            TextColor = Text1
        };

        _apiKeyLabel = new Label
        {
            Text = GetApiKeyLabel(),
            TextColor = Text2,
            Font = new Font("Space Grotesk", 10)
        };
        _apiKeyHint = new Label
        {
            TextColor = AccentDim,
            Font = new Font("JetBrains Mono", 9)
        };
        LoadApiKeyHint();

        _apiKeyPanel = new Panel { BackgroundColor = Bg };
        UpdateApiKeyPanel();

        var saveButton = new Button
        {
            Text = "Save",
            Width = 80,
            BackgroundColor = Accent,
            TextColor = Bg
        };
        saveButton.Click += OnSave;

        var cancelButton = new Button
        {
            Text = "Cancel",
            Width = 80,
            BackgroundColor = BgSurface,
            TextColor = Text2
        };
        cancelButton.Click += (_, _) => Close();

        // ── Title bar ──
        var titleBar = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Padding = new Padding(16, 10),
            BackgroundColor = BgCard,
            Items =
            {
                new Label
                {
                    Text = "SETTINGS",
                    Font = new Font("Space Grotesk", 12, FontStyle.Bold),
                    TextColor = Accent
                }
            }
        };
        var sep1 = new Panel { BackgroundColor = Border, Height = 1 };

        // ── Provider section ──
        var providerSection = new StackLayout
        {
            Spacing = 6,
            Padding = new Padding(16, 12),
            BackgroundColor = Bg,
            Items =
            {
                new Label
                {
                    Text = "AI PROVIDER",
                    Font = new Font("JetBrains Mono", 8),
                    TextColor = TextD
                },
                _providerPicker
            }
        };
        var sep2 = new Panel { BackgroundColor = Border, Height = 1 };

        // ── API key section ──
        var keySection = new StackLayout
        {
            Spacing = 6,
            Padding = new Padding(16, 12),
            BackgroundColor = Bg,
            Items =
            {
                new Label
                {
                    Text = "AUTHENTICATION",
                    Font = new Font("JetBrains Mono", 8),
                    TextColor = TextD
                },
                _apiKeyPanel
            }
        };
        var sep3 = new Panel { BackgroundColor = Border, Height = 1 };

        // ── Help links ──
        var helpSection = new StackLayout
        {
            Spacing = 4,
            Padding = new Padding(16, 10),
            BackgroundColor = BgCard,
            Items =
            {
                new Label
                {
                    Text = "GET API KEYS",
                    Font = new Font("JetBrains Mono", 8),
                    TextColor = TextD
                },
                new Label
                {
                    Text = "Claude  console.anthropic.com",
                    Font = new Font("JetBrains Mono", 9),
                    TextColor = AccentDim
                },
                new Label
                {
                    Text = "Gemini  aistudio.google.com",
                    Font = new Font("JetBrains Mono", 9),
                    TextColor = AccentDim
                },
                new Label
                {
                    Text = "OpenRouter  openrouter.ai",
                    Font = new Font("JetBrains Mono", 9),
                    TextColor = AccentDim
                },
                new Label
                {
                    Text = "Ollama  localhost (no key needed)",
                    Font = new Font("JetBrains Mono", 9),
                    TextColor = AccentDim
                }
            }
        };

        // ── Button row ──
        var buttonRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Padding = new Padding(16, 10),
            BackgroundColor = Bg,
            Items =
            {
                new StackLayoutItem(null, expand: true),
                cancelButton,
                saveButton
            }
        };

        Content = new StackLayout
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BackgroundColor = Bg,
            Items =
            {
                titleBar,
                sep1,
                providerSection,
                sep2,
                keySection,
                sep3,
                helpSection,
                new StackLayoutItem(null, expand: true),
                buttonRow
            }
        };

        DefaultButton = saveButton;
        AbortButton = cancelButton;
    }

    private void OnProviderPickerChanged(object? sender, EventArgs e)
    {
        UpdateApiKeyPanel();
        LoadApiKeyHint();
    }

    private string GetSelectedProvider() =>
        _providerPicker.SelectedKey ?? "Gemini";

    private string GetApiKeyLabel()
    {
        var provider = GetSelectedProvider();
        return provider switch
        {
            "Claude" => "Claude API Key",
            "Gemini" => "Gemini API Key",
            "OpenRouter" => "OpenRouter API Key",
            "Ollama" => "No API key required",
            _ => "API Key"
        };
    }

    private void UpdateApiKeyPanel()
    {
        var provider = GetSelectedProvider();
        _apiKeyLabel.Text = GetApiKeyLabel();

        if (provider == "Ollama")
        {
            _apiKeyPanel.Content = new StackLayout
            {
                Spacing = 6,
                Items =
                {
                    _apiKeyLabel,
                    new Label
                    {
                        Text = "◆ Ollama runs locally — no internet, no API key",
                        TextColor = Accent,
                        Font = new Font("JetBrains Mono", 10)
                    },
                    new Label
                    {
                        Text = "Ensure Ollama is running at localhost:11434",
                        TextColor = Text2,
                        Font = new Font("Space Grotesk", 9)
                    }
                }
            };
        }
        else
        {
            _apiKeyPanel.Content = new StackLayout
            {
                Spacing = 6,
                Items =
                {
                    _apiKeyLabel,
                    _apiKeyBox,
                    _apiKeyHint
                }
            };
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var provider = GetSelectedProvider();

        // Switch provider
        _service.SetProvider(provider);

        // Set API key (skip for Ollama)
        if (provider != "Ollama")
        {
            var key = _apiKeyBox.Text?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show(this,
                    $"Please enter a valid {provider} API key.",
                    "Missing API Key",
                    MessageBoxType.Warning);
                return;
            }

            _service.SetApiKey(provider, key);
            SaveSettings(provider, key);
        }
        else
        {
            SaveSettings(provider, null);
        }

        Close();
    }

    private void LoadApiKeyHint()
    {
        var provider = GetSelectedProvider();
        var hint = LoadSavedHint(provider);
        _apiKeyHint.Text = hint != null
            ? $"Saved: {hint}"
            : "No key saved";
        _apiKeyBox.Text = "";
    }

    private static void SaveSettings(string provider, string? apiKey)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mantis");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.json");

            // Load existing settings
            var settings = new Dictionary<string, object>();
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(existing);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    settings[prop.Name] = prop.Value.ToString()!;
                }
            }

            settings["activeProvider"] = provider;
            settings["savedAt"] = DateTime.UtcNow.ToString("o");

            if (apiKey != null)
            {
                var masked = apiKey.Length > 8
                    ? apiKey[..4] + new string('*', apiKey.Length - 8) + apiKey[^4..]
                    : new string('*', apiKey.Length);
                settings[$"{provider.ToLower()}KeyHint"] = masked;
            }

            File.WriteAllText(path,
                System.Text.Json.JsonSerializer.Serialize(settings,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Settings save is non-critical
        }
    }

    private static string? LoadSavedHint(string provider)
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
            return doc.RootElement.TryGetProperty(key, out var hint)
                ? hint.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Load the saved active provider from settings.</summary>
    public static string? LoadSavedProvider()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mantis", "settings.json");
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("activeProvider", out var provider)
                ? provider.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
