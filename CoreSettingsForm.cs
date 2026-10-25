namespace AetherGui;

sealed class CoreSettingsForm : Form
{
    public CoreOptions Options { get; private set; }
    private readonly TextBox _arguments = Editor();
    private readonly TextBox _environment = Editor();
    private readonly NumericUpDown _timeout = new() { Minimum = 30, Maximum = 3600, Width = 120 };
    private readonly CancellationTokenSource _helpCancellation = new(TimeSpan.FromSeconds(10));

    private static TextBox Editor() => new()
    {
        Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10)
    };

    public CoreSettingsForm(CoreOptions options)
    {
        Options = options;
        Text = "Core settings";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(800, 650);
        MinimumSize = new Size(520, 460);
        StartPosition = FormStartPosition.CenterParent;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var settings = new TabPage("Settings") { Padding = new Padding(12) };
        var helpTab = new TabPage("Bundled core help");
        var help = Editor(); help.ReadOnly = true; help.Text = "Loading...";
        helpTab.Controls.Add(help);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var warning = new Label { AutoSize = true, Dock = DockStyle.Fill, Text =
            "Expert options apply to every protocol. Use a manual protocol for protocol-specific flags. " +
            "Ports/protocols are app-managed. Settings are stored in LocalAppData, NOT encrypted. " +
            "Direct routes bypass the tunnel; Gateway/debug logs can expose browsing or secrets. " +
            "SO_MARK is not a Windows feature. File options require existing accessible files." };
        layout.Controls.Add(warning, 0, 0);
        layout.Controls.Add(new Label { Text = "Arguments (JSON array): [\"--scan\", \"balanced\", \"--stats\"]", AutoSize = true }, 0, 1);
        _arguments.Text = options.ArgumentsJson;
        layout.Controls.Add(_arguments, 0, 2);
        layout.Controls.Add(new Label { Text = "Environment (JSON object): {\"AETHER_ROUTE_SNIFF_MS\":\"600\"}", AutoSize = true }, 0, 3);
        _environment.Text = options.EnvironmentJson;
        layout.Controls.Add(_environment, 0, 4);
        var timeRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        timeRow.Controls.Add(new Label { Text = "Advanced startup timeout (seconds)", AutoSize = true });
        _timeout.Value = options.StartupSeconds; timeRow.Controls.Add(_timeout);
        layout.Controls.Add(timeRow, 0, 5);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var save = new Button { Text = "Save", AutoSize = true };
        var reset = new Button { Text = "Reset fields", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => SaveOptions();
        reset.Click += (_, _) => { _arguments.Text = "[]"; _environment.Text = "{}"; _timeout.Value = 300; };
        buttons.Controls.AddRange([save, reset, cancel]);
        layout.Controls.Add(buttons, 0, 6);
        settings.Controls.Add(layout); tabs.TabPages.AddRange([settings, helpTab]); Controls.Add(tabs);
        CancelButton = cancel;
        Shown += async (_, _) =>
        {
            try
            {
                var text = await Aether.ReadHelpAsync(_helpCancellation.Token);
                if (!IsDisposed) help.Text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            }
            catch { if (!IsDisposed) help.Text = "Could not read aether.exe --help. Check the core executable beside the GUI."; }
        };
        FormClosed += (_, _) => _helpCancellation.Cancel();
    }

    private void SaveOptions()
    {
        try
        {
            var options = CoreOptions.Parse(_arguments.Text, _environment.Text, (int)_timeout.Value);
            options.Save(); Options = options; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
        { MessageBox.Show(this, "Invalid JSON/settings. Use string arrays and string environment values; do not override managed protocols or ports.", Text); }
        catch { MessageBox.Show(this, "Could not save settings to LocalAppData.", Text); }
    }
}
