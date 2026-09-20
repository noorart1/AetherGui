namespace AetherGui;

partial class Form1
{
    private Button btnConnect;
    private Button btnInfo;
    private Label lblStatus;
    private Label lblDelay;
    private RichTextBox txtLog;
    private CheckBox chkStartup;
    private CheckBox chkSysProxy;

    private void InitializeComponent()
    {
        btnConnect = new Button { Text = "Auto Connect", Location = new Point(12, 12), Size = new Size(140, 36) };
        lblStatus = new Label { Text = "● Disconnected", ForeColor = Color.Red, AutoSize = true, Location = new Point(166, 14), Font = new Font("Segoe UI", 10F) };
        lblDelay = new Label { Text = "Delay: -", AutoSize = true, Location = new Point(166, 34) };
        chkStartup = new CheckBox { Text = "Connect on startup", AutoSize = true, Location = new Point(420, 14) };
        chkSysProxy = new CheckBox { Text = "Set system proxy", AutoSize = true, Location = new Point(420, 38) };
        btnInfo = new Button { Text = "Proxy info", Location = new Point(12, 56), Size = new Size(140, 26) };
        btnInfo.Click += (_, _) => ShowProxyInfo();
        txtLog = new RichTextBox
        {
            Location = new Point(12, 90), Size = new Size(560, 260), ReadOnly = true,
            BackColor = Color.Black, ForeColor = Color.LimeGreen, Font = new Font("Consolas", 9F),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        btnConnect.Click += btnConnect_Click;

        Text = "Aether GUI";
        ClientSize = new Size(584, 362);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.AddRange(new Control[] { btnConnect, btnInfo, lblStatus, lblDelay, chkStartup, chkSysProxy, txtLog });
    }
}
