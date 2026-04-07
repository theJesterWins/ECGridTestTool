using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ECGridOsSafeWorkbench;

internal sealed class MainForm : Form
{
    private readonly EcGridOsClient _client;
    private readonly string _settingsPath;
    private readonly ToolTip _toolTip = new();

    private readonly TextBox txtServiceUrl = new();
    private readonly TextBox txtSessionId = new();
    private readonly TextBox txtUserId = new();
    private readonly TextBox txtCurrentNetworkId = new();
    private readonly TextBox txtCurrentMailboxId = new();
    private readonly TextBox txtMailboxFilter = new();
    private readonly TextBox txtSendNetworkId = new();
    private readonly TextBox txtSendMailboxId = new();
    private readonly TextBox txtFileName = new();
    private readonly TextBox txtRestoreParcelId = new();

    private readonly CheckBox chkExplicitSendTarget = new();
    private readonly CheckBox chkRememberSessionId = new();
    private readonly CheckBox chkShowSessionId = new();
    private readonly CheckBox chkRequireDownloadConfirmation = new();
    private readonly CheckBox chkBlockCrossMailboxDownloads = new();

    private readonly RichTextBox txtPayload = new();
    private readonly RichTextBox txtLog = new();
    private readonly RichTextBox txtResponse = new();

    private readonly DataGridView dgvInbox = new();
    private readonly DataGridView dgvMailboxes = new();
    private readonly DataGridView dgvErrors = new();

    private readonly Button btnSaveSettings = new();
    private readonly Button btnWhoAmI = new();
    private readonly Button btnListMailboxes = new();
    private readonly Button btnCheckInbox = new();
    private readonly Button btnCheckOutboxErrors = new();
    private readonly Button btnLoadFile = new();
    private readonly Button btnSend = new();
    private readonly Button btnPreviewDownload = new();
    private readonly Button btnSaveDownloaded = new();
    private readonly Button btnConfirmDownload = new();
    private readonly Button btnRestoreToInbox = new();

    private readonly TabControl bottomTabs = new();

    private SessionInfo? _session;
    private DownloadedFileInfo? _lastDownloaded;
    private byte[]? _currentPayloadBytes;
    private string _currentPayloadTextSnapshot = string.Empty;
    private long? _lastPreviewedParcelId;
    private int? _lastInboxNetworkId;
    private int? _lastInboxMailboxId;
    private long? _lastConfirmedParcelId;
    private string _lastConfirmedFileName = string.Empty;

    public MainForm()
    {
        Text = "ECGridOS Safe Workbench";
        Width = 1600;
        Height = 980;
        MinimumSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "ecgridapp.ico");
        if (File.Exists(iconPath))
        {
            try { Icon = new Icon(iconPath); } catch { }
        }

        _client = new EcGridOsClient(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        });

        var settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECGridOsSafeWorkbench");
        _settingsPath = Path.Combine(settingsDir, "ecgrid_os_safe_workbench_settings.json");

        BuildUi();
        ConfigureTips();
        LoadSettings();

        Log("Loaded settings from: " + _settingsPath);
        Log("Run WhoAmI first. Inbox checks and safe downloads are anchored to the current mailbox returned by ECGrid.");
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
        Controls.Add(root);

        var top = BuildTopArea();
        root.Controls.Add(top, 0, 0);

        var payloadGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "EDI Payload / Downloaded File Preview"
        };
        ConfigureTextWindow(txtPayload, readOnly: false);
        payloadGroup.Controls.Add(txtPayload);
        root.Controls.Add(payloadGroup, 0, 1);

        bottomTabs.Dock = DockStyle.Fill;
        bottomTabs.TabPages.Add(MakeTextTab("Activity Log", txtLog));
        bottomTabs.TabPages.Add(MakeTextTab("Last Response", txtResponse));
        bottomTabs.TabPages.Add(MakeGridTab("Inbox", dgvInbox));
        bottomTabs.TabPages.Add(MakeGridTab("Mailboxes", dgvMailboxes));
        bottomTabs.TabPages.Add(MakeGridTab("Outbox Errors", dgvErrors));
        root.Controls.Add(bottomTabs, 0, 2);

        ConfigureTextWindow(txtLog, readOnly: true);
        ConfigureTextWindow(txtResponse, readOnly: true);
        ConfigureGrid(dgvInbox);
        ConfigureGrid(dgvMailboxes);
        ConfigureGrid(dgvErrors);

        dgvInbox.SelectionChanged += (_, _) => ShowSelectedInboxParcelSummary();
        dgvInbox.CellDoubleClick += async (_, _) => await RunBusyAsync(PreviewAndDownloadSelectedInboxAsync);
    }

    private Control BuildTopArea()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8)
        };
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        area.Controls.Add(BuildHeaderPanel(), 0, 0);
        area.Controls.Add(BuildControlPanel(), 0, 1);

        return area;
    }

    private Control BuildHeaderPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var pic = new PictureBox
        {
            Width = 72,
            Height = 72,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 0, 10, 0)
        };
        var logoPath = Path.Combine(AppContext.BaseDirectory, "ecgridapp.png");
        if (File.Exists(logoPath))
        {
            try { pic.Image = Image.FromFile(logoPath); } catch { }
        }

        var labels = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3
        };

        var lblTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            Text = "ECGridOS Safe Workbench"
        };

        var lblSubtitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Text = "Safer send and receive testing for your ECGrid mailbox. Previewing inbox data requires confirmation first."
        };

        var lblSafety = new Label
        {
            AutoSize = true,
            ForeColor = Color.DarkGreen,
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            Text = "Safety defaults: current-mailbox inbox only, confirm before preview, and block cross-mailbox downloads."
        };

        labels.Controls.Add(lblTitle, 0, 0);
        labels.Controls.Add(lblSubtitle, 0, 1);
        labels.Controls.Add(lblSafety, 0, 2);

        panel.Controls.Add(pic, 0, 0);
        panel.Controls.Add(labels, 1, 0);

        return panel;
    }

    private Control BuildControlPanel()
    {
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 10,
            RowCount = 7
        };

        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        for (var i = 0; i < 7; i++)
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        txtServiceUrl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtSessionId.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtSessionId.UseSystemPasswordChar = true;
        txtFileName.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtMailboxFilter.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtSendNetworkId.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtSendMailboxId.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtRestoreParcelId.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        txtUserId.ReadOnly = true;
        txtCurrentNetworkId.ReadOnly = true;
        txtCurrentMailboxId.ReadOnly = true;

        btnSaveSettings.Text = "Save Settings";
        btnSaveSettings.AutoSize = true;
        btnSaveSettings.Click += (_, _) => RunAction(SaveSettings);

        btnWhoAmI.Text = "WhoAmI";
        btnWhoAmI.AutoSize = true;
        btnWhoAmI.Click += async (_, _) => await RunBusyAsync(WhoAmIAsync);

        btnListMailboxes.Text = "List Mailboxes";
        btnListMailboxes.AutoSize = true;
        btnListMailboxes.Click += async (_, _) => await RunBusyAsync(ListMailboxesAsync);

        btnCheckInbox.Text = "Check Inbox";
        btnCheckInbox.AutoSize = true;
        btnCheckInbox.Click += async (_, _) => await RunBusyAsync(CheckInboxAsync);

        btnCheckOutboxErrors.Text = "Check Outbox Errors";
        btnCheckOutboxErrors.AutoSize = true;
        btnCheckOutboxErrors.Click += async (_, _) => await RunBusyAsync(CheckOutboxErrorsAsync);

        btnLoadFile.Text = "Load File";
        btnLoadFile.AutoSize = true;
        btnLoadFile.Click += (_, _) => RunAction(LoadPayloadFile);

        btnSend.Text = "Send";
        btnSend.AutoSize = true;
        btnSend.Click += async (_, _) => await RunBusyAsync(SendAsync);

        btnPreviewDownload.Text = "Preview / Download Selected";
        btnPreviewDownload.AutoSize = true;
        btnPreviewDownload.Click += async (_, _) => await RunBusyAsync(PreviewAndDownloadSelectedInboxAsync);

        btnSaveDownloaded.Text = "Save Downloaded";
        btnSaveDownloaded.AutoSize = true;
        btnSaveDownloaded.Click += (_, _) => RunAction(SaveDownloadedToDisk);

        btnConfirmDownload.Text = "Confirm Download";
        btnConfirmDownload.AutoSize = true;
        btnConfirmDownload.Click += async (_, _) => await RunBusyAsync(ConfirmSelectedDownloadAsync);

        btnRestoreToInbox.Text = "Restore to Inbox";
        btnRestoreToInbox.AutoSize = true;
        btnRestoreToInbox.Click += async (_, _) => await RunBusyAsync(RestoreParcelToInboxAsync);

        chkExplicitSendTarget.Text = "Send to explicit Network/Mailbox outbox";
        chkExplicitSendTarget.AutoSize = true;

        chkRememberSessionId.Text = "Remember SessionID locally";
        chkRememberSessionId.AutoSize = true;

        chkShowSessionId.Text = "Show";
        chkShowSessionId.AutoSize = true;
        chkShowSessionId.CheckedChanged += (_, _) => txtSessionId.UseSystemPasswordChar = !chkShowSessionId.Checked;

        chkRequireDownloadConfirmation.Text = "Require confirmation before preview/download";
        chkRequireDownloadConfirmation.AutoSize = true;
        chkRequireDownloadConfirmation.Checked = true;

        chkBlockCrossMailboxDownloads.Text = "Block cross-mailbox download";
        chkBlockCrossMailboxDownloads.AutoSize = true;
        chkBlockCrossMailboxDownloads.Checked = true;

        top.Controls.Add(MakeLabel("Service URL"), 0, 0);
        top.Controls.Add(txtServiceUrl, 1, 0);
        top.SetColumnSpan(txtServiceUrl, 7);
        top.Controls.Add(btnSaveSettings, 8, 0);
        top.Controls.Add(btnWhoAmI, 9, 0);

        top.Controls.Add(MakeLabel("SessionID / API Key"), 0, 1);
        top.Controls.Add(txtSessionId, 1, 1);
        top.SetColumnSpan(txtSessionId, 5);
        top.Controls.Add(chkRememberSessionId, 6, 1);
        top.Controls.Add(chkShowSessionId, 7, 1);
        top.Controls.Add(btnCheckInbox, 8, 1);
        top.Controls.Add(btnCheckOutboxErrors, 9, 1);

        top.Controls.Add(MakeLabel("User ID"), 0, 2);
        top.Controls.Add(txtUserId, 1, 2);
        top.Controls.Add(MakeLabel("Current Network ID"), 2, 2);
        top.Controls.Add(txtCurrentNetworkId, 3, 2);
        top.Controls.Add(MakeLabel("Current Mailbox ID"), 4, 2);
        top.Controls.Add(txtCurrentMailboxId, 5, 2);
        top.Controls.Add(btnPreviewDownload, 8, 2);
        top.Controls.Add(btnConfirmDownload, 9, 2);

        top.Controls.Add(chkExplicitSendTarget, 1, 3);
        top.SetColumnSpan(chkExplicitSendTarget, 2);
        top.Controls.Add(MakeLabel("Send Network ID"), 4, 3);
        top.Controls.Add(txtSendNetworkId, 5, 3);
        top.Controls.Add(MakeLabel("Send Mailbox ID"), 6, 3);
        top.Controls.Add(txtSendMailboxId, 7, 3);
        top.Controls.Add(btnSaveDownloaded, 8, 3);
        top.Controls.Add(btnSend, 9, 3);

        top.Controls.Add(MakeLabel("Mailbox filter"), 0, 4);
        top.Controls.Add(txtMailboxFilter, 1, 4);
        top.SetColumnSpan(txtMailboxFilter, 2);
        top.Controls.Add(btnListMailboxes, 3, 4);
        top.Controls.Add(MakeLabel("File Name"), 4, 4);
        top.Controls.Add(txtFileName, 5, 4);
        top.SetColumnSpan(txtFileName, 3);
        top.Controls.Add(btnLoadFile, 8, 4);

        top.Controls.Add(MakeLabel("Restore ParcelID"), 4, 5);
        top.Controls.Add(txtRestoreParcelId, 5, 5);
        top.SetColumnSpan(txtRestoreParcelId, 3);
        top.Controls.Add(btnRestoreToInbox, 9, 5);

        top.Controls.Add(chkRequireDownloadConfirmation, 1, 6);
        top.SetColumnSpan(chkRequireDownloadConfirmation, 3);
        top.Controls.Add(chkBlockCrossMailboxDownloads, 4, 6);
        top.SetColumnSpan(chkBlockCrossMailboxDownloads, 3);

        return top;
    }

    private void ConfigureTips()
    {
        _toolTip.SetToolTip(chkExplicitSendTarget, "Unchecked: send through your current session mailbox outbox. Checked: upload this one file into the specific NetworkID/MailboxID outbox below.");
        _toolTip.SetToolTip(chkRememberSessionId, "Leave unchecked to avoid saving the SessionID/API key to disk. You can also set ECGRID_SESSION_ID in your environment.");
        _toolTip.SetToolTip(chkShowSessionId, "Temporarily reveals the SessionID/API key in the textbox.");
        _toolTip.SetToolTip(txtSendNetworkId, "Used only when the explicit send checkbox is checked.");
        _toolTip.SetToolTip(txtSendMailboxId, "Used only when the explicit send checkbox is checked.");
        _toolTip.SetToolTip(btnPreviewDownload, "Shows a warning with parcel metadata before any inbox data is downloaded.");
        _toolTip.SetToolTip(btnConfirmDownload, "Confirms the selected parcel after you intentionally downloaded and reviewed it.");
        _toolTip.SetToolTip(txtRestoreParcelId, "Auto-filled from the last confirmed parcel. You can also paste a ParcelID from the log to restore it to the inbox.");
        _toolTip.SetToolTip(btnRestoreToInbox, "Calls ParcelDownloadReset to restore a previously confirmed parcel back into the inbox for controlled retesting.");
        _toolTip.SetToolTip(chkBlockCrossMailboxDownloads, "Leave this checked to prevent preview/download when the parcel does not appear to target the current mailbox context.");
    }

    private static Label MakeLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 7, 8, 0)
        };
    }

    private static void ConfigureTextWindow(RichTextBox box, bool readOnly)
    {
        box.Dock = DockStyle.Fill;
        box.Font = new Font("Consolas", 10f);
        box.ReadOnly = readOnly;
        box.WordWrap = false;
        box.ScrollBars = RichTextBoxScrollBars.Both;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AutoGenerateColumns = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
    }

    private static TabPage MakeTextTab(string title, Control child)
    {
        var page = new TabPage(title);
        page.Controls.Add(child);
        return page;
    }

    private static TabPage MakeGridTab(string title, Control child)
    {
        var page = new TabPage(title);
        page.Controls.Add(child);
        return page;
    }

    private void LoadSettings()
    {
        var settings = SettingsStore.Load(_settingsPath);
        var sessionFromEnvironment = Environment.GetEnvironmentVariable("ECGRID_SESSION_ID") ?? string.Empty;

        txtServiceUrl.Text = settings.ServiceUrl;
        txtSessionId.Text = string.IsNullOrWhiteSpace(settings.SessionId) ? sessionFromEnvironment : settings.SessionId;
        chkRememberSessionId.Checked = settings.RememberSessionId;
        chkShowSessionId.Checked = false;
        txtMailboxFilter.Text = settings.MailboxFilter;
        txtFileName.Text = string.IsNullOrWhiteSpace(settings.FileName) ? "test.edi" : settings.FileName;
        chkExplicitSendTarget.Checked = settings.UseExplicitSendTarget;
        txtSendNetworkId.Text = settings.SendNetworkId;
        txtSendMailboxId.Text = settings.SendMailboxId;
        chkRequireDownloadConfirmation.Checked = settings.RequireDownloadConfirmation;
        chkBlockCrossMailboxDownloads.Checked = settings.BlockCrossMailboxDownloads;
    }

    private void SaveSettings()
    {
        var settings = new AppSettings
        {
            ServiceUrl = txtServiceUrl.Text.Trim(),
            SessionId = chkRememberSessionId.Checked ? txtSessionId.Text.Trim() : string.Empty,
            RememberSessionId = chkRememberSessionId.Checked,
            MailboxFilter = txtMailboxFilter.Text.Trim(),
            FileName = txtFileName.Text.Trim(),
            UseExplicitSendTarget = chkExplicitSendTarget.Checked,
            SendNetworkId = txtSendNetworkId.Text.Trim(),
            SendMailboxId = txtSendMailboxId.Text.Trim(),
            RequireDownloadConfirmation = chkRequireDownloadConfirmation.Checked,
            BlockCrossMailboxDownloads = chkBlockCrossMailboxDownloads.Checked
        };

        SettingsStore.Save(_settingsPath, settings);
        Log("Settings saved to: " + _settingsPath);
    }

    private void RunAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        try
        {
            UseWaitCursor = true;
            ToggleButtons(false);
            await action();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            ToggleButtons(true);
            UseWaitCursor = false;
        }
    }

    private void ToggleButtons(bool enabled)
    {
        foreach (var button in new[]
                 {
                     btnSaveSettings,
                     btnWhoAmI,
                     btnListMailboxes,
                     btnCheckInbox,
                     btnCheckOutboxErrors,
                     btnLoadFile,
                     btnSend,
                     btnPreviewDownload,
                     btnSaveDownloaded,
                     btnConfirmDownload,
                     btnRestoreToInbox
                 })
        {
            button.Enabled = enabled;
        }
    }

    private void ShowError(Exception ex)
    {
        var message = SanitizeSensitiveText(ex.Message);
        Log("ERROR: " + message.Replace(Environment.NewLine, " "));
        SetResponse(SanitizeSensitiveText(ex.ToString()));
        bottomTabs.SelectedIndex = 1;
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private async Task WhoAmIAsync()
    {
        RequireServiceAndSession();

        var info = await _client.WhoAmIAsync(
            txtServiceUrl.Text.Trim(),
            txtSessionId.Text.Trim(),
            CancellationToken.None);

        _session = info;
        _lastDownloaded = null;
        _lastPreviewedParcelId = null;
        _lastConfirmedParcelId = null;
        _lastConfirmedFileName = string.Empty;
        txtRestoreParcelId.Text = string.Empty;
        txtUserId.Text = info.UserID.ToString();
        txtCurrentNetworkId.Text = info.NetworkID.ToString();
        txtCurrentMailboxId.Text = info.MailboxID.ToString();

        var sb = new StringBuilder();
        sb.AppendLine("WhoAmI returned:");
        sb.AppendLine("  ECGridOSVersion: " + info.ECGridOSVersion);
        sb.AppendLine("  SessionID: " + MaskSecret(info.SessionID));
        sb.AppendLine("  UserID: " + info.UserID);
        sb.AppendLine("  LoginName: " + info.LoginName);
        sb.AppendLine("  FirstName: " + info.FirstName);
        sb.AppendLine("  LastName: " + info.LastName);
        sb.AppendLine("  Company: " + info.Company);
        sb.AppendLine("  EMail: " + info.EMail);
        sb.AppendLine("  AuthLevel: " + info.AuthLevel);
        sb.AppendLine("  NetworkID: " + info.NetworkID);
        sb.AppendLine("  MailboxID: " + info.MailboxID);
        sb.AppendLine("  TimeOut: " + info.TimeOut);
        sb.AppendLine("  OpenSessions: " + info.OpenSessions);
        sb.AppendLine("  ip: " + info.Ip);

        SetResponse(sb.ToString());
        foreach (var line in sb.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None))
        {
            if (!string.IsNullOrWhiteSpace(line))
                Log(line);
        }

        if (info.AuthLevel.IndexOf("MailboxUser", StringComparison.OrdinalIgnoreCase) >= 0)
            Log("Note: mailbox-level sessions may be denied for broader network calls such as MailboxListEx.");

        SaveSettings();
    }

    private async Task ListMailboxesAsync()
    {
        RequireServiceAndSession();
        var networkId = RequireCurrentNetworkId();

        var mailboxes = await _client.MailboxListExAsync(
            txtServiceUrl.Text.Trim(),
            txtSessionId.Text.Trim(),
            networkId,
            txtMailboxFilter.Text.Trim(),
            CancellationToken.None);

        dgvMailboxes.DataSource = null;
        dgvMailboxes.DataSource = mailboxes;

        Log("MailboxListEx returned " + mailboxes.Count + " mailbox record(s) for network " + networkId + ".");
        SetResponse("Mailbox list refreshed. Open the Mailboxes tab to inspect it.");
        bottomTabs.SelectedIndex = 3;
    }

    private async Task CheckInboxAsync()
    {
        RequireServiceAndSession();
        var (networkId, mailboxId) = RequireCurrentMailboxContext();
        _lastInboxNetworkId = networkId;
        _lastInboxMailboxId = mailboxId;
        _lastDownloaded = null;
        _lastPreviewedParcelId = null;

        var inbox = await _client.ParcelInBoxExShortAsync(
            txtServiceUrl.Text.Trim(),
            txtSessionId.Text.Trim(),
            networkId,
            mailboxId,
            CancellationToken.None);

        dgvInbox.DataSource = null;
        dgvInbox.DataSource = inbox;

        Log("ParcelInBoxExShort returned " + inbox.Count + " inbox parcel(s) for current mailbox " + networkId + "/" + mailboxId + ".");
        SetResponse("Inbox refreshed for the current mailbox only. Select a row to inspect metadata. Preview/download still requires confirmation.");
        bottomTabs.SelectedIndex = 2;
    }

    private async Task CheckOutboxErrorsAsync()
    {
        RequireServiceAndSession();
        var (networkId, mailboxId) = RequireCurrentMailboxContext();

        var errors = await _client.ParcelOutBoxErrorExAsync(
            txtServiceUrl.Text.Trim(),
            txtSessionId.Text.Trim(),
            networkId,
            mailboxId,
            DateTime.UtcNow.AddDays(-14),
            DateTime.UtcNow,
            CancellationToken.None);

        dgvErrors.DataSource = null;
        dgvErrors.DataSource = errors;

        Log("ParcelOutBoxErrorEx returned " + errors.Count + " error parcel(s) for " + networkId + "/" + mailboxId + " in the last 14 days.");
        SetResponse("Outbox errors refreshed. Open the Outbox Errors tab to inspect it.");
        bottomTabs.SelectedIndex = 4;
    }

    private async Task SendAsync()
    {
        RequireServiceAndSession();

        var outboundBytes = GetOutboundPayloadBytes();
        if (outboundBytes.Length == 0)
            throw new InvalidOperationException("There is no EDI payload to send.");

        var fileName = string.IsNullOrWhiteSpace(txtFileName.Text) ? "test.edi" : txtFileName.Text.Trim();
        var contentBase64 = Convert.ToBase64String(outboundBytes);

        long parcelId;
        if (chkExplicitSendTarget.Checked)
        {
            if (!int.TryParse(txtSendNetworkId.Text.Trim(), out var sendNetworkId))
                throw new InvalidOperationException("Send Network ID is required when the explicit send checkbox is checked.");

            if (!int.TryParse(txtSendMailboxId.Text.Trim(), out var sendMailboxId))
                throw new InvalidOperationException("Send Mailbox ID is required when the explicit send checkbox is checked.");

            if (!ConfirmExplicitSend(sendNetworkId, sendMailboxId, fileName))
            {
                Log("Explicit send was canceled by the user.");
                return;
            }

            parcelId = await _client.ParcelUploadExAAsync(
                txtServiceUrl.Text.Trim(),
                txtSessionId.Text.Trim(),
                sendNetworkId,
                sendMailboxId,
                fileName,
                contentBase64,
                CancellationToken.None);

            Log("ParcelUploadExA accepted '" + fileName + "' into outbox " + sendNetworkId + "/" + sendMailboxId + ". ParcelID=" + parcelId + ".");
            SetResponse("ParcelUploadExA accepted '" + fileName + "'. ParcelID=" + parcelId);
        }
        else
        {
            parcelId = await _client.ParcelUploadAAsync(
                txtServiceUrl.Text.Trim(),
                txtSessionId.Text.Trim(),
                fileName,
                contentBase64,
                CancellationToken.None);

            Log("ParcelUploadA accepted '" + fileName + "' into the current session outbox. ParcelID=" + parcelId + ".");
            SetResponse("ParcelUploadA accepted '" + fileName + "'. ParcelID=" + parcelId);
        }

        bottomTabs.SelectedIndex = 1;
    }

    private bool ConfirmExplicitSend(int sendNetworkId, int sendMailboxId, string fileName)
    {
        var current = SafeGetCurrentMailboxContext();
        var message = new StringBuilder();
        message.AppendLine("You are about to upload a file into an explicit ECGrid outbox.");
        message.AppendLine();
        if (current.HasValue)
            message.AppendLine("Current mailbox: " + current.Value.NetworkId + "/" + current.Value.MailboxId);
        message.AppendLine("Target outbox: " + sendNetworkId + "/" + sendMailboxId);
        message.AppendLine("File: " + fileName);
        message.AppendLine();
        message.AppendLine("This does not switch your session context; it only changes the upload target for this send.");
        message.AppendLine("Continue?");

        var result = MessageBox.Show(this, message.ToString(), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        return result == DialogResult.Yes;
    }

    private async Task PreviewAndDownloadSelectedInboxAsync()
    {
        RequireServiceAndSession();
        var selected = GetSelectedInboxParcel();
        if (selected == null)
            throw new InvalidOperationException("Select an inbox row first.");

        var current = RequireCurrentMailboxContext();
        if (chkBlockCrossMailboxDownloads.Checked && IsCrossMailboxSelection(selected, current.NetworkId, current.MailboxId))
        {
            throw new InvalidOperationException(
                "Safety block: the selected parcel does not appear to target the current mailbox context. Review the parcel metadata first, or uncheck 'Block cross-mailbox download' if you intentionally need to continue.");
        }

        if (chkRequireDownloadConfirmation.Checked)
        {
            var previewMessage = BuildPreviewWarning(selected, current.NetworkId, current.MailboxId);
            var answer = MessageBox.Show(this, previewMessage, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                Log("Preview/download was canceled for ParcelID=" + selected.ParcelID + ".");
                return;
            }
        }

        _lastDownloaded = await _client.ParcelDownloadNoUpdateAAsync(
            txtServiceUrl.Text.Trim(),
            txtSessionId.Text.Trim(),
            selected.ParcelID,
            CancellationToken.None);

        LoadPayloadBytesIntoEditor(_lastDownloaded.ContentBytes, _lastDownloaded.FileName);
        _lastPreviewedParcelId = selected.ParcelID;

        Log("Downloaded ParcelID=" + selected.ParcelID + " using ParcelDownloadNoUpdateA after confirmation. FileName=" + _lastDownloaded.FileName + ", Bytes=" + _lastDownloaded.Bytes + ".");
        SetResponse("Downloaded ParcelID=" + selected.ParcelID + " without changing inbox status. The file is now loaded into the preview editor. Use Confirm Download only when you intentionally want to mark it confirmed.");
    }

    private async Task ConfirmSelectedDownloadAsync()
    {
        RequireServiceAndSession();
        var selected = GetSelectedInboxParcel();
        if (selected == null)
            throw new InvalidOperationException("Select an inbox row first.");

        if (_lastDownloaded == null || _lastPreviewedParcelId != selected.ParcelID || _lastDownloaded.ParcelID != selected.ParcelID)
            throw new InvalidOperationException("Preview/download the same selected parcel first before confirming it.");

        var ask = MessageBox.Show(
            this,
            "Confirming tells ECGrid you are done with the selected parcel. Continue?",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (ask != DialogResult.Yes)
        {
            Log("Confirm Download was canceled for ParcelID=" + selected.ParcelID + ".");
            return;
        }

        var ok = await _client.ParcelDownloadConfirmAsync(
            txtServiceUrl.Text.Trim(),
            txtSessionId.Text.Trim(),
            selected.ParcelID,
            CancellationToken.None);

        Log("ParcelDownloadConfirm for ParcelID=" + selected.ParcelID + " returned " + ok + ".");
        SetResponse("ParcelDownloadConfirm returned " + ok + " for ParcelID=" + selected.ParcelID + ".");

        if (ok)
        {
            _lastConfirmedParcelId = selected.ParcelID;
            _lastConfirmedFileName = selected.FileName;
            txtRestoreParcelId.Text = selected.ParcelID.ToString();
            _lastDownloaded = null;
            _lastPreviewedParcelId = null;
            Log("You can use Restore to Inbox to place this parcel back into the pending inbox list for plumbing tests.");
        }

        await CheckInboxAsync();
    }

    private async Task RestoreParcelToInboxAsync()
    {
        RequireServiceAndSession();

        var parcelId = ResolveRestoreParcelId();
        var current = SafeGetCurrentMailboxContext();

        var ask = new StringBuilder();
        ask.AppendLine("This will call ParcelDownloadReset and ask ECGrid to restore the parcel to the inbox.");
        ask.AppendLine();
        ask.AppendLine("ParcelID: " + parcelId);
        if (_lastConfirmedParcelId == parcelId && !string.IsNullOrWhiteSpace(_lastConfirmedFileName))
            ask.AppendLine("FileName: " + _lastConfirmedFileName);
        if (current.HasValue)
            ask.AppendLine("Current mailbox: " + current.Value.NetworkId + "/" + current.Value.MailboxId);
        ask.AppendLine();
        ask.AppendLine("Use this only for controlled testing when you intentionally want the parcel to appear in the inbox again.");
        ask.AppendLine("Continue?");

        var answer = MessageBox.Show(this, ask.ToString(), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
        {
            Log("Restore to Inbox was canceled for ParcelID=" + parcelId + ".");
            return;
        }

        var ok = await _client.ParcelDownloadResetAsync(
            txtServiceUrl.Text.Trim(),
            txtSessionId.Text.Trim(),
            parcelId,
            CancellationToken.None);

        Log("ParcelDownloadReset for ParcelID=" + parcelId + " returned " + ok + ".");

        if (!ok)
        {
            SetResponse("ParcelDownloadReset returned false for ParcelID=" + parcelId + ".");
            bottomTabs.SelectedIndex = 1;
            return;
        }

        _lastConfirmedParcelId = parcelId;
        txtRestoreParcelId.Text = parcelId.ToString();

        await CheckInboxAsync();
        TrySelectInboxParcel(parcelId);

        SetResponse("ParcelDownloadReset returned true for ParcelID=" + parcelId + ". Inbox refreshed and the parcel was targeted for reappearance in the current inbox.");
        bottomTabs.SelectedIndex = 2;
    }

    private long ResolveRestoreParcelId()
    {
        if (long.TryParse(txtRestoreParcelId.Text.Trim(), out var parcelId) && parcelId > 0)
            return parcelId;

        if (_lastConfirmedParcelId.HasValue)
            return _lastConfirmedParcelId.Value;

        throw new InvalidOperationException("Enter a ParcelID to restore, or confirm a download first so the Restore ParcelID box can be auto-filled.");
    }

    private void TrySelectInboxParcel(long parcelId)
    {
        foreach (DataGridViewRow row in dgvInbox.Rows)
        {
            if (row.DataBoundItem is ParcelRecord parcel && parcel.ParcelID == parcelId)
            {
                row.Selected = true;
                dgvInbox.CurrentCell = row.Cells.Count > 0 ? row.Cells[0] : null;
                break;
            }
        }
    }

    private void LoadPayloadFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "EDI Files (*.edi;*.txt;*.x12)|*.edi;*.txt;*.x12|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var bytes = File.ReadAllBytes(dialog.FileName);
        LoadPayloadBytesIntoEditor(bytes, Path.GetFileName(dialog.FileName));
        Log("Loaded file: " + dialog.FileName + " (" + bytes.Length + " bytes).");
    }

    private void SaveDownloadedToDisk()
    {
        if (_lastDownloaded == null || _lastDownloaded.ContentBytes.Length == 0)
            throw new InvalidOperationException("No downloaded file is loaded yet.");

        using var dialog = new SaveFileDialog
        {
            FileName = string.IsNullOrWhiteSpace(_lastDownloaded.FileName) ? "downloaded.edi" : _lastDownloaded.FileName,
            Filter = "EDI Files (*.edi;*.txt;*.x12)|*.edi;*.txt;*.x12|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        File.WriteAllBytes(dialog.FileName, _lastDownloaded.ContentBytes);
        Log("Saved downloaded file to: " + dialog.FileName);
    }

    private void LoadPayloadBytesIntoEditor(byte[] bytes, string? fileName)
    {
        _currentPayloadBytes = bytes.Length == 0 ? Array.Empty<byte>() : (byte[])bytes.Clone();
        txtPayload.Text = TryDecodeToText(bytes);
        _currentPayloadTextSnapshot = txtPayload.Text;

        if (!string.IsNullOrWhiteSpace(fileName))
            txtFileName.Text = fileName;
    }

    private byte[] GetOutboundPayloadBytes()
    {
        if (_currentPayloadBytes is { Length: > 0 } && string.Equals(txtPayload.Text, _currentPayloadTextSnapshot, StringComparison.Ordinal))
            return _currentPayloadBytes;

        var payloadText = txtPayload.Text;
        if (string.IsNullOrWhiteSpace(payloadText))
            return Array.Empty<byte>();

        if (ContainsNonAsciiCharacters(payloadText))
        {
            throw new InvalidOperationException(
                "Payload contains non-ASCII characters. For EDI testing, load the original file bytes or remove smart quotes / other Unicode characters before sending.");
        }

        var bytes = Encoding.ASCII.GetBytes(payloadText);
        _currentPayloadBytes = bytes;
        _currentPayloadTextSnapshot = payloadText;
        return bytes;
    }

    private void ShowSelectedInboxParcelSummary()
    {
        var selected = GetSelectedInboxParcel();
        if (selected == null)
            return;

        SetResponse(BuildParcelSummary(selected));
    }

    private string BuildParcelSummary(ParcelRecord selected)
    {
        var current = SafeGetCurrentMailboxContext();
        var sb = new StringBuilder();
        sb.AppendLine("Selected inbox parcel:");
        sb.AppendLine("  ParcelID: " + selected.ParcelID);
        sb.AppendLine("  FileName: " + selected.FileName);
        sb.AppendLine("  ParcelDate: " + (selected.ParcelDate?.ToString("u") ?? string.Empty));
        sb.AppendLine("  Bytes: " + (selected.ActualBytes?.ToString() ?? string.Empty));
        sb.AppendLine("  Status: " + selected.StatusCode + " " + selected.StatusMessage);
        sb.AppendLine("  From: " + FormatEndpoint(selected.NetworkIDFrom, selected.MailboxIDFrom, selected.NetworkNameFrom, selected.MailboxNameFrom));
        sb.AppendLine("  To: " + FormatEndpoint(selected.NetworkIDTo, selected.MailboxIDTo, selected.NetworkNameTo, selected.MailboxNameTo));
        if (current.HasValue)
            sb.AppendLine("  Current mailbox: " + current.Value.NetworkId + "/" + current.Value.MailboxId);
        sb.AppendLine("  Cross-mailbox risk: " + (current.HasValue && IsCrossMailboxSelection(selected, current.Value.NetworkId, current.Value.MailboxId) ? "Potential mismatch" : "No obvious mismatch"));
        sb.AppendLine();
        sb.AppendLine("Double-click or use 'Preview / Download Selected' to continue.");
        return sb.ToString();
    }

    private string BuildPreviewWarning(ParcelRecord selected, int currentNetworkId, int currentMailboxId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are about to preview/download inbox data from ECGrid.");
        sb.AppendLine();
        sb.AppendLine("Current mailbox: " + currentNetworkId + "/" + currentMailboxId);
        if (_lastInboxNetworkId.HasValue && _lastInboxMailboxId.HasValue)
            sb.AppendLine("Inbox query mailbox: " + _lastInboxNetworkId.Value + "/" + _lastInboxMailboxId.Value);
        sb.AppendLine("ParcelID: " + selected.ParcelID);
        sb.AppendLine("FileName: " + selected.FileName);
        sb.AppendLine("ParcelDate: " + (selected.ParcelDate?.ToString("u") ?? string.Empty));
        sb.AppendLine("Bytes: " + (selected.ActualBytes?.ToString() ?? string.Empty));
        sb.AppendLine("From: " + FormatEndpoint(selected.NetworkIDFrom, selected.MailboxIDFrom, selected.NetworkNameFrom, selected.MailboxNameFrom));
        sb.AppendLine("To: " + FormatEndpoint(selected.NetworkIDTo, selected.MailboxIDTo, selected.NetworkNameTo, selected.MailboxNameTo));
        sb.AppendLine();
        sb.AppendLine("This action downloads the parcel into local memory for preview. It does not confirm/remove it.");
        if (IsCrossMailboxSelection(selected, currentNetworkId, currentMailboxId))
        {
            sb.AppendLine();
            sb.AppendLine("WARNING: this parcel does not appear to target the current mailbox context.");
        }
        sb.AppendLine();
        sb.AppendLine("Continue?");
        return sb.ToString();
    }

    private static string FormatEndpoint(int? networkId, int? mailboxId, string networkName, string mailboxName)
    {
        var idPart = (networkId?.ToString() ?? "?") + "/" + (mailboxId?.ToString() ?? "?");
        var namePart = string.Empty;
        if (!string.IsNullOrWhiteSpace(networkName) || !string.IsNullOrWhiteSpace(mailboxName))
            namePart = " (" + networkName + " / " + mailboxName + ")";
        return idPart + namePart;
    }

    private bool IsCrossMailboxSelection(ParcelRecord selected, int currentNetworkId, int currentMailboxId)
    {
        if (_lastInboxNetworkId.HasValue && _lastInboxMailboxId.HasValue)
        {
            if (_lastInboxNetworkId.Value != currentNetworkId || _lastInboxMailboxId.Value != currentMailboxId)
                return true;
        }

        if (selected.NetworkIDTo.HasValue && selected.NetworkIDTo.Value != currentNetworkId)
            return true;

        if (selected.MailboxIDTo.HasValue && selected.MailboxIDTo.Value != currentMailboxId)
            return true;

        return false;
    }

    private void Log(string message)
    {
        txtLog.AppendText(DateTime.Now.ToString("G") + " - " + SanitizeSensitiveText(message) + Environment.NewLine);
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.ScrollToCaret();
    }

    private void SetResponse(string message)
    {
        txtResponse.Text = SanitizeSensitiveText(message);
        txtResponse.SelectionStart = 0;
        txtResponse.SelectionLength = 0;
    }

    private string SanitizeSensitiveText(string? message)
    {
        message ??= string.Empty;

        var sessionId = txtSessionId.Text.Trim();
        if (!string.IsNullOrWhiteSpace(sessionId))
            message = message.Replace(sessionId, MaskSecret(sessionId), StringComparison.Ordinal);

        return message;
    }

    private void RequireServiceAndSession()
    {
        if (string.IsNullOrWhiteSpace(txtServiceUrl.Text))
            throw new InvalidOperationException("Service URL is required.");

        if (string.IsNullOrWhiteSpace(txtSessionId.Text))
            throw new InvalidOperationException("SessionID / API Key is required.");
    }

    private int RequireCurrentNetworkId()
    {
        if (!int.TryParse(txtCurrentNetworkId.Text.Trim(), out var value))
            throw new InvalidOperationException("Run WhoAmI first so the current Network ID is populated.");
        return value;
    }

    private (int NetworkId, int MailboxId) RequireCurrentMailboxContext()
    {
        var networkId = RequireCurrentNetworkId();
        if (!int.TryParse(txtCurrentMailboxId.Text.Trim(), out var mailboxId))
            throw new InvalidOperationException("Run WhoAmI first so the current Mailbox ID is populated.");
        return (networkId, mailboxId);
    }

    private (int NetworkId, int MailboxId)? SafeGetCurrentMailboxContext()
    {
        if (int.TryParse(txtCurrentNetworkId.Text.Trim(), out var networkId) &&
            int.TryParse(txtCurrentMailboxId.Text.Trim(), out var mailboxId))
            return (networkId, mailboxId);

        return null;
    }

    private ParcelRecord? GetSelectedInboxParcel()
    {
        if (dgvInbox.CurrentRow?.DataBoundItem is ParcelRecord current)
            return current;

        if (dgvInbox.SelectedRows.Count > 0 && dgvInbox.SelectedRows[0].DataBoundItem is ParcelRecord selected)
            return selected;

        return null;
    }

    private static string TryDecodeToText(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        if (!LooksLikeText(bytes))
            return Convert.ToBase64String(bytes);

        foreach (var encoding in new Encoding[]
                 {
                     new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                     Encoding.ASCII,
                     Encoding.Latin1
                 })
        {
            try
            {
                return encoding.GetString(bytes);
            }
            catch
            {
                // Try the next encoding.
            }
        }

        return Convert.ToBase64String(bytes);
    }

    private static bool LooksLikeText(byte[] bytes)
    {
        var sampleLength = Math.Min(bytes.Length, 4096);
        var suspicious = 0;

        for (var i = 0; i < sampleLength; i++)
        {
            var b = bytes[i];
            if (b == 0)
                return false;

            var isControl = b < 0x20 && b != 0x0D && b != 0x0A && b != 0x09;
            if (isControl)
                suspicious++;
        }

        return suspicious <= Math.Max(2, sampleLength / 20);
    }

    private static bool ContainsNonAsciiCharacters(string text)
    {
        foreach (var ch in text)
        {
            if (ch > 127)
                return true;
        }

        return false;
    }

    private static string MaskSecret(string? value)
    {
        value ??= string.Empty;
        if (value.Length <= 8)
            return new string('*', value.Length);
        return value.Substring(0, 4) + new string('*', value.Length - 8) + value.Substring(value.Length - 4);
    }
}
