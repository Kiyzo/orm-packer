using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace OrmPacker;

/// <summary>Button with rounded corners, hover/pressed states, and a "selected" mode.</summary>
public sealed class ModernButton : Control
{
    private bool _hover;
    private bool _pressed;

    public Color BaseColor { get; set; } = Color.FromArgb(45, 53, 68);
    public Color HoverColor { get; set; } = Color.FromArgb(60, 71, 91);
    public Color PressColor { get; set; } = Color.FromArgb(75, 147, 246);
    public Color TextColor { get; set; } = Color.FromArgb(232, 238, 247);

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }
    public int Radius { get; set; } = 6;

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        BackColor = Color.FromArgb(23, 29, 40);
        Font = new Font("Segoe UI", 9f, FontStyle.Bold);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true; Invalidate();
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false; Invalidate();
        base.OnMouseUp(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
        {
            _pressed = false; Invalidate();
            // Control has no built-in keyboard click, so Space/Enter must raise Click explicitly.
            OnClick(EventArgs.Empty);
        }
        base.OnKeyUp(e);
    }

    // Text is drawn manually: system invalidation does not guarantee a repaint when the language changes.
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnEnabledChanged(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(ResolveBackground(Parent)))
            e.Graphics.FillRectangle(background, ClientRectangle);
        var fill = (_selected || _pressed) ? PressColor : _hover ? HoverColor : BaseColor;
        // The path is 1 px wider than the client area: straight edges are fully painted rather than
        // anti-aliased into the background (otherwise a visible border appears around the button).
        using var path = RoundedPath(new Rectangle(-1, -1, ClientSize.Width + 2, ClientSize.Height + 2), Radius);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        if (!Enabled)
        {
            using var dim = new SolidBrush(Color.FromArgb(130, 20, 26, 37));
            e.Graphics.FillPath(dim, path);
        }

        // TextRenderer keeps short labels (single letters, symbols) crisp at small sizes.
        var textColor = Enabled ? TextColor : Color.FromArgb(120, 140, 168);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    internal static Color ResolveBackground(Control? control)
    {
        while (control is not null)
        {
            if (control.BackColor != Color.Transparent) return control.BackColor;
            control = control.Parent;
        }
        return Color.FromArgb(16, 20, 27);
    }

    internal static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
        path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
        path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Card with rounded corners and a thin border, matching the web app's .panel.</summary>
public sealed class RoundedPanel : Panel
{
    public Color BorderColor { get; set; } = Color.FromArgb(48, 60, 80);
    public int Radius { get; set; } = 12;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(28, 35, 48);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
        using var path = ModernButton.RoundedPath(rect, Radius);
        using (var brush = new SolidBrush(BackColor)) e.Graphics.FillPath(brush, path);
        using (var pen = new Pen(BorderColor)) e.Graphics.DrawPath(pen, path);
        base.OnPaint(e);
    }
}

public sealed class ModernCheckBox : Control
{
    private bool _hover;
    public bool Checked { get; set; }
    public Color AccentColor { get; set; } = Color.FromArgb(75, 147, 246);

    public event EventHandler? CheckedChanged;

    public ModernCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        BackColor = Color.FromArgb(30, 37, 49);
        ForeColor = Color.FromArgb(232, 238, 247);
        Height = 26;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        Invalidate();
        CheckedChanged?.Invoke(this, EventArgs.Empty);
        base.OnClick(e);
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(ModernButton.ResolveBackground(Parent)))
            e.Graphics.FillRectangle(background, ClientRectangle);
        var box = new Rectangle(2, (ClientSize.Height - 16) / 2, 16, 16);
        using (var path = ModernButton.RoundedPath(box, 4))
        {
            using var fill = new SolidBrush(Checked ? AccentColor : Color.FromArgb(48, 56, 72));
            e.Graphics.FillPath(fill, path);
            using var pen = new Pen(_hover ? Color.FromArgb(120, 160, 220) : Color.FromArgb(90, 104, 128));
            e.Graphics.DrawPath(pen, path);
        }
        if (Checked)
        {
            var points = new[]
            {
                new Point(box.X + 4, box.Y + 8),
                new Point(box.X + 7, box.Y + 11),
                new Point(box.X + 12, box.Y + 5)
            };
            using var pen = new Pen(Color.White, 2f);
            e.Graphics.DrawLines(pen, points);
        }
        using var text = new SolidBrush(ForeColor);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter
        };
        e.Graphics.DrawString(Text, Font, text,
            new Rectangle(24, 0, Math.Max(1, ClientSize.Width - 24), ClientSize.Height), format);
    }
}

/// <summary>Custom-drawn 0..255 slider; keyboard/wheel step is 1 (10 with Shift).</summary>
public sealed class ModernTrackBar : Control
{
    private bool _dragging;
    public int Minimum { get; set; } = 0;
    public int Maximum { get; set; } = 255;
    private int _value;
    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, Minimum, Maximum);
            if (_value == next) return;
            _value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Color AccentColor { get; set; } = Color.FromArgb(75, 147, 246);
    public event EventHandler? ValueChanged;

    public ModernTrackBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        BackColor = Color.FromArgb(30, 37, 49);
        Height = 24;
    }

    private int ValueFromX(int x)
    {
        var span = Math.Max(1, ClientSize.Width - 24);
        return Minimum + (int)Math.Round((x - 12) / (double)span * (Maximum - Minimum));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragging = true;
        // Capture keeps drag updates arriving after the pointer leaves the slider.
        Capture = true;
        Value = ValueFromX(e.X);
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) Value = ValueFromX(e.X);
        base.OnMouseMove(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        base.OnMouseUp(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value += Math.Sign(e.Delta) * (ModifierKeys == Keys.Shift ? 10 : 1);
        base.OnMouseWheel(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = e.Shift ? 10 : 1;
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value -= step;
        else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value += step;
        else if (e.KeyCode == Keys.Home) Value = Minimum;
        else if (e.KeyCode == Keys.End) Value = Maximum;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(ModernButton.ResolveBackground(Parent)))
            e.Graphics.FillRectangle(background, ClientRectangle);
        var y = ClientSize.Height / 2;
        int left = 12, right = ClientSize.Width - 12;
        using (var track = new Pen(Color.FromArgb(74, 86, 106), 4f))
        {
            track.StartCap = LineCap.Round;
            track.EndCap = LineCap.Round;
            e.Graphics.DrawLine(track, left, y, right, y);
        }
        var ratio = (double)(Value - Minimum) / Math.Max(1, Maximum - Minimum);
        var thumbX = left + (int)Math.Round(ratio * (right - left));
        using (var filled = new Pen(AccentColor, 4f))
        {
            filled.StartCap = LineCap.Round;
            e.Graphics.DrawLine(filled, left, y, thumbX, y);
        }
        using var brush = new SolidBrush(Color.FromArgb(240, 245, 252));
        e.Graphics.FillEllipse(brush, thumbX - 5, y - 5, 10, 10);
        using var border = new Pen(AccentColor, 2f);
        e.Graphics.DrawEllipse(border, thumbX - 5, y - 5, 10, 10);
    }
}

/// <summary>Fully custom-drawn 0..255 number box — no white system controls.</summary>
public sealed class ModernNumberBox : Control
{
    private bool _hoverUp;
    private bool _hoverDown;

    public int Minimum { get; set; } = 0;
    public int Maximum { get; set; } = 255;

    private int _value = 255;
    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, Minimum, Maximum);
            if (_value == next) return;
            _value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;

    public ModernNumberBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(44, 52, 67);
        ForeColor = Color.FromArgb(235, 240, 248);
        Height = 26;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var up = e.X > ClientSize.Width - 16 && e.Y < ClientSize.Height / 2;
        var down = e.X > ClientSize.Width - 16 && e.Y >= ClientSize.Height / 2;
        if (up != _hoverUp || down != _hoverDown)
        {
            _hoverUp = up;
            _hoverDown = down;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverUp = _hoverDown = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.X > ClientSize.Width - 16)
            Value = e.Y < ClientSize.Height / 2 ? _value + 1 : _value - 1;
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up) Value = _value + 1;
        else if (e.KeyCode == Keys.Down) Value = _value - 1;
        else if (e.KeyCode == Keys.PageUp) Value = _value + 10;
        else if (e.KeyCode == Keys.PageDown) Value = _value - 10;
        else if (e.KeyCode == Keys.Home) Value = Minimum;
        else if (e.KeyCode == Keys.End) Value = Maximum;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = ModernButton.RoundedPath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 5))
        {
            using var fill = new SolidBrush(BackColor);
            e.Graphics.FillPath(fill, path);
        }

        // Reserve the right 18 px for the spinner arrows.
        var textWidth = ClientSize.Width - 18;
        using var text = new SolidBrush(ForeColor);
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        e.Graphics.DrawString(_value.ToString(), Font, text, new Rectangle(0, 0, textWidth, ClientSize.Height), format);

        var arrowX = ClientSize.Width - 11;
        var mid = ClientSize.Height / 2;
        using var upBrush = new SolidBrush(_hoverUp ? Color.FromArgb(90, 170, 255) : Color.FromArgb(160, 175, 198));
        using var downBrush = new SolidBrush(_hoverDown ? Color.FromArgb(90, 170, 255) : Color.FromArgb(160, 175, 198));
        e.Graphics.FillPolygon(upBrush, new[]
        {
            new Point(arrowX - 4, 11), new Point(arrowX + 4, 11), new Point(arrowX, 5)
        });
        e.Graphics.FillPolygon(downBrush, new[]
        {
            new Point(arrowX - 4, 15), new Point(arrowX + 4, 15), new Point(arrowX, 21)
        });
    }
}


/// <summary>Fully dark-themed drop-down list — without the system's gray arrow.</summary>
public sealed class ModernComboBox : Control
{
    private static readonly Color ListBackground = Color.FromArgb(44, 52, 67);
    private static readonly Color ListForeground = Color.FromArgb(235, 240, 248);

    private readonly ListBox _list = new();
    private readonly ToolStripDropDown _popup = new();
    private readonly ToolStripControlHost _host;
    private int _selectedIndex = -1;
    private bool _hover;

    public List<object> Items { get; } = new();
    public Func<object?, string>? ItemTextSelector { get; set; }
    public Func<object?, string>? ItemListTextSelector { get; set; }
    public object? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : Items.IndexOf(value);
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = value >= 0 && value < Items.Count ? value : -1;
            if (_selectedIndex == next) return;
            _selectedIndex = next;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectedIndexChanged;

    public ModernComboBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = ListBackground;
        ForeColor = ListForeground;
        Height = 28;
        Cursor = Cursors.Hand;

        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = 22;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = ListBackground;
        _list.ForeColor = ListForeground;
        _list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            e.DrawBackground();
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? Color.FromArgb(75, 147, 246) : ListBackground);
            e.Graphics.FillRectangle(background, e.Bounds);
            using var text = new SolidBrush(selected ? Color.White : ListForeground);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            };
            var itemText = ItemListTextSelector is null
                ? ItemTextOf(Items[e.Index])
                : (ItemListTextSelector(Items[e.Index]) ?? "");
            e.Graphics.DrawString(itemText, Font, text,
                new Rectangle(e.Bounds.X + 5, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), format);
        };
        _list.Click += (_, _) =>
        {
            if (_list.SelectedIndex >= 0) SelectedIndex = _list.SelectedIndex;
            _popup.Close();
        };

        // Zero the ToolStrip's default inset so the list aligns flush with the control.
        _popup.BackColor = ListBackground;
        _popup.Padding = Padding.Empty;
        _popup.Margin = Padding.Empty;
        _popup.AutoSize = false;
        _host = new ToolStripControlHost(_list)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false
        };
        _popup.Items.Add(_host);
    }

    private string ItemTextOf(object? item) =>
        ItemTextSelector is null ? item?.ToString() ?? "" : ItemTextSelector(item);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        _list.Items.Clear();
        foreach (var item in Items) _list.Items.Add(item);
        _list.SelectedIndex = _selectedIndex;
        var listHeight = Math.Max(_list.ItemHeight + 4,
            Math.Min(Items.Count, 10) * _list.ItemHeight + 4);
        _list.Size = new Size(Width, listHeight);
        _host.Size = new Size(Width, listHeight);
        _popup.Size = new Size(Width + 2, listHeight + 2);
        _popup.ClientSize = new Size(Width + 2, listHeight + 2);
        _popup.Show(this, new Point(0, Height));
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up && _selectedIndex > 0) SelectedIndex = _selectedIndex - 1;
        else if (e.KeyCode == Keys.Down && _selectedIndex < Items.Count - 1) SelectedIndex = _selectedIndex + 1;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = ModernButton.RoundedPath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 5))
        {
            using var fill = new SolidBrush(_hover ? Color.FromArgb(54, 64, 82) : BackColor);
            e.Graphics.FillPath(fill, path);
        }

        using var text = new SolidBrush(ForeColor);
        using var textPath = new GraphicsPath();
        textPath.AddString(
            SelectedItem is null ? "" : ItemTextOf(SelectedItem),
            Font.FontFamily, (int)Font.Style, Font.Size,
            new PointF(7, (ClientSize.Height - Font.Height) / 2f),
            StringFormat.GenericDefault);
        e.Graphics.FillPath(text, textPath);

        var x = ClientSize.Width - 14;
        var y = ClientSize.Height / 2;
        using var arrow = new SolidBrush(Color.FromArgb(180, 194, 214));
        e.Graphics.FillPolygon(arrow, new[]
        {
            new Point(x - 3, y - 1), new Point(x + 3, y - 1), new Point(x, y + 3)
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _popup.Dispose();
        base.Dispose(disposing);
    }
}
