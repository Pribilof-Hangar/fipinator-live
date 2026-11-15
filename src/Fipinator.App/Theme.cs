// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Runtime.InteropServices;

namespace Fipinator.App;

/// <summary>
/// FIPinator Live's "cockpit dark" look, matching FIPinator 3000: panel greys, warm light text, amber for the main
/// button, green/yellow/red for status.
/// Only colors and fonts: nothing here changes what a window does. <see cref="Apply"/> styles a window
/// and everything on it, including controls added later (new page rows, picture tiles).
/// </summary>
internal static class Theme
{
    // "Cockpit dark" (owner, 2026-11-14): the same panel greys and amber as FIPinator 3000, so the two look like a set.
    public static readonly Color Back = Color.FromArgb(27, 30, 33);
    public static readonly Color Card = Color.FromArgb(38, 42, 46);
    public static readonly Color Surface = Color.FromArgb(46, 51, 56);
    public static readonly Color SurfaceHover = Color.FromArgb(60, 66, 72);
    public static readonly Color Border = Color.FromArgb(58, 64, 70);
    public static readonly Color Text = Color.FromArgb(232, 229, 220);
    public static readonly Color Muted = Color.FromArgb(169, 165, 155);
    public static readonly Color Good = Color.FromArgb(76, 175, 80);
    public static readonly Color Bad = Color.FromArgb(239, 83, 80);
    public static readonly Color Wait = Color.FromArgb(230, 190, 60);
    public static readonly Color Accent = Color.FromArgb(242, 160, 61);
    public static readonly Color AccentHover = Color.FromArgb(245, 178, 94);
    public static readonly Color OnAccent = Color.FromArgb(27, 30, 33);
    public static readonly Color FreeGreen = Color.FromArgb(159, 211, 138);

    /// <summary>Styles a window and all its controls, now and as controls are added.</summary>
    public static void Apply(Form form)
    {
        form.BackColor = Back;
        form.ForeColor = Text;
        form.HandleCreated += (_, _) => DarkTitleBar(form.Handle);
        if (form.IsHandleCreated) DarkTitleBar(form.Handle);
        Style(form);
    }

    private static void Style(Control parent)
    {
        parent.ControlAdded -= OnAdded;
        parent.ControlAdded += OnAdded;
        foreach (Control c in parent.Controls) StyleOne(c);
    }

    private static void OnAdded(object? sender, ControlEventArgs e) => StyleOne(e.Control!);

    private static void StyleOne(Control c)
    {
        switch (c)
        {
            case Button b:
                // Buttons marked as the main one (Tag "accent") are amber with dark text; the rest are dark grey.
                var accent = Equals(b.Tag, "accent");
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = accent ? Accent : Surface;
                b.ForeColor = accent ? OnAccent : Text;
                b.FlatAppearance.BorderColor = accent ? Accent : Border;
                b.FlatAppearance.MouseOverBackColor = accent ? AccentHover : SurfaceHover;
                b.FlatAppearance.MouseDownBackColor = Border;
                b.Cursor = Cursors.Hand;
                break;
            case LinkLabel l:
                l.LinkColor = Color.FromArgb(120, 190, 255);
                l.ActiveLinkColor = l.VisitedLinkColor = l.LinkColor;
                break;
            case TextBox t:
                t.BackColor = Surface;
                t.ForeColor = Text;
                t.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox box:
                box.FlatStyle = FlatStyle.Flat;
                box.BackColor = Surface;
                box.ForeColor = Text;
                DarkControl(box, "DarkMode_CFD");
                break;
            case ListView list:
                list.BackColor = Card;
                list.ForeColor = Text;
                list.BorderStyle = BorderStyle.FixedSingle;
                DarkListHeader(list);
                DarkControl(list, "DarkMode_Explorer");
                break;
            case CheckBox check:
                check.ForeColor = Text;
                break;
            case Label label:
                label.ForeColor = Recolor(label.ForeColor);
                break;
            case ScrollableControl scroll when scroll.AutoScroll:
                DarkControl(scroll, "DarkMode_Explorer");
                break;
        }
        // Old light-theme panel colors: white boxes become dark cards, light grey areas the dark background.
        if (c is System.Windows.Forms.Panel && c.BackColor.ToArgb() == Color.White.ToArgb()) c.BackColor = Card;
        if (c is System.Windows.Forms.Panel && c.BackColor.ToArgb() == Color.FromArgb(242, 242, 242).ToArgb()) c.BackColor = Back;
        Style(c);
    }

    /// <summary>Maps the old light-theme text colors (black, greys) to their dark-theme versions.</summary>
    public static Color Recolor(Color color) =>
        color.ToArgb() == Color.Black.ToArgb() || color.ToArgb() == SystemColors.ControlText.ToArgb() ? Text
        : color.R == color.G && color.G == color.B && color.R is >= 40 and <= 140 ? Muted
        : color;

    /// <summary>A small button for the Tools/Help rows.</summary>
    public static Button SmallButton(string text, char icon, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9f), Padding = new Padding(3, 1, 3, 1) };
        AddIcon(b, icon, 9f);
        b.Click += click;
        return b;
    }

    /// <summary>Puts a Windows icon-font symbol in front of a button's text (skipped if the font is missing).</summary>
    public static void AddIcon(Button button, char glyph, float size = 10f)
    {
        var family = IconFont.Value;
        if (family is null) return;
        var px = (int)Math.Ceiling(size * 1.8f);
        var image = new Bitmap(px, px);
        using (var g = Graphics.FromImage(image))
        using (var font = new Font(family, size, GraphicsUnit.Point))
        using (var brush = new SolidBrush(Text))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var at = g.MeasureString(glyph.ToString(), font);
            g.DrawString(glyph.ToString(), font, brush, (px - at.Width) / 2 + 1, (px - at.Height) / 2 + 1);
        }
        button.Image = image;
        button.ImageAlign = ContentAlignment.MiddleLeft;
        button.TextImageRelation = TextImageRelation.ImageBeforeText;
        button.Disposed += (_, _) => image.Dispose();
    }

    // Windows 11 has "Segoe Fluent Icons"; Windows 10 has "Segoe MDL2 Assets" (same symbol codes).
    private static readonly Lazy<FontFamily?> IconFont = new(() =>
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        return fonts.Families.FirstOrDefault(f => f.Name == "Segoe Fluent Icons")
            ?? fonts.Families.FirstOrDefault(f => f.Name == "Segoe MDL2 Assets");
    });

    public static class Icons
    {
        public const char Settings = '', Play = '', Photo = '', Swap = '', Reset = '',
            Check = '', Wrench = '', Folder = '', Report = '', Logs = '', Info = '',
            Download = '', Power = '';
    }

    /// <summary>A colored dot (●) label for status.</summary>
    public static Label Dot() => new() { Text = "●", AutoSize = true, Font = new Font("Segoe UI", 12f), ForeColor = Muted, Margin = new Padding(0, 0, 0, 0) };

    private static void DarkListHeader(ListView list)
    {
        if (list.OwnerDraw) return;
        list.OwnerDraw = true;
        list.DrawColumnHeader += (_, e) =>
        {
            using var back = new SolidBrush(Card);
            using var line = new Pen(Border);
            e.Graphics.FillRectangle(back, e.Bounds);
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, list.Font, Rectangle.Inflate(e.Bounds, -4, 0), Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        list.DrawItem += (_, e) => e.DrawDefault = true;
        list.DrawSubItem += (_, e) => e.DrawDefault = true;
    }

    // Dark scrollbars and drop-down lists (Windows 10 1809 and later; ignored elsewhere).
    private static void DarkControl(Control control, string theme)
    {
        void Set() { try { SetWindowTheme(control.Handle, theme, null); } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { } }
        if (control.IsHandleCreated) Set();
        control.HandleCreated += (_, _) => Set();
    }

    private static void DarkTitleBar(IntPtr handle)
    {
        try
        {
            var on = 1;
            // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 10 20H1+ and 11; 19 on older Windows 10.
            if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);
}
