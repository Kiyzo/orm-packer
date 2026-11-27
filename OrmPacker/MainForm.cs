using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OrmPacker;

public sealed class MainForm : Form
{
    private sealed class LoadedFile
    {
        public required TextureImage Image { get; init; }
        public required MapKind Kind { get; init; }
        public required string Path { get; init; }
    }

    private enum PreviewMode { Rgb, Rgba, R, G, B, A }

    private static readonly Channel[] AllChannels =
        [Channel.R, Channel.G, Channel.B, Channel.A];

    private sealed class SlotView : Panel
    {
        private static readonly Color CardBackground = Color.FromArgb(30, 37, 49);

        public int Index { get; }
        public PictureBox Thumb { get; } = new();
        public Label Title { get; } = new();
        public Label Info { get; } = new();
        public Label Meta { get; } = new();
        public Label Stats { get; } = new();
        public Label Notice { get; } = new();
        public Label Notice2 { get; } = new();
        public ModernComboBox Source { get; } = new();
        public ModernCheckBox Invert { get; } = new();
        public ModernNumberBox Number { get; } = new();
        public ModernTrackBar Slider { get; } = new();
        public ModernButton Clear { get; } = new();

        public TextureImage? Image;
        public string? Path;
        public MapKind? MismatchKind;
        public SourceComponent SourceComponent;
        public bool InvertValue;
        public byte SolidValue;
        // Mirrors Number ↔ Slider without re-firing each other's ValueChanged.
        private bool _syncingValue;

        // Diagnostics and manual mismatch are shown on their own full-width line,
        // like the web UI, instead of being squeezed into the stats label.
        public string DiagnosticWarning { get; set; } = "";
        private string _mismatchNotice = "";
        public string MismatchNotice
        {
            get => _mismatchNotice;
            set { _mismatchNotice = value; UpdateNotice(); }
        }

        public void UpdateNotice()
        {
            var lines = new[] { DiagnosticWarning, _mismatchNotice }
                .Where(part => !string.IsNullOrEmpty(part))
                .SelectMany(part => part.Split('\n'))
                .ToArray();
            Notice.Text = lines.FirstOrDefault() ?? "";
            Notice2.Text = lines.Length > 1 ? string.Join(" · ", lines.Skip(1)) : "";
            Notice.Visible = Notice.Text.Length > 0;
            Notice2.Visible = Notice2.Text.Length > 0;
        }

        public event Action<SlotView>? LoadClicked;
        public event Action<SlotView, string[]>? FilesDropped;
        public event Action? Changed;
        // Number-box edits commit immediately; slider drags are coalesced by the form.
        public event Action? ValueCommitted;
        public event Action? ValueDragging;

        public SlotView(int index, Color channelColor)
        {
            Index = index;
            BackColor = PanelColor;
            Height = 144;
            Dock = DockStyle.Top;
            // No default TableLayoutPanel margin: the fixed 144 px rows must stack exactly.
            Margin = Padding.Empty;

            var separator = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = BorderColor };

            Thumb.Size = new Size(56, 56);
            Thumb.SizeMode = PictureBoxSizeMode.Zoom;
            Thumb.BackColor = Color.FromArgb(11, 15, 22);
            Thumb.Cursor = Cursors.Hand;
            Thumb.Click += (_, _) => LoadClicked?.Invoke(this);
            Thumb.MouseEnter += (_, _) => Thumb.BackColor = Panel2;
            Thumb.MouseLeave += (_, _) => Thumb.BackColor = Color.FromArgb(11, 15, 22);

            var text = new Panel { Dock = DockStyle.Fill, BackColor = PanelColor };
            // The docked rows keep fixed heights; Info/Stats/Notice ellipsize as the card narrows.
            Title.Dock = DockStyle.Top;
            Title.Height = 20;
            Title.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            Title.ForeColor = channelColor;
            Title.BackColor = PanelColor;
            Info.Dock = DockStyle.Top;
            Info.Height = 16;
            Info.Font = new Font("Segoe UI", 9f);
            Info.ForeColor = MutedColor;
            Info.BackColor = PanelColor;
            Info.AutoSize = false;
            Info.AutoEllipsis = true;
            Stats.Dock = DockStyle.Top;
            Stats.Height = 16;
            Stats.Font = new Font("Segoe UI", 9f);
            Stats.ForeColor = MutedColor;
            Stats.BackColor = PanelColor;
            Stats.AutoSize = false;
            Stats.AutoEllipsis = true;
            Notice.Height = 15;
            Notice2.Height = 15;
            foreach (var label in new[] { Notice, Notice2 })
            {
                label.Dock = DockStyle.Top;
                label.Font = new Font("Segoe UI", 9f);
                label.ForeColor = WarnColor;
                label.BackColor = PanelColor;
                label.AutoSize = false;
                label.Visible = false;
            }
            // Docking stacks in reverse add order: Title ends up at the top.
            text.Controls.AddRange(new Control[] { Notice2, Notice, Stats, Info, Title });

            // The size/bit/extension row spans the whole slot, so it stays readable at 900 px.
            var metaRow = new Panel
            {
                Dock = DockStyle.Top, Height = 18, BackColor = PanelColor,
                Padding = new Padding(16, 0, 16, 0)
            };
            Meta.Dock = DockStyle.Fill;
            Meta.Font = new Font("Segoe UI", 9f);
            Meta.ForeColor = MutedColor;
            Meta.BackColor = PanelColor;
            Meta.AutoSize = false;
            metaRow.Controls.Add(Meta);

            Source.Items.AddRange(SourceNames());
            Source.SelectedIndex = 0;
            Source.Size = new Size(72, 26);
            Source.Margin = new Padding(8, 0, 0, 0);
            Source.SelectedIndexChanged += (_, _) =>
            {
                SourceComponent = (SourceComponent)Source.SelectedIndex;
                Changed?.Invoke();
            };

            Invert.Text = Localization.Get("slot.invert");
            Invert.ForeColor = MutedColor;
            Invert.Size = new Size(88, 26);
            Invert.Margin = new Padding(8, 0, 0, 0);
            Invert.CheckedChanged += (_, _) =>
            {
                InvertValue = Invert.Checked;
                Changed?.Invoke();
            };

            Number.Minimum = 0;
            Number.Maximum = 255;
            Number.Size = new Size(52, 26);
            Number.Margin = new Padding(8, 0, 0, 0);
            Number.ValueChanged += (_, _) =>
            {
                if (_syncingValue) return;
                _syncingValue = true;
                if (Slider.Value != Number.Value) Slider.Value = Number.Value;
                _syncingValue = false;
                SolidValue = (byte)Number.Value;
                ValueCommitted?.Invoke();
            };
            Slider.Minimum = 0;
            Slider.Maximum = 255;
            Slider.Size = new Size(70, 26);
            Slider.AccentColor = Accent;
            Slider.ValueChanged += (_, _) =>
            {
                if (_syncingValue) return;
                _syncingValue = true;
                if (Number.Value != Slider.Value) Number.Value = Slider.Value;
                _syncingValue = false;
                SolidValue = (byte)Slider.Value;
                ValueDragging?.Invoke();
            };

            Clear.Text = "×";
            Clear.Size = new Size(26, 26);
            Clear.BackColor = PanelColor;
            Clear.BaseColor = Panel2;
            Clear.TextColor = DangerColor;
            Clear.Radius = 5;
            Clear.Click += (_, _) =>
            {
                ClearSlot();
                Changed?.Invoke();
            };
            var clearCell = new Panel { Dock = DockStyle.Fill, BackColor = PanelColor };
            clearCell.Controls.Add(Clear);
            Clear.Location = new Point(clearCell.Width - Clear.Width, 0);
            // Location is only a starting point; Anchor keeps the button at the cell's right edge after layout.
            Clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var topRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 82, ColumnCount = 4, RowCount = 1,
                Padding = new Padding(16, 8, 16, 0), Margin = Padding.Empty, BackColor = PanelColor
            };
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            topRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            topRow.Controls.Add(Thumb, 0, 0);
            topRow.Controls.Add(text, 1, 0);
            topRow.Controls.Add(Source, 2, 0);
            topRow.Controls.Add(Invert, 3, 0);

            var bottomRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 42, ColumnCount = 3, RowCount = 1,
                Padding = new Padding(16, 0, 16, 8), Margin = Padding.Empty, BackColor = PanelColor
            };
            bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottomRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bottomRow.Controls.Add(Slider, 0, 0);
            bottomRow.Controls.Add(Number, 1, 0);
            bottomRow.Controls.Add(clearCell, 2, 0);

            Controls.AddRange(new Control[] { separator, metaRow, bottomRow, topRow });
            EnableDrop(this, files => FilesDropped?.Invoke(this, files));
        }

        /// <summary>Resets both the visible controls and the values used by the packer.</summary>
        public void ClearSlot()
        {
            Image = null;
            Path = null;
            MismatchKind = null;
            DiagnosticWarning = "";
            MismatchNotice = "";
            SourceComponent = SourceComponent.Red;
            Source.SelectedIndex = 0;
            InvertValue = false;
            Invert.Checked = false;
        }

    }

    /// <summary>The single map entry point in Convert mode: click or drop anywhere on the card.</summary>
    private sealed class SourceCard : Panel
    {
        public PictureBox Thumb { get; } = new();
        public Label FileName { get; } = new();
        public Label Details { get; } = new();
        public Label Hint { get; } = new();

        public event Action? OpenClicked;
        public event Action<string[]>? FilesDropped;

        public SourceCard()
        {
            BackColor = Panel2;
            Height = 80;
            AllowDrop = true;

            Thumb.SetBounds(12, 14, 52, 52);
            Thumb.SizeMode = PictureBoxSizeMode.Zoom;
            Thumb.BackColor = Color.FromArgb(11, 15, 22);
            Thumb.Cursor = Cursors.Hand;
            Thumb.Visible = false;

            FileName.SetBounds(72, 20, 204, 22);
            FileName.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            FileName.ForeColor = TextColor;
            FileName.AutoEllipsis = true;
            FileName.Visible = false;

            Details.SetBounds(72, 44, 204, 18);
            Details.Font = new Font("Segoe UI", 9f);
            Details.ForeColor = MutedColor;
            Details.AutoEllipsis = true;
            Details.Visible = false;

            Hint.Text = Localization.Get("convert.dropHint");
            Hint.Dock = DockStyle.Fill;
            Hint.TextAlign = ContentAlignment.MiddleCenter;
            Hint.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            Hint.ForeColor = MutedColor;
            Hint.BackColor = Color.Transparent;
            Hint.Visible = true;

            Controls.AddRange(new Control[] { Thumb, FileName, Details, Hint });
            EnableDrop(this, files => FilesDropped?.Invoke(files));
            EnableClick(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(BorderColor, 1f) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        public void ShowMap(bool hasMap)
        {
            Thumb.Visible = hasMap;
            FileName.Visible = hasMap;
            Details.Visible = hasMap;
            Hint.Visible = !hasMap;
        }

        // Labels (including the full-size empty hint) swallow parent clicks, so every
        // child forwards the click to the file picker just like the card itself.
        private void EnableClick(Control control)
        {
            control.Cursor = Cursors.Hand;
            control.Click += (_, _) => OpenClicked?.Invoke();
            foreach (Control child in control.Controls) EnableClick(child);
        }
    }

    // WinForms sends drop events to the child under the cursor, so every descendant must
    // accept files or drops on a label are lost.
    private static void EnableDrop(Control control, Action<string[]> dropped)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        control.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                dropped(files);
        };
        foreach (Control child in control.Controls) EnableDrop(child, dropped);
    }

    private static readonly Color WindowBackground = Color.FromArgb(20, 26, 36);   // --bg
    private static readonly Color PanelColor = Color.FromArgb(28, 35, 48);         // --panel
    private static readonly Color Panel2 = Color.FromArgb(35, 44, 59);             // --panel-2
    private static readonly Color BorderColor = Color.FromArgb(48, 60, 80);        // --border
    private static readonly Color TextColor = Color.FromArgb(232, 238, 247);       // --text
    private static readonly Color MutedColor = Color.FromArgb(169, 182, 202);      // --muted
    private static readonly Color DangerColor = Color.FromArgb(255, 138, 138);     // --danger
    private static readonly Color WarnColor = Color.FromArgb(255, 209, 102);       // --warn
    private static readonly Color Accent = Color.FromArgb(75, 147, 246);            // --accent

    private static readonly Color[] ChannelColors =
    {
        Color.FromArgb(255, 107, 107), // R
        Color.FromArgb(105, 219, 124), // G
        Color.FromArgb(77, 171, 247),  // B
        Color.FromArgb(255, 224, 102)  // A
    };

    private readonly List<LoadedFile> _files = new();
    private readonly SlotView[] _slots = new SlotView[4];
    private readonly RoundedPanel _slotsHost = new();
    private readonly RoundedPanel _convertHost = new();
    private readonly SourceCard _sourceCard = new();
    private readonly Label _fromPresetLabel = new();
    private readonly ModernComboBox _fromPresetBox = new();
    private readonly Label _convertToLabel = new();
    private readonly ModernComboBox _convertToPresetBox = new();
    private readonly Label _samePresetHint = new();
    private readonly Label _mappingTitle = new();
    private readonly Label[] _mappingLabels = new Label[4];
    private readonly Label[] _mappingBadges = new Label[4];
    private readonly Label[] _stepLabels = new Label[3];
    private readonly ModernButton _unpackButton = new();
    private readonly ModernButton _unpackFooterButton = new();
    private readonly ModernButton _clearButton = new();
    private readonly Label _settingsNote = new();
    private readonly ModernButton _packModeButton = new();
    private readonly ModernButton _convertModeButton = new();
    private readonly Label _subtitle = new();
    private readonly Label _presetLabel = new();
    private readonly Label _sizeLabel = new();
    private readonly Label _outputTitle = new();
    private readonly ModernButton _langRu = new();
    private readonly ModernButton _langEn = new();
    private readonly ModernComboBox _presetBox = new();
    private readonly ModernComboBox _sizeBox = new();
    private readonly ModernComboBox _modeBox = new();
    private readonly PictureBox _previewBox = new();
    private readonly Label _previewHint = new();
    private readonly Label _previewInfo = new();
    private readonly ModernButton _fitButton = new();
    private readonly Dictionary<PreviewMode, Button> _viewButtons = new();
    private readonly TextBox _nameBox = new();
    private readonly Label _formatLabel = new();
    private readonly ModernComboBox _formatBox = new();
    private readonly Label _slotsNote = new();
    private readonly Label _statusLabel = new();
    private readonly Label _modeLabel = new();
    private readonly Label _nameLabel = new();
    private readonly ModernButton _saveButton = new();
    private readonly OpenFileDialog _openDialog = new();
    private readonly ToolTip _tooltips = new();
    private TableLayoutPanel? _mainGrid;
    private readonly System.Windows.Forms.Timer _slotChangeTimer = new() { Interval = 120 };

    private PreviewMode _previewMode = PreviewMode.Rgb;
    private bool _fitMode = true;
    private int _previewWidth;
    private int _previewHeight;
    private Bitmap? _rgbBitmap;
    private Bitmap? _rgbaBitmap;
    private readonly Dictionary<Channel, Bitmap> _channelBitmaps = new();
    private bool _nameEdited;
    private bool _updatingName;
    private bool _convertMode;
    private TextureImage? _convertSource;
    private string? _convertSourcePath;
    private string? _convertNote;
    private bool _syncingPresets;
    private string? _savedFileName;
    private bool _savedInSession;

    private bool HasInputs => _convertMode ? _convertSource is not null : _files.Count > 0;

    /// <summary>Hook for hotkey tests: by default, performs a normal save.</summary>
    internal Action? SaveAction { get; set; }

    /// <summary>Session file path; replaced with a temporary one in tests.</summary>
    internal string SessionPath { get; set; } = SessionStore.DefaultPath;

    private string? _lastOutputDirectory;

    public MainForm(string[]? args = null, string? sessionPath = null)
    {
        if (sessionPath is not null) SessionPath = sessionPath;
        // ApplicationIcon marks the exe; the form needs its own icon for the title bar
        // and taskbar. Load the same asset from resources so every host shows it.
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("OrmPacker.orm-packer.ico"))
        {
            if (iconStream is not null) Icon = new Icon(iconStream);
        }
        Text = Localization.Get("app.title");
        ClientSize = new Size(1240, 860);
        MinimumSize = new Size(900, 650);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = WindowBackground;
        ForeColor = Color.FromArgb(235, 240, 248);
        Font = new Font("Segoe UI", 9f);
        AllowDrop = true;
        KeyPreview = true;
        _slotChangeTimer.Tick += (_, _) =>
        {
            _slotChangeTimer.Stop();
            ApplySlotChange();
        };

        // Slot events fire while presets and the session are applied. Persisting there
        // would overwrite the saved file before it is even read.
        _suppressPersist = true;
        BuildUi();
        ApplyPreset();
        try
        {
            // Command-line files are added on top of the session, never instead of it.
            ApplySession(SessionStore.Load(SessionPath), false);
        }
        catch (Exception)
        {
            // A hand-edited or outdated session must never prevent startup.
        }
        _suppressPersist = false;
        UpdateStatusForCurrentMap();
        if (args is { Length: > 0 }) AddFiles(args);
        Localization.Changed += ApplyLanguage;
        FormClosing += (_, _) => PersistSession();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Localization.Changed -= ApplyLanguage;
            _slotChangeTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private Preset CurrentPreset => (_presetBox.SelectedItem as Preset) ?? Preset.Orm;
    private Preset SourcePreset => (_fromPresetBox.SelectedItem as Preset) ?? Preset.Orm;
    private OutputSize CurrentSize => (_sizeBox.SelectedItem as OutputSize) ?? OutputSize.Default;
    private OutputFormat CurrentFormat => (_formatBox.SelectedItem as OutputFormat) ?? OutputFormat.Png;

    private void BuildUi()
    {
        // --- Header: rounded card, matching the web app ---
        var header = new RoundedPanel { Dock = DockStyle.Top, Height = 78, Margin = Padding.Empty };

        var title = new Label
        {
            Text = Localization.Get("app.title"), Location = new Point(18, 12), Size = new Size(400, 28),
            Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = TextColor, BackColor = Color.Transparent
        };
        _subtitle.Text = Localization.Get("app.subtitle");
        _subtitle.Location = new Point(19, 44);
        _subtitle.Size = new Size(800, 18);
        _subtitle.ForeColor = MutedColor;
        _subtitle.BackColor = Color.Transparent;
        _subtitle.Font = new Font("Segoe UI", 9f);
        // Two-position toggle: highlights the CURRENT UI language.
        ConfigureLangButton(_langRu, "lang.ru", Localization.Russian, 12);
        ConfigureLangButton(_langEn, "lang.en", Localization.English, 56);

        _modeBox.Location = new Point(502, 6);
        _modeBox.Size = new Size(80, 26);
        _modeBox.Items.AddRange(new object[] { Localization.Get("mode.pack"), Localization.Get("mode.convert") });
        _modeBox.SelectedIndex = 0;
        _modeBox.SelectedIndexChanged += (_, _) => SetConvertMode(_modeBox.SelectedIndex == 1);

        // A right-docked group keeps the switch visible at any window width.
        // Transparent background: an opaque panel covered the header's rounded corner.
        var langGroup = new Panel { Dock = DockStyle.Right, Width = 108, BackColor = Color.Transparent };
        langGroup.Controls.AddRange(new Control[] { _langRu, _langEn });
        header.Controls.AddRange(new Control[] { title, _subtitle, langGroup });
        void FitHeaderText()
        {
            var textWidth = Math.Max(240, header.ClientSize.Width - 140);
            title.Width = textWidth;
            _subtitle.Width = textWidth;
        }
        header.Resize += (_, _) => FitHeaderText();
        FitHeaderText();

        static void ConfigureModeButton(ModernButton button, string key, int x, int mode)
        {
            button.Text = Localization.Get(key);
            button.Location = new Point(x, 96);
            button.Size = new Size(mode == 0 ? 92 : 100, 30);
            button.BackColor = Panel2;
            button.BaseColor = Panel2;
            button.HoverColor = Color.FromArgb(42, 53, 70);
            button.PressColor = Accent;
            button.TextColor = TextColor;
            button.Radius = 5;
        }

        static void ConfigureLangButton(ModernButton button, string key, string language, int x)
        {
            button.Text = Localization.Get(key);
            button.Selected = language == Localization.Language;
            button.Location = new Point(x, 12);
            button.Size = new Size(40, 26);
            button.BackColor = PanelColor;
            button.BaseColor = Color.FromArgb(45, 53, 68);
            button.HoverColor = Color.FromArgb(64, 76, 96);
            button.PressColor = Accent;
            button.Radius = 5;
            button.Click += (_, _) => Localization.SetLanguage(language);
        }

        // --- Slots (Pack mode only) ---
        _slotsHost.Dock = DockStyle.Fill;
        _slotsHost.MinimumSize = new Size(340, 300);
        _slotsHost.Padding = Padding.Empty;
        _slotsNote.Text = Localization.Get("slots.hint");
        _slotsNote.Location = new Point(16, 14);
        _slotsNote.Size = new Size(420, 20);
        _slotsNote.ForeColor = MutedColor;
        _slotsNote.BackColor = Color.Transparent;
        _slotsNote.Font = new Font("Segoe UI", 9f);

        var slotsBody = new Panel
        {
            // Top padding leaves space for the hint label drawn above the first slot.
            Dock = DockStyle.Fill, BackColor = PanelColor, Padding = new Padding(0, 40, 0, 0)
        };
        var slotsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            BackColor = Color.Transparent, Margin = Padding.Empty
        };
        slotsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) slotsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));

        for (var i = 0; i < 4; i++)
        {
            var slot = new SlotView(i, ChannelColors[i]);
            slot.LoadClicked += OpenFilesForSlot;
            slot.FilesDropped += (s, files) => AddFiles(files, s);
            void MarkSlotDirty()
            {
                _savedFileName = null;
                _savedInSession = false;
            }
            void ApplySlotEdit()
            {
                MarkSlotDirty();
                if (!_suppressPersist) ApplySlotChange();
            }
            slot.Changed += ApplySlotEdit;
            slot.ValueCommitted += ApplySlotEdit;
            slot.ValueDragging += () =>
            {
                MarkSlotDirty();
                if (_suppressPersist) return;
                // A slider fires dozens of events per drag. Coalesce the expensive
                // repack/thumbnail/session work into one update after the drag settles.
                _slotChangeTimer.Stop();
                _slotChangeTimer.Start();
            };
            _slots[i] = slot;
            slotsTable.Controls.Add(slot, 0, i);
        }
        slotsBody.Controls.Add(slotsTable);
        _slotsHost.Controls.Add(_slotsNote);
        _slotsHost.Margin = new Padding(0, 0, 16, 0);
        _slotsHost.Controls.Add(slotsBody);

        // --- Convert section, placed inside the settings card like the web app ---
        _convertHost.Location = new Point(16, 96);
        _convertHost.Size = new Size(288, 418);
        _convertHost.BackColor = PanelColor;
        _convertHost.Visible = false;

        _sourceCard.Location = new Point(0, 0);
        _sourceCard.Size = new Size(288, 80);
        _sourceCard.OpenClicked += () =>
        {
            if (_openDialog.ShowDialog() == DialogResult.OK) AddFiles(_openDialog.FileNames);
        };
        _sourceCard.FilesDropped += files => AddFiles(files);
        _convertHost.Controls.Add(_sourceCard);

        _fromPresetLabel.Text = Localization.Get("convert.from");
        _fromPresetLabel.Location = new Point(0, 90);
        _fromPresetLabel.Size = new Size(100, 18);
        _fromPresetLabel.ForeColor = MutedColor;
        _fromPresetBox.Location = new Point(0, 108);
        _fromPresetBox.Size = new Size(288, 28);
        _fromPresetBox.Items.AddRange(Preset.All.ToArray());
        _fromPresetBox.ItemTextSelector = item => ((Preset)item!).DisplayName;
        _fromPresetBox.SelectedItem = Preset.UnityHdrp;
        _fromPresetBox.SelectedIndexChanged += (_, _) =>
        {
            if (_convertMode) UpdateMapping();
            if (_convertMode) { UpdatePreview(); UpdateName(); UpdateSteps(); UpdateStatusForCurrentMap(); }
        };

        _convertToLabel.Text = Localization.Get("convert.to");
        _convertToLabel.Location = new Point(0, 146);
        _convertToLabel.Size = new Size(100, 18);
        _convertToLabel.ForeColor = MutedColor;
        _convertToPresetBox.Location = new Point(0, 164);
        _convertToPresetBox.Size = new Size(288, 28);
        _convertToPresetBox.Items.AddRange(Preset.All.ToArray());
        _convertToPresetBox.ItemTextSelector = item => ((Preset)item!).DisplayName;
        _convertToPresetBox.SelectedIndex = 0;
        _convertToPresetBox.SelectedIndexChanged += (_, _) =>
        {
            if (_syncingPresets || _presetBox.SelectedItem == _convertToPresetBox.SelectedItem) return;
            _syncingPresets = true;
            _presetBox.SelectedItem = _convertToPresetBox.SelectedItem;
            _syncingPresets = false;
        };

        _samePresetHint.Location = new Point(0, 198);
        _samePresetHint.Size = new Size(288, 18);
        _samePresetHint.ForeColor = WarnColor;
        _samePresetHint.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _samePresetHint.AutoEllipsis = true;
        _samePresetHint.Visible = false;

        var stepsStrip = new Panel { Location = new Point(0, 222), Size = new Size(288, 30), BackColor = PanelColor };
        for (var i = 0; i < 3; i++)
        {
            var stepBox = new RoundedPanel
            {
                Location = new Point(i * 98, 0), Size = new Size(92, 28), Radius = 6
            };
            _stepLabels[i] = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(120, 140, 168), BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoEllipsis = true
            };
            stepBox.Controls.Add(_stepLabels[i]);
            stepsStrip.Controls.Add(stepBox);
        }

        var mappingCard = new RoundedPanel
        {
            Location = new Point(0, 258), Size = new Size(288, 156), Radius = 10
        };
        _mappingTitle.Text = Localization.Get("convert.mapping");
        _mappingTitle.Location = new Point(10, 8);
        _mappingTitle.Size = new Size(268, 20);
        _mappingTitle.ForeColor = TextColor;
        _mappingTitle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        mappingCard.Controls.Add(_mappingTitle);

        for (var i = 0; i < 4; i++)
        {
            var row = new Panel
            {
                Location = new Point(10, 36 + i * 28), Size = new Size(268, 28), BackColor = Color.Transparent
            };
            _mappingLabels[i] = new Label
            {
                Location = new Point(2, 4), Size = new Size(178, 20), ForeColor = ChannelColors[i],
                Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoEllipsis = true
            };
            _mappingBadges[i] = new Label
            {
                Location = new Point(180, 3), Size = new Size(80, 20), TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(148, 74, 158), ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold), Visible = false
            };
            row.Controls.AddRange(new Control[] { _mappingLabels[i], _mappingBadges[i] });
            mappingCard.Controls.Add(row);
        }

        _unpackButton.Visible = false;
        _unpackButton.Location = new Point(0, 422);
        _unpackButton.Size = new Size(200, 28);
        _unpackButton.BackColor = Panel2;
        _unpackButton.BaseColor = Panel2;
        _unpackButton.HoverColor = Color.FromArgb(42, 53, 70);
        _unpackButton.PressColor = Accent;
        _unpackButton.Radius = 5;
        _unpackButton.Enabled = false;
        _unpackButton.Click += (_, _) => UnpackCurrentResult();

        _convertHost.Controls.AddRange(new Control[]
        {
            _sourceCard, _fromPresetLabel, _fromPresetBox, _convertToLabel, _convertToPresetBox,
            _samePresetHint, stepsStrip, mappingCard, _unpackButton
        });

        // --- Output-style preview ---
        var previewHost = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        var previewHeader = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = PanelColor };
        _outputTitle.Text = Localization.Get("preview.output");
        _outputTitle.Visible = false;
        _outputTitle.Location = new Point(12, 4);
        _outputTitle.Size = new Size(100, 22);
        _outputTitle.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _outputTitle.ForeColor = TextColor;
        // The web app shows only the tabs here; the status line lives in the viewport.
        _previewInfo.Visible = false;
        previewHeader.Controls.Add(_outputTitle);

        var viewStrip = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.Transparent };
        var tabX = 8;
        foreach (var mode in Enum.GetValues<PreviewMode>())
        {
            var button = new Button
            {
                Text = Localization.Get("preview." + mode.ToString().ToLowerInvariant()),
                Location = new Point(tabX, 4),
                Size = new Size(mode is PreviewMode.Rgb ? 42 : mode is PreviewMode.Rgba ? 52 : 28, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Panel2,
                ForeColor = TextColor,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand,
                Tag = false
            };
            var flat = ((Button)button).FlatAppearance;
            flat.BorderColor = BorderColor;
            flat.BorderSize = 1;
            tabX += button.Width + 4;
            button.Click += (_, _) => SetPreviewMode(mode);
            _viewButtons[mode] = button;
            viewStrip.Controls.Add(button);
        }
        _fitButton.Text = Localization.Get("preview.fit");
        _fitButton.Location = new Point(tabX, 4);
        _fitButton.Size = new Size(62, 26);
        _fitButton.BackColor = PanelColor;
        _fitButton.BaseColor = Panel2;
        _fitButton.HoverColor = Color.FromArgb(64, 76, 96);
        _fitButton.PressColor = Accent;
        _fitButton.Selected = true;
        _fitButton.Radius = 5;
        _fitButton.Click += (_, _) =>
        {
            _fitMode = !_fitMode;
            _fitButton.Selected = _fitMode;
            ShowPreview();
        };
        viewStrip.Controls.Add(_fitButton);
        previewHeader.Controls.Add(viewStrip);

        _previewBox.Dock = DockStyle.Fill;
        _previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _previewHint.Dock = DockStyle.Fill;
        _previewHint.Text = Localization.Get("preview.hint");
        _previewHint.TextAlign = ContentAlignment.MiddleCenter;
        _previewHint.ForeColor = MutedColor;
        _previewHint.Font = new Font("Segoe UI", 9f);

        var viewport = new Panel { Dock = DockStyle.Fill, BackColor = Panel2 };
        viewport.BackgroundImage = Checkerboard();
        viewport.BackgroundImageLayout = ImageLayout.Tile;
        _previewBox.BackColor = Color.Transparent;
        viewport.Controls.Add(_previewBox);
        viewport.Controls.Add(_previewHint);
        previewHost.Controls.Add(viewport);
        previewHost.Controls.Add(previewHeader);

        // --- Settings column (left, as in the web app) ---
        var settings = new RoundedPanel { Dock = DockStyle.Fill, AutoScroll = false };
        _modeLabel.Text = Localization.Get("mode.label");
        _modeLabel.Location = new Point(16, 100);
        _modeLabel.Size = new Size(80, 20);
        _modeLabel.ForeColor = MutedColor;
        _modeLabel.BackColor = Color.Transparent;
        _statusLabel.Location = new Point(16, 268);
        _statusLabel.Size = new Size(300, 20);
        _statusLabel.ForeColor = MutedColor;
        _statusLabel.BackColor = Color.Transparent;
        _statusLabel.Font = new Font("Segoe UI", 9f);
        _presetLabel.Text = Localization.Get("header.preset");
        _presetLabel.Location = new Point(16, 20);
        _presetLabel.Size = new Size(80, 20);
        _presetLabel.ForeColor = MutedColor;
        _presetBox.Location = new Point(104, 16);
        _presetBox.Size = new Size(228, 28);
        _presetBox.Items.AddRange(Preset.All.ToArray());
        _presetBox.ItemTextSelector = item => ((Preset)item!).DisplayName;
        _presetBox.SelectedIndex = 0;
        _presetBox.SelectedIndexChanged += (_, _) =>
        {
            if (!_syncingPresets && _convertToPresetBox.SelectedItem != _presetBox.SelectedItem)
            {
                _syncingPresets = true;
                _convertToPresetBox.SelectedItem = _presetBox.SelectedItem;
                _syncingPresets = false;
            }
            ApplyPreset();
        };

        _sizeLabel.Text = Localization.Get("header.size");
        _sizeLabel.Location = new Point(16, 60);
        _sizeLabel.Size = new Size(80, 20);
        _sizeLabel.ForeColor = MutedColor;
        _sizeBox.Location = new Point(104, 56);
        _sizeBox.Size = new Size(228, 28);
        _sizeBox.Items.AddRange(OutputSize.All.ToArray());
        _sizeBox.ItemTextSelector = item => ((OutputSize)item!).DisplayName;
        _sizeBox.SelectedItem = OutputSize.Default;
        _sizeBox.SelectedIndexChanged += (_, _) => { UpdatePreview(); UpdateStatusForCurrentMap(); };

        ConfigureModeButton(_packModeButton, "mode.pack", 104, 0);
        ConfigureModeButton(_convertModeButton, "mode.convert", 196, 1);
        _packModeButton.Click += (_, _) => _modeBox.SelectedIndex = 0;
        _convertModeButton.Click += (_, _) => _modeBox.SelectedIndex = 1;

        _formatLabel.Text = Localization.Get("format.label");
        _formatLabel.Location = new Point(16, 140);
        _formatLabel.Size = new Size(80, 20);
        _formatLabel.ForeColor = MutedColor;
        _formatBox.Location = new Point(104, 136);
        _formatBox.Size = new Size(228, 28);
        _formatBox.Items.AddRange(OutputFormat.All.ToArray());
        _formatBox.ItemTextSelector = item => ((OutputFormat)item!).DisplayName;
        _formatBox.ItemListTextSelector = item => ((OutputFormat)item!).DescriptionText;
        _formatBox.SelectedIndex = 0;
        _formatBox.SelectedIndexChanged += (_, _) => { UpdateName(); ShowPreview(); };

        _nameLabel.Text = Localization.Get("filename.label");
        _nameLabel.Location = new Point(16, 180);
        _nameLabel.Size = new Size(80, 20);
        _nameLabel.ForeColor = MutedColor;
        _nameBox.Location = new Point(104, 176);
        _nameBox.Size = new Size(228, 28);
        _nameBox.BackColor = Panel2;
        _nameBox.ForeColor = TextColor;
        _nameBox.BorderStyle = BorderStyle.FixedSingle;
        _nameBox.PlaceholderText = Localization.Get("filename.placeholder");
        _nameBox.TextChanged += (_, _) =>
        {
            if (!_updatingName) _nameEdited = true;
            if (_convertMode) UpdateSteps();
        };

        _saveButton.Text = Localization.Get("save.button");
        _saveButton.Location = new Point(12, 220);
        _saveButton.Size = new Size(104, 32);
        _saveButton.BackColor = Accent;
        _saveButton.BaseColor = Accent;
        _saveButton.HoverColor = Color.FromArgb(96, 164, 255);
        _saveButton.PressColor = Color.FromArgb(52, 118, 214);
        _saveButton.TextColor = Color.White;
        _saveButton.Radius = 8;
        _saveButton.Click += (_, _) => Save();

        _unpackFooterButton.Text = Localization.Get("convert.unpack");
        _unpackFooterButton.Location = new Point(120, 220);
        _unpackFooterButton.Size = new Size(134, 32);
        _unpackFooterButton.BackColor = PanelColor;
        _unpackFooterButton.BaseColor = Panel2;
        _unpackFooterButton.HoverColor = Color.FromArgb(64, 76, 96);
        _unpackFooterButton.PressColor = Accent;
        _unpackFooterButton.Radius = 6;
        _unpackFooterButton.Click += (_, _) => UnpackCurrentResult();

        _clearButton.Text = Localization.Get("clear.slots");
        _clearButton.Location = new Point(258, 220);
        _clearButton.Size = new Size(70, 32);
        _clearButton.BackColor = Panel2;
        _clearButton.BaseColor = Panel2;
        _clearButton.HoverColor = Color.FromArgb(42, 53, 70);
        _clearButton.PressColor = DangerColor;
        _clearButton.TextColor = DangerColor;
        _clearButton.Radius = 5;
        _clearButton.Click += (_, _) => ClearSlots();

        _settingsNote.Location = new Point(16, 292);
        _settingsNote.Size = new Size(300, 70);
        _settingsNote.ForeColor = MutedColor;
        _settingsNote.Text = Localization.Get("settings.hint");

        settings.Controls.AddRange(new Control[]
        {
            _presetLabel, _presetBox, _sizeLabel, _sizeBox, _modeLabel,
            _packModeButton, _convertModeButton,
            _statusLabel, _formatLabel, _formatBox, _nameLabel, _nameBox,
            _saveButton, _unpackFooterButton, _clearButton, _settingsNote, _convertHost
        });

        // Three columns: settings / slots / preview (same order as the web UI).
        var leftColumn = new Panel
        {
            Dock = DockStyle.Fill, BackColor = WindowBackground, Margin = new Padding(0, 0, 16, 0)
        };
        leftColumn.Controls.Add(settings);

        _mainGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Margin = Padding.Empty, BackColor = WindowBackground, Padding = new Padding(0, 12, 0, 0)
        };
        _mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        _mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        _mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        _mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _mainGrid.Controls.Add(leftColumn, 0, 0);
        _mainGrid.Controls.Add(_slotsHost, 1, 0);
        _mainGrid.Controls.Add(previewHost, 2, 0);

        // Dock ignores Margin: a padding container is what actually insets both the
        // header and the grid by 16 px, like the web app's .app padding.
        var content = new Panel
        {
            Dock = DockStyle.Fill, BackColor = WindowBackground,
            Padding = new Padding(16, 0, 16, 16)
        };
        content.Controls.Add(_mainGrid);
        content.Controls.Add(header);
        Controls.Add(content);

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
        };
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                e.SuppressKeyPress = true;
                (SaveAction ?? Save)();
            }
            else if (e.Control && e.KeyCode == Keys.K)
            {
                e.SuppressKeyPress = true;
                ClearSlots();
            }
            else if (!e.Control && !e.Alt && !_convertMode && ActiveControl is not TextBox)
            {
                // 1–4 (including numpad) select a preset — same as the web app.
                var index = e.KeyCode is >= Keys.D1 and <= Keys.D4 ? e.KeyCode - Keys.D1
                    : e.KeyCode is >= Keys.NumPad1 and <= Keys.NumPad4 ? e.KeyCode - Keys.NumPad1
                    : -1;
                if (index >= 0)
                {
                    e.SuppressKeyPress = true;
                    _presetBox.SelectedIndex = index;
                }
            }
        };

        ConfigureOpenDialog();
        _tooltips.SetToolTip(_saveButton, Localization.Get("save.short"));
        SetPreviewMode(PreviewMode.Rgb);
    }

    private void ConfigureOpenDialog()
    {
        _openDialog.Multiselect = true;
        _openDialog.Filter =
            $"{Localization.Get("dialog.textures")}|*.png;*.tga;*.jpg;*.jpeg;*.jpe;*.jfif;*.pjpeg;*.pjp;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.exr|" +
            "PNG|*.png|TGA|*.tga|JPEG|*.jpg;*.jpeg;*.jpe;*.jfif;*.pjpeg;*.pjp|BMP|*.bmp|GIF|*.gif|TIFF|*.tif;*.tiff|ICO|*.ico|EXR|*.exr|" +
            $"{Localization.Get("dialog.all")}|*.*";
        _openDialog.Title = Localization.Get("dialog.open.title");
    }

    private void ClearSlots()
    {
        _savedFileName = null;
        _nameEdited = false;
        for (var i = 0; i < 4; i++)
        {
            var slot = _slots[i];
            slot.ClearSlot();
            slot.SolidValue = CurrentPreset.Channels[(Channel)i].DefaultValue;
            slot.Number.Value = slot.SolidValue;
            slot.Slider.Value = slot.SolidValue;
        }
        _files.Clear();
        _savedInSession = false;
        _convertSource = null;
        _convertSourcePath = null;
        _convertNote = null;
        RefreshThumbnails();
        UpdateSourceCard();
        UpdatePreview();
        UpdateName();
        UpdateSteps();
        UpdateStatusForCurrentMap();
        PersistSession();
    }

    private void LayoutSettings(bool convert)
    {
        _presetLabel.Visible = !convert;
        _presetBox.Visible = !convert;

        var top = convert ? 20 : 60;
        _sizeLabel.Location = new Point(16, top);
        _sizeBox.Location = new Point(104, top - 4);
        _modeLabel.Location = new Point(16, top + 40);
        _packModeButton.Location = new Point(104, top + 36);
        _convertModeButton.Location = new Point(196, top + 36);

        var row = convert ? 528 : 140;
        _formatLabel.Location = new Point(16, row);
        _formatBox.Location = new Point(104, row - 4);
        _nameLabel.Location = new Point(16, row + 40);
        _nameBox.Location = new Point(104, row + 36);

        var buttons = convert ? 608 : 220;
        _saveButton.Location = new Point(12, buttons);
        _unpackFooterButton.Location = new Point(120, buttons);
        _clearButton.Location = new Point(258, buttons);
        _statusLabel.Location = new Point(16, buttons + 48);
        _settingsNote.Location = new Point(16, buttons + 72);
        _convertHost.Visible = convert;
    }

    private void SetConvertMode(bool enabled)
    {
        // Defensive guard: a mode-change event can arrive before BuildUi has created the slot views.
        if (_slots.Any(slot => slot is null)) return;
        _convertMode = enabled;
        _savedInSession = false;
        _packModeButton.Selected = !enabled;
        _convertModeButton.Selected = enabled;
        _slotsHost.Visible = !enabled;
        LayoutSettings(enabled);
        if (_mainGrid is not null)
        {
            _mainGrid.ColumnStyles[0].SizeType = SizeType.Percent;
            _mainGrid.ColumnStyles[0].Width = 30;
            _mainGrid.ColumnStyles[1].SizeType = SizeType.Percent;
            _mainGrid.ColumnStyles[1].Width = enabled ? 0 : 40;
            _mainGrid.ColumnStyles[2].SizeType = SizeType.Percent;
            _mainGrid.ColumnStyles[2].Width = enabled ? 70 : 30;
        }
        foreach (var slot in _slots)
        {
            slot.AllowDrop = !enabled;
            // Hidden slots must not leave stale warnings visible.
            if (enabled)
            {
                slot.Stats.Text = "";
                slot.DiagnosticWarning = "";
                slot.MismatchNotice = "";
            }
        }
        if (enabled && !ReferenceEquals(_convertToPresetBox.SelectedItem, _presetBox.SelectedItem))
        {
            _syncingPresets = true;
            _convertToPresetBox.SelectedItem = _presetBox.SelectedItem;
            _syncingPresets = false;
        }
        _subtitle.Text = Localization.Get(enabled ? "convert.hint" : "app.subtitle");
        if (!enabled) RefreshThumbnails();
        UpdateSourceCard();
        UpdateMapping();
        UpdateSteps();
        UpdatePreview();
        UpdateName();
        UpdateStatusForCurrentMap();
    }

    private static string ShortPresetName(Preset preset) => preset switch
    {
        _ when ReferenceEquals(preset, Preset.Orm) => Localization.Get("preset.short.orm"),
        _ when ReferenceEquals(preset, Preset.UnityHdrp) => Localization.Get("preset.short.hdrp"),
        _ when ReferenceEquals(preset, Preset.UnityUrp) => Localization.Get("preset.short.urp"),
        _ => Localization.Get("preset.short.custom")
    };

    private void UpdateSourceCard()
    {
        _unpackButton.Enabled = _convertSource is not null;
        if (_convertSource is null)
        {
            _sourceCard.Thumb.Image = null;
            _sourceCard.FileName.Text = "";
            _sourceCard.Details.Text = "";
            _sourceCard.ShowMap(false);
            return;
        }

        _sourceCard.Thumb.Image?.Dispose();
        _sourceCard.Thumb.Image = _convertSource.CreateThumbnail(SourceComponent.Red, false, 118);
        var path = _convertSourcePath ?? _convertSource.FileName;
        _sourceCard.FileName.Text = Path.GetFileName(path);
        var format = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        _sourceCard.Details.Text = string.Format(CultureInfo.InvariantCulture,
            Localization.Get("convert.card.details"), _convertSource.Width, _convertSource.Height,
            Localization.Get("bits.8"), format);
        _sourceCard.ShowMap(true);
    }

    private void UpdateMapping()
    {
        if (!_convertMode) return;
        var mappings =
            PackerConvert.BuildMappings(SourcePreset, CurrentPreset);
        for (var i = 0; i < 4; i++)
        {
            var mapping = mappings[i];
            var source = mapping.Source is null
                ? Localization.Get("convert.mapping.constant")
                : SourcePreset.Channels[mapping.Source.Value].DisplayName;
            var targetName = CurrentPreset.Channels[mapping.Target].DisplayName;
            _mappingLabels[i].Text = string.Format(CultureInfo.InvariantCulture,
                Localization.Get("convert.mapping.row"), targetName, source);
            _mappingLabels[i].ForeColor = ChannelColors[i];
            _mappingBadges[i].Text = Localization.Get("convert.mapping.invert");
            _mappingBadges[i].Visible = mapping.Invert;
            // Inline: the badge goes right after the source channel name, not at the row's right edge.
            var textWidth = TextRenderer.MeasureText(_mappingLabels[i].Text, _mappingLabels[i].Font).Width;
            var badgeX = Math.Min(_mappingLabels[i].Left + textWidth + 8,
                520 - _mappingBadges[i].Width - 2);
            _mappingBadges[i].Location = new Point(badgeX, 2);

            var tooltip = mapping.Source is null
                ? string.Format(CultureInfo.InvariantCulture,
                    Localization.Get("convert.mapping.tooltip.constant"),
                    CurrentPreset.DisplayName, targetName)
                : string.Format(CultureInfo.InvariantCulture,
                    Localization.Get("convert.mapping.tooltip"),
                    CurrentPreset.DisplayName, targetName, SourcePreset.DisplayName, source);
            _tooltips.SetToolTip(_mappingLabels[i], tooltip);
            if (mapping.Invert) _tooltips.SetToolTip(_mappingBadges[i], tooltip);
        }
        _samePresetHint.Text = Localization.Get("convert.same");
        _samePresetHint.Visible = ReferenceEquals(SourcePreset, CurrentPreset);
    }

    private void UnpackCurrentResult()
    {
        if (!HasInputs) return;
        var path = _convertMode
            ? _convertSourcePath ?? _convertSource!.FileName
            : _files.First().Path ?? _files.First().Image.FileName;
        var directory = _lastOutputDirectory ?? Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(
            OutputNaming.Suggest(new[] { path }, "", ".png"));
        try
        {
            // Same full-resolution result as Save: pack result in Pack, converted map in Convert.
            (var width, var height) = TargetOutputSize();
            if (width == 0 || height == 0) return;
            var packed = Pack(width, height);
            var result = TextureImage.FromPixels(stem, width, height, packed.Pixels);
            var saved = PackerConvert.UnpackChannels(result, directory, stem);
            _convertNote = string.Format(CultureInfo.InvariantCulture,
                Localization.Get("convert.unpacked"), saved.Count);
            UpdateStatusForCurrentMap();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Localization.Get("msg.saveError")} {ex.Message}",
                Localization.Get("msg.noMapsTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateSteps()
    {
        bool[] done =
        {
            !_convertMode || _convertSource is not null,
            !ReferenceEquals(SourcePreset, CurrentPreset),
            _savedInSession
        };
        string[] keys = { "convert.step.map", "convert.step.pair", "convert.step.save" };
        for (var i = 0; i < 3; i++)
        {
            _stepLabels[i].Text = done[i]
                ? string.Format(CultureInfo.InvariantCulture,
                    Localization.Get("convert.step.done"), Localization.Get(keys[i]))
                : Localization.Get(keys[i]);
            _stepLabels[i].ForeColor = done[i]
                ? Color.FromArgb(120, 220, 150)
                : Color.FromArgb(120, 140, 168);
        }
    }

    private void ApplySession(SessionState session, bool skipSlotPaths)
    {
        _presetBox.SelectedItem = SessionStore.PresetById(session.PresetId);
        var size = OutputSize.All.FirstOrDefault(s => s.Pixels == session.SizePixels);
        if (size is not null) _sizeBox.SelectedItem = size;
        var format = OutputFormat.All.FirstOrDefault(f =>
            f.Format == SessionStore.FormatById(session.FormatId));
        if (format is not null) _formatBox.SelectedItem = format;
        _lastOutputDirectory = session.LastOutputDirectory;
        if (session.FromPresetId is not null)
            _fromPresetBox.SelectedItem = SessionStore.PresetById(session.FromPresetId);

        if (!skipSlotPaths && session.ConvertSourcePath is not null && File.Exists(session.ConvertSourcePath))
        {
            try
            {
                _convertSource = TextureImage.Load(session.ConvertSourcePath);
                _convertSourcePath = session.ConvertSourcePath;
                _convertNote = null;
            }
            catch (Exception) { /* source map is gone — leave the card empty */ }
        }
        _modeBox.SelectedIndex = session.ConvertMode ? 1 : 0;

        if (!skipSlotPaths)
        {
            for (var i = 0; i < 4; i++)
            {
                var saved = session.Slots.ElementAtOrDefault(i) ?? new SlotState(null, SourceComponent.Red, 255, false);
                var slot = _slots[i];
                slot.Path = null;
                slot.Image = null;
                slot.MismatchKind = null;
                slot.DiagnosticWarning = "";
                slot.MismatchNotice = "";
                if (saved.Path is not null && File.Exists(saved.Path))
                {
                    try
                    {
                        var image = TextureImage.Load(saved.Path);
                        slot.Image = image;
                        slot.Path = saved.Path;
                        _files.Add(new LoadedFile { Image = image, Kind = Preset.DetectKind(saved.Path), Path = saved.Path });
                    }
                    catch (Exception) { /* map is gone — leave the slot empty */ }
                }
                slot.SourceComponent = saved.Source;
                slot.Source.SelectedIndex = (int)saved.Source;
                slot.InvertValue = saved.Invert;
                slot.Invert.Checked = saved.Invert;
                slot.SolidValue = saved.SolidValue;
                slot.Number.Value = saved.SolidValue;
                slot.Slider.Value = saved.SolidValue;
            }
        }
        RefreshThumbnails();
        UpdatePreview();
        UpdateName();
    }

    private SessionState CaptureSession() => new()
    {
        PresetId = SessionStore.PresetId(CurrentPreset),
        SizePixels = CurrentSize.Pixels,
        FormatId = SessionStore.FormatId(CurrentFormat.Format),
        ConvertMode = _convertMode,
        FromPresetId = SessionStore.PresetId(SourcePreset),
        ConvertSourcePath = _convertMode ? _convertSourcePath : null,
        Slots = _slots.Select(slot => new SlotState(slot.Path, slot.SourceComponent,
            slot.SolidValue, slot.InvertValue)).ToArray(),
        RecentPaths = _files.Select(f => f.Path).Distinct().Take(20).ToList(),
        LastOutputDirectory = _lastOutputDirectory
    };

    // True only during startup while presets/session are loaded; see constructor.
    private bool _suppressPersist;

    private void PersistSession()
    {
        if (_suppressPersist) return;
        try { SessionStore.Save(SessionPath, CaptureSession()); }
        catch (Exception) { /* session is a convenience, not a critical result */ }
    }

    /// <summary>Refreshes thumbnails/preview and persists after a slot edit; slider drags reach it via the debounce timer.</summary>
    private void ApplySlotChange()
    {
        if (_suppressPersist) return;
        try { RefreshThumbnails(); UpdatePreview(); }
        catch (Exception ex) { _statusLabel.Text = $"{Localization.Get("msg.saveError")} {ex.Message}"; }
        PersistSession();
    }

    private IReadOnlyList<string> SourcePaths() => _convertMode && _convertSource is not null
        ? new[] { _convertSourcePath ?? _convertSource.FileName }
        : _files.Select(f => f.Path).ToList();

    private void SetPreviewMode(PreviewMode mode)
    {
        if (!HasInputs && mode is PreviewMode.R or PreviewMode.G or PreviewMode.B or PreviewMode.A) return;
        _previewMode = mode;
        foreach ((var key, var button) in _viewButtons)
        {
            var selected = key == mode;
            button.Tag = selected;
            button.BackColor = selected ? Accent : Panel2;
        }
        ShowPreview();
    }

    private static Bitmap Checkerboard()
    {
        var bitmap = new Bitmap(20, 20);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(32, 40, 54));
        using var light = new SolidBrush(Color.FromArgb(43, 51, 65));
        g.FillRectangle(light, 0, 0, 10, 10);
        g.FillRectangle(light, 10, 10, 10, 10);
        return bitmap;
    }

    private void ShowPreview()
    {
        if (_previewWidth == 0)
        {
            _previewBox.Image = null;
            _previewInfo.Text = "";
            return;
        }
        var image = _previewMode switch
        {
            PreviewMode.Rgb => _rgbBitmap,
            PreviewMode.Rgba => _rgbaBitmap,
            PreviewMode.R => _channelBitmaps.GetValueOrDefault(Channel.R),
            PreviewMode.G => _channelBitmaps.GetValueOrDefault(Channel.G),
            PreviewMode.B => _channelBitmaps.GetValueOrDefault(Channel.B),
            _ => _channelBitmaps.GetValueOrDefault(Channel.A)
        };
        _previewBox.Image = image;
        _previewBox.SizeMode = _fitMode ? PictureBoxSizeMode.Zoom : PictureBoxSizeMode.CenterImage;
        // The header describes the result, not the selected preview tab — same in both modes.
        var channels = Localization.Get(
            CurrentAlpha() && CurrentFormat.SupportsAlpha ? "preview.rgba" : "preview.rgb");
        _previewInfo.Text = string.Format(CultureInfo.InvariantCulture,
            Localization.Get("preview.header"), _previewWidth, _previewHeight,
            channels, Localization.Get("bits.8"), CurrentFormat.Name);
    }

    private void UpdateSlotTitles()
    {
        var preset = CurrentPreset;
        foreach (var channel in AllChannels)
            _slots[(int)channel].Title.Text = preset.Channels[channel].DisplayName;
    }

    private static string[] SourceNames() => new[]
    {
        Localization.Get("source.red"), Localization.Get("source.green"),
        Localization.Get("source.blue"), Localization.Get("source.alpha"),
        Localization.Get("source.gray")
    };

    private void ApplyLanguage()
    {
        _langRu.Text = Localization.Get("lang.ru");
        _langEn.Text = Localization.Get("lang.en");
        _langRu.Selected = Localization.Language == Localization.Russian;
        _langEn.Selected = Localization.Language == Localization.English;

        _subtitle.Text = Localization.Get(_convertMode ? "convert.hint" : "app.subtitle");
        var modeIndex = _modeBox.SelectedIndex;
        _modeBox.Items.Clear();
        _modeBox.Items.AddRange(new object[] { Localization.Get("mode.pack"), Localization.Get("mode.convert") });
        _modeBox.SelectedIndex = modeIndex;
        _modeBox.Invalidate();

        _fromPresetLabel.Text = Localization.Get("convert.from");
        _convertToLabel.Text = Localization.Get("convert.to");
        _sourceCard.Hint.Text = Localization.Get("convert.dropHint");
        _mappingTitle.Text = Localization.Get("convert.mapping");
        _presetLabel.Text = Localization.Get("header.preset");
        _sizeLabel.Text = Localization.Get("header.size");
        _outputTitle.Text = Localization.Get("preview.output");
        _nameLabel.Text = Localization.Get("filename.label");
        _nameBox.PlaceholderText = Localization.Get("filename.placeholder");
        _formatLabel.Text = Localization.Get("format.label");
        _saveButton.Text = Localization.Get("save.button");
        _unpackButton.Text = Localization.Get("convert.unpack");
        _fitButton.Text = Localization.Get("preview.fit");
        _previewHint.Text = Localization.Get("preview.hint");
        _clearButton.Text = Localization.Get("clear.slots");
        _unpackFooterButton.Text = Localization.Get("convert.unpack");
        _settingsNote.Text = Localization.Get("settings.hint");
        _slotsNote.Text = Localization.Get("slots.hint");
        _modeLabel.Text = Localization.Get("mode.label");
        _packModeButton.Text = Localization.Get("mode.pack");
        _convertModeButton.Text = Localization.Get("mode.convert");
        _packModeButton.Selected = !_convertMode;
        _convertModeButton.Selected = _convertMode;
        _tooltips.SetToolTip(_saveButton, Localization.Get("save.short"));
        _tooltips.SetToolTip(_unpackButton, Localization.Get("unpack.tip"));
        _tooltips.SetToolTip(_unpackFooterButton, Localization.Get("unpack.tip"));
        ConfigureOpenDialog();

        foreach ((var mode, var button) in _viewButtons)
            button.Text = Localization.Get("preview." + mode.ToString().ToLowerInvariant());

        _formatBox.Invalidate();
        _presetBox.Invalidate();
        _sizeBox.Invalidate();
        _fromPresetBox.Invalidate();
        _convertToPresetBox.Invalidate();
        UpdateSlotTitles();

        foreach (var slot in _slots)
        {
            var selected = slot.Source.SelectedIndex;
            slot.Source.Items.Clear();
            slot.Source.Items.AddRange(SourceNames());
            slot.Source.SelectedIndex = selected;
            slot.Invert.Text = Localization.Get("slot.invert");
            if (slot.MismatchKind is MapKind kind)
                slot.MismatchNotice = string.Format(CultureInfo.InvariantCulture,
                    Localization.Get("slot.warn.manualMismatch"),
                    Preset.KindDisplayName(kind), slot.Title.Text);
        }
        RefreshThumbnails();
        UpdateSourceCard();
        UpdateMapping();
        UpdateSteps();
        ShowPreview();
        UpdateStatusForCurrentMap();
    }

    private void ApplyPreset()
    {
        _nameEdited = false;
        var preset = CurrentPreset;
        UpdateSlotTitles();

        // Convert: the target preset affects only mapping/preview; Pack slots are left untouched.
        if (_convertMode)
        {
            UpdateMapping();
            UpdateSteps();
            UpdatePreview();
            UpdateName();
            UpdateStatusForCurrentMap();
            return;
        }

        foreach (var channel in AllChannels)
        {
            var slot = _slots[(int)channel];
            var def = preset.Channels[channel];
            slot.Image = null;
            slot.SourceComponent = SourceComponent.Red;
            slot.Source.SelectedIndex = 0;
            slot.InvertValue = false;
            slot.Invert.Checked = false;
            slot.SolidValue = def.DefaultValue;
            slot.Number.Value = slot.SolidValue;
            slot.Slider.Value = slot.SolidValue;
        }

        foreach (var file in _files) AutoAssign(file);
        RefreshThumbnails();
        UpdatePreview();
        UpdateName();
    }

    private void OpenFilesForSlot(SlotView slot)
    {
        if (_openDialog.ShowDialog() == DialogResult.OK)
            AddFiles(_openDialog.FileNames, slot);
    }

    private void AddFiles(string[] paths, SlotView? target = null)
    {
        _savedFileName = null;
        _savedInSession = false;
        var loaded = new List<LoadedFile>();
        var errors = new List<string>();

        foreach (var path in paths)
        {
            // Any file can be added: its name is only a hint, not a restriction.
            try
            {
                var file = new LoadedFile
                {
                    Image = TextureImage.Load(path),
                    Kind = Preset.DetectKind(path),
                    Path = path
                };
                loaded.Add(file);
                if (!_convertMode) _files.Add(file);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (_convertMode && loaded.Count > 0)
        {
            // Any drop (form, card, or former slot area) is treated as one input map.
            _convertSource = loaded[0].Image;
            _convertSourcePath = loaded[0].Path;
            _nameEdited = false;
            _convertNote = loaded.Count > 1
                ? string.Format(CultureInfo.InvariantCulture, Localization.Get("convert.extraFiles"),
                    loaded.Count, Path.GetFileName(loaded[0].Path))
                : null;
            UpdateSourceCard();
            UpdateSteps();
        }
        else if (target is not null && loaded.Count > 0)
        {
            SetSlotImage(target, loaded[0]);
            ShowManualMismatch(target, loaded[0]);
            foreach (var file in loaded.Skip(1)) AutoAssign(file);
        }
        else if (CurrentPreset == Preset.Custom)
        {
            // Custom has no name hints: fill free slots, never destroy loaded maps.
            foreach (var file in loaded)
            {
                var slot = _slots.FirstOrDefault(s => s.Image is null);
                if (slot is null)
                {
                    _statusLabel.Text = Localization.Get("msg.slotsFull");
                    break;
                }
                SetSlotImage(slot, file);
            }
        }
        else
        {
            foreach (var file in loaded) AutoAssign(file);
        }

        RefreshThumbnails();
        UpdatePreview();
        UpdateName();
        UpdateStatusForCurrentMap();

        if (errors.Count > 0)
            MessageBox.Show(string.Join(Environment.NewLine, errors), Localization.Get("msg.loadError"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        PersistSession();
    }

    private void AutoAssign(LoadedFile file)
    {
        var preset = CurrentPreset;

        // First, look for an exact map-kind match. One source may fill several slots
        // (Unity URP: R=G=B=Metallic).
        var targets = preset.ChannelsFor(file.Kind);
        if (targets.Count > 0)
        {
            AssignToSlots(file, targets);
            return;
        }

        // Next, Roughness ↔ Smoothness: the paired slot with inversion.
        var inverted = preset.InvertedChannelsFor(file.Kind);
        if (inverted.Count > 0)
        {
            AssignToSlots(file, inverted);
            return;
        }

        // The name matches no preset mask (e.g., AO named _diff).
        // The user can still use the map: place it in the first free
        // slot and mark it with a soft warning so it can be moved manually.
        if (file.Kind is MapKind.Albedo or MapKind.Unknown)
        {
            for (var i = 0; i < 4; i++)
            {
                var slot = _slots[i];
                if (slot.Image is not null) continue;
                SetSlotImage(slot, file);
                ShowManualMismatch(slot, file);
                return;
            }
        }
    }

    private void AssignToSlots(LoadedFile file, IReadOnlyList<Channel> targets)
    {
        if (targets.Count == 0) return;

        Channel? target = null;
        foreach (var channel in targets)
            if (_slots[(int)channel].Image is null) { target = channel; break; }
        if (target is not null)
        {
            SetSlotImage(_slots[(int)target.Value], file);
            return;
        }

        // When every target slot is taken, move an unrecognized occupant to a spare slot
        // and give the recognized map its channel.
        target = null;
        foreach (var channel in targets)
        {
            var slot = _slots[(int)channel];
            if (slot.MismatchKind is not null || SlotKind((int)channel) == MapKind.Unknown)
            {
                target = channel;
                break;
            }
        }
        if (target is not null)
        {
            var occupantSlot = _slots[(int)target.Value];
            var occupant = new LoadedFile
            {
                Image = occupantSlot.Image!,
                Kind = MapKind.Unknown,
                Path = occupantSlot.Path ?? ""
            };
            var spare = FirstSpareSlot(targets);
            if (spare is not null) SetSlotImage(_slots[(int)spare.Value], occupant);
            SetSlotImage(occupantSlot, file);
            return;
        }

        // All target slots already hold recognized maps: keep them instead of overwriting.
        _statusLabel.Text = Localization.Get("msg.slotsFull");
    }

    private MapKind SlotKind(int index)
    {
        var path = _slots[index].Path;
        return path is null ? MapKind.Unknown : Preset.DetectKind(path);
    }

    private Channel? FirstSpareSlot(IReadOnlyList<Channel> targets)
    {
        for (var i = 0; i < 4; i++)
            if (_slots[i].Image is null && !targets.Contains((Channel)i)) return (Channel)i;
        for (var i = 0; i < 4; i++)
            if (_slots[i].Image is null) return (Channel)i;
        for (var i = 0; i < 4; i++)
            if (!targets.Contains((Channel)i) && SlotKind(i) == MapKind.Unknown) return (Channel)i;
        return null;
    }

    private void ShowManualMismatch(SlotView slot, LoadedFile file)
    {
        if (file.Kind != MapKind.Unknown)
        {
            var def = CurrentPreset.Channels[(Channel)slot.Index];
            if (def.Kind != file.Kind && !Preset.IsInvertedPair(file.Kind, def.Kind))
            {
                slot.MismatchKind = file.Kind;
                slot.MismatchNotice = string.Format(CultureInfo.InvariantCulture,
                    Localization.Get("slot.warn.manualMismatch"),
                    Preset.KindDisplayName(file.Kind), slot.Title.Text);
                return;
            }
        }
        slot.MismatchKind = null;
        slot.MismatchNotice = "";
    }

    private void SetSlotImage(SlotView slot, LoadedFile file)
    {
        var def = CurrentPreset.Channels[(Channel)slot.Index];
        slot.Image = file.Image;
        slot.Path = file.Path;
        slot.MismatchKind = null;
        slot.MismatchNotice = "";
        slot.SourceComponent = SourceComponent.Red;
        slot.Source.SelectedIndex = 0;
        slot.InvertValue = Preset.IsInvertedPair(file.Kind, def.Kind);
        slot.Invert.Checked = slot.InvertValue;
    }

    private ChannelInput[] AllInputs() => new[]
    {
        Input(Channel.R), Input(Channel.G), Input(Channel.B), Input(Channel.A)
    };

    private static Bitmap SolidThumb(byte value, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(255, value, value, value));
        return bitmap;
    }

    private void RefreshThumbnails()
    {
        // In Convert, slots are hidden: their diagnostics are not computed or drawn.
        if (_convertMode) return;
        var inputs = AllInputs();
        foreach (var slot in _slots)
        {
            slot.Thumb.Image?.Dispose();
            // An empty slot is a constant channel: show its value instead of black.
            slot.Thumb.Image = slot.Image is { } image
                ? image.CreateThumbnail(slot.SourceComponent, slot.InvertValue, 56)
                : SolidThumb(slot.InvertValue ? (byte)(255 - slot.SolidValue) : slot.SolidValue, 56);
            slot.Info.Text = slot.Image?.FileName ?? Localization.Get("slot.empty");
            var extension = Path.GetExtension(slot.Path ?? slot.Image?.FileName ?? "")
                .TrimStart('.').ToUpperInvariant();
            slot.Meta.Text = slot.Image is null
                ? $"— · {Localization.Get("bits.8")} · —"
                : $"{slot.Image.Width}×{slot.Image.Height} · {Localization.Get("bits.8")} · {extension}";

            var stats = SlotDiagnostics.Stats(inputs[slot.Index]);
            var values = string.Format(CultureInfo.InvariantCulture, Localization.Get("slot.stats"),
                stats.Min, stats.Max, stats.Avg.ToString("F0", CultureInfo.InvariantCulture));
            var warnings = SlotDiagnostics.Warnings(inputs, slot.Index, stats, HasInputs);
            slot.Stats.Text = values;
            slot.Stats.ForeColor = MutedColor;
            slot.DiagnosticWarning = string.Join("\n",
                warnings.Select(w => Localization.Get("slot.warn." + w)));
            slot.UpdateNotice();

            var empty = slot.Image is null;
            slot.Number.Enabled = empty;
            slot.Slider.Enabled = empty;
        }
    }

    private bool PackNeedsAlpha() => CurrentPreset.AlphaMode switch
    {
        AlphaMode.Required => true,
        AlphaMode.Optional => _slots[(int)Channel.A].Image is not null,
        _ => false
    };

    private (int Width, int Height) TargetOutputSize()
    {
        if (_convertMode)
            return _convertSource is null ? (0, 0) : CurrentSize.Apply(_convertSource.Width, _convertSource.Height);
        return OutputSizing.TargetSize(CurrentSize, AllInputs());
    }

    private void UpdatePreview()
    {
        try
        {
            UpdatePreviewCore();
        }
        catch (Exception ex)
        {
            ClearPreviewImages();
            _previewHint.Visible = true;
            _statusLabel.Text = $"{Localization.Get("msg.saveError")} {ex.Message}";
        }
    }

    private void UpdatePreviewCore()
    {
        (var width, var height) = TargetOutputSize();
        if (width == 0)
        {
            ClearPreviewImages();
            _previewHint.Visible = true;
            _statusLabel.Text = Localization.Get(_convertMode ? "convert.status.empty" : "status.empty");
            return;
        }

        var scale = Math.Min(1f, Packer.PreviewMaxSide / (float)Math.Max(width, height));
        var pw = Math.Max(1, (int)Math.Round(width * scale));
        var ph = Math.Max(1, (int)Math.Round(height * scale));

        var stopwatch = Stopwatch.StartNew();
        var packed = Pack(pw, ph);
        var rgb = Packer.ToBitmap(packed, forceOpaque: true);
        var rgba = Packer.ToBitmap(packed, showCheckerboard: true);
        var channels = new Dictionary<Channel, Bitmap>();
        foreach (var channel in AllChannels)
            channels[channel] = Packer.ToChannelBitmap(packed, channel);
        stopwatch.Stop();

        ClearPreviewImages();
        _previewWidth = width;
        _previewHeight = height;
        _rgbBitmap = rgb;
        _rgbaBitmap = rgba;
        foreach (var channel in AllChannels)
            _channelBitmaps[channel] = channels[channel];
        _previewHint.Visible = false;
        ShowPreview();
        UpdateStatusForCurrentMap(stopwatch.ElapsedMilliseconds);
    }

    private void ClearPreviewImages()
    {
        _previewWidth = _previewHeight = 0;
        _previewBox.Image = null;
        _rgbBitmap?.Dispose();
        _rgbaBitmap?.Dispose();
        _rgbBitmap = _rgbaBitmap = null;
        foreach (var channel in AllChannels)
        {
            if (_channelBitmaps.TryGetValue(channel, out var bitmap)) bitmap.Dispose();
            _channelBitmaps.Remove(channel);
        }
    }

    private void UpdateStatusForCurrentMap(long? previewMs = null)
    {
        // No inputs means no action: keep Save/Unpack/Clear and the channel preview tabs inert.
        var hasInputs = HasInputs;
        _saveButton.Enabled = hasInputs;
        _unpackFooterButton.Enabled = hasInputs;
        _unpackButton.Enabled = hasInputs;
        _clearButton.Enabled = hasInputs;
        foreach (var (mode, tab) in _viewButtons)
            tab.Enabled = hasInputs || mode is PreviewMode.Rgb or PreviewMode.Rgba;

        var note = _convertMode && _convertNote is not null ? " · " + _convertNote : "";

        // A successful save keeps its status line until the next edit; language changes re-render the localized prefix.
        if (_savedFileName is not null)
        {
            _statusLabel.Text = $"{Localization.Get("status.saved")} {_savedFileName}";
            return;
        }
        var preview = previewMs is null ? "" :
            " · " + string.Format(CultureInfo.InvariantCulture, Localization.Get("status.ms"), previewMs);

        (var width, var height) = TargetOutputSize();
        if (width == 0)
        {
            _statusLabel.Text = Localization.Get(_convertMode ? "convert.status.empty" : "status.empty") + note;
            return;
        }

        if (_convertMode)
        {
            var status = string.Format(CultureInfo.InvariantCulture, Localization.Get("convert.status"),
                "1", width, height, ShortPresetName(SourcePreset), ShortPresetName(CurrentPreset));
            _statusLabel.Text = status + preview + note;
            return;
        }

        var distinctSizes = _slots.Where(s => s.Image is not null)
            .Select(s => (s.Image!.Width, s.Image.Height)).Distinct().Count();
        var warning = distinctSizes > 1 ? "   " + Localization.Get("status.sizeWarning") : "";
        _statusLabel.Text = string.Format(CultureInfo.InvariantCulture, Localization.Get("status.pack"),
            _files.Count, width, height) + preview + warning;
    }

    private bool CurrentAlpha()
    {
        if (!_convertMode || _convertSource is null) return PackNeedsAlpha();
        var inputs = PackerConvert.BuildInputs(_convertSource, SourcePreset, CurrentPreset);
        return PackerConvert.NeedsAlpha(CurrentPreset, inputs);
    }

    private PackedTexture Pack(int width, int height)
    {
        if (_convertMode && _convertSource is not null)
        {
            var inputs = PackerConvert.BuildInputs(_convertSource, SourcePreset, CurrentPreset);
            return Packer.Pack(width, height, inputs[0], inputs[1], inputs[2], inputs[3], CurrentAlpha());
        }
        return Packer.Pack(width, height,
            Input(Channel.R), Input(Channel.G), Input(Channel.B), Input(Channel.A), PackNeedsAlpha());
    }

    private ChannelInput Input(Channel channel)
    {
        var slot = _slots[(int)channel];
        var kind = CurrentPreset.Channels[channel].Kind;
        return new ChannelInput(slot.Image, slot.SourceComponent, slot.SolidValue, slot.InvertValue,
            Preset.FilterFor(kind));
    }

    private void Save()
    {
        (var width, var height) = TargetOutputSize();
        if (width == 0)
        {
            MessageBox.Show(Localization.Get("msg.noMaps"), Localization.Get("msg.noMapsTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var format = CurrentFormat;
        string name;
        try
        {
            name = _nameBox.Text.Trim();
            if (name.Length == 0)
                name = OutputNaming.Suggest(SourcePaths(), CurrentPreset.Suffix, format.Extension);
            name = Path.ChangeExtension(name, format.Extension);
            name = OutputNaming.EnsureUnique(name, _lastOutputDirectory);
        }
        catch (Exception)
        {
            // A bad saved output directory or file name must not stop the export.
            name = "Texture" + format.Extension;
            _lastOutputDirectory = null;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = format.Filter,
            FileName = name,
            AddExtension = true,
            Title = Localization.Get("dialog.save.title")
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;

        _lastOutputDirectory = Path.GetDirectoryName(dialog.FileName);
        PersistSession();
        var alpha = CurrentAlpha() && format.SupportsAlpha;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var packed = Pack(width, height);
            Packer.Save(packed, dialog.FileName, format.Format, alpha);
        }
        catch (OutOfMemoryException)
        {
            MessageBox.Show(Localization.Get("msg.memory"), Localization.Get("msg.noMapsTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Localization.Get("msg.saveError")} {ex.Message}",
                Localization.Get("msg.noMapsTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        stopwatch.Stop();

        _savedFileName = Path.GetFileName(dialog.FileName);
        _savedInSession = true;
        UpdateSteps();
        _statusLabel.Text = $"{Localization.Get("status.saved")} {_savedFileName} · " +
                            $"{width}×{height} · " +
                            string.Format(CultureInfo.InvariantCulture,
                                Localization.Get("status.ms"), stopwatch.ElapsedMilliseconds);
    }

    private void UpdateName()
    {
        if (_convertMode && _convertSource is null)
        {
            // Without a map there is nothing to build a name from: leave the field empty with its placeholder.
            _updatingName = true;
            _nameBox.Text = "";
            _updatingName = false;
            _nameEdited = false;
        }
        else if (!_nameEdited)
        {
            _updatingName = true;
            _nameBox.Text = OutputNaming.Suggest(SourcePaths(), CurrentPreset.Suffix, CurrentFormat.Extension);
            _updatingName = false;
        }
        if (_convertMode) UpdateSteps();
    }
}
