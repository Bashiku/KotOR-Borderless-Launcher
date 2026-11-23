// KotorLauncher UI - custom dark "holo" theme inspired by KotOR's own interface.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KotorLauncher
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(7, 11, 20);
        public static readonly Color Card = Color.FromArgb(13, 20, 34);
        public static readonly Color CardBorder = Color.FromArgb(30, 52, 82);
        public static readonly Color Field = Color.FromArgb(9, 15, 27);
        public static readonly Color Holo = Color.FromArgb(70, 170, 245);
        public static readonly Color HoloDim = Color.FromArgb(40, 90, 140);
        public static readonly Color Gold = Color.FromArgb(222, 180, 96);
        public static readonly Color GoldHot = Color.FromArgb(245, 205, 120);
        public static readonly Color Text = Color.FromArgb(222, 232, 245);
        public static readonly Color Muted = Color.FromArgb(120, 140, 168);
        public static readonly Color Off = Color.FromArgb(38, 50, 70);
        public static float Scale = 1f;
        public static int S(float v) { return (int)Math.Round(v * Scale); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static Font F(float pt, FontStyle st = FontStyle.Regular) { return new Font("Segoe UI", pt, st); }
        public static Font FSemi(float pt) { return new Font("Segoe UI Semibold", pt); }
    }

    /* ---------------- custom controls ---------------- */

    class Toggle : Control
    {
        bool on; float anim; Timer t;
        public event EventHandler Changed;
        public string Caption, Hint;
        public bool Checked { get { return on; } set { if (on != value) { on = value; StartAnim(); if (Changed != null) Changed(this, EventArgs.Empty); } } }
        public Toggle(string caption, string hint, bool value)
        {
            Caption = caption; Hint = hint; on = value; anim = value ? 1 : 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
            Height = Theme.S(hint == null ? 30 : 42);
            t = new Timer { Interval = 15 };
            t.Tick += (o, e) => { float tg = on ? 1 : 0; anim += (tg - anim) * 0.35f; if (Math.Abs(tg - anim) < 0.02f) { anim = tg; t.Stop(); } Invalidate(); };
        }
        void StartAnim() { t.Start(); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        bool hover;
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float w = Theme.S(38), h = Theme.S(20), x = 0, y = Theme.S(5);
            Color track = Lerp(Theme.Off, Theme.Holo, anim);
            using (var p = Theme.Round(new RectangleF(x, y, w, h), h / 2)) using (var b = new SolidBrush(track)) g.FillPath(b, p);
            if (anim > 0.05f)
                using (var p = Theme.Round(new RectangleF(x - 2, y - 2, w + 4, h + 4), h / 2 + 2))
                using (var pen = new Pen(Color.FromArgb((int)(60 * anim), Theme.Holo), 2)) g.DrawPath(pen, p);
            float k = h - Theme.S(6), kx = x + Theme.S(3) + (w - k - Theme.S(6)) * anim;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, y + Theme.S(3), k, k);
            using (var f = Theme.F(9.5f)) using (var b = new SolidBrush(hover ? Color.White : Theme.Text))
                g.DrawString(Caption, f, b, w + Theme.S(12), Theme.S(3));
            if (Hint != null) using (var f = Theme.F(8f)) using (var b = new SolidBrush(Theme.Muted))
                g.DrawString(Hint, f, b, w + Theme.S(12), Theme.S(22));
        }
        static Color Lerp(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    class Segmented : Control
    {
        public string[] Items; int sel, hot = -1;
        public event EventHandler Changed;
        public int Selected { get { return sel; } set { sel = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); } }
        public Segmented(params string[] items)
        {
            Items = items;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(34); Cursor = Cursors.Hand;
        }
        int Hit(int x) { return Math.Max(0, Math.Min(Items.Length - 1, x * Items.Length / Math.Max(1, Width))); }
        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.X); if (h != hot) { hot = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { Selected = Hit(e.X); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent.BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var p = Theme.Round(r, Theme.S(6))) { using (var b = new SolidBrush(Theme.Field)) g.FillPath(b, p); using (var pen = new Pen(Theme.CardBorder)) g.DrawPath(pen, p); }
            float sw = (Width - Theme.S(6)) / (float)Items.Length;
            for (int i = 0; i < Items.Length; i++)
            {
                var c = new RectangleF(Theme.S(3) + i * sw, Theme.S(3), sw, Height - Theme.S(6));
                if (i == sel)
                    using (var p = Theme.Round(c, Theme.S(4)))
                    using (var b = new LinearGradientBrush(c, Color.FromArgb(40, 120, 200), Color.FromArgb(25, 80, 150), 90f)) { g.FillPath(b, p); using (var pen = new Pen(Theme.Holo)) g.DrawPath(pen, p); }
                else if (i == hot)
                    using (var p = Theme.Round(c, Theme.S(4))) using (var b = new SolidBrush(Color.FromArgb(22, 34, 54))) g.FillPath(b, p);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var f = i == sel ? Theme.FSemi(9.5f) : Theme.F(9.5f)) using (var b = new SolidBrush(i == sel ? Color.White : Theme.Muted))
                    g.DrawString(Items[i], f, b, c, sf);
            }
        }
    }

    class FancyButton : Control
    {
        public bool Primary; bool hover, down;
        public FancyButton(string text, bool primary)
        {
            Text = text; Primary = primary;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent.BackColor);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            if (down) r.Offset(0, 1);
            using (var p = Theme.Round(r, Theme.S(Primary ? 7 : 5)))
            {
                if (Primary)
                {
                    if (hover && Enabled)
                        using (var glow = Theme.Round(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Theme.S(8)))
                        using (var pen = new Pen(Color.FromArgb(110, Theme.GoldHot), 2)) g.DrawPath(pen, glow);
                    Color a = Enabled ? (hover ? Theme.GoldHot : Theme.Gold) : Theme.Off, b2 = Enabled ? Color.FromArgb(170, 125, 50) : Theme.Off;
                    using (var b = new LinearGradientBrush(r, a, b2, 90f)) g.FillPath(b, p);
                }
                else
                {
                    using (var b = new SolidBrush(hover ? Color.FromArgb(22, 36, 58) : Theme.Card)) g.FillPath(b, p);
                    using (var pen = new Pen(hover ? Theme.Holo : Theme.CardBorder)) g.DrawPath(pen, p);
                }
            }
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var f = Primary ? new Font("Segoe UI Semibold", 13f) : Theme.F(9f))
            using (var b = new SolidBrush(Primary ? Color.FromArgb(30, 20, 5) : (hover ? Color.White : Theme.Text)))
                g.DrawString(Primary ? Spaced(Text) : Text, f, b, r, sf);
        }
        static string Spaced(string s) { var sb = new StringBuilder(); foreach (var c in s) { sb.Append(c); sb.Append(' '); } return sb.ToString().TrimEnd(' '); }
    }

    class Card : Panel
    {
        public string Title;
        public Card(string title)
        {
            Title = title; BackColor = Theme.Card; Padding = new Padding(Theme.S(16), Theme.S(38), Theme.S(16), Theme.S(10));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, Theme.S(8)))
            {
                using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (var pen = new Pen(Theme.CardBorder)) g.DrawPath(pen, p);
            }
            // holo accent bar + title
            using (var b = new SolidBrush(Theme.Holo)) g.FillRectangle(b, Theme.S(16), Theme.S(15), Theme.S(3), Theme.S(13));
            using (var f = new Font("Segoe UI Semibold", 8.5f)) using (var b = new SolidBrush(Theme.Holo))
                g.DrawString(Title.ToUpperInvariant(), f, b, Theme.S(24), Theme.S(12));
        }
    }

    class FieldBox : Panel
    {
        public TextBox Box;
        public FieldBox(string text, int width, string placeholder = null)
        {
            Size = new Size(width, Theme.S(30)); BackColor = Theme.Field; Padding = new Padding(Theme.S(8), Theme.S(6), Theme.S(6), 0);
            Box = new TextBox { Text = text, BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text, Dock = DockStyle.Fill, Font = Theme.F(10f), TextAlign = HorizontalAlignment.Center };
            Controls.Add(Box);
            Box.GotFocus += (o, e) => Invalidate(); Box.LostFocus += (o, e) => Invalidate();
            if (placeholder != null) SetCue(Box, placeholder);
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        static void SetCue(TextBox b, string cue) { b.HandleCreated += (o, e) => SendMessage(b.Handle, 0x1501, (IntPtr)1, cue); }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var pen = new Pen(Box.Focused ? Theme.Holo : Theme.CardBorder)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    class DarkCombo : ComboBox
    {
        public DarkCombo(int width)
        {
            DrawMode = DrawMode.OwnerDrawFixed; DropDownStyle = ComboBoxStyle.DropDownList; FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Field; ForeColor = Theme.Text; Width = width; Font = Theme.F(10f); ItemHeight = Theme.S(22);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Color.FromArgb(30, 80, 140) : Theme.Field)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    /* ---------------- main window ---------------- */

    class MainForm : Form
    {
        Settings s;
        Image logo, bgArt;
        Segmented segMode;
        DarkCombo cbRes, cbMon;
        FieldBox fbW, fbH, fbMW, fbMH;
        Toggle tAllRes, tMovies, tSubs, tHud, tIntro, tClip, tDpi, tGamma, tClose;
        FancyButton bPlay;
        Label lblStatus, lblModeHint;
        TextBox tbLog;
        Panel logPanel;
        int baseHeight;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int sz);

        public MainForm()
        {
            using (var g = CreateGraphics()) Theme.Scale = g.DpiX / 96f;
            s = Settings.Load();
            s.GamePath = Core.FindGameDir(s.GamePath);
            ReadGameRes();
            LoadArt();

            Text = "KotOR Launcher";
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(9.5f);
            AutoScaleMode = AutoScaleMode.None; DoubleBuffered = true;
            ClientSize = new Size(Theme.S(900), Theme.S(640));
            baseHeight = ClientSize.Height;
            try { Icon = Icon.ExtractAssociatedIcon(Path.Combine(s.GamePath, "swkotor.exe")); } catch { }
            BuildUi();
            KeyPreview = true;
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter && !(ActiveControl is TextBox)) DoLaunch(); if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ cp.Style |= 0x20000; /* WS_MINIMIZEBOX */ return cp; }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }

        void LoadArt()
        {
            // The KotOR logo is taken at runtime from the game's own launcher art (launcher\swlauncher.bmp).
            try
            {
                string bmp = Path.Combine(s.GamePath ?? "", @"launcher\swlauncher.bmp");
                if (File.Exists(bmp))
                    using (var src = new Bitmap(bmp))
                    {
                        var crop = new Rectangle(165, 22, 270, 118);
                        var lg = new Bitmap(crop.Width, crop.Height);
                        using (var g = Graphics.FromImage(lg)) g.DrawImage(src, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
                        // knock out the dark navy backdrop so the logo floats on our header
                        for (int y = 0; y < lg.Height; y++)
                            for (int x = 0; x < lg.Width; x++)
                            {
                                var c = lg.GetPixel(x, y); int m = Math.Max(c.R, Math.Max(c.G, c.B));
                                int a = Math.Max(0, Math.Min(255, (m - 45) * 4));
                                if (c.B > c.R + 10 && m < 90) a = 0;
                                lg.SetPixel(x, y, Color.FromArgb(a, c));
                            }
                        logo = lg;
                    }
                string art = Path.Combine(Program.BaseDir, @"resources\template_load.jpg");
                if (File.Exists(art)) bgArt = Image.FromFile(art);
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int hh = Theme.S(150);
            var hdr = new Rectangle(0, 0, ClientSize.Width, hh);
            if (bgArt != null)
            {
                // crop the holo strip from the art as a backdrop
                var src = new Rectangle(380, 470, 840, 220);
                g.DrawImage(bgArt, hdr, src, GraphicsUnit.Pixel);
            }
            using (var b = new LinearGradientBrush(hdr, Color.FromArgb(bgArt != null ? 150 : 255, 7, 11, 20), Theme.Bg, 90f)) g.FillRectangle(b, hdr);
            // holo scan lines
            using (var pen = new Pen(Color.FromArgb(14, Theme.Holo))) for (int y = 0; y < hh; y += Theme.S(4)) g.DrawLine(pen, 0, y, ClientSize.Width, y);
            using (var pen = new Pen(Color.FromArgb(90, Theme.Holo))) g.DrawLine(pen, Theme.S(24), hh - 1, ClientSize.Width - Theme.S(24), hh - 1);
            if (logo != null)
            {
                float lh = Theme.S(104), lw = logo.Width * lh / logo.Height;
                g.DrawImage(logo, Theme.S(28), Theme.S(26), lw, lh);
            }
            else
                using (var f = new Font("Segoe UI Light", 24f)) using (var b = new SolidBrush(Theme.Gold))
                    g.DrawString("KNIGHTS OF THE OLD REPUBLIC", f, b, Theme.S(28), Theme.S(50));
            using (var f = Theme.FSemi(15f)) using (var b = new SolidBrush(Theme.Text))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Far };
                g.DrawString("Borderless + Widescreen", f, b, ClientSize.Width - Theme.S(28), Theme.S(62), sf);
                using (var f2 = Theme.F(9f)) using (var b2 = new SolidBrush(Theme.Muted))
                    g.DrawString(L.T("Widescreen düzeltmeleri dahil  •  portable launcher", "Widescreen fixes built in  •  portable launcher"), f2, b2, ClientSize.Width - Theme.S(28), Theme.S(92), sf);
            }
            using (var pen = new Pen(Theme.CardBorder)) g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < Theme.S(150)) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); }
        }

        Label TitleBtn(string glyph, int right, Action act)
        {
            var l = new Label { Text = glyph, Font = new Font("Segoe MDL2 Assets", 9f), ForeColor = Theme.Muted, BackColor = Color.Transparent,
                Size = new Size(Theme.S(44), Theme.S(32)), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            l.Location = new Point(ClientSize.Width - right, 1);
            l.MouseEnter += (o, e) => { l.ForeColor = Color.White; l.BackColor = glyph == "" ? Color.FromArgb(200, 50, 50) : Color.FromArgb(40, 60, 90); };
            l.MouseLeave += (o, e) => { l.ForeColor = Theme.Muted; l.BackColor = Color.Transparent; };
            l.Click += (o, e) => act();
            Controls.Add(l); return l;
        }

        static Label Lbl(string t, int x, int y, Color? c = null, float pt = 9f)
        {
            return new Label { Text = t, AutoSize = true, Location = new Point(x, y), ForeColor = c ?? Theme.Muted, BackColor = Color.Transparent, Font = Theme.F(pt) };
        }

        void BuildUi()
        {
            Func<float, int> S = Theme.S;
            TitleBtn("", S(46), Close);
            TitleBtn("", S(90), () => WindowState = FormWindowState.Minimized);
            var segLang = new Segmented("TR", "EN") { Location = new Point(ClientSize.Width - S(190), S(6)), Size = new Size(S(84), S(24)) };
            segLang.Selected = L.Tr ? 0 : 1;
            segLang.Changed += (o, e) => SwitchLanguage(segLang.Selected == 0 ? "tr" : "en");
            Controls.Add(segLang);

            int top = S(166), gap = S(16), left = S(24), colW = S(418);

            /* ---- DISPLAY card ---- */
            var cDisp = new Card(L.T("Görüntü", "Display")) { Location = new Point(left, top), Size = new Size(colW, S(262)) };
            Controls.Add(cDisp);
            segMode = new Segmented(L.T("Tam Ekran", "Fullscreen"), L.T("Pencereli", "Windowed"), "Borderless") { Location = new Point(S(16), S(40)), Width = colW - S(32) };
            segMode.Selected = Math.Max(0, Math.Min(2, s.Mode));
            cDisp.Controls.Add(segMode);
            lblModeHint = Lbl("", S(16), S(80), Theme.Muted, 8.5f); cDisp.Controls.Add(lblModeHint);
            segMode.Changed += (o, e) => UpdateModeHint();
            UpdateModeHint();

            cDisp.Controls.Add(Lbl(L.T("Çözünürlük", "Resolution"), S(16), S(110)));
            fbW = new FieldBox(s.Width.ToString(), S(70)) { Location = new Point(S(16), S(132)) };
            fbH = new FieldBox(s.Height.ToString(), S(70)) { Location = new Point(S(110), S(132)) };
            cDisp.Controls.Add(fbW); cDisp.Controls.Add(Lbl("×", S(91), S(136), Theme.Muted, 10f)); cDisp.Controls.Add(fbH);
            cbRes = new DarkCombo(colW - S(212)) { Location = new Point(S(196), S(133)) };
            cbRes.Items.Add(L.T("Listeden seç…", "Pick from list…")); FillResolutions(); cbRes.SelectedIndex = 0;
            cbRes.SelectedIndexChanged += (o, e) => { if (cbRes.SelectedIndex <= 0) return; var p = ((string)cbRes.SelectedItem).Split('x'); fbW.Box.Text = p[0]; fbH.Box.Text = p[1]; };
            cDisp.Controls.Add(cbRes);

            cDisp.Controls.Add(Lbl(L.T("Monitör", "Monitor"), S(16), S(176)));
            cbMon = new DarkCombo(S(220)) { Location = new Point(S(16), S(198)) };
            cbMon.Items.Add(L.T("Birincil monitör", "Primary monitor"));
            var scr = Screen.AllScreens;
            for (int i = 0; i < scr.Length; i++)
                cbMon.Items.Add(string.Format(L.T("Monitör {0}  ({1}×{2}){3}", "Monitor {0}  ({1}×{2}){3}"), i + 1, scr[i].Bounds.Width, scr[i].Bounds.Height, scr[i].Primary ? "  ★" : ""));
            cbMon.SelectedIndex = s.Monitor >= 0 && s.Monitor < cbMon.Items.Count ? s.Monitor : 0;
            cDisp.Controls.Add(cbMon);
            var bDesk = new FancyButton(L.T("Monitöre eşitle", "Match monitor"), false) { Location = new Point(S(246), S(197)), Size = new Size(colW - S(262), S(32)) };
            bDesk.Click += (o, e) => { var b = MonitorBounds(); fbW.Box.Text = b.Width.ToString(); fbH.Box.Text = b.Height.ToString(); };
            cDisp.Controls.Add(bDesk);

            /* ---- FIXES card ---- */
            int rx = left + colW + gap;
            var cFix = new Card(L.T("Widescreen düzeltmeleri", "Widescreen fixes")) { Location = new Point(rx, top), Size = new Size(colW, S(262)) };
            Controls.Add(cFix);
            int ty = S(40), tw = colW - S(32);
            tAllRes = new Toggle(L.T("Tüm çözünürlükleri etkinleştir", "Enable all resolutions"), L.T("1920×1080 ve üstü için gerekli", "Required for 1920×1080 and above"), s.EnableAllRes) { Location = new Point(S(16), ty), Width = tw }; cFix.Controls.Add(tAllRes); ty += S(46);
            tMovies = new Toggle(L.T("Film çözünürlüğü düzeltmesi", "Movie resolution fix"), null, s.MoviesFix) { Location = new Point(S(16), ty), Width = S(230) }; cFix.Controls.Add(tMovies);
            fbMW = new FieldBox(s.MoviesWidth > 0 ? s.MoviesWidth.ToString() : "", S(58), L.T("oto", "auto")) { Location = new Point(colW - S(150), ty - S(2)), Size = new Size(S(58), S(28)) };
            fbMH = new FieldBox(s.MoviesHeight > 0 ? s.MoviesHeight.ToString() : "", S(58), L.T("oto", "auto")) { Location = new Point(colW - S(74), ty - S(2)), Size = new Size(S(58), S(28)) };
            cFix.Controls.Add(fbMW); cFix.Controls.Add(Lbl("×", colW - S(89), ty + S(2), Theme.Muted, 10f)); cFix.Controls.Add(fbMH); ty += S(40);
            tSubs = new Toggle(L.T("Altyazı düzeltmesi", "Subtitle fix"), L.T("Ara sahnelerde altyazı konumu", "Subtitle position in cutscenes"), s.SubtitleFix) { Location = new Point(S(16), ty), Width = tw }; cFix.Controls.Add(tSubs); ty += S(46);
            tHud = new Toggle(L.T("HUD + arka planları üret", "Generate HUD + backgrounds"), L.T("Override klasörüne, çözünürlük değişince", "Into Override, when resolution changes"), s.GenerateHud) { Location = new Point(S(16), ty), Width = S(290) }; cFix.Controls.Add(tHud);
            var lnkDel = new LinkLabel { Text = L.T("Sil", "Delete"), AutoSize = true, Location = new Point(colW - S(44), ty + S(4)), LinkColor = Theme.Holo, ActiveLinkColor = Color.White, BackColor = Color.Transparent, Font = Theme.F(9f), LinkBehavior = LinkBehavior.HoverUnderline };
            lnkDel.Click += (o, e) => RemoveHud(); cFix.Controls.Add(lnkDel);

            /* ---- EXTRAS card ---- */
            int y2 = top + S(262) + gap;
            var cExt = new Card(L.T("Ekstra", "Extras")) { Location = new Point(left, y2), Size = new Size(colW * 2 + gap, S(118)) };
            Controls.Add(cExt);
            int third = (colW * 2 + gap - S(32)) / 3;
            tIntro = new Toggle(L.T("İntroları atla", "Skip intro videos"), L.T("LucasArts / BioWare / yasal uyarı videoları", "LucasArts / BioWare / legal logo videos"), s.SkipIntro) { Location = new Point(S(16), S(36)), Width = third - S(8) }; cExt.Controls.Add(tIntro);
            tClip = new Toggle(L.T("Fareyi pencereye kilitle", "Lock mouse to window"), L.T("Çoklu monitörde fare kaçmasın", "Keeps the cursor in on multi-monitor setups"), s.ClipCursor) { Location = new Point(S(16) + third, S(36)), Width = third * 2 - S(8) }; cExt.Controls.Add(tClip);
            tDpi = new Toggle(L.T("DPI ölçeklemesini kapat", "Disable DPI scaling"), null, s.DpiAware) { Location = new Point(S(16), S(82)), Width = third - S(8) }; cExt.Controls.Add(tDpi);
            tGamma = new Toggle(L.T("Gamma'yı engelle", "Block gamma changes"), null, s.BlockGamma) { Location = new Point(S(16) + third, S(82)), Width = third - S(8) }; cExt.Controls.Add(tGamma);
            tClose = new Toggle(L.T("Başlayınca launcher'ı kapat", "Close launcher on start"), null, s.CloseAfterLaunch) { Location = new Point(S(16) + third * 2, S(82)), Width = third - S(8) }; cExt.Controls.Add(tClose);

            /* ---- bottom bar ---- */
            int by = ClientSize.Height - S(70);
            bPlay = new FancyButton(L.T("OYNA", "PLAY"), true) { Location = new Point(ClientSize.Width - S(24) - S(200), by), Size = new Size(S(200), S(50)) };
            bPlay.Click += (o, e) => DoLaunch(); Controls.Add(bPlay);
            var bShort = new FancyButton(L.T("Masaüstü kısayolu", "Desktop shortcut"), false) { Location = new Point(bPlay.Left - S(152), by + S(9)), Size = new Size(S(140), S(32)) };
            bShort.Click += (o, e) => MakeShortcut(); Controls.Add(bShort);
            var bFolder = new FancyButton(L.T("Oyun klasörü…", "Game folder…"), false) { Location = new Point(bShort.Left - S(132), by + S(9)), Size = new Size(S(120), S(32)) };
            bFolder.Click += (o, e) => Browse(); Controls.Add(bFolder);

            lblStatus = new Label { AutoSize = false, Location = new Point(left, by + S(4)), Size = new Size(bFolder.Left - left - S(12), S(22)), ForeColor = Theme.Muted, BackColor = Color.Transparent, Font = Theme.F(9f), AutoEllipsis = true };
            Controls.Add(lblStatus);
            var lnkLog = new LinkLabel { Text = L.T("Ayrıntılar ▾", "Details ▾"), AutoSize = true, Location = new Point(left, by + S(26)), LinkColor = Theme.HoloDim, ActiveLinkColor = Theme.Holo, BackColor = Color.Transparent, Font = Theme.F(8.5f), LinkBehavior = LinkBehavior.HoverUnderline };
            lnkLog.Click += (o, e) => ToggleLog(lnkLog); Controls.Add(lnkLog);

            logPanel = new Panel { Location = new Point(left, ClientSize.Height), Size = new Size(ClientSize.Width - left * 2, S(120)), BackColor = Theme.Field, Visible = false };
            tbLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Theme.Field, ForeColor = Theme.Muted, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 8.5f) };
            logPanel.Padding = new Padding(S(8)); logPanel.Controls.Add(tbLog); Controls.Add(logPanel);

            if (!Core.IsGameDir(s.GamePath)) Log(L.T("Oyun klasörünü seçin (swkotor.exe içeren klasör) → 'Oyun klasörü…'", "Select the game folder (containing swkotor.exe) → 'Game folder…'"));
            else Log(L.T("Hazır  •  ", "Ready  •  ") + s.GamePath);
            if (Core.FindResources() == null) Log(L.T("Not: HUD kaynakları bulunamadı ('resources' klasörü launcher'ın yanında olmalı).", "Note: HUD resources not found (the 'resources' folder must sit next to the launcher)."));
        }

        void SwitchLanguage(string lang)
        {
            if ((lang == "tr") == L.Tr) return;
            Collect();                       // keep what the user already typed
            L.Init(lang); s.Language = lang;
            try { Ini.Set(Settings.IniPath, "Launcher", "Language", lang); } catch { }
            SuspendLayout();
            Controls.Clear();
            ClientSize = new Size(ClientSize.Width, baseHeight);
            BuildUi();
            ResumeLayout();
            Invalidate(true);
        }

        void ToggleLog(LinkLabel l)
        {
            bool show = !logPanel.Visible;
            logPanel.Top = baseHeight - Theme.S(6);
            logPanel.Visible = show;
            ClientSize = new Size(ClientSize.Width, baseHeight + (show ? logPanel.Height + Theme.S(12) : 0));
            l.Text = show ? L.T("Ayrıntılar ▴", "Details ▴") : L.T("Ayrıntılar ▾", "Details ▾");
            Invalidate();
        }

        void UpdateModeHint()
        {
            string[] h = { L.T("Orijinal tam ekran: ekran modu değişir, alt-tab yavaş.", "Original fullscreen: changes display mode, slow alt-tab."),
                           L.T("Çerçeveli pencere.", "Framed window."),
                           L.T("Önerilen: çerçevesiz tam ekran pencere, anında alt-tab.", "Recommended: frameless fullscreen window, instant alt-tab.") };
            lblModeHint.Text = h[segMode.Selected];
            lblModeHint.ForeColor = segMode.Selected == 2 ? Theme.Holo : Theme.Muted;
        }

        void ReadGameRes()
        {
            if (s.Width > 0 && s.Height > 0) return;
            string f = Path.Combine(s.GamePath ?? "", "swkotor.ini");
            if (File.Exists(f)) { s.Width = Ini.GetInt(f, "Graphics Options", "Width", 0); s.Height = Ini.GetInt(f, "Graphics Options", "Height", 0); }
            if (s.Width <= 0 || s.Height <= 0) { s.Width = Screen.PrimaryScreen.Bounds.Width; s.Height = Screen.PrimaryScreen.Bounds.Height; }
        }

        Rectangle MonitorBounds()
        {
            int i = cbMon.SelectedIndex;
            return i > 0 && i <= Screen.AllScreens.Length ? Screen.AllScreens[i - 1].Bounds : Screen.PrimaryScreen.Bounds;
        }

        void FillResolutions()
        {
            var seen = new HashSet<string>(); var list = new List<Size>();
            var dm = new Native.DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE));
            for (int i = 0; Native.EnumDisplaySettingsA(null, i, ref dm); i++)
                if (dm.dmPelsWidth >= 800 && dm.dmPelsHeight >= 600 && seen.Add(dm.dmPelsWidth + "x" + dm.dmPelsHeight))
                    list.Add(new Size(dm.dmPelsWidth, dm.dmPelsHeight));
            list.Sort((a, b) => a.Width != b.Width ? b.Width - a.Width : b.Height - a.Height);
            foreach (var z in list) cbRes.Items.Add(z.Width + "x" + z.Height);
        }

        bool Collect()
        {
            int w, h, mw = 0, mh = 0;
            if (!int.TryParse(fbW.Box.Text.Trim(), out w) || !int.TryParse(fbH.Box.Text.Trim(), out h) || w < 640 || h < 480)
            { Warn(L.T("Geçerli bir çözünürlük girin.", "Enter a valid resolution.")); return false; }
            string a = fbMW.Box.Text.Trim(), b = fbMH.Box.Text.Trim();
            if (a != "" || b != "")
                if (!int.TryParse(a, out mw) || !int.TryParse(b, out mh) || mw < 320 || mh < 200)
                { Warn(L.T("Film çözünürlüğü geçersiz (boş = oyun çözünürlüğü).", "Invalid movie resolution (empty = game resolution).")); return false; }
            s.Mode = segMode.Selected; s.Monitor = cbMon.SelectedIndex;
            s.Width = w; s.Height = h; s.MoviesWidth = mw; s.MoviesHeight = mh;
            s.EnableAllRes = tAllRes.Checked; s.MoviesFix = tMovies.Checked; s.SubtitleFix = tSubs.Checked; s.GenerateHud = tHud.Checked;
            s.SkipIntro = tIntro.Checked; s.ClipCursor = tClip.Checked; s.DpiAware = tDpi.Checked; s.BlockGamma = tGamma.Checked; s.CloseAfterLaunch = tClose.Checked;
            return true;
        }

        void Warn(string m) { Log(m); MessageBox.Show(this, m, "KotOR Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (Collect()) try { s.Save(); } catch { }
            base.OnFormClosing(e);
        }

        void DoLaunch()
        {
            if (!bPlay.Enabled || !Collect()) return;
            if (s.Mode == 2)
            {
                var b = MonitorBounds();
                if (s.Width != b.Width || s.Height != b.Height)
                    Log(string.Format(L.T("Not: {0}×{1} monitörden ({2}×{3}) farklı; pencere ortalanacak.", "Note: {0}×{1} differs from the monitor ({2}×{3}); the window will be centered."), s.Width, s.Height, b.Width, b.Height));
            }
            if (Core.IsRunning("FlawlessWidescreen"))
            {
                if (MessageBox.Show(this, L.T("Flawless Widescreen açık. Oyuna bağlanıp aynı yamaları tekrar uygulamaya çalışır ve oyunu kilitler.\n", "Flawless Widescreen is running. It attaches to the game, re-applies the same patches and freezes it.\n") +
                        L.T("Bu launcher FWS'in düzeltmelerini zaten içeriyor.\n\nFWS kapatılsın mı?", "This launcher already includes the FWS fixes.\n\nClose FWS now?"), "KotOR Launcher",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                foreach (var p in Process.GetProcessesByName("FlawlessWidescreen"))
                    try { p.CloseMainWindow(); if (!p.WaitForExit(3000)) p.Kill(); p.WaitForExit(3000); } catch { }
                Log(L.T("Flawless Widescreen kapatıldı.", "Flawless Widescreen closed."));
            }
            try
            {
                bPlay.Enabled = false; Cursor = Cursors.WaitCursor;
                Core.Launch(s, Log);
                if (s.CloseAfterLaunch) { var t = new Timer { Interval = 1200 }; t.Tick += (o, e) => Close(); t.Start(); }
                else { var t = new Timer { Interval = 4000 }; t.Tick += (o, e) => { bPlay.Enabled = true; t.Stop(); }; t.Start(); }
            }
            catch (Exception ex)
            {
                bPlay.Enabled = true;
                Log(L.T("HATA: ", "ERROR: ") + ex.Message); MessageBox.Show(this, ex.Message, "KotOR Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        void RemoveHud()
        {
            if (!Core.IsGameDir(s.GamePath)) return;
            if (MessageBox.Show(this, L.T("Override klasöründeki üretilmiş HUD/arka plan dosyaları (mipc210x7.gui, 800x600*.tga) silinsin mi?", "Delete the generated HUD/background files (mipc210x7.gui, 800x600*.tga) from the Override folder?"),
                    "KotOR Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int n = Core.RemoveHud(Path.Combine(s.GamePath, "Override"));
            s.HudGeneratedFor = ""; Ini.Set(Settings.IniPath, "Launcher", "HudGeneratedFor", "");
            Log(n + L.T(" HUD dosyası silindi.", " HUD file(s) deleted."));
        }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = L.T("swkotor.exe içeren klasörü seçin", "Select the folder containing swkotor.exe"), SelectedPath = s.GamePath })
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    if (!Core.IsGameDir(d.SelectedPath)) { Warn(L.T("Bu klasörde swkotor.exe yok.", "There is no swkotor.exe in this folder.")); return; }
                    s.GamePath = d.SelectedPath; Log(L.T("Oyun klasörü: ", "Game folder: ") + s.GamePath);
                    if (logo == null) { LoadArt(); Invalidate(); }
                }
        }

        void MakeShortcut()
        {
            try
            {
                if (Collect()) s.Save();
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "KotOR (Borderless).lnk");
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic sc = sh.CreateShortcut(lnk);
                sc.TargetPath = Application.ExecutablePath;
                sc.Arguments = "-launch";
                sc.WorkingDirectory = Program.BaseDir;
                string ico = Path.Combine(s.GamePath ?? "", "swkotor.exe");
                if (File.Exists(ico)) sc.IconLocation = ico + ",0";
                sc.Save();
                Log(L.T("Masaüstü kısayolu oluşturuldu — ayarları sormadan direkt başlatır.", "Desktop shortcut created — launches directly with saved settings."));
            }
            catch (Exception ex) { Log(L.T("Kısayol oluşturulamadı: ", "Could not create shortcut: ") + ex.Message); }
        }

        void Log(string m)
        {
            if (lblStatus != null) { lblStatus.Text = m; lblStatus.ForeColor = (m.StartsWith("HATA") || m.StartsWith("ERROR")) ? Color.FromArgb(240, 110, 100) : Theme.Muted; }
            if (tbLog != null) tbLog.AppendText(DateTime.Now.ToString("HH:mm:ss  ") + m + Environment.NewLine);
        }
    }

    static class Program
    {
        public static string BaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

        [STAThread]
        static int Main(string[] args)
        {
            Native.MakeDpiAware();
            L.Init(Settings.Load().Language);
            if (args.Length >= 4 && args[0] == "-genhud")
            {
                Console.WriteLine(Core.GenerateHud(Core.FindResources(), args[3], int.Parse(args[1]), int.Parse(args[2])));
                return 0;
            }
            if (args.Length >= 1 && args[0] == "-launch")
            {
                var s = Settings.Load(); s.GamePath = Core.FindGameDir(s.GamePath);
                var sb = new StringBuilder();
                try
                {
                    if (s.Width <= 0 || s.Height <= 0) { s.Width = Screen.PrimaryScreen.Bounds.Width; s.Height = Screen.PrimaryScreen.Bounds.Height; }
                    Core.Launch(s, m => sb.AppendLine(m)); return 0;
                }
                catch (Exception ex) { MessageBox.Show(sb + "\n" + ex.Message, "KotOR Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
