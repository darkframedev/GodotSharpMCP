using System.IO;
using Godot;

namespace GodotSharp.Plugin;

/// <summary>
/// Bottom panel for the GodotSharp MCP plugin.
/// Three tabs: Status, Log, Tools.
/// </summary>
[Tool]
public partial class McpPanel : VBoxContainer
{
    private readonly McpPlugin _plugin;

    // Status tab
    private Label?         _statusLabel;
    private Button?        _relayButton;
    private VBoxContainer? _configSection;

    // Log tab
    private RichTextLabel? _logOutput;

    // Tools tab
    private LineEdit?      _toolSearch;
    private Label?         _toolCountLabel;
    private VBoxContainer? _toolList;

    public McpPanel(McpPlugin plugin)
    {
        _plugin = plugin;
    }

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical   = SizeFlags.ExpandFill;

        var tabs = new TabContainer();
        tabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tabs.SizeFlagsVertical   = SizeFlags.ExpandFill;
        AddChild(tabs);

        tabs.AddChild(BuildStatusTab());
        tabs.AddChild(BuildLogTab());
        tabs.AddChild(BuildToolsTab());

        _plugin.OnLog += OnLogEntry;

        Refresh();
    }

    public override void _ExitTree()
    {
        _plugin.OnLog -= OnLogEntry;
    }

    // ------------------------------------------------------------------
    // Tab builders
    // ------------------------------------------------------------------

    private Control BuildStatusTab()
    {
        var margin = new MarginContainer { Name = "Status" };
        margin.AddThemeConstantOverride("margin_top",    8);
        margin.AddThemeConstantOverride("margin_left",   8);
        margin.AddThemeConstantOverride("margin_right",  8);
        margin.AddThemeConstantOverride("margin_bottom", 8);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        // Relay status row
        var statusRow = new HBoxContainer();
        statusRow.AddThemeConstantOverride("separation", 8);
        root.AddChild(statusRow);

        _statusLabel = new Label { Text = "Relay: Stopped" };
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        statusRow.AddChild(_statusLabel);

        _relayButton = new Button { Text = "▶  Start Relay" };
        _relayButton.Pressed += OnRelayButtonPressed;
        statusRow.AddChild(_relayButton);

        root.AddChild(new HSeparator());

        var configLabel = new Label { Text = "AI Client Configuration" };
        root.AddChild(configLabel);

        _configSection = new VBoxContainer();
        _configSection.AddThemeConstantOverride("separation", 4);
        root.AddChild(_configSection);

        return margin;
    }

    private Control BuildLogTab()
    {
        var vbox = new VBoxContainer { Name = "Log" };
        vbox.AddThemeConstantOverride("separation", 4);
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.SizeFlagsVertical   = SizeFlags.ExpandFill;

        // Toolbar row
        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(toolbar);

        var clearBtn = new Button { Text = "Clear" };
        clearBtn.Pressed += () => _logOutput?.Clear();
        toolbar.AddChild(clearBtn);

        // Log output
        _logOutput = new RichTextLabel
        {
            BbcodeEnabled  = true,
            ScrollFollowing = true,
            SelectionEnabled = true,
        };
        _logOutput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _logOutput.SizeFlagsVertical   = SizeFlags.ExpandFill;
        vbox.AddChild(_logOutput);

        return vbox;
    }

    private Control BuildToolsTab()
    {
        var margin = new MarginContainer { Name = "Tools" };
        margin.AddThemeConstantOverride("margin_top",    6);
        margin.AddThemeConstantOverride("margin_left",   8);
        margin.AddThemeConstantOverride("margin_right",  8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        margin.SizeFlagsVertical   = SizeFlags.ExpandFill;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.SizeFlagsVertical   = SizeFlags.ExpandFill;
        margin.AddChild(vbox);

        // Search row
        var searchRow = new HBoxContainer();
        searchRow.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(searchRow);

        _toolSearch = new LineEdit
        {
            PlaceholderText      = "Search tools...",
            ClearButtonEnabled   = true,
        };
        _toolSearch.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _toolSearch.TextChanged += _ => RebuildToolList();
        searchRow.AddChild(_toolSearch);

        _toolCountLabel = new Label();
        searchRow.AddChild(_toolCountLabel);

        // Scrollable tool list
        var scroll = new ScrollContainer();
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.SizeFlagsVertical   = SizeFlags.ExpandFill;
        vbox.AddChild(scroll);

        _toolList = new VBoxContainer();
        _toolList.AddThemeConstantOverride("separation", 2);
        _toolList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_toolList);

        RebuildToolList();

        return margin;
    }

    // ------------------------------------------------------------------
    // Public — called by McpPlugin whenever relay state changes
    // ------------------------------------------------------------------

    public void Refresh()
    {
        if (_statusLabel is null || _relayButton is null || _configSection is null) return;

        if (_plugin.IsRelayRunning)
        {
            _statusLabel.Text = "● Relay: Running";
            _statusLabel.Modulate = new Color(0.4f, 1f, 0.4f);
            _relayButton.Text = "■  Stop Relay";
        }
        else
        {
            _statusLabel.Text = "○ Relay: Stopped";
            _statusLabel.Modulate = Colors.White;
            _relayButton.Text = "▶  Start Relay";
        }

        // Rebuild config buttons
        foreach (var child in _configSection.GetChildren())
            child.QueueFree();

        var relayExe  = ProjectSettings.GlobalizePath(McpPlugin.RelayExeResPath);
        var exeExists = File.Exists(relayExe);

        foreach (var (label, kind, status) in McpConfigSetup.GetConfigStatus())
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var rowLabel = new Label { Text = label };
            rowLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(rowLabel);

            if (!exeExists)
            {
                row.AddChild(new Label { Text = "Relay exe not found — run build.ps1 first." });
            }
            else if (status == McpConfigSetup.ConfigStatus.Registered)
            {
                var check = new Label { Text = "✓ Registered" };
                check.Modulate = new Color(0.4f, 1f, 0.4f);
                row.AddChild(check);
            }
            else
            {
                var btn = new Button { Text = "Add Config" };
                var capturedKind = kind;
                btn.Pressed += () =>
                {
                    McpConfigSetup.Apply(capturedKind, relayExe);
                    Refresh();
                };
                row.AddChild(btn);
            }

            _configSection.AddChild(row);
        }

        if (_configSection.GetChildCount() == 0)
        {
            _configSection.AddChild(new Label
            {
                Text     = "No OpenCode or VS Code config detected on this machine.",
                Modulate = new Color(1, 1, 1, 0.5f)
            });
        }
    }

    // ------------------------------------------------------------------
    // Log tab
    // ------------------------------------------------------------------

    private void OnLogEntry(LogEntry entry)
    {
        if (_logOutput is null) return;

        var (color, prefix) = entry.Level switch
        {
            LogLevel.Success => ("#6ddb6d", ""),
            LogLevel.Warning => ("#f0c040", ""),
            LogLevel.Error   => ("#ff6060", ""),
            LogLevel.Relay   => ("#c8a0f8", "[relay] "),
            LogLevel.Tool    => ("#60c8ff", ""),
            _                => ("#c8c8c8", ""),
        };

        var time = entry.Time.ToString("HH:mm:ss");
        _logOutput.AppendText(
            $"[color=#606060]{time}[/color] " +
            $"[color={color}]{prefix}{entry.Message.Replace("[", "[")}[/color]\n");
    }

    // ------------------------------------------------------------------
    // Tools tab
    // ------------------------------------------------------------------

    private void RebuildToolList()
    {
        if (_toolList is null || _toolCountLabel is null) return;

        foreach (var child in _toolList.GetChildren())
            child.QueueFree();

        var filter = _toolSearch?.Text.Trim() ?? string.Empty;
        var matches = CommandDispatcher.AllTools
            .Where(t => string.IsNullOrEmpty(filter) ||
                        t.Tool.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        t.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        t.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _toolCountLabel.Text = string.IsNullOrEmpty(filter)
            ? $"{matches.Count} tools"
            : $"{matches.Count} / {CommandDispatcher.AllTools.Count}";

        string? lastCategory = null;
        foreach (var (tool, category, description) in matches)
        {
            // Category header (only when not filtering — grouping is noise during search)
            if (string.IsNullOrEmpty(filter) && category != lastCategory)
            {
                var header = new Label
                {
                    Text     = category,
                    Modulate = new Color(1, 1, 1, 0.45f),
                };
                header.AddThemeFontSizeOverride("font_size", 11);
                _toolList.AddChild(header);
                lastCategory = category;
            }

            var entry = new VBoxContainer();
            entry.AddThemeConstantOverride("separation", 1);
            entry.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            // Name row
            var nameRow = new HBoxContainer();
            nameRow.AddThemeConstantOverride("separation", 8);
            entry.AddChild(nameRow);

            var nameLabel = new Label { Text = tool };
            nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            nameRow.AddChild(nameLabel);

            if (!string.IsNullOrEmpty(filter))
            {
                var catLabel = new Label
                {
                    Text     = category,
                    Modulate = new Color(1, 1, 1, 0.45f),
                };
                catLabel.AddThemeFontSizeOverride("font_size", 11);
                nameRow.AddChild(catLabel);
            }

            // Description
            var descLabel = new Label
            {
                Text            = description,
                AutowrapMode    = TextServer.AutowrapMode.WordSmart,
                Modulate        = new Color(1, 1, 1, 0.55f),
            };
            descLabel.AddThemeFontSizeOverride("font_size", 11);
            descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            entry.AddChild(descLabel);

            _toolList.AddChild(entry);
        }
    }

    // ------------------------------------------------------------------

    private void OnRelayButtonPressed()
    {
        if (_plugin.IsRelayRunning)
            _plugin.StopRelay();
        else
            _plugin.LaunchRelay();

        Refresh();
    }
}

