using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace AudioToggle;

internal class StyledButton : Button
{
    private bool hovered;
    private bool pressed;
    private readonly bool quiet;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Primary { get; set; }

    internal StyledButton() : this(false) { }

    internal StyledButton(bool quiet)
    {
        this.quiet = quiet;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; base.OnMouseCaptureChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressed = true; base.OnKeyDown(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; base.OnKeyUp(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        var background = quiet ? (pressed ? Color.FromArgb(128, 36, 44) : hovered ? Color.FromArgb(161, 47, 56) : BackColor) :
            !Enabled ? Color.FromArgb(39, 39, 39) :
            pressed ? Color.FromArgb(69, 69, 69) :
            Primary ? (hovered ? Color.FromArgb(64, 94, 118) : Color.FromArgb(51, 77, 99)) :
            hovered ? Color.FromArgb(62, 62, 62) : Color.FromArgb(51, 51, 51);
        var border = quiet ? background : !Enabled ? Color.FromArgb(64, 64, 64) :
            Focused ? Color.FromArgb(132, 179, 212) :
            Primary ? Color.FromArgb(83, 121, 150) :
            hovered ? Color.FromArgb(172, 172, 172) : Color.FromArgb(133, 133, 133);
        var foreground = Enabled ? Color.WhiteSmoke : Color.FromArgb(128, 128, 128);
        using var fill = new SolidBrush(background);
        e.Graphics.FillRectangle(fill, ClientRectangle);
        using var pen = new Pen(border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        DrawContent(e.Graphics, ClientRectangle, foreground);
    }

    protected virtual void DrawContent(Graphics graphics, Rectangle bounds, Color color) =>
        TextRenderer.DrawText(graphics, Text, Font, bounds, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
}

internal sealed class ChevronButton : StyledButton
{
    private readonly bool up;

    internal ChevronButton(bool up)
    {
        this.up = up;
        Text = up ? "↑" : "↓";
        AccessibleName = up ? "Move device up" : "Move device down";
        AutoSize = false;
        Margin = new Padding(4);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
    }

    protected override void DrawContent(Graphics graphics, Rectangle bounds, Color color)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        float x = bounds.Width / 2f;
        float y = bounds.Height / 2f;
        float half = 4.5f * scale;
        float direction = up ? -1 : 1;
        using var pen = new Pen(color, 1.7f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        graphics.DrawLines(pen, new[] { new PointF(x - half, y - direction * 2 * scale), new PointF(x, y + direction * 2 * scale), new PointF(x + half, y - direction * 2 * scale) });
    }
}
