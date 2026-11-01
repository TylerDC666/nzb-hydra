using System;
using System.Drawing;
using System.Windows.Forms;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: System.Reflection.AssemblyTitle("NZB Hydra")]
[assembly: System.Reflection.AssemblyProduct("NZB Hydra")]
[assembly: System.Reflection.AssemblyDescription("NZB Hydra – NZBLNK und NZB-Dateien")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.0.0.0")]
namespace NZBHydra {
    public class HydraButton : Button {
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            if (!Enabled) {
                e.Graphics.Clear(Color.FromArgb(20,17,19));
                using (var border = new Pen(Color.FromArgb(54,37,42))) e.Graphics.DrawRectangle(border, 0,0,Width-1,Height-1);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.FromArgb(113,96,101), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
    public class HydraProgress : Control {
        readonly Timer animation = new Timer { Interval = 35 };
        int position, value; ProgressBarStyle style;
        public int Value { get { return value; } set { this.value = Math.Max(0, Math.Min(100,value)); Invalidate(); } }
        public int MarqueeAnimationSpeed { get; set; }
        public ProgressBarStyle Style { get { return style; } set { style=value; animation.Enabled = value == ProgressBarStyle.Marquee; Invalidate(); } }
        public HydraProgress() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); animation.Tick += (s,e) => { position = (position + 9) % Math.Max(1, Width + Width/4); Invalidate(); }; }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Color.FromArgb(35,21,25));
            using (var brush = new SolidBrush(Color.FromArgb(214,42,62))) {
                if (Style == ProgressBarStyle.Marquee) e.Graphics.FillRectangle(brush, position - Width/4, 0, Width/4, Height);
                else e.Graphics.FillRectangle(brush, 0, 0, Width * Value / 100, Height);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }
}
