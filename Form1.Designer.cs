namespace AetherGui;

// Windows 98 look: MS Sans Serif, 3D gray surfaces, navy accents, white sunken log, status bar.
static class Palette
{
    public static readonly Color Background = ColorTranslator.FromHtml("#C0C0C0");
    public static readonly Color LogBackground = Color.White;
    public static readonly Color MutedText = ColorTranslator.FromHtml("#404040");
    public static readonly Color Accent = ColorTranslator.FromHtml("#000080");
    public static readonly Color AccentPressed = ColorTranslator.FromHtml("#800080");
    public static readonly Color Connected = ColorTranslator.FromHtml("#008000");
    public static readonly Color Connecting = ColorTranslator.FromHtml("#806000");
    public static readonly Color Disconnected = ColorTranslator.FromHtml("#800000");
}

partial class Form1
{
    private Button btnConnect = null!;
    private ToolStripMenuItem btnInfo = null!;
    private ToolStripMenuItem btnCoreSettings = null!;
    private ToolStripMenuItem btnCoreInput = null!;
    private ToolStripMenuItem btnUpdates = null!;
    private Label lblStatus = null!;
    private ToolStripStatusLabel lblDelay = null!;
    private Label lblProtocol = null!;
    private ComboBox cboProtocol = null!;
    private Label lblScan = null!;
    private ComboBox cboScan = null!;
    private RichTextBox txtLog = null!;
    private ToolStripMenuItem chkStartup = null!;
    private CheckBox chkSysProxy = null!;
    private ToolStripMenuItem chkPsiphon = null!;
    private ToolStripMenuItem chkShowActivity = null!;
    private ProgressBar prgConnecting = null!;
    private Panel activityCard = null!;
    private TableLayoutPanel shell = null!;

    // Raised 3D push button; the primary one is bold, like a classic default button.
    private static Button ClassicButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, FlatStyle = FlatStyle.Standard, UseVisualStyleBackColor = false, BackColor = Palette.Background,
            AutoSize = true, MinimumSize = new Size(90, 23), Margin = new Padding(0, 0, 6, 6)
        };
        if (primary) button.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        return button;
    }

    // Gray panel with an etched 3D frame, named so tests can find it.
    private static Panel Card(string name)
    {
        var card = new Panel { Name = name, Dock = DockStyle.Fill, BackColor = Palette.Background, Padding = new Padding(8) };
        card.Paint += (_, e) => ControlPaint.DrawBorder3D(e.Graphics, card.ClientRectangle, Border3DStyle.Etched);
        card.Resize += (_, _) => card.Invalidate();
        return card;
    }

    private void InitializeComponent()
    {
        Font = new Font("Microsoft Sans Serif", 8.25F);
        var bold = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);
        var header = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Bold);

        btnConnect = ClassicButton("Connect securely", primary: true);
        btnConnect.AutoSize = false;
        btnConnect.Dock = DockStyle.Top;
        btnConnect.Height = 30;
                btnConnect.Margin = new Padding(0, 6, 0, 6);
        btnConnect.Click += btnConnect_Click;

        // Rarely used actions and options live in a classic menu bar so the window shows only what connecting needs.
        btnInfo = new ToolStripMenuItem("&Proxy info") { AccessibleDescription = "Write proxy ports and security recommendations to the activity log" };
        btnInfo.Click += (_, _) => ShowProxyInfo();
        btnCoreSettings = new ToolStripMenuItem("Core &settings...");
        btnCoreSettings.Click += (_, _) => ShowCoreSettings();
        btnCoreInput = new ToolStripMenuItem("Core &input...") { Enabled = false };
        btnCoreInput.Click += async (_, _) => await SendCoreInputAsync();
        btnUpdates = new ToolStripMenuItem("Check for &updates...") { AccessibleDescription = "Check for a new AetherGui release and a newer core" };
        btnUpdates.Click += async (_, _) => await CheckUpdatesAsync(interactive: true);

        lblStatus = new Label { Text = "●  Disconnected", ForeColor = Palette.Disconnected, AutoSize = true, Font = new Font("Microsoft Sans Serif", 10F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 0), AccessibleName = "Connection status: Disconnected" };
        // Windows 98 style status bar along the bottom, with a sunken pane and a sizing grip.
        lblDelay = new ToolStripStatusLabel("Delay: -") { Spring = true, TextAlign = ContentAlignment.MiddleLeft, BorderSides = ToolStripStatusLabelBorderSides.All, BorderStyle = Border3DStyle.SunkenOuter };
        var statusBar = new StatusStrip { RenderMode = ToolStripRenderMode.System, BackColor = Palette.Background };
        statusBar.Items.Add(lblDelay);
        // Tall enough to show the classic chunky blocks.
        prgConnecting = new ProgressBar
        {
            Style = SystemInformation.UIEffectsEnabled ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous,
            Value = 100, MarqueeAnimationSpeed = 30, Dock = DockStyle.Top, Height = 14, Visible = false,
            Margin = new Padding(0, 4, 0, 0)
        };

        lblProtocol = new Label { Text = "Protocol", Font = bold, AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        cboProtocol = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, DropDownWidth = 360, Margin = new Padding(0, 0, 0, 4), AccessibleName = "Connection protocol" };
        lblScan = new Label { Text = "Server search", Font = bold, AutoSize = true, Margin = new Padding(0, 6, 0, 2) };
        cboScan = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, DropDownWidth = 360, Margin = new Padding(0, 0, 0, 4), AccessibleName = "Server search mode" };

        chkSysProxy = new CheckBox { Text = "Set Windows system proxy", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        chkStartup = new ToolStripMenuItem("&Connect on startup") { CheckOnClick = true };
        chkPsiphon = new ToolStripMenuItem("Open &Psiphon after connecting") { CheckOnClick = true };
        chkShowActivity = new ToolStripMenuItem("Show &activity log") { CheckOnClick = true };

        var menu = new MenuStrip { RenderMode = ToolStripRenderMode.System, BackColor = Palette.Background, Dock = DockStyle.Top };
        menu.Items.AddRange([
            new ToolStripMenuItem("&Options", null, chkStartup, chkPsiphon, chkShowActivity),
            new ToolStripMenuItem("&Tools", null, btnCoreSettings, btnCoreInput, btnInfo),
            new ToolStripMenuItem("&Help", null, btnUpdates)]);

        var status = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = Padding.Empty };
        status.Controls.Add(lblStatus);

        var controlLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 8, Margin = Padding.Empty };
        controlLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // fit the panel, never the content
        for (var i = 0; i < 8; i++) controlLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // Connect sits right under the status so it stays in view; the rest scrolls if large text needs more room.
        controlLayout.Controls.Add(status, 0, 0);
        controlLayout.Controls.Add(prgConnecting, 0, 1);
        controlLayout.Controls.Add(btnConnect, 0, 2);
        controlLayout.Controls.Add(lblProtocol, 0, 3);
        controlLayout.Controls.Add(cboProtocol, 0, 4);
        controlLayout.Controls.Add(lblScan, 0, 5);
        controlLayout.Controls.Add(cboScan, 0, 6);
        controlLayout.Controls.Add(chkSysProxy, 0, 7);

        var controlScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty }; // scrollbar only when needed
        controlScroll.Controls.Add(controlLayout);

        var connectionCard = Card("connectionPanel");
        connectionCard.Margin = new Padding(0, 0, 4, 0);
        connectionCard.Controls.Add(controlScroll);

        txtLog = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.Fixed3D, BackColor = Palette.LogBackground,
            Font = new Font("Courier New", 8.25F), DetectUrls = true, AccessibleName = "Connection activity log"
        };
        // Sunken white log, as in classic Windows.
        var logFrame = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty };
        logFrame.Controls.Add(txtLog);

        var clear = ClassicButton("&Clear");
        clear.Anchor = AnchorStyles.Right;
        clear.MinimumSize = new Size(75, 23);
        clear.Margin = Padding.Empty;
        clear.AccessibleDescription = "Clear the activity log";
        clear.Click += (_, _) => txtLog.Clear();
        var logHeader = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 0, 0, 6) };
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        logHeader.Controls.Add(new Label { Text = "Activity", Font = header, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty }, 0, 0);
        logHeader.Controls.Add(clear, 1, 0);

        activityCard = Card("activityPanel");
        activityCard.Margin = new Padding(4, 0, 0, 0);
        activityCard.Controls.Add(logFrame);
        activityCard.Controls.Add(logHeader);

        shell = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 2, RowCount = 1, BackColor = Palette.Background };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); // a share, not pixels: follows display scaling
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        shell.Controls.Add(connectionCard, 0, 0);
        shell.Controls.Add(activityCard, 1, 0);

        Text = "Aether — Private network client";
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Palette.Background;
        ClientSize = new Size(820, 322);
        MinimumSize = new Size(640, 342);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        // Docked controls lay out last-added first: the menu takes the top, then the shell fills the rest.
        Controls.Add(shell);
        Controls.Add(statusBar);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }
}
