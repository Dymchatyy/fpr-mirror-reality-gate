using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MonitorBrowser107;

internal sealed class MainForm : Form
{
	private const double SideFieldWidthMillimeters = 70.0;

	private const double FolderButtonWidthMillimeters = 40.0;

	private const double FolderButtonHeightMillimeters = 9.0;

	private const double FolderButtonMarginMillimeters = 3.0;

	private const double TransferPreviewHeightMillimeters = 62.0;

	private const double MillimetersPerInch = 25.4;

	private const int NormalTopPanelHeight = 106;

	private const int PilotSetupRowHeight = 38;

	private readonly ComboBox siteSelector = new ComboBox();

	private readonly TextBox addressBox = new TextBox();

	private readonly AppSurfacePanel addressSurface = new AppSurfacePanel();

	private readonly AppButton refreshButton = new AppButton();

	private readonly AppButton renamePilotButton = new AppButton();

	private readonly AppButton goButton = new AppButton();

	private readonly AppButton cancelSetupButton = new AppButton();

	private readonly Label addressSetupLabel = new Label();

	private readonly Label statusLabel = new Label();

	private readonly ToolTip statusToolTip = new ToolTip();

	private readonly Label branchCharacterCountLabel = new Label();

	private readonly Label branchLoadStatusLabel = new Label();

	private readonly BranchLoadBar branchLoadBar = new BranchLoadBar();

	private readonly Label branchLoadCaptionLabel = new Label();

	private readonly AppSurfacePanel branchMeterSurface = new AppSurfacePanel();

	private readonly AppSurfacePanel browserHost = new AppSurfacePanel();

	private readonly AppSurfacePanel leftField = new AppSurfacePanel();

	private readonly AppSurfacePanel rightField = new AppSurfacePanel();

	private readonly AppButton leftPanelToggleButton = new AppButton();

	private readonly AppButton graphicTrunkEntryButton = new AppButton();

	private readonly WorkspaceDrawerHost workspaceDrawerHost = new WorkspaceDrawerHost();

	private readonly Panel browserStage = new Panel();

	private readonly AppButton folderPickerButton = new AppButton();

	private readonly AppButton handshakeButton = new AppButton();

	private readonly AppButton captureResponseButton = new AppButton();

	private readonly AppButton transferButton = new AppButton();

	private readonly TextBox transferPreview = new TextBox();

	private readonly TextBox liveConsole = new TextBox();

	private readonly FprLifecycleConsoleReader lifecycleConsoleReader = new FprLifecycleConsoleReader();

	private readonly AppSurfacePanel projectSurface = new AppSurfacePanel();

	private readonly Label projectSectionLabel = new Label();

	private readonly Label activeProjectLabel = new Label();

	private readonly ComboBox projectSelector = new ComboBox();

	private readonly AppButton addProjectButton = new AppButton();

	private readonly AppButton openProjectButton = new AppButton();

	private readonly AppButton fprDevelopmentButton = new AppButton();

	private readonly TextBox newProjectNameBox = new TextBox();

	private readonly RowStyle newProjectEditorRowStyle = new RowStyle(SizeType.Absolute, 0f);

	private readonly AppSurfacePanel transferPreviewSurface = new AppSurfacePanel();

	private readonly AppSurfacePanel liveConsoleSurface = new AppSurfacePanel();

	private readonly ColumnStyle leftFieldColumn = new ColumnStyle(SizeType.Absolute, 0f);

	private readonly ColumnStyle rightFieldColumn = new ColumnStyle(SizeType.Absolute, 0f);

	private readonly RowStyle pilotSetupRowStyle = new RowStyle(SizeType.Absolute, 0f);

	private readonly RowStyle rootTopRowStyle = new RowStyle(SizeType.Absolute, 106f);

	private readonly TableLayoutPanel pilotSetupPanel = new TableLayoutPanel();

	private readonly PilotNavigationController pilotNavigationController;

	private readonly PilotWebViewSessionController pilotWebViewSessionController;

	private readonly ResponseTransferController responseTransferController;

	private readonly BranchMeterController branchMeterController;

	private readonly FprOutboxConnector fprOutboxConnector = new FprOutboxConnector();

	private readonly FprProjectClient fprProjectClient = new FprProjectClient();

	private readonly FprGraphicTrunkClient fprGraphicTrunkClient = new FprGraphicTrunkClient();

	private readonly PilotContactSessionTracker pilotContactSessionTracker = new PilotContactSessionTracker();

	private readonly DownloadRouter downloadRouter;

	private CollapsiblePanelController? leftPanelController;

	private int folderButtonMarginPixels;

	private bool formShown;

	private bool isClosing;

	private bool suppressSiteSelectionChange;

	private bool suppressProjectSelectionChange;

	private string activeProjectId = string.Empty;

	private string activeProjectTitle = string.Empty;

	private string activeProjectStorageRoot = string.Empty;

	private bool newProjectEditorVisible;

	private bool pilotSetupVisible;

	private int onboardingDeliveryInProgress;

	private int transferOperationInProgress;

	private string pendingMachineFrame = string.Empty;

	private string lastCapturedMachineFrameKey = string.Empty;

	internal MainForm()
	{
		pilotNavigationController = new PilotNavigationController();
		InitializeLayout();
		ConfigureFprRootFromEnvironment();
		InitializeCollapsibleLeftPanel();
		lifecycleConsoleReader.Start(AppendLiveConsole);
		downloadRouter = new DownloadRouter(this, GetDownloadProjectContext, SetVisibleStatusMessage);
		pilotWebViewSessionController = new PilotWebViewSessionController(delegate(WebView2 view)
		{
			browserHost.Controls.Add(view);
		}, delegate(WebView2 view)
		{
			browserHost.Controls.Remove(view);
		}, () => isClosing, AppColors.SecondaryBackground);
		workspaceDrawerHost.ConfigureConsilium(GetConsiliumPilotRegistry, SendConsiliumPromptAsync, CaptureConsiliumOpinionAsync, PrepareConsiliumVerdictAsync, SetVisibleStatusMessage);
		responseTransferController = new ResponseTransferController(() => ChatResponseTransfer.CaptureLastResponseAsync(pilotWebViewSessionController.CurrentWebView));
		branchMeterController = new BranchMeterController(() => pilotWebViewSessionController.CurrentWebView, IsChatGptPage, SetBranchMeterDisplay, () => isClosing);
		branchMeterController.StartAutomaticRefresh();
		AppTheme.StyleToolTip(statusToolTip);
		if (!pilotWebViewSessionController.IsProfileRootReady)
		{
			SetBrowserControlsEnabled(enabled: false);
			SetStatusText("Корень профилей недоступен.", AppStatusTone.Error);
			base.Shown += delegate
			{
				MessageBox.Show(this, pilotWebViewSessionController.ProfileRootError, "Faysy Patch Runner", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				Close();
			};
		}
		else
		{
			SetStatusText("Ожидание запуска WebView2...", AppStatusTone.Neutral);
			siteSelector.DropDown += SiteSelector_DropDown;
			siteSelector.SelectedIndexChanged += SiteSelector_SelectedIndexChanged;
			base.Shown += MainForm_Shown;
		}
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		isClosing = true;
		lifecycleConsoleReader.Dispose();
		responseTransferController.Stop();
		branchMeterController.Stop();
		pilotWebViewSessionController.Stop();
		responseTransferController.Dispose();
		branchMeterController.Dispose();
		pilotWebViewSessionController.Dispose();
		downloadRouter.Dispose();
		workspaceDrawerHost.Dispose();
		leftPanelController?.SaveState();
		leftPanelController?.Dispose();
		statusToolTip.Dispose();
		base.OnFormClosing(e);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		UpdateSideFieldWidths(base.DeviceDpi);
		UpdatePilotSetupLayout(base.DeviceDpi);
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		UpdateSideFieldWidths(e.DeviceDpiNew);
		UpdatePilotSetupLayout(e.DeviceDpiNew);
	}

	private void InitializeLayout()
	{
		Text = "Faysy Patch Runner";
		base.StartPosition = FormStartPosition.CenterScreen;
		base.ClientSize = new Size(1200, 800);
		MinimumSize = new Size(900, 600);
		base.AutoScaleMode = AutoScaleMode.Dpi;
		DoubleBuffered = true;
		AppTheme.ApplyTo(this);
		siteSelector.DropDownStyle = ComboBoxStyle.DropDownList;
		siteSelector.Dock = DockStyle.Fill;
		siteSelector.Margin = new Padding(0, 2, 6, 2);
		RefreshPilotSelector();
		AppTheme.StyleComboBox(siteSelector);
		renamePilotButton.Text = "Имя";
		renamePilotButton.Icon = AppButtonIcon.None;
		renamePilotButton.CornerRadius = 9;
		renamePilotButton.Dock = DockStyle.Fill;
		renamePilotButton.Margin = new Padding(0, 1, 6, 1);
		renamePilotButton.Role = AppButtonRole.Default;
		renamePilotButton.Click += RenamePilotButton_Click;
		addressBox.Dock = DockStyle.Fill;
		addressBox.Margin = Padding.Empty;
		addressBox.KeyDown += AddressBox_KeyDown;
		AppTheme.StyleTextBox(addressBox);
		addressSurface.Dock = DockStyle.Fill;
		addressSurface.Margin = new Padding(0, 1, 6, 1);
		addressSurface.Padding = new Padding(11, 8, 11, 5);
		addressSurface.FillColor = AppColors.WindowBackground;
		addressSurface.BorderColor = AppColors.ButtonBorder;
		addressSurface.BorderRadius = 8;
		addressSurface.Controls.Add(addressBox);
		addressBox.Enter += delegate
		{
			addressSurface.BorderColor = (addressBox.ReadOnly ? AppColors.Border : AppColors.Accent);
		};
		addressBox.Leave += delegate
		{
			addressSurface.BorderColor = (addressBox.ReadOnly ? AppColors.Border : AppColors.ButtonBorder);
		};
		refreshButton.Text = "Обновить";
		refreshButton.Icon = AppButtonIcon.Refresh;
		refreshButton.CornerRadius = 9;
		refreshButton.Dock = DockStyle.Fill;
		refreshButton.Margin = new Padding(6, 1, 0, 1);
		refreshButton.Role = AppButtonRole.Default;
		refreshButton.Enabled = false;
		refreshButton.Click += RefreshButton_Click;
		goButton.Text = "Перейти";
		goButton.Icon = AppButtonIcon.ArrowRight;
		goButton.CornerRadius = 9;
		goButton.Dock = DockStyle.Fill;
		goButton.Margin = new Padding(6, 1, 0, 1);
		goButton.Role = AppButtonRole.Default;
		goButton.Enabled = false;
		goButton.Click += PilotSetupGo_Click;
		cancelSetupButton.Text = "Отмена";
		cancelSetupButton.Icon = AppButtonIcon.Close;
		cancelSetupButton.CornerRadius = 9;
		cancelSetupButton.Dock = DockStyle.Fill;
		cancelSetupButton.Margin = new Padding(6, 1, 0, 1);
		cancelSetupButton.Role = AppButtonRole.Default;
		cancelSetupButton.Click += CancelPilotSetup_Click;
		addressSetupLabel.Text = "Адрес:";
		addressSetupLabel.TextAlign = ContentAlignment.MiddleLeft;
		addressSetupLabel.Dock = DockStyle.Fill;
		addressSetupLabel.Margin = Padding.Empty;
		addressSetupLabel.BackColor = Color.Transparent;
		addressSetupLabel.ForeColor = AppColors.TextSecondary;
		addressSetupLabel.Font = AppTheme.UiFont;
		statusLabel.Text = "● Загрузка";
		statusLabel.AutoEllipsis = true;
		statusLabel.TextAlign = ContentAlignment.MiddleLeft;
		statusLabel.Dock = DockStyle.Fill;
		statusLabel.Margin = Padding.Empty;
		statusLabel.Padding = new Padding(8, 0, 8, 0);
		statusLabel.BackColor = Color.Transparent;
		statusLabel.ForeColor = AppColors.TextSecondary;
		statusLabel.Font = AppTheme.UiFont;
		branchCharacterCountLabel.Text = "НАГРУЗКА ВЕТКИ НЕ ОПРЕДЕЛЕНА";
		branchCharacterCountLabel.AutoEllipsis = false;
		branchCharacterCountLabel.TextAlign = ContentAlignment.MiddleLeft;
		branchCharacterCountLabel.Dock = DockStyle.Fill;
		branchCharacterCountLabel.Margin = Padding.Empty;
		branchCharacterCountLabel.Padding = new Padding(0, 0, 8, 0);
		branchCharacterCountLabel.BackColor = Color.Transparent;
		branchCharacterCountLabel.ForeColor = AppColors.TextMuted;
		branchCharacterCountLabel.Font = AppTheme.BranchLoadCountFont;
		branchCharacterCountLabel.Cursor = Cursors.Hand;
		branchCharacterCountLabel.Click += BranchLoadStatusLabel_Click;
		branchLoadStatusLabel.Text = string.Empty;
		branchLoadStatusLabel.AutoEllipsis = false;
		branchLoadStatusLabel.TextAlign = ContentAlignment.MiddleRight;
		branchLoadStatusLabel.Dock = DockStyle.Fill;
		branchLoadStatusLabel.Margin = Padding.Empty;
		branchLoadStatusLabel.Padding = new Padding(8, 0, 0, 0);
		branchLoadStatusLabel.BackColor = Color.Transparent;
		branchLoadStatusLabel.ForeColor = AppColors.TextMuted;
		branchLoadStatusLabel.Font = AppTheme.BranchLoadStatusFont;
		branchLoadStatusLabel.Cursor = Cursors.Hand;
		branchLoadStatusLabel.Click += BranchLoadStatusLabel_Click;
		statusToolTip.SetToolTip(branchCharacterCountLabel, "Нажмите, чтобы пересчитать всю ленту ChatGPT");
		statusToolTip.SetToolTip(branchLoadStatusLabel, "Нажмите, чтобы пересчитать всю ленту ChatGPT");
		branchLoadBar.Dock = DockStyle.Fill;
		branchLoadCaptionLabel.Text = "Точный подсчёт всей ленты в отдельном безопасном процессе";
		branchLoadCaptionLabel.AutoEllipsis = false;
		branchLoadCaptionLabel.TextAlign = ContentAlignment.MiddleLeft;
		branchLoadCaptionLabel.Dock = DockStyle.Fill;
		branchLoadCaptionLabel.Margin = Padding.Empty;
		branchLoadCaptionLabel.BackColor = Color.Transparent;
		branchLoadCaptionLabel.ForeColor = AppColors.TextMuted;
		branchLoadCaptionLabel.Font = AppTheme.BranchLoadCaptionFont;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = Color.Transparent
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 4f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(branchCharacterCountLabel, 0, 0);
		tableLayoutPanel.Controls.Add(branchLoadStatusLabel, 1, 0);
		tableLayoutPanel.Controls.Add(branchLoadBar, 0, 1);
		tableLayoutPanel.SetColumnSpan(branchLoadBar, 2);
		tableLayoutPanel.Controls.Add(branchLoadCaptionLabel, 0, 2);
		tableLayoutPanel.SetColumnSpan(branchLoadCaptionLabel, 2);
		branchMeterSurface.Dock = DockStyle.Fill;
		branchMeterSurface.Margin = new Padding(0, 4, 0, 0);
		branchMeterSurface.Padding = new Padding(16, 2, 16, 2);
		branchMeterSurface.FillColor = AppColors.PanelBackground;
		branchMeterSurface.BorderColor = AppColors.Border;
		branchMeterSurface.BorderRadius = 9;
		branchMeterSurface.Controls.Add(tableLayoutPanel);
		browserHost.Dock = DockStyle.Fill;
		browserHost.Margin = new Padding(3, 6, 3, 6);
		browserHost.Padding = new Padding(1);
		browserHost.FillColor = AppColors.SecondaryBackground;
		browserHost.BorderColor = AppColors.Border;
		browserHost.BorderRadius = 10;
		pilotSetupPanel.Dock = DockStyle.Fill;
		pilotSetupPanel.ColumnCount = 4;
		pilotSetupPanel.RowCount = 1;
		pilotSetupPanel.Margin = Padding.Empty;
		pilotSetupPanel.Padding = Padding.Empty;
		pilotSetupPanel.BackColor = Color.Transparent;
		pilotSetupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60f));
		pilotSetupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		pilotSetupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
		pilotSetupPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));
		pilotSetupPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		pilotSetupPanel.Controls.Add(addressSetupLabel, 0, 0);
		pilotSetupPanel.Controls.Add(addressSurface, 1, 0);
		pilotSetupPanel.Controls.Add(goButton, 2, 0);
		pilotSetupPanel.Controls.Add(cancelSetupButton, 3, 0);
		pilotSetupPanel.Visible = false;
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 4,
			RowCount = 3,
			Margin = Padding.Empty,
			Padding = new Padding(10, 10, 10, 6),
			BackColor = AppColors.SecondaryBackground
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
		tableLayoutPanel2.RowStyles.Add(pilotSetupRowStyle);
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
		tableLayoutPanel2.Controls.Add(siteSelector, 0, 0);
		tableLayoutPanel2.Controls.Add(renamePilotButton, 1, 0);
		tableLayoutPanel2.Controls.Add(statusLabel, 2, 0);
		tableLayoutPanel2.Controls.Add(refreshButton, 3, 0);
		tableLayoutPanel2.Controls.Add(pilotSetupPanel, 0, 1);
		tableLayoutPanel2.SetColumnSpan(pilotSetupPanel, 4);
		tableLayoutPanel2.Controls.Add(branchMeterSurface, 0, 2);
		tableLayoutPanel2.SetColumnSpan(branchMeterSurface, 4);
		leftField.Dock = DockStyle.Fill;
		leftField.Margin = new Padding(6, 6, 3, 6);
		leftField.FillColor = AppColors.PanelBackground;
		leftField.BorderColor = AppColors.Border;
		leftField.BorderRadius = 10;
		leftField.TabStop = false;
		leftPanelToggleButton.Text = string.Empty;
		leftPanelToggleButton.Icon = AppButtonIcon.HorizontalArrows;
		leftPanelToggleButton.CornerRadius = 7;
		leftPanelToggleButton.AutoSize = false;
		leftPanelToggleButton.AutoEllipsis = false;
		leftPanelToggleButton.Role = AppButtonRole.Default;
		leftPanelToggleButton.AccessibleName = "Свернуть или развернуть левую панель";
		leftField.Controls.Add(leftPanelToggleButton);
		graphicTrunkEntryButton.Text = "Graphic / Consilium (Граф / Консилиум)";
		graphicTrunkEntryButton.Icon = AppButtonIcon.HorizontalArrows;
		graphicTrunkEntryButton.CornerRadius = 8;
		graphicTrunkEntryButton.AutoSize = false;
		graphicTrunkEntryButton.AutoEllipsis = true;
		graphicTrunkEntryButton.Role = AppButtonRole.Default;
		graphicTrunkEntryButton.AccessibleName = "Open Graphic / Consilium (Открыть Граф / Консилиум)";
		graphicTrunkEntryButton.Click += delegate
		{
			SetWorkspaceDrawerOpen(!workspaceDrawerHost.IsOpen);
		};
		leftField.Controls.Add(graphicTrunkEntryButton);
		projectSectionLabel.Text = "ПРОЕКТ";
		projectSectionLabel.Dock = DockStyle.Fill;
		projectSectionLabel.TextAlign = ContentAlignment.MiddleLeft;
		projectSectionLabel.Margin = Padding.Empty;
		projectSectionLabel.BackColor = Color.Transparent;
		projectSectionLabel.ForeColor = AppColors.TextMuted;
		projectSectionLabel.Font = AppTheme.BranchLoadCaptionFont;
		activeProjectLabel.Text = "Подключение к FPR...";
		activeProjectLabel.Dock = DockStyle.Fill;
		activeProjectLabel.TextAlign = ContentAlignment.MiddleLeft;
		activeProjectLabel.AutoEllipsis = true;
		activeProjectLabel.Margin = Padding.Empty;
		activeProjectLabel.BackColor = Color.Transparent;
		activeProjectLabel.ForeColor = AppColors.TextPrimary;
		activeProjectLabel.Font = AppTheme.UiFont;
		projectSelector.DropDownStyle = ComboBoxStyle.DropDownList;
		projectSelector.Dock = DockStyle.Fill;
		projectSelector.Margin = Padding.Empty;
		projectSelector.MaxDropDownItems = 30;
		projectSelector.IntegralHeight = false;
		projectSelector.DropDownHeight = ScaleLogicalPixels(520, base.DeviceDpi);
		projectSelector.SelectedIndexChanged += ProjectSelector_SelectedIndexChanged;
		AppTheme.StyleComboBox(projectSelector);
		addProjectButton.Text = string.Empty;
		addProjectButton.Icon = AppButtonIcon.Plus;
		addProjectButton.CornerRadius = 8;
		addProjectButton.Dock = DockStyle.Fill;
		addProjectButton.Margin = new Padding(8, 0, 0, 0);
		addProjectButton.Role = AppButtonRole.Default;
		addProjectButton.AccessibleName = "Создать новый проект";
		addProjectButton.Click += AddProjectButton_Click;
		openProjectButton.Text = string.Empty;
		openProjectButton.Icon = AppButtonIcon.ArrowRight;
		openProjectButton.CornerRadius = 8;
		openProjectButton.Dock = DockStyle.Fill;
		openProjectButton.Margin = new Padding(6, 0, 0, 0);
		openProjectButton.Role = AppButtonRole.Default;
		openProjectButton.AccessibleName = "Открыть существующий проект из папки";
		openProjectButton.Click += OpenProjectButton_Click;
		fprDevelopmentButton.Text = "FPR";
		fprDevelopmentButton.CornerRadius = 8;
		fprDevelopmentButton.Dock = DockStyle.Fill;
		fprDevelopmentButton.Margin = new Padding(6, 0, 0, 0);
		fprDevelopmentButton.Role = AppButtonRole.Default;
		fprDevelopmentButton.AccessibleName = "Подготовить и открыть проект FPR DEVELOPMENT";
		fprDevelopmentButton.Click += FprDevelopmentButton_Click;
		newProjectNameBox.Dock = DockStyle.Fill;
		newProjectNameBox.Margin = new Padding(0, 6, 0, 0);
		newProjectNameBox.PlaceholderText = "Название нового проекта";
		newProjectNameBox.MaxLength = 80;
		newProjectNameBox.Visible = false;
		newProjectNameBox.KeyDown += NewProjectNameBox_KeyDown;
		AppTheme.StyleTextBox(newProjectNameBox);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 4,
			RowCount = 4,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = Color.Transparent
		};
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
		tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
		tableLayoutPanel3.RowStyles.Add(newProjectEditorRowStyle);
		tableLayoutPanel3.Controls.Add(projectSectionLabel, 0, 0);
		tableLayoutPanel3.SetColumnSpan(projectSectionLabel, 4);
		tableLayoutPanel3.Controls.Add(activeProjectLabel, 0, 1);
		tableLayoutPanel3.SetColumnSpan(activeProjectLabel, 4);
		tableLayoutPanel3.Controls.Add(projectSelector, 0, 2);
		tableLayoutPanel3.Controls.Add(addProjectButton, 1, 2);
		tableLayoutPanel3.Controls.Add(openProjectButton, 2, 2);
		tableLayoutPanel3.Controls.Add(fprDevelopmentButton, 3, 2);
		tableLayoutPanel3.Controls.Add(newProjectNameBox, 0, 3);
		tableLayoutPanel3.SetColumnSpan(newProjectNameBox, 4);
		projectSurface.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		projectSurface.Padding = new Padding(12, 6, 12, 8);
		projectSurface.FillColor = AppColors.ElevatedSurface;
		projectSurface.BorderColor = AppColors.Border;
		projectSurface.BorderRadius = 9;
		projectSurface.Controls.Add(tableLayoutPanel3);
		leftField.Controls.Add(projectSurface);
		leftField.SizeChanged += delegate
		{
			PositionLeftPanelControls();
		};
		rightField.Dock = DockStyle.Fill;
		rightField.Margin = new Padding(3, 6, 6, 6);
		rightField.FillColor = AppColors.PanelBackground;
		rightField.BorderColor = AppColors.Border;
		rightField.BorderRadius = 10;
		rightField.TabStop = false;
		folderPickerButton.Text = "Выбрать папку";
		folderPickerButton.Icon = AppButtonIcon.StatusDot;
		folderPickerButton.IconColor = AppColors.TextMuted;
		folderPickerButton.CornerRadius = 9;
		folderPickerButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		folderPickerButton.AutoSize = false;
		folderPickerButton.AutoEllipsis = true;
		folderPickerButton.Role = AppButtonRole.Default;
		folderPickerButton.Click += FolderPickerButton_Click;
		handshakeButton.Text = "Рукопожатие";
		handshakeButton.Icon = AppButtonIcon.StatusDot;
		handshakeButton.CornerRadius = 9;
		handshakeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		handshakeButton.AutoSize = false;
		handshakeButton.AutoEllipsis = true;
		handshakeButton.Role = AppButtonRole.Default;
		handshakeButton.Click += delegate
		{
			TryStartPilotOnboardingAsync("USER_EXPLICIT_HANDSHAKE");
		};
		rightField.Controls.Add(handshakeButton);
		captureResponseButton.Text = "Считать ответ";
		captureResponseButton.Icon = AppButtonIcon.ArrowRight;
		captureResponseButton.CornerRadius = 9;
		captureResponseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		captureResponseButton.AutoSize = false;
		captureResponseButton.AutoEllipsis = true;
		captureResponseButton.Role = AppButtonRole.Default;
		captureResponseButton.Click += CaptureResponseButton_Click;
		rightField.Controls.Add(captureResponseButton);
		// FIX (сессия 2 из 2, тема: "считать ответ"): в 4.084 эта кнопка была
		// превращена в отключённый заглушечный "Не назначено", а её реальная
		// функция ("Принять в FPR") была влита в captureResponseButton, из-за
		// чего "считать" и "передать" стали одной операцией без права владельца
		// на промежуточную проверку. Возвращаем сценарий 4.015: отдельная кнопка
		// со своим обработчиком, включаемая только после успешного считывания.
		transferButton.Text = "Принять в FPR";
		transferButton.Icon = AppButtonIcon.Check;
		transferButton.CornerRadius = 9;
		transferButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		transferButton.AutoSize = false;
		transferButton.AutoEllipsis = true;
		transferButton.Role = AppButtonRole.Primary;
		transferButton.Enabled = false;
		transferButton.Click += TransferButton_Click;
		rightField.Controls.Add(transferButton);
		transferPreview.Multiline = true;
		transferPreview.ReadOnly = true;
		transferPreview.ScrollBars = ScrollBars.Vertical;
		transferPreview.WordWrap = true;
		transferPreview.Dock = DockStyle.Fill;
		transferPreview.Margin = Padding.Empty;
		AppTheme.StyleTextBox(transferPreview);
		transferPreview.Text = string.Empty;
		transferPreviewSurface.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		transferPreviewSurface.Padding = new Padding(10);
		transferPreviewSurface.FillColor = AppColors.ElevatedSurface;
		transferPreviewSurface.BorderColor = AppColors.Border;
		transferPreviewSurface.BorderRadius = 9;
		transferPreviewSurface.Controls.Add(transferPreview);
		rightField.Controls.Add(transferPreviewSurface);
		liveConsole.Multiline = true;
		liveConsole.ReadOnly = true;
		liveConsole.ScrollBars = ScrollBars.Vertical;
		liveConsole.WordWrap = true;
		liveConsole.Dock = DockStyle.Fill;
		liveConsole.Margin = Padding.Empty;
		AppTheme.StyleTextBox(liveConsole);
		liveConsole.BorderStyle = BorderStyle.None;
		liveConsole.BackColor = AppColors.WindowBackground;
		liveConsole.ForeColor = AppColors.TextMuted;
		liveConsole.Font = new Font(FontFamily.GenericMonospace, 8.5f, FontStyle.Regular);
		liveConsole.Text = string.Empty;
		liveConsoleSurface.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
		liveConsoleSurface.Padding = new Padding(8);
		liveConsoleSurface.FillColor = AppColors.WindowBackground;
		liveConsoleSurface.BorderColor = AppColors.Border;
		liveConsoleSurface.BorderRadius = 9;
		liveConsoleSurface.Controls.Add(liveConsole);
		rightField.Controls.Add(liveConsoleSurface);
		rightField.SizeChanged += delegate
		{
			PositionRightPanelControls();
		};
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = AppColors.WindowBackground
		};
		tableLayoutPanel4.ColumnStyles.Add(leftFieldColumn);
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel4.ColumnStyles.Add(rightFieldColumn);
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel4.Controls.Add(leftField, 0, 0);
		tableLayoutPanel4.Controls.Add(browserHost, 1, 0);
		tableLayoutPanel4.Controls.Add(rightField, 2, 0);
		browserStage.Dock = DockStyle.Fill;
		browserStage.Margin = Padding.Empty;
		browserStage.Padding = Padding.Empty;
		browserStage.BackColor = AppColors.WindowBackground;
		browserStage.Controls.Add(tableLayoutPanel4);
		workspaceDrawerHost.Visible = false;
		workspaceDrawerHost.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
		workspaceDrawerHost.OpenStateChanged += delegate
		{
			graphicTrunkEntryButton.Role = (workspaceDrawerHost.IsOpen ? AppButtonRole.Primary : AppButtonRole.Default);
		};
		browserStage.Controls.Add(workspaceDrawerHost);
		browserStage.SizeChanged += delegate
		{
			PositionWorkspaceDrawer();
		};
		TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			BackColor = AppColors.WindowBackground
		};
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel5.RowStyles.Add(rootTopRowStyle);
		tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel5.Controls.Add(tableLayoutPanel2, 0, 0);
		tableLayoutPanel5.Controls.Add(browserStage, 0, 1);
		base.Controls.Add(tableLayoutPanel5);
		UpdateSideFieldWidths(base.DeviceDpi);
		UpdatePilotSetupLayout(base.DeviceDpi);
	}

	private void InitializeCollapsibleLeftPanel()
	{
		int defaultOpenColumnWidth = (int)Math.Round(leftFieldColumn.Width, MidpointRounding.AwayFromZero);
		leftPanelController = new CollapsiblePanelController(leftField, leftFieldColumn, leftPanelToggleButton, defaultOpenColumnWidth, base.DeviceDpi, delegate
		{
			browserHost.Parent?.PerformLayout();
		});
	}

	private void UpdateSideFieldWidths(int dpi)
	{
		int num = (int)Math.Round((double)dpi * 70.0 / 25.4, MidpointRounding.AwayFromZero);
		if (leftPanelController == null)
		{
			leftFieldColumn.Width = num;
		}
		else
		{
			leftPanelController.UpdateDpi(dpi, num);
		}
		rightFieldColumn.Width = num;
		browserHost.Parent?.PerformLayout();
		folderPickerButton.Size = new Size(MillimetersToPixels(dpi, 40.0), MillimetersToPixels(dpi, 9.0));
		handshakeButton.Size = folderPickerButton.Size;
		captureResponseButton.Size = folderPickerButton.Size;
		transferButton.Size = folderPickerButton.Size;
		transferPreviewSurface.Height = MillimetersToPixels(dpi, 62.0);
		folderButtonMarginPixels = MillimetersToPixels(dpi, 3.0);
		PositionRightPanelControls();
		PositionLeftPanelControls();
		PositionWorkspaceDrawer();
	}

	private void UpdatePilotSetupLayout(int dpi)
	{
		int num = ScaleLogicalPixels(38, dpi);
		int num2 = ScaleLogicalPixels(106, dpi);
		pilotSetupRowStyle.Height = (pilotSetupVisible ? ((float)num) : 0f);
		rootTopRowStyle.Height = (pilotSetupVisible ? (num2 + num) : num2);
		pilotSetupPanel.Parent?.PerformLayout();
	}

	private static int ScaleLogicalPixels(int logicalPixels, int dpi)
	{
		return (int)Math.Round((double)(logicalPixels * dpi) / 96.0, MidpointRounding.AwayFromZero);
	}

	private static int MillimetersToPixels(int dpi, double millimeters)
	{
		return (int)Math.Round((double)dpi * millimeters / 25.4, MidpointRounding.AwayFromZero);
	}

	private void PositionLeftPanelControls()
	{
		int num = Math.Max(10, ScaleLogicalPixels(12, base.DeviceDpi));
		int num2 = Math.Max(42, ScaleLogicalPixels(46, base.DeviceDpi));
		int num3 = leftField.ClientSize.Width - 2 * num;
		bool flag = num3 >= ScaleLogicalPixels(150, base.DeviceDpi);
		projectSurface.Visible = flag;
		if (flag)
		{
			projectSurface.Location = new Point(num, num2);
			projectSurface.Width = num3;
			projectSurface.Height = ScaleLogicalPixels(newProjectEditorVisible ? 150 : 106, base.DeviceDpi);
			projectSurface.SendToBack();
			graphicTrunkEntryButton.Text = "Graphic / Consilium (Граф / Консилиум)";
			graphicTrunkEntryButton.Icon = AppButtonIcon.HorizontalArrows;
			graphicTrunkEntryButton.Location = new Point(num, projectSurface.Bottom + ScaleLogicalPixels(12, base.DeviceDpi));
			graphicTrunkEntryButton.Size = new Size(num3, ScaleLogicalPixels(36, base.DeviceDpi));
		}
		else
		{
			graphicTrunkEntryButton.Text = string.Empty;
			graphicTrunkEntryButton.Icon = AppButtonIcon.StatusDot;
			graphicTrunkEntryButton.Location = new Point(Math.Max(4, (leftField.ClientSize.Width - ScaleLogicalPixels(34, base.DeviceDpi)) / 2), Math.Max(leftPanelToggleButton.Bottom + ScaleLogicalPixels(14, base.DeviceDpi), num2));
			graphicTrunkEntryButton.Size = new Size(Math.Min(ScaleLogicalPixels(34, base.DeviceDpi), Math.Max(26, leftField.ClientSize.Width - 8)), ScaleLogicalPixels(34, base.DeviceDpi));
		}
		graphicTrunkEntryButton.BringToFront();
		leftPanelToggleButton.BringToFront();
	}

	private void SetWorkspaceDrawerOpen(bool open)
	{
		workspaceDrawerHost.SetOpen(open);
		graphicTrunkEntryButton.Role = (open ? AppButtonRole.Primary : AppButtonRole.Default);
		PositionWorkspaceDrawer();
		if (open && workspaceDrawerHost.Mode == WorkspaceDrawerMode.GraphicTrunk)
		{
			RefreshGraphicTrunkAsync(showStatus: false);
		}
		if (!open)
		{
			graphicTrunkEntryButton.Focus();
		}
	}

	private void PositionWorkspaceDrawer()
	{
		workspaceDrawerHost.SetAvailableBounds(browserStage.ClientSize, base.DeviceDpi);
		if (workspaceDrawerHost.IsOpen)
		{
			workspaceDrawerHost.BringToFront();
		}
	}

	private void PositionRightPanelControls()
	{
		int x = Math.Max(0, rightField.ClientSize.Width - handshakeButton.Width - folderButtonMarginPixels);
		handshakeButton.Location = new Point(x, folderButtonMarginPixels);
		captureResponseButton.Location = new Point(x, handshakeButton.Bottom + folderButtonMarginPixels);
		transferButton.Location = new Point(x, captureResponseButton.Bottom + folderButtonMarginPixels);
		transferPreviewSurface.Location = new Point(folderButtonMarginPixels, transferButton.Bottom + folderButtonMarginPixels);
		transferPreviewSurface.Width = Math.Max(0, rightField.ClientSize.Width - 2 * folderButtonMarginPixels);
		int num = transferPreviewSurface.Bottom + folderButtonMarginPixels;
		liveConsoleSurface.Location = new Point(folderButtonMarginPixels, num);
		liveConsoleSurface.Width = Math.Max(0, rightField.ClientSize.Width - 2 * folderButtonMarginPixels);
		liveConsoleSurface.Height = Math.Max(0, rightField.ClientSize.Height - num - folderButtonMarginPixels);
	}

	private void ConfigureFprRootFromEnvironment()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("FPR_LITE_ROOT");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			environmentVariable = Path.TrimEndingDirectorySeparator(environmentVariable.Trim());
			if (!fprOutboxConnector.TrySelectRoot(environmentVariable, out string errorMessage))
			{
				folderPickerButton.Text = "FPR: ошибка";
				folderPickerButton.IconColor = AppColors.Error;
				statusToolTip.SetToolTip(folderPickerButton, string.IsNullOrWhiteSpace(errorMessage) ? "Не удалось подключить папку FPR." : errorMessage);
			}
			else
			{
				folderPickerButton.Text = "FPR подключён";
				folderPickerButton.IconColor = AppColors.Success;
				statusToolTip.SetToolTip(folderPickerButton, environmentVariable);
			}
		}
	}

	private void FolderPickerButton_Click(object? sender, EventArgs e)
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = "Выберите папку для будущего доступа FPR",
			UseDescriptionForTitle = true,
			ShowNewFolderButton = false
		};
		if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
		{
			string text = Path.TrimEndingDirectorySeparator(folderBrowserDialog.SelectedPath);
			if (!fprOutboxConnector.TrySelectRoot(text, out string _))
			{
				SetVisibleStatusMessage("Не удалось подготовить папку FPR.", AppStatusTone.Error);
				return;
			}
			string fileName = Path.GetFileName(text);
			folderPickerButton.Text = (string.IsNullOrWhiteSpace(fileName) ? text : fileName);
			folderPickerButton.IconColor = AppColors.Success;
		}
	}

	private void AddProjectButton_Click(object? sender, EventArgs e)
	{
		SetNewProjectEditorVisible(!newProjectEditorVisible);
	}

	private async void OpenProjectButton_Click(object? sender, EventArgs e)
	{
		await OpenExistingProjectFromMonitorAsync();
	}

	private async void NewProjectNameBox_KeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Escape)
		{
			e.SuppressKeyPress = true;
			SetNewProjectEditorVisible(visible: false);
		}
		else if (e.KeyCode == Keys.Return)
		{
			e.SuppressKeyPress = true;
			await CreateProjectFromMonitorAsync();
		}
	}

	private async void ProjectSelector_SelectedIndexChanged(object? sender, EventArgs e)
	{
		if (!suppressProjectSelectionChange)
		{
			await SwitchSelectedProjectAsync();
		}
	}

	private async Task CreateProjectFromMonitorAsync()
	{
		string text = newProjectNameBox.Text.Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			SetVisibleStatusMessage("Введите название проекта.", AppStatusTone.Warning);
			newProjectNameBox.Focus();
			return;
		}
		if (text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || text == "." || text == "..")
		{
			SetVisibleStatusMessage("Название проекта содержит символы, которые нельзя использовать в имени папки.", AppStatusTone.Warning);
			newProjectNameBox.Focus();
			return;
		}
		using FolderBrowserDialog projectParentDialog = new FolderBrowserDialog
		{
			Description = "Выберите папку, внутри которой FPR создаст отдельную папку проекта «" + text + "».",
			UseDescriptionForTitle = true,
			ShowNewFolderButton = true
		};
		if (projectParentDialog.ShowDialog(this) != DialogResult.OK)
		{
			SetVisibleStatusMessage("Создание проекта отменено: папка размещения не выбрана.", AppStatusTone.Warning);
			return;
		}
		string text2 = Path.Combine(Path.TrimEndingDirectorySeparator(projectParentDialog.SelectedPath), text);
		try
		{
			if (Directory.Exists(text2))
			{
				SetVisibleStatusMessage("Папка «" + text2 + "» уже существует. Выберите другое название проекта или откройте существующий проект.", AppStatusTone.Warning);
				return;
			}
			Directory.CreateDirectory(text2);
		}
		catch (Exception ex)
		{
			SetVisibleStatusMessage("FPR не смог создать папку проекта: " + ex.Message, AppStatusTone.Error);
			return;
		}
		SetProjectControlsEnabled(enabled: false);
		activeProjectLabel.Text = "Создание проекта...";
		try
		{
			FprProjectCreateResult result = await fprProjectClient.CreateAndActivateAsync(text, text2);
			if (!isClosing)
			{
				if (!result.Success)
				{
					activeProjectLabel.Text = "Проект не создан";
					SetVisibleStatusMessage(ProjectErrorMessage(result.Error), AppStatusTone.Error);
					return;
				}
				newProjectNameBox.Clear();
				SetNewProjectEditorVisible(visible: false);
				await RefreshProjectListAsync();
				SetVisibleStatusMessage("Проект «" + result.ProjectTitle + "» создан в выбранной папке и открыт.", AppStatusTone.Success);
			}
		}
		finally
		{
			if (!isClosing)
			{
				SetProjectControlsEnabled(enabled: true);
			}
		}
	}

	private async Task OpenExistingProjectFromMonitorAsync()
	{
		using FolderBrowserDialog projectFolderDialog = new FolderBrowserDialog
		{
			Description = "Выберите существующую папку проекта FPR",
			UseDescriptionForTitle = true,
			ShowNewFolderButton = false
		};
		if (projectFolderDialog.ShowDialog(this) != DialogResult.OK)
		{
			SetVisibleStatusMessage("Открытие проекта отменено: папка не выбрана.", AppStatusTone.Info);
			return;
		}
		string storageRoot = Path.TrimEndingDirectorySeparator(projectFolderDialog.SelectedPath);
		SetProjectControlsEnabled(enabled: false);
		activeProjectLabel.Text = "Открытие проекта из папки...";
		try
		{
			FprProjectOpenResult result = await fprProjectClient.OpenAndActivateAsync(storageRoot);
			if (!result.Success)
			{
				activeProjectLabel.Text = "Проект не открыт";
				SetVisibleStatusMessage(ProjectErrorMessage(result.Error), AppStatusTone.Error);
			}
			else if (!result.IdentityPreserved)
			{
				activeProjectLabel.Text = "Проект не открыт";
				SetVisibleStatusMessage("FPR не подтвердил сохранение PROJECT_ID существующего проекта.", AppStatusTone.Error);
			}
			else
			{
				await RefreshProjectListAsync();
				SetVisibleStatusMessage("Проект «" + result.ProjectTitle + "» открыт из выбранной папки без изменения PROJECT_ID.", AppStatusTone.Success);
			}
		}
		finally
		{
			SetProjectControlsEnabled(enabled: true);
		}
	}

	private async Task SwitchSelectedProjectAsync()
	{
		if (!(projectSelector.SelectedItem is FprProjectListItem fprProjectListItem))
		{
			SetVisibleStatusMessage("Выберите проект.", AppStatusTone.Warning);
		}
		else
		{
			if (string.Equals(fprProjectListItem.ProjectId, activeProjectId, StringComparison.Ordinal))
			{
				return;
			}
			SetProjectControlsEnabled(enabled: false);
			activeProjectLabel.Text = "Открытие проекта...";
			try
			{
				FprProjectSwitchResult result = await fprProjectClient.SwitchProjectAsync(fprProjectListItem.ProjectId);
				if (!isClosing)
				{
					if (!result.Success)
					{
						SetVisibleStatusMessage(ProjectErrorMessage(result.Error), AppStatusTone.Error);
						await RefreshProjectListAsync();
					}
					else
					{
						await RefreshProjectListAsync();
						SetVisibleStatusMessage("Проект «" + result.ProjectTitle + "» открыт.", AppStatusTone.Success);
					}
				}
			}
			finally
			{
				if (!isClosing)
				{
					SetProjectControlsEnabled(enabled: true);
				}
			}
		}
	}

	private async Task RefreshProjectListAsync()
	{
		FprProjectListResult fprProjectListResult = await fprProjectClient.ListProjectsAsync();
		if (isClosing)
		{
			return;
		}
		if (!fprProjectListResult.Success)
		{
			activeProjectId = string.Empty;
			activeProjectTitle = string.Empty;
			activeProjectStorageRoot = string.Empty;
			activeProjectLabel.Text = "FPR не подключён";
			activeProjectLabel.ForeColor = AppColors.Warning;
			suppressProjectSelectionChange = true;
			projectSelector.Items.Clear();
			suppressProjectSelectionChange = false;
			statusToolTip.SetToolTip(activeProjectLabel, fprProjectListResult.Error);
			return;
		}
		activeProjectId = fprProjectListResult.ActiveProjectId;
		FprProjectListItem activeProject = fprProjectListResult.Projects.FirstOrDefault((FprProjectListItem project) => string.Equals(project.ProjectId, activeProjectId, StringComparison.Ordinal));
		activeProjectTitle = activeProject?.ProjectTitle ?? string.Empty;
		activeProjectStorageRoot = activeProject?.StorageRoot ?? string.Empty;
		activeProjectLabel.Text = (((object)activeProject == null) ? "ПРОЕКТОВ НЕТ · СОЗДАТЬ (+) ИЛИ ОТКРЫТЬ ИЗ СПИСКА" : ("Открыт: " + activeProject.ProjectTitle));
		activeProjectLabel.ForeColor = AppColors.TextPrimary;
		statusToolTip.SetToolTip(activeProjectLabel, ((object)activeProject == null) ? "Создайте проект в выбранной вами папке или откройте зарегистрированный проект из списка." : ("Активный проект FPR: " + activeProject.StorageRoot));
		suppressProjectSelectionChange = true;
		projectSelector.BeginUpdate();
		try
		{
			projectSelector.Items.Clear();
			foreach (FprProjectListItem project in fprProjectListResult.Projects)
			{
				projectSelector.Items.Add(project);
			}
			if ((object)activeProject != null)
			{
				projectSelector.SelectedItem = projectSelector.Items.Cast<FprProjectListItem>().FirstOrDefault((FprProjectListItem project) => string.Equals(project.ProjectId, activeProject.ProjectId, StringComparison.Ordinal));
			}
			else if (projectSelector.Items.Count > 0)
			{
				projectSelector.SelectedIndex = 0;
			}
		}
		finally
		{
			projectSelector.EndUpdate();
			suppressProjectSelectionChange = false;
		}
		await RefreshGraphicTrunkAsync(showStatus: false);
	}

	private async Task RefreshGraphicTrunkAsync(bool showStatus)
	{
		if (string.IsNullOrWhiteSpace(activeProjectId))
		{
			workspaceDrawerHost.SetGraphicTrunkModel(null);
			return;
		}
		FprGraphicTrunkLoadResult fprGraphicTrunkLoadResult = await fprGraphicTrunkClient.LoadAsync(activeProjectId, activeProjectTitle);
		if (!isClosing)
		{
			workspaceDrawerHost.SetGraphicTrunkModel(fprGraphicTrunkLoadResult.Model);
			if (showStatus)
			{
				SetVisibleStatusMessage(fprGraphicTrunkLoadResult.Success ? $"GRAPHIC TRUNK (ГРАФ) · LADYBUGDB CONFIRMED · {fprGraphicTrunkLoadResult.Model.Nodes.Count} NODES (УЗЛОВ)" : ("GRAPHIC TRUNK (ГРАФ) · НЕТ ПОДТВЕРЖДЁННЫХ ДАННЫХ: " + fprGraphicTrunkLoadResult.Error), fprGraphicTrunkLoadResult.Success ? AppStatusTone.Success : AppStatusTone.Warning);
			}
		}
	}

	private IReadOnlyList<ConsiliumPilotDescriptor> GetConsiliumPilotRegistry()
	{
		PilotSiteOption active = pilotNavigationController.ActivePilot;
		bool activeReady = pilotWebViewSessionController.CurrentWebView?.CoreWebView2 != null;
		return pilotNavigationController.AvailablePilots.Select(delegate(PilotSiteOption pilot)
		{
			bool flag = (object)active != null && string.Equals(active.InstanceId, pilot.InstanceId, StringComparison.Ordinal);
			bool flag2 = HasSavedProfileState(pilot);
			bool flag3 = pilot.ProviderId.Contains("granite", StringComparison.OrdinalIgnoreCase) || pilot.Name.Contains("Granite", StringComparison.OrdinalIgnoreCase) || pilot.Name.Contains("Груня", StringComparison.OrdinalIgnoreCase);
			return new ConsiliumPilotDescriptor(pilot.InstanceId, pilot.Name, pilot.ProviderId, (flag && activeReady) || flag2, !flag3, (flag && activeReady) ? "ACTIVE_SESSION" : (flag2 ? "SAVED_SESSION" : "REGISTERED_NO_SESSION"));
		}).ToArray();
	}

	private PilotSiteOption? FindConsiliumPilot(string pilotRef)
	{
		return pilotNavigationController.AvailablePilots.FirstOrDefault((PilotSiteOption pilot) => string.Equals(pilot.InstanceId, pilotRef, StringComparison.Ordinal));
	}

	private async Task<(PilotSiteOption? Pilot, WebView2? WebView, string Error)> EnsureConsiliumPilotAsync(string pilotRef)
	{
		PilotSiteOption requested = FindConsiliumPilot(pilotRef);
		if ((object)requested == null)
		{
			return (Pilot: null, WebView: null, Error: "PARTICIPANT_NOT_REGISTERED");
		}
		PilotSiteOption activePilot = pilotNavigationController.ActivePilot;
		if ((object)activePilot == null || !string.Equals(activePilot.InstanceId, requested.InstanceId, StringComparison.Ordinal))
		{
			suppressSiteSelectionChange = true;
			try
			{
				siteSelector.SelectedItem = requested;
			}
			finally
			{
				suppressSiteSelectionChange = false;
			}
			await ActivateSelectedPilotAsync(requested);
		}
		activePilot = pilotNavigationController.ActivePilot;
		WebView2 currentWebView = pilotWebViewSessionController.CurrentWebView;
		bool num = (object)activePilot != null && string.Equals(activePilot.InstanceId, requested.InstanceId, StringComparison.Ordinal);
		workspaceDrawerHost.RefreshConsiliumRegistry();
		return (num && currentWebView?.CoreWebView2 != null) ? (Pilot: requested, WebView: currentWebView, Error: string.Empty) : (Pilot: requested, WebView: currentWebView, Error: "PARTICIPANT_UNAVAILABLE");
	}

	private async Task<ConsiliumActionResult> SendConsiliumPromptAsync(string pilotRef, string prompt)
	{
		var (pilot, webView, text) = await EnsureConsiliumPilotAsync(pilotRef);
		if ((object)pilot == null || webView?.CoreWebView2 == null)
		{
			return new ConsiliumActionResult(Success: false, "Консилиум: " + text + ".");
		}
		ChatPromptWriteResult chatPromptWriteResult = await ChatPromptWriter.WriteAsync(webView, prompt);
		return new ConsiliumActionResult(chatPromptWriteResult.Success, chatPromptWriteResult.Success ? ("Консилиум: независимая задача вставлена в " + pilot.Name + " · нажмите Enter.") : ("Консилиум: задача не вставлена: " + chatPromptWriteResult.ErrorMessage));
	}

	private async Task<ConsiliumCaptureResult> CaptureConsiliumOpinionAsync(string pilotRef)
	{
		var (pilot, webView, error) = await EnsureConsiliumPilotAsync(pilotRef);
		if ((object)pilot == null || webView?.CoreWebView2 == null)
		{
			return new ConsiliumCaptureResult(Success: false, pilotRef, pilot?.Name ?? string.Empty, string.Empty, error);
		}
		string text = await ChatResponseTransfer.CaptureLastResponseAsync(webView);
		if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "Страница чата не готова.", StringComparison.Ordinal) || string.Equals(text, "Кнопка «Копировать» последнего ответа не найдена.", StringComparison.Ordinal) || string.Equals(text, "Текст не получен.", StringComparison.Ordinal))
		{
			return new ConsiliumCaptureResult(Success: false, pilot.InstanceId, pilot.Name, string.Empty, string.IsNullOrWhiteSpace(text) ? "ответ не получен" : text);
		}
		return new ConsiliumCaptureResult(Success: true, pilot.InstanceId, pilot.Name, text, string.Empty);
	}

	private async Task<ConsiliumActionResult> PrepareConsiliumVerdictAsync(string verdict)
	{
		if (string.IsNullOrWhiteSpace(activeProjectId))
		{
			return new ConsiliumActionResult(Success: false, "Owner Verdict: сначала выберите активный проект.");
		}
		if (!string.IsNullOrWhiteSpace(pendingMachineFrame))
		{
			return new ConsiliumActionResult(Success: false, "Owner Verdict: справа уже ожидает другой проверенный объект.");
		}
		string text = verdict.Trim();
		if (text.Length > 512000)
		{
			return new ConsiliumActionResult(Success: false, "Owner Verdict слишком велик для одного смыслового объекта.");
		}
		string[] source = new string[4] { "FPR_FRAME_BEGIN_V1", "FPR_PAYLOAD_BEGIN", "FPR_PAYLOAD_END", "FPR_FRAME_END_V1" };
		HashSet<string> hashSet = (from line in text.Replace("\r\n", "\n").Split('\n')
			select line.Trim()).ToHashSet<string>(StringComparer.Ordinal);
		if (source.Any(hashSet.Contains))
		{
			return new ConsiliumActionResult(Success: false, "Owner Verdict содержит зарезервированный маркер протокола.");
		}
		string text2 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
		string text3 = "Consilium Owner Verdict " + text2.Substring(0, 12);
		string frame = "FPR_FRAME_BEGIN_V1\r\nFRAME_TYPE: SEMANTIC_OBJECT\r\nSCHEMA: FPR_SEMANTIC_OBJECT_V1\r\nPROJECT_ID: " + activeProjectId + "\r\nOBJECT_TYPE: decision\r\nTARGET: 05_DECISIONS/item\r\nSTATUS: awaiting_roman_approval\r\nSOURCE: pilot\r\nTITLE: " + text3 + "\r\nWRITE_BASIS: explicit_user_request\r\nMODE: create\r\nFPR_PAYLOAD_BEGIN\r\n" + text + "\r\nFPR_PAYLOAD_END\r\nFPR_FRAME_END_V1";
		FprFramePreflightResult fprFramePreflightResult = await PreflightCurrentPilotFrameAsync(frame);
		if (!fprFramePreflightResult.Success || !fprFramePreflightResult.AllowAccept || fprFramePreflightResult.RouteRead)
		{
			return new ConsiliumActionResult(Success: false, string.IsNullOrWhiteSpace(fprFramePreflightResult.PanelMessage) ? ("Owner Verdict не прошёл FPR preflight: " + fprFramePreflightResult.Error) : fprFramePreflightResult.PanelMessage);
		}
		transferPreview.Text = GetFprFrameDisplayText(frame);
		pendingMachineFrame = frame;
		bool accepted = await TransferPendingFrameAsync();
		return new ConsiliumActionResult(accepted, accepted ? "Owner Verdict проверен и передан в FPR." : "Owner Verdict проверен, но FPR его не принял. Повторите передачу общей кнопкой справа.");
	}

	private FprDownloadProjectContext GetDownloadProjectContext()
	{
		return new FprDownloadProjectContext(activeProjectId, activeProjectTitle, activeProjectStorageRoot);
	}

	private async void FprDevelopmentButton_Click(object? sender, EventArgs e)
	{
		string environmentVariable = Environment.GetEnvironmentVariable("FPR_RELEASE_ROOT");
		if (string.IsNullOrWhiteSpace(environmentVariable) || !Path.IsPathFullyQualified(environmentVariable) || !Directory.Exists(environmentVariable))
		{
			SetVisibleStatusMessage("FPR DEVELOPMENT недоступен: launcher не передал FPR_RELEASE_ROOT.", AppStatusTone.Error);
			return;
		}
		string text = Directory.GetParent(environmentVariable)?.FullName ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
		{
			SetVisibleStatusMessage("Не удалось определить корень FPR DEVELOPMENT.", AppStatusTone.Error);
			return;
		}
		string developmentRoot = Path.Combine(text, "FPR_DEVELOPMENT");
		SetProjectControlsEnabled(enabled: false);
		SetVisibleStatusMessage("Готовлю безопасный снимок FPR DEVELOPMENT...", AppStatusTone.Info);
		try
		{
			await FprDevelopmentSeeder.PrepareSnapshotAsync(environmentVariable, developmentRoot);
			FprProjectListResult fprProjectListResult = await fprProjectClient.ListProjectsAsync();
			FprProjectListItem fprProjectListItem = (fprProjectListResult.Success ? fprProjectListResult.Projects.FirstOrDefault((FprProjectListItem p) => string.Equals(Path.TrimEndingDirectorySeparator(p.StorageRoot), Path.TrimEndingDirectorySeparator(developmentRoot), StringComparison.OrdinalIgnoreCase)) : null);
			if ((object)fprProjectListItem != null)
			{
				FprProjectSwitchResult fprProjectSwitchResult = await fprProjectClient.SwitchProjectAsync(fprProjectListItem.ProjectId);
				if (!fprProjectSwitchResult.Success)
				{
					throw new InvalidOperationException(fprProjectSwitchResult.Error);
				}
			}
			else
			{
				FprProjectCreateResult fprProjectCreateResult = await fprProjectClient.CreateAndActivateAsync("FPR DEVELOPMENT", developmentRoot);
				if (!fprProjectCreateResult.Success)
				{
					throw new InvalidOperationException(fprProjectCreateResult.Error);
				}
			}
			await RefreshProjectListAsync();
			SetVisibleStatusMessage("FPR DEVELOPMENT открыт. Работающий FPR не изменён; проект содержит отдельный снимок.", AppStatusTone.Success);
		}
		catch (Exception ex)
		{
			SetVisibleStatusMessage("FPR DEVELOPMENT: " + ex.Message, AppStatusTone.Error);
		}
		finally
		{
			if (!isClosing)
			{
				SetProjectControlsEnabled(enabled: true);
			}
		}
	}

	private void SetProjectControlsEnabled(bool enabled)
	{
		projectSelector.Enabled = enabled;
		addProjectButton.Enabled = enabled;
		openProjectButton.Enabled = enabled;
		fprDevelopmentButton.Enabled = enabled;
		newProjectNameBox.Enabled = enabled;
	}

	private void SetNewProjectEditorVisible(bool visible)
	{
		newProjectEditorVisible = visible;
		newProjectNameBox.Visible = visible;
		newProjectEditorRowStyle.Height = (visible ? ((float)ScaleLogicalPixels(44, base.DeviceDpi)) : 0f);
		addProjectButton.Icon = (visible ? AppButtonIcon.Minus : AppButtonIcon.Plus);
		PositionLeftPanelControls();
		projectSurface.PerformLayout();
		if (visible)
		{
			newProjectNameBox.Focus();
			newProjectNameBox.SelectAll();
		}
		else
		{
			newProjectNameBox.Clear();
		}
	}

	private static string ProjectErrorMessage(string error)
	{
		return error switch
		{
			"CURRENT_PROJECT_TEMP_NOT_EMPTY" => "Сначала завершите текущий блок FPR.", 
			"PROJECT_NOT_REGISTERED" => "Выбранный проект больше не найден.", 
			"PROJECT_ROOT_MISSING" => "Папка выбранного проекта не найдена.", 
			"PROJECT_MEMORY_MISSING" => "Память выбранного проекта не найдена.", 
			"PROJECT_CABINET_PREFLIGHT_FAILED" => "Шкаф выбранного проекта не прошёл проверку.", 
			"PROJECT_TITLE_ALREADY_REGISTERED" => "Проект с таким названием уже существует.", 
			"PROJECT_TITLE_TOO_LONG" => "Название проекта слишком длинное.", 
			"PROJECT_TITLE_INVALID_CHARACTER" => "В названии проекта есть запрещённый символ.", 
			"PROJECT_TITLE_INVALID_WINDOWS_ENDING" => "Название не должно заканчиваться точкой или пробелом.", 
			"FPR_HTTP_UNAVAILABLE" => "Тело FPR не подключено.", 
			_ => string.IsNullOrWhiteSpace(error) ? "Проект не создан." : ("Проект не создан: " + error), 
		};
	}

	private async Task<FprFramePreflightResult> PreflightCurrentPilotFrameAsync(string text)
	{
		PilotSiteOption activePilot = pilotNavigationController.ActivePilot;
		WebView2 currentWebView = pilotWebViewSessionController.CurrentWebView;
		string text2 = activePilot?.InstanceId ?? string.Empty;
		string text3 = currentWebView?.Source?.AbsoluteUri ?? string.Empty;
		string sessionId = pilotContactSessionTracker.Observe(activePilot, currentWebView?.Source).SessionId;
		if (string.IsNullOrWhiteSpace(text2) || string.IsNullOrWhiteSpace(text3) || string.IsNullOrWhiteSpace(sessionId))
		{
			return new FprFramePreflightResult(Success: false, AllowAccept: false, RouteRead: false, string.Empty, string.Empty, "PILOT_SOURCE_AND_CONTACT_SESSION_REQUIRED", "PROTOCOL FAIL · PILOT/CONTACT SESSION НЕ ГОТОВЫ");
		}
		return await fprOutboxConnector.PreflightPilotFrameAsync(text, text2, text3, sessionId);
	}

	// FIX (сессия 2 из 2, тема: "считать ответ"): в 4.084 этот обработчик сам
	// вызывал TransferPendingFrameAsync() сразу после успешной проверки кадра
	// (READ_AND_TRANSFER_ARE_ONE_OPERATION), лишая владельца шанса посмотреть
	// считанный ответ перед отправкой в FPR. Возвращаем сценарий 4.015: клик
	// только считывает ответ Pilot, показывает предпросмотр и включает
	// отдельную кнопку "Принять в FPR" — передачу всегда запускает владелец
	// вторым, отдельным кликом.
	private async void CaptureResponseButton_Click(object? sender, EventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(pendingMachineFrame))
		{
			SetVisibleStatusMessage("ОТВЕТ УЖЕ СЧИТАН · СНАЧАЛА ПРИНЯТЬ В FPR", AppStatusTone.Warning);
			return;
		}
		captureResponseButton.Enabled = false;
		captureResponseButton.Text = "Считываю…";
		transferButton.Enabled = false;
		pendingMachineFrame = string.Empty;
		try
		{
			SetVisibleStatusMessage("Считываю последний ответ Pilot...", AppStatusTone.Neutral);
			ResponseTransferResult responseTransferResult = await responseTransferController.RequestTransferAsync();
			if (isClosing)
			{
				return;
			}
			switch (responseTransferResult.Status)
			{
			case ResponseTransferStatus.Completed:
			{
				if (IsTransferDiagnosticMessage(responseTransferResult.Text))
				{
					string text = (string.IsNullOrWhiteSpace(responseTransferResult.Text) ? "Текст не получен." : responseTransferResult.Text.Trim());
					transferPreview.Text = text;
					SetVisibleStatusMessage(text, AppStatusTone.Warning);
					break;
				}
				if (!TryExtractFprFrames(responseTransferResult.Text, out string machineText))
				{
					transferPreview.Text = responseTransferResult.Text;
					SetVisibleStatusMessage("FPR FRAME НЕ НАЙДЕН В ПОСЛЕДНЕМ ОТВЕТЕ PILOT", AppStatusTone.Warning);
					break;
				}
				string sessionId = pilotContactSessionTracker.Observe(pilotNavigationController.ActivePilot, pilotWebViewSessionController.CurrentWebView?.Source).SessionId;
				string captureKey = sessionId + Environment.NewLine + machineText;
				if (string.Equals(captureKey, lastCapturedMachineFrameKey, StringComparison.Ordinal))
				{
					transferPreview.Text = GetFprFrameDisplayText(machineText);
					SetVisibleStatusMessage("ЭТОТ ОТВЕТ УЖЕ СЧИТАН · ЖДУ НОВЫЙ ОТВЕТ PILOT", AppStatusTone.Warning);
					break;
				}
				transferPreview.Text = GetFprFrameDisplayText(machineText);
				transferPreview.SelectionStart = transferPreview.TextLength;
				transferPreview.SelectionLength = 0;
				FprFramePreflightResult fprFramePreflightResult = await PreflightCurrentPilotFrameAsync(machineText);
				if (!fprFramePreflightResult.Success)
				{
					SetVisibleStatusMessage(string.IsNullOrWhiteSpace(fprFramePreflightResult.PanelMessage) ? "PROTOCOL FAIL · MACHINE FRAME НЕ ПРОШЁЛ ПРОВЕРКУ" : fprFramePreflightResult.PanelMessage, AppStatusTone.Error);
					break;
				}
				lastCapturedMachineFrameKey = captureKey;
				if (fprFramePreflightResult.RouteRead)
				{
					transferButton.Enabled = false;
					await ExecutePilotReadRequestAsync(machineText);
					break;
				}
				pendingMachineFrame = fprFramePreflightResult.AllowAccept ? machineText : string.Empty;
				transferButton.Text = "Принять в FPR";
				transferButton.Enabled = fprFramePreflightResult.AllowAccept;
				SetVisibleStatusMessage(
					string.IsNullOrWhiteSpace(fprFramePreflightResult.PanelMessage)
						? (fprFramePreflightResult.AllowAccept ? "MACHINE FRAME FPR ПРОВЕРЕН" : "FPR НЕ РАЗРЕШИЛ ПРИНЯТИЕ ЭТОГО FRAME")
						: fprFramePreflightResult.PanelMessage,
					fprFramePreflightResult.AllowAccept ? AppStatusTone.Success : AppStatusTone.Warning);
				break;
			}
			case ResponseTransferStatus.Failed:
				SetVisibleStatusMessage("СЧИТЫВАНИЕ НЕ УДАЛОСЬ: " + (string.IsNullOrWhiteSpace(responseTransferResult.ErrorMessage) ? "неизвестная ошибка" : responseTransferResult.ErrorMessage.Trim()), AppStatusTone.Error);
				break;
			case ResponseTransferStatus.Busy:
				SetVisibleStatusMessage("СЧИТЫВАНИЕ УЖЕ ВЫПОЛНЯЕТСЯ", AppStatusTone.Warning);
				break;
			}
		}
		finally
		{
			if (!isClosing)
			{
				captureResponseButton.Text = "Считать ответ";
				captureResponseButton.Enabled = string.IsNullOrWhiteSpace(pendingMachineFrame);
			}
		}
	}

	// FIX (сессия 2 из 2): отдельный обработчик второй, ранее "отключённой"
	// кнопки — восстанавливает шаг "Принять в FPR" из сценария 4.015. Вся
	// бизнес-логика передачи (preflight, fprOutboxConnector, обновление
	// Graphic Trunk, вставка ответа FPR обратно в чат) уже была аккуратно
	// вынесена в переиспользуемый TransferPendingFrameAsync() и используется
	// без изменений — здесь только восстановлена кнопка, которая его вызывает.
	private async void TransferButton_Click(object? sender, EventArgs e)
	{
		await TransferPendingFrameAsync();
	}

	private static bool TryExtractFprFrames(string? text, out string frameText)
	{
		frameText = string.Empty;
		string text2 = text ?? string.Empty;
		List<string> list = new List<string>();
		int num = 0;
		while (num < text2.Length)
		{
			int num2 = text2.IndexOf("FPR_FRAME_BEGIN_V1", num, StringComparison.Ordinal);
			if (num2 < 0)
			{
				break;
			}
			int num3 = text2.IndexOf("FPR_FRAME_END_V1", num2 + "FPR_FRAME_BEGIN_V1".Length, StringComparison.Ordinal);
			if (num3 < 0)
			{
				return false;
			}
			int num4 = num3 + "FPR_FRAME_END_V1".Length;
			string text3 = text2.Substring(num2, num4 - num2).Trim();
			if (text3.Length > 0)
			{
				list.Add(text3);
			}
			num = num4;
		}
		if (list.Count == 0)
		{
			return false;
		}
		frameText = string.Join(Environment.NewLine, list);
		return true;
	}

	private static bool IsFprReadRequest(string? text)
	{
		string text2 = (text ?? string.Empty).Replace("\\_", "_", StringComparison.Ordinal).Replace("\\<", "<", StringComparison.Ordinal).Replace("\\>", ">", StringComparison.Ordinal);
		if (text2.Contains("<<<FPR_READ_REQUEST_V1", StringComparison.Ordinal))
		{
			return text2.Contains("<<<END_FPR_READ_REQUEST_V1>>>", StringComparison.Ordinal);
		}
		return false;
	}

	private static string GetFprFrameDisplayText(string machineText)
	{
		string text = machineText ?? string.Empty;
		List<string> list = new List<string>();
		int num = 0;
		while (num < text.Length)
		{
			int num2 = text.IndexOf("FPR_FRAME_BEGIN_V1", num, StringComparison.Ordinal);
			if (num2 < 0)
			{
				break;
			}
			int num3 = num2 + "FPR_FRAME_BEGIN_V1".Length;
			int num4 = text.IndexOf("FPR_FRAME_END_V1", num3, StringComparison.Ordinal);
			if (num4 < 0)
			{
				return text.Trim();
			}
			string text2 = text.Substring(num3, num4 - num3).Trim();
			if (text2.Length > 0)
			{
				list.Add(text2);
			}
			num = num4 + "FPR_FRAME_END_V1".Length;
		}
		if (list.Count != 0)
		{
			return string.Join(Environment.NewLine + Environment.NewLine, list);
		}
		return text.Trim();
	}

	private async Task ExecutePilotReadRequestAsync(string requestText)
	{
		PilotSiteOption activePilot = pilotNavigationController.ActivePilot;
		WebView2 currentWebView = pilotWebViewSessionController.CurrentWebView;
		string text = activePilot?.InstanceId ?? string.Empty;
		string text2 = currentWebView?.Source?.AbsoluteUri ?? string.Empty;
		string sessionId = pilotContactSessionTracker.Observe(activePilot, currentWebView?.Source).SessionId;
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text2) || string.IsNullOrWhiteSpace(sessionId))
		{
			SetVisibleStatusMessage("FPR READ: PILOT ИЛИ CONTACT SESSION НЕ ГОТОВЫ", AppStatusTone.Warning);
			return;
		}
		SetVisibleStatusMessage("Pilot запросил чтение текущего шкафа FPR...", AppStatusTone.Neutral);
		FprDiscoveryDeliveryResult fprDiscoveryDeliveryResult = await fprOutboxConnector.RequestProjectReadAsync(requestText, text, text2, sessionId);
		if (!isClosing)
		{
			if (!fprDiscoveryDeliveryResult.Success || !fprDiscoveryDeliveryResult.HasText)
			{
				SetVisibleStatusMessage("FPR READ ОТКЛОНЁН: " + (string.IsNullOrWhiteSpace(fprDiscoveryDeliveryResult.Error) ? fprDiscoveryDeliveryResult.Status : fprDiscoveryDeliveryResult.Error), AppStatusTone.Error);
				return;
			}
			transferPreview.Text = fprDiscoveryDeliveryResult.Text;
			ChatPromptWriteResult chatPromptWriteResult = await ChatPromptWriter.WriteAsync(currentWebView, fprDiscoveryDeliveryResult.Text);
			SetVisibleStatusMessage(chatPromptWriteResult.Success ? "FPR ПРОЧИТАЛ ТЕКУЩИЙ ШКАФ · ОТВЕТ ВСТАВЛЕН В ЧАТ · НАЖМИТЕ ENTER" : ("FPR ПРОЧИТАЛ ТЕКУЩИЙ ШКАФ · ОТВЕТ СПРАВА · ВСТАВКА В ЧАТ НЕ УДАЛАСЬ: " + chatPromptWriteResult.ErrorMessage), chatPromptWriteResult.Success ? AppStatusTone.Success : AppStatusTone.Warning);
		}
	}

	// FIX (сессия 2 из 2, тема: "считать ответ"): состояние "занято/принято"
	// возвращено с captureResponseButton на transferButton — captureResponseButton
	// больше не эта кнопка, она снова относится только к шагу считывания.
	// Восстановлены leaveTransferDisabled/operationAccepted из сценария 4.015,
	// чтобы кнопка "Принять в FPR" корректно оставалась выключенной после
	// неуспешной попытки, а не включалась вслепую в общем finally.
	private async Task<bool> TransferPendingFrameAsync()
	{
		if (Interlocked.Exchange(ref transferOperationInProgress, 1) != 0)
		{
			SetVisibleStatusMessage("ОПЕРАЦИЯ УЖЕ ВЫПОЛНЯЕТСЯ · ПОВТОРНЫЙ КЛИК ИГНОРИРОВАН", AppStatusTone.Warning);
			return false;
		}
		transferButton.Enabled = false;
		transferButton.Text = "Принимаю…";
		SetVisibleStatusMessage("ОБРАБОТКА… FPR ПРИНЯЛ КЛИК", AppStatusTone.Neutral);
		string manualText = pendingMachineFrame.Trim();
		if (string.IsNullOrWhiteSpace(manualText) || IsTransferDiagnosticMessage(manualText))
		{
			Interlocked.Exchange(ref transferOperationInProgress, 0);
			transferPreview.Clear();
			transferButton.Text = "Принять в FPR";
			transferButton.Enabled = false;
			captureResponseButton.Text = "Считать ответ";
			captureResponseButton.Enabled = true;
			SetVisibleStatusMessage("Сначала считайте ответ Pilot.", AppStatusTone.Warning);
			return false;
		}
		string pilotName = pilotNavigationController.ActivePilot?.InstanceId ?? string.Empty;
		WebView2 currentWebView = pilotWebViewSessionController.CurrentWebView;
		string sourceUrl = currentWebView?.Source?.AbsoluteUri ?? string.Empty;
		string contactSessionId = pilotContactSessionTracker.Observe(pilotNavigationController.ActivePilot, currentWebView?.Source).SessionId;
		bool leaveTransferDisabled = false;
		bool operationAccepted = false;
		try
		{
			SetVisibleStatusMessage("Проверяю machine frame перед передачей в FPR...", AppStatusTone.Neutral);
			FprFramePreflightResult fprFramePreflightResult = await PreflightCurrentPilotFrameAsync(manualText);
			if (!fprFramePreflightResult.Success)
			{
				leaveTransferDisabled = true;
				SetVisibleStatusMessage(string.IsNullOrWhiteSpace(fprFramePreflightResult.PanelMessage) ? "PROTOCOL FAIL · MACHINE FRAME НЕ ПРОШЁЛ ПРОВЕРКУ" : fprFramePreflightResult.PanelMessage, AppStatusTone.Error);
				return false;
			}
			if (fprFramePreflightResult.RouteRead)
			{
				leaveTransferDisabled = true;
				await ExecutePilotReadRequestAsync(manualText);
				pendingMachineFrame = string.Empty;
				captureResponseButton.Text = "Считать ответ";
				captureResponseButton.Enabled = true;
				return true;
			}
			if (!fprFramePreflightResult.AllowAccept)
			{
				leaveTransferDisabled = true;
				SetVisibleStatusMessage(string.IsNullOrWhiteSpace(fprFramePreflightResult.PanelMessage) ? "FPR НЕ РАЗРЕШИЛ ПРИНЯТИЕ ЭТОГО FRAME" : fprFramePreflightResult.PanelMessage, AppStatusTone.Warning);
				return false;
			}
			SetVisibleStatusMessage("Machine frame проверен · тело FPR выполняет операцию...", AppStatusTone.Neutral);
			FprTransferReceipt receipt = await fprOutboxConnector.TransferToBodyAsync(manualText, pilotName, sourceUrl, contactSessionId);
			if (isClosing)
			{
				return false;
			}
			if (!receipt.Received)
			{
				string text = (string.IsNullOrWhiteSpace(receipt.Error) ? "неизвестная ошибка" : receipt.Error.Trim());
				SetVisibleStatusMessage("Тело FPR не ответило: " + text, AppStatusTone.Error);
				return false;
			}
			operationAccepted = receipt.Accepted;
			if (receipt.Accepted)
			{
				pendingMachineFrame = string.Empty;
				captureResponseButton.Text = "Считать ответ";
				captureResponseButton.Enabled = true;
				leaveTransferDisabled = true;
				transferButton.Text = "Принято ✓";
				transferButton.Enabled = false;
				SetVisibleStatusMessage("ПРИНЯТО В FPR · ВСТАВЛЯЮ СЛЕДУЮЩИЙ ШАГ…", AppStatusTone.Neutral);
				await RefreshGraphicTrunkAsync(showStatus: false);
			}
			string panelMessage = ((!string.IsNullOrWhiteSpace(receipt.PanelMessage)) ? receipt.PanelMessage.Trim() : (receipt.Accepted ? "ПРИНЯТО ТЕЛОМ FPR" : "ФОРМАТ НЕ СООТВЕТСТВУЕТ"));
			if (!string.IsNullOrWhiteSpace(receipt.OutboxText))
			{
				leaveTransferDisabled = true;
				transferPreview.Text = receipt.OutboxText;
				ChatPromptWriteResult chatPromptWriteResult = await ChatPromptWriter.WriteAsync(currentWebView, receipt.OutboxText);
				string text2 = (chatPromptWriteResult.Success ? " · ОТВЕТ FPR ВСТАВЛЕН В ЧАТ · НАЖМИТЕ ENTER" : (" · ОТВЕТ FPR СПРАВА · ВСТАВКА В ЧАТ НЕ УДАЛАСЬ: " + chatPromptWriteResult.ErrorMessage));
				SetVisibleStatusMessage(panelMessage + text2, (chatPromptWriteResult.Success && receipt.Accepted) ? AppStatusTone.Success : AppStatusTone.Warning);
			}
			else
			{
				SetVisibleStatusMessage(panelMessage, receipt.Accepted ? AppStatusTone.Success : AppStatusTone.Warning);
			}
			return receipt.Accepted;
		}
		finally
		{
			Interlocked.Exchange(ref transferOperationInProgress, 0);
			if (!isClosing)
			{
				transferButton.Text = operationAccepted ? "Принято ✓" : "Принять в FPR";
				transferButton.Enabled = !leaveTransferDisabled;
			}
		}
	}

	private async Task TryStartPilotOnboardingAsync(string reason = "")
	{
		if (string.IsNullOrWhiteSpace(activeProjectId))
		{
			SetVisibleStatusMessage("РУКОПОЖАТИЕ НЕ НАЧАТО · СНАЧАЛА ОТКРОЙТЕ ИЛИ СОЗДАЙТЕ ПРОЕКТ", AppStatusTone.Warning);
		}
		else
		{
			if (isClosing || Interlocked.Exchange(ref onboardingDeliveryInProgress, 1) != 0)
			{
				return;
			}
			handshakeButton.Enabled = false;
			handshakeButton.Text = "Подготавливаю…";
			try
			{
				WebView2 webView = null;
				string pilotName = string.Empty;
				string sourceUrl = string.Empty;
				string contactSessionId = string.Empty;
				for (int attempt = 0; attempt < 24; attempt++)
				{
					PilotSiteOption activePilot = pilotNavigationController.ActivePilot;
					webView = pilotWebViewSessionController.CurrentWebView;
					pilotName = activePilot?.InstanceId ?? string.Empty;
					sourceUrl = webView?.Source?.AbsoluteUri ?? string.Empty;
					contactSessionId = pilotContactSessionTracker.Observe(activePilot, webView?.Source).SessionId;
					if (!string.IsNullOrWhiteSpace(pilotName) && !string.IsNullOrWhiteSpace(sourceUrl) && !string.IsNullOrWhiteSpace(contactSessionId) && webView?.CoreWebView2 != null && webView.IsHandleCreated)
					{
						break;
					}
					await Task.Delay(125);
				}
				if (string.IsNullOrWhiteSpace(pilotName) || string.IsNullOrWhiteSpace(sourceUrl) || string.IsNullOrWhiteSpace(contactSessionId) || webView?.CoreWebView2 == null || !webView.IsHandleCreated)
				{
					SetVisibleStatusMessage("PILOT НЕ ГОТОВ ДЛЯ РУКОПОЖАТИЯ ПОСЛЕ ОЖИДАНИЯ", AppStatusTone.Warning);
					return;
				}
				FprDiscoveryDeliveryResult delivery = await fprOutboxConnector.RequestPilotOnboardingAsync(pilotName, sourceUrl, contactSessionId, reason);
				if (!delivery.Success)
				{
					SetVisibleStatusMessage("FPR НЕ ПОДГОТОВИЛ РУКОПОЖАТИЕ: " + delivery.Error, AppStatusTone.Error);
					return;
				}
				if (!delivery.HasText)
				{
					SetVisibleStatusMessage("FPR НЕ ВЕРНУЛ ТЕКСТ ДЛЯ PILOT", AppStatusTone.Warning);
					return;
				}
				transferPreview.Text = delivery.Text;
				ChatPromptWriteResult chatPromptWriteResult = await ChatPromptWriter.WriteAsync(webView, delivery.Text);
				if (!chatPromptWriteResult.Success || isClosing)
				{
					SetVisibleStatusMessage("ТЕКСТ FPR ПОДГОТОВЛЕН СПРАВА · ВСТАВКА В ЧАТ НЕ УДАЛАСЬ: " + chatPromptWriteResult.ErrorMessage, AppStatusTone.Warning);
					return;
				}
				bool flag = string.Equals(delivery.Status, "pilot_protocol_handshake_required", StringComparison.OrdinalIgnoreCase);
				captureResponseButton.Text = "Считать ответ";
				captureResponseButton.Enabled = true;
				SetVisibleStatusMessage(flag ? "РУКОПОЖАТИЕ FPR ВСТАВЛЕНО В ЧАТ · НАЖМИТЕ ENTER" : "КОНТЕКСТ FPR ВСТАВЛЕН В ЧАТ · НАЖМИТЕ ENTER", AppStatusTone.Success);
			}
			finally
			{
				Interlocked.Exchange(ref onboardingDeliveryInProgress, 0);
				if (!isClosing)
				{
					handshakeButton.Text = "Рукопожатие";
					handshakeButton.Enabled = true;
				}
			}
		}
	}

	private static bool IsTransferDiagnosticMessage(string? text)
	{
		string text2 = text?.Trim() ?? string.Empty;
		if (text2.Length != 0 && !string.Equals(text2, "Текст не получен.", StringComparison.Ordinal) && !string.Equals(text2, "Кнопка «Копировать» не найдена.", StringComparison.Ordinal) && !string.Equals(text2, "Кнопка «Копировать» последнего ответа не найдена.", StringComparison.Ordinal) && !string.Equals(text2, "Страница чата не получила фокус.", StringComparison.Ordinal) && !string.Equals(text2, "Страница чата не готова.", StringComparison.Ordinal))
		{
			return string.Equals(text2, "Выделите нужный текст ответа и повторите.", StringComparison.Ordinal);
		}
		return true;
	}

	private void SetVisibleStatusMessage(string message, AppStatusTone tone)
	{
		SetStatusText(message, tone);
		statusLabel.Text = message;
		AppendLiveConsole("[MON] " + message);
	}

	private void AppendLiveConsole(string message)
	{
		if (!string.IsNullOrWhiteSpace(message))
		{
			string text = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
			string[] lines = liveConsole.Lines;
			if (lines.Length >= 120)
			{
				string[] array = new string[100];
				Array.Copy(lines, lines.Length - 100, array, 0, 100);
				liveConsole.Lines = array;
			}
			if (liveConsole.TextLength > 0)
			{
				liveConsole.AppendText(Environment.NewLine);
			}
			liveConsole.AppendText(text);
			liveConsole.SelectionStart = liveConsole.TextLength;
			liveConsole.ScrollToCaret();
		}
	}

	private async void BranchLoadStatusLabel_Click(object? sender, EventArgs e)
	{
		branchCharacterCountLabel.Enabled = false;
		branchLoadStatusLabel.Enabled = false;
		SetStatusText("Считаю всю ленту ChatGPT...", AppStatusTone.Neutral);
		try
		{
			BranchClipboardCaptureResult branchClipboardCaptureResult = await branchMeterController.RequestRefreshAsync(userInitiated: true);
			SetStatusText(branchClipboardCaptureResult.Success ? "Счётчик всей ветки обновлён." : branchClipboardCaptureResult.ErrorMessage, branchClipboardCaptureResult.Success ? AppStatusTone.Success : AppStatusTone.Warning);
		}
		finally
		{
			if (!isClosing)
			{
				branchCharacterCountLabel.Enabled = true;
				branchLoadStatusLabel.Enabled = true;
			}
		}
	}

	private void SetStatusText(string message, AppStatusTone tone)
	{
		bool flag = (uint)(tone - 3) <= 1u;
		bool flag2 = flag;
		bool flag3 = tone == AppStatusTone.Success;
		statusLabel.Text = (flag2 ? "● Ошибка" : (flag3 ? "● Готово" : "● Загрузка"));
		statusLabel.ForeColor = (flag2 ? AppColors.Error : (flag3 ? AppColors.Success : AppColors.Warning));
		PilotSiteOption pilotSiteOption = pilotNavigationController.PendingPilot ?? pilotNavigationController.ActivePilot ?? (siteSelector.SelectedItem as PilotSiteOption);
		string value = pilotSiteOption?.Name ?? "не выбран";
		string profileRoot = pilotWebViewSessionController.ProfileRoot;
		string value2 = ((profileRoot != null && (object)pilotSiteOption != null) ? Path.Combine(profileRoot, pilotSiteOption.ProfileKey) : "недоступен");
		statusToolTip.SetToolTip(statusLabel, $"Сервис: {value}\nПрофиль: {value2}\n{message}");
	}

	private bool IsChatGptPage(WebView2? candidate)
	{
		if (candidate?.CoreWebView2 != null)
		{
			PilotSiteOption activePilot = pilotNavigationController.ActivePilot;
			if ((object)activePilot != null && string.Equals(activePilot.ProviderId, "openai", StringComparison.OrdinalIgnoreCase))
			{
				string text = candidate.Source?.Host ?? string.Empty;
				if (!string.Equals(text, "chatgpt.com", StringComparison.OrdinalIgnoreCase))
				{
					return text.EndsWith(".chatgpt.com", StringComparison.OrdinalIgnoreCase);
				}
				return true;
			}
		}
		return false;
	}

	private void SetBranchMeterDisplay(int? characterCount)
	{
		if (!characterCount.HasValue)
		{
			branchCharacterCountLabel.Text = "НАГРУЗКА ВЕТКИ НЕ ОПРЕДЕЛЕНА";
			branchCharacterCountLabel.ForeColor = AppColors.TextMuted;
			branchLoadStatusLabel.Text = string.Empty;
			branchLoadStatusLabel.ForeColor = AppColors.TextMuted;
			branchLoadBar.ShowUndefined();
			branchMeterSurface.BorderColor = AppColors.Border;
		}
		else
		{
			BranchLoadPresentation branchLoadPresentation = BranchLoadRules.Calculate(characterCount.Value);
			Color color = branchLoadPresentation.State switch
			{
				BranchLoadState.Heavy => AppColors.BranchLoadHeavy, 
				BranchLoadState.NewBranch => AppColors.BranchLoadNewBranch, 
				_ => AppColors.BranchLoadNormal, 
			};
			branchCharacterCountLabel.Text = "ВЕТКА: " + FormatCharacterCount(branchLoadPresentation.CharacterCount) + " ЗНАКОВ";
			branchCharacterCountLabel.ForeColor = color;
			branchLoadStatusLabel.Text = FormatCharacterCount(branchLoadPresentation.Percentage) + "% · " + branchLoadPresentation.Status;
			branchLoadStatusLabel.ForeColor = color;
			branchLoadBar.ShowLoad(branchLoadPresentation.FillPercentage, color);
			branchMeterSurface.BorderColor = AppColors.Border;
		}
	}

	private static string FormatCharacterCount(int characterCount)
	{
		return characterCount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
	}

	private async void MainForm_Shown(object? sender, EventArgs e)
	{
		formShown = true;
		await RefreshProjectListAsync();
		PilotSiteOption initialSite = (siteSelector.SelectedItem as PilotSiteOption) ?? pilotNavigationController.AvailablePilots[0];
		bool hasSavedState = HasSavedProfileState(initialSite);
		if (await SwitchSiteAsync(initialSite, initialSite.HomeUri) && !hasSavedState)
		{
			ShowPilotSetup(initialSite);
		}
	}

	private void RefreshButton_Click(object? sender, EventArgs e)
	{
		WebView2 currentWebView = pilotWebViewSessionController.CurrentWebView;
		if (currentWebView?.CoreWebView2 != null)
		{
			branchMeterController.Reset();
			currentWebView.CoreWebView2.Reload();
		}
	}

	private void SiteSelector_DropDown(object? sender, EventArgs e)
	{
	}

	private async void SiteSelector_SelectedIndexChanged(object? sender, EventArgs e)
	{
		if (formShown && !isClosing && !suppressSiteSelectionChange && siteSelector.SelectedItem is PilotSiteOption pilotSiteOption)
		{
			renamePilotButton.Enabled = !pilotNavigationController.IsAddSlot(pilotSiteOption);
			if (pilotNavigationController.IsAddSlot(pilotSiteOption))
			{
				BeginAddPilot();
			}
			else
			{
				await ActivateSelectedPilotAsync(pilotSiteOption);
			}
		}
	}

	private async Task ActivateSelectedPilotAsync(PilotSiteOption site)
	{
		if (isClosing)
		{
			return;
		}
		if (pilotNavigationController.IsActive(site))
		{
			SetPilotSetupVisible(visible: false);
			return;
		}
		Uri targetUri = pilotNavigationController.BeginSetup(site, null);
		SetPilotSetupVisible(visible: false);
		if (await SwitchSiteAsync(site, targetUri))
		{
			pilotNavigationController.CompleteSetup();
			SetPilotSetupVisible(visible: false);
		}
		else
		{
			PilotSiteOption pilot = pilotNavigationController.CancelSetup();
			RestorePilotSelection(pilot);
		}
	}

	private void RefreshPilotSelector(PilotSiteOption? selected = null)
	{
		suppressSiteSelectionChange = true;
		try
		{
			siteSelector.Items.Clear();
			ComboBox.ObjectCollection items = siteSelector.Items;
			object[] items2 = pilotNavigationController.SelectorItems.ToArray();
			items.AddRange(items2);
			PilotSiteOption pilotSiteOption = selected ?? pilotNavigationController.ActivePilot ?? pilotNavigationController.AvailablePilots.FirstOrDefault();
			if ((object)pilotSiteOption != null)
			{
				siteSelector.SelectedItem = pilotSiteOption;
				renamePilotButton.Enabled = !pilotNavigationController.IsAddSlot(pilotSiteOption);
			}
			else if (siteSelector.Items.Count > 0)
			{
				siteSelector.SelectedIndex = 0;
			}
		}
		finally
		{
			suppressSiteSelectionChange = false;
		}
	}

	private void BeginAddPilot()
	{
		PilotSiteOption activePilot = pilotNavigationController.ActivePilot;
		using PilotAddDialog pilotAddDialog = new PilotAddDialog();
		if (pilotAddDialog.ShowDialog(this) != DialogResult.OK || (object)pilotAddDialog.HomeUri == null)
		{
			RestorePilotSelection(activePilot);
			return;
		}
		PilotSiteOption pilotSiteOption = pilotNavigationController.AddPilot(pilotAddDialog.ProviderId, pilotAddDialog.BaseDisplayName, pilotAddDialog.HomeUri);
		RefreshPilotSelector(pilotSiteOption);
		ShowPilotSetup(pilotSiteOption);
		SetVisibleStatusMessage("ДОБАВЛЕН PILOT: " + pilotSiteOption.Name + " · ВОЙДИТЕ В НУЖНЫЙ АККАУНТ", AppStatusTone.Info);
	}

	private void RenamePilotButton_Click(object? sender, EventArgs e)
	{
		if (!(siteSelector.SelectedItem is PilotSiteOption pilotSiteOption) || pilotNavigationController.IsAddSlot(pilotSiteOption))
		{
			SetVisibleStatusMessage("Выберите Pilot, который нужно переименовать.", AppStatusTone.Warning);
			return;
		}
		using PilotRenameDialog pilotRenameDialog = new PilotRenameDialog(pilotSiteOption.Name);
		if (pilotRenameDialog.ShowDialog(this) != DialogResult.OK)
		{
			return;
		}
		try
		{
			PilotSiteOption pilotSiteOption2 = pilotNavigationController.RenamePilot(pilotSiteOption, pilotRenameDialog.DisplayName);
			RefreshPilotSelector(pilotSiteOption2);
			SetVisibleStatusMessage("PILOT ПЕРЕИМЕНОВАН: " + pilotSiteOption2.Name, AppStatusTone.Success);
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "Переименовать Pilot", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			RestorePilotSelection(pilotSiteOption);
		}
	}

	private void ShowPilotSetup(PilotSiteOption site, bool focusAddress = true)
	{
		Uri currentActiveAddress = ((!pilotNavigationController.IsActive(site)) ? null : pilotWebViewSessionController.CurrentWebView?.Source);
		Uri uri = pilotNavigationController.BeginSetup(site, currentActiveAddress);
		addressBox.Text = uri.AbsoluteUri;
		SetPilotSetupVisible(visible: true);
		if (focusAddress)
		{
			addressBox.Focus();
			addressBox.SelectAll();
		}
	}

	private void SetPilotSetupVisible(bool visible)
	{
		pilotSetupVisible = visible;
		pilotSetupPanel.Visible = visible;
		addressBox.TabStop = visible;
		if (visible && pilotWebViewSessionController.IsProfileRootReady)
		{
			addressBox.ReadOnly = false;
			addressBox.ForeColor = AppColors.TextPrimary;
		}
		UpdatePilotSetupLayout(base.DeviceDpi);
		UpdateNavigationControls();
	}

	private void CancelPilotSetup_Click(object? sender, EventArgs e)
	{
		PilotSiteOption pilot = pilotNavigationController.CancelSetup();
		RestorePilotSelection(pilot);
		SetPilotSetupVisible(visible: false);
	}

	private void RestorePilotSelection(PilotSiteOption? pilot)
	{
		if ((object)pilot == null || object.Equals(siteSelector.SelectedItem, pilot))
		{
			return;
		}
		suppressSiteSelectionChange = true;
		try
		{
			siteSelector.SelectedItem = pilot;
		}
		finally
		{
			suppressSiteSelectionChange = false;
		}
	}

	private async void PilotSetupGo_Click(object? sender, EventArgs e)
	{
		PilotSiteOption pendingPilot = pilotNavigationController.PendingPilot;
		if ((object)pendingPilot == null)
		{
			return;
		}
		PilotAddressValidationResult pilotAddressValidationResult = pilotNavigationController.ValidateAddress(addressBox.Text);
		if (pilotAddressValidationResult.IsValid)
		{
			Uri address = pilotAddressValidationResult.Address;
			if ((object)address != null)
			{
				goButton.Enabled = false;
				cancelSetupButton.Enabled = false;
				try
				{
					WebView2 currentWebView = pilotWebViewSessionController.CurrentWebView;
					bool hasInitializedWebView = currentWebView?.CoreWebView2 != null;
					bool flag;
					if (pilotNavigationController.CanNavigateInCurrentSession(pendingPilot, hasInitializedWebView))
					{
						pilotNavigationController.RememberAddressForActivePilot(pendingPilot, address);
						currentWebView.Source = address;
						flag = true;
					}
					else
					{
						flag = await SwitchSiteAsync(pendingPilot, address);
					}
					if (flag)
					{
						pilotNavigationController.CompleteSetup();
						SetPilotSetupVisible(visible: false);
					}
					return;
				}
				catch (Exception ex)
				{
					SetStatusText("Не удалось начать переход: " + ex.Message, AppStatusTone.Error);
					return;
				}
				finally
				{
					cancelSetupButton.Enabled = true;
					UpdateNavigationControls();
				}
			}
		}
		SetStatusText(pilotAddressValidationResult.ErrorMessage, AppStatusTone.Warning);
	}

	private async Task<bool> SwitchSiteAsync(PilotSiteOption site, Uri targetUri)
	{
		if (isClosing || !pilotWebViewSessionController.IsProfileRootReady)
		{
			return false;
		}
		branchMeterController.Reset();
		SetBrowserControlsEnabled(enabled: false);
		SetStatusText("Открытие...", AppStatusTone.Info);
		try
		{
			PilotWebViewOpenResult pilotWebViewOpenResult = await pilotWebViewSessionController.OpenAsync(site.ProfileKey, targetUri);
			if (pilotWebViewOpenResult.Status == PilotWebViewOpenStatus.Superseded || isClosing)
			{
				return false;
			}
			if (pilotWebViewOpenResult.Status == PilotWebViewOpenStatus.Failed)
			{
				SetBrowserControlsEnabled(enabled: false);
				SetStatusText("WebView2 не запущен: " + pilotWebViewOpenResult.ErrorMessage, AppStatusTone.Error);
				MessageBox.Show(this, "Не удалось открыть " + site.Name + ".\n\n" + pilotWebViewOpenResult.ErrorMessage, "Faysy Patch Runner", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				return false;
			}
			WebView2 newWebView = pilotWebViewOpenResult.WebView;
			if (newWebView == null || pilotWebViewSessionController.CurrentWebView != newWebView)
			{
				return false;
			}
			downloadRouter.Attach(newWebView);
			pilotNavigationController.ConfirmActivePilot(site, pilotWebViewOpenResult.TargetUri);
			RestorePilotSelection(pilotNavigationController.ActivePilot);
			newWebView.CoreWebView2.NavigationStarting += delegate(object? _, CoreWebView2NavigationStartingEventArgs args)
			{
				branchMeterController.Reset();
				SetStatusText("Загрузка: " + args.Uri, AppStatusTone.Info);
			};
			newWebView.CoreWebView2.NavigationCompleted += delegate(object? _, CoreWebView2NavigationCompletedEventArgs args)
			{
				if (args.IsSuccess)
				{
					SetStatusText("Готово", AppStatusTone.Success);
				}
				else
				{
					SetStatusText($"Ошибка навигации: {args.WebErrorStatus}", AppStatusTone.Error);
				}
				UpdateNavigationControls();
				_ = branchMeterController.HandleNavigationCompletedAsync(newWebView);
			};
			newWebView.CoreWebView2.SourceChanged += delegate
			{
				branchMeterController.Reset();
				Uri source = newWebView.Source;
				if ((object)source != null)
				{
					if (pilotContactSessionTracker.Observe(site, source).ShouldResetUi)
					{
						transferPreview.Clear();
						handshakeButton.Enabled = true;
						SetVisibleStatusMessage("НОВЫЙ ЧАТ · ТРЕБУЕТСЯ РУКОПОЖАТИЕ", AppStatusTone.Warning);
					}
					if (pilotNavigationController.RememberAddressForActivePilot(site, source) && pilotSetupVisible && pilotNavigationController.PendingPilot == site)
					{
						addressBox.Text = source.AbsoluteUri;
					}
				}
			};
			newWebView.CoreWebView2.HistoryChanged += delegate
			{
				UpdateNavigationControls();
			};
			SetBrowserControlsEnabled(enabled: true);
			newWebView.Source = pilotWebViewOpenResult.TargetUri;
			return true;
		}
		catch (Exception ex)
		{
			if (isClosing)
			{
				return false;
			}
			pilotWebViewSessionController.DestroyCurrentWebView();
			SetBrowserControlsEnabled(enabled: false);
			SetStatusText("WebView2 не запущен: " + ex.Message, AppStatusTone.Error);
			MessageBox.Show(this, "Не удалось открыть " + site.Name + ".\n\n" + ex.Message, "Faysy Patch Runner", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return false;
		}
	}

	private bool HasSavedProfileState(PilotSiteOption site)
	{
		string profileRoot = pilotWebViewSessionController.ProfileRoot;
		if (profileRoot == null)
		{
			return false;
		}
		try
		{
			string path = Path.Combine(profileRoot, site.ProfileKey);
			return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
		}
		catch
		{
			return false;
		}
	}

	private void SetBrowserControlsEnabled(bool enabled)
	{
		siteSelector.Enabled = enabled || pilotWebViewSessionController.IsProfileRootReady;
		addressBox.ReadOnly = !enabled;
		addressBox.TabStop = enabled && pilotSetupVisible;
		addressBox.ForeColor = (enabled ? AppColors.TextPrimary : AppColors.TextDisabled);
		addressSurface.BorderColor = (enabled ? AppColors.ButtonBorder : AppColors.Border);
		refreshButton.Enabled = enabled;
		goButton.Enabled = enabled && pilotSetupVisible;
	}

	private void UpdateNavigationControls()
	{
		refreshButton.Enabled = pilotWebViewSessionController.CurrentWebView?.CoreWebView2 != null;
		goButton.Enabled = pilotSetupVisible && pilotWebViewSessionController.IsProfileRootReady;
	}

	private void AddressBox_KeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Return)
		{
			e.SuppressKeyPress = true;
			if (pilotSetupVisible && goButton.Enabled)
			{
				goButton.PerformClick();
			}
		}
	}
}
