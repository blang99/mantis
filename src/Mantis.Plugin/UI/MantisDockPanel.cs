using System;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;

namespace Mantis.Plugin.UI;

/// <summary>
/// Dockable Rhino panel that hosts the MANTIS chat UI. Registered by
/// <see cref="MantisRhinoPlugin"/> so it shows up as a Rhino tab. Reuses the very
/// same <see cref="ChatPanel"/> the Grasshopper float uses, sharing one
/// <see cref="MantisService"/> so conversation and component knowledge are
/// consistent across both entry points.
/// </summary>
[Guid("BA6BEED0-D3BF-4BE5-8CF2-65C3C88BB738")]
public class MantisDockPanel : Panel
{
    /// <summary>Stable id used by the <c>Mantis</c> command to open/close this panel.</summary>
    public static Guid PanelId => typeof(MantisDockPanel).GUID;

    public MantisDockPanel()
    {
        BackgroundColor = MantisPanelHost.BgDeep;

        // The panel must NEVER come up blank. On a platform we can't directly test
        // (Windows/WPF), a swallowed exception during service init or UI construction
        // would leave an empty tab with no clue why. So build defensively: each stage
        // is isolated, logged, and any failure is shown IN the panel (copyable) plus
        // written to <AppData>/Mantis/mantis-log.txt.
        MantisService service;
        try
        {
            // Creating the service loads Grasshopper in the background and scans the
            // component catalog — so a build works even though the user never opened
            // the Grasshopper editor.
            service = MantisPanelHost.GetOrCreateService();
            MantisLog.Info("Dock panel: service ready.");
        }
        catch (Exception ex)
        {
            MantisLog.Error("Dock panel: service init failed", ex);
            Content = BuildErrorView("MANTIS couldn't start its engine", ex);
            return;
        }

        try
        {
            Content = new ChatPanel(service);
            MantisLog.Info("Dock panel: UI built OK.");
        }
        catch (Exception ex)
        {
            MantisLog.Error("Dock panel: UI build failed", ex);
            Content = BuildErrorView("MANTIS couldn't draw its panel", ex);
        }
    }

    /// <summary>
    /// A visible, copyable fallback so a failure is diagnosable instead of blank.
    /// Uses only system fonts + basic controls so it can't itself fail to render.
    /// </summary>
    private static Control BuildErrorView(string headline, Exception ex)
    {
        var details = ex.GetType().FullName + ": " + ex.Message + Environment.NewLine + Environment.NewLine + ex;
        return new Scrollable
        {
            Padding = new Padding(16),
            BackgroundColor = MantisPanelHost.BgDeep,
            Content = new StackLayout
            {
                Spacing = 12,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new Label { Text = headline, TextColor = MantisPanelHost.MantisGreen, Font = new Font(SystemFont.Bold, 15) },
                    new Label
                    {
                        Text = "Please copy the text below (also saved to %AppData%\\Mantis\\mantis-log.txt) and send it over so we can fix it.",
                        TextColor = MantisPanelHost.TextSecondary,
                        Font = new Font(SystemFont.Default, 10),
                        Wrap = WrapMode.Word
                    },
                    new TextArea
                    {
                        Text = details,
                        ReadOnly = true,
                        Wrap = true,
                        Height = 560,
                        BackgroundColor = MantisPanelHost.BgInput,
                        TextColor = MantisPanelHost.TextPrimary
                    }
                }
            }
        };
    }
}
