namespace AetherGui;

// Status colors only; everything else uses the default Windows look.
static class Palette
{
    public static readonly Color Connected = Color.Green;
    public static readonly Color Connecting = Color.DarkOrange;
    public static readonly Color Disconnected = Color.Firebrick;
}

partial class Form1
{
    private Button btnConnect = null!;
    private Button btnInfo = null!;
    private Button btnCoreSettings = null!;
    private Button btnCoreInput = null!;
    private Button btnUpdates = null!;
    private Label lblStatus = null!;
    private Label lblDelay = null!;
    private Label lblProtocol = null!;
    private ComboBox cboProtocol = null!;
    private Label lblScan = null!;
    private ComboBox cboScan = null!;
    private RichTextBox txtLog = null!;
    private CheckBox chkStartup = null!;
    private CheckBox chkSysProxy = null!;
    private ProgressBar prgConnecting = null!;

    private static Button ActionButton(string text) => new() { Text = text, AutoSize = true, MinimumSize = new Size(110, 30) };

    private void InitializeComponent()
    {
        btnConnect = new Button { Text = "Connect securely", Dock = DockStyle.Top, Height = 36, Margin = new Padding(0, 8, 0, 0) };
        btnConnect.Click += btnConnect_Click;

        btnInfo = ActionButton("Proxy info");
        btnInfo.AccessibleDescription = "Write proxy ports and security recommendations to the activity log";
        btnInfo.Click += (_, _) => ShowProxyInfo();
        btnCoreSettings = ActionButton("Core settings");
        btnCoreSettings.Click += (_, _) => ShowCoreSettings();
        btnCoreInput = ActionButton("Core input");
        btnCoreInput.Enabled = false;
        btnCoreInput.Click += async (_, _) => await SendCoreInputAsync();
        btnUpdates = ActionButton("Check for updates");
        btnUpdates.Click += async (_, _) => await CheckUpdatesAsync(interactive: true);

        lblStatus = new Label { Text = "●  Disconnected", ForeColor = Palette.Disconnected, AutoSize = true, Font = new Font(Font.FontFamily, 12F, FontStyle.Bold), AccessibleName = "Connection status: Disconnected" };
        lblDelay = new Label { Text = "Delay: -", AutoSize = true, Margin = new Padding(12, 4, 0, 0) };
        prgConnecting = new ProgressBar
        {
            Style = SystemInformation.UIEffectsEnabled ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous,
            Value = 100, MarqueeAnimationSpeed = 30, Dock = DockStyle.Top, Height = 6, Visible = false,
            Margin = new Padding(0, 4, 0, 8)
        };

        lblProtocol = new Label { Text = "Protocol", AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
        cboProtocol = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, DropDownWidth = 360, AccessibleName = "Connection protocol" };
        lblScan = new Label { Text = "Server search", AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
        cboScan = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, DropDownWidth = 360, AccessibleName = "Server search mode" };

        chkStartup = new CheckBox { Text = "Connect on startup", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        chkSysProxy = new CheckBox { Text = "Set Windows system proxy", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };

        var status = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
        status.Controls.AddRange([lblStatus, lblDelay]);
        var helper = new Label
        {
            Text = "Choose a route, then connect. Auto switches fast routes when a connection drops.",
            Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(300, 0), Margin = new Padding(0, 0, 0, 4)
        };

        var controlLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, Margin = Padding.Empty };
        for (var i = 0; i < 10; i++) controlLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        controlLayout.Controls.Add(status, 0, 0);
        controlLayout.Controls.Add(prgConnecting, 0, 1);
        controlLayout.Controls.Add(helper, 0, 2);
        controlLayout.Controls.Add(lblProtocol, 0, 3);
        controlLayout.Controls.Add(cboProtocol, 0, 4);
        controlLayout.Controls.Add(lblScan, 0, 5);
        controlLayout.Controls.Add(cboScan, 0, 6);
        controlLayout.Controls.Add(chkStartup, 0, 7);
        controlLayout.Controls.Add(chkSysProxy, 0, 8);
        controlLayout.Controls.Add(btnConnect, 0, 9);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 8, 0, 0) };
        actions.Controls.AddRange([btnCoreSettings, btnCoreInput, btnInfo, btnUpdates]);

        var leftStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        leftStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftStack.Controls.Add(controlLayout, 0, 0);
        leftStack.Controls.Add(actions, 0, 1);
        var controlBox = new GroupBox { Text = "Connection", Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(0, 0, 8, 0) };
        controlBox.Controls.Add(leftStack);

        txtLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9.5F), DetectUrls = true, AccessibleName = "Connection activity log" };
        var clear = new Button { Text = "Clear", AutoSize = true, Dock = DockStyle.Bottom, AccessibleDescription = "Clear the activity log" };
        clear.Click += (_, _) => txtLog.Clear();
        var logBox = new GroupBox { Text = "Activity", Dock = DockStyle.Fill, Padding = new Padding(8), Margin = Padding.Empty };
        logBox.Controls.Add(txtLog);
        logBox.Controls.Add(clear);

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 1 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.Controls.Add(controlBox, 0, 0);
        shell.Controls.Add(logBox, 1, 0);

        Text = "Aether — Private network client";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(900, 520);
        MinimumSize = new Size(700, 480);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Controls.Add(shell);
    }
}
