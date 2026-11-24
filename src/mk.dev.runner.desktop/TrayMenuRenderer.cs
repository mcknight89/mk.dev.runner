using System.Drawing;
using System.Windows.Forms;

namespace mk.dev.runner.desktop
{
    internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Text = Color.FromArgb(0xe4, 0xe4, 0xe7);
        private static readonly Color Disabled = Color.FromArgb(0x71, 0x71, 0x7a);

        public TrayMenuRenderer() : base(new DarkColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Text;
            base.OnRenderArrow(e);
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            private static readonly Color Back = Color.FromArgb(0x27, 0x27, 0x2a);
            private static readonly Color Hover = Color.FromArgb(0x2a, 0x3a, 0x55);
            private static readonly Color Edge = Color.FromArgb(0x3f, 0x3f, 0x46);

            public override Color ToolStripDropDownBackground => Back;
            public override Color ImageMarginGradientBegin => Back;
            public override Color ImageMarginGradientMiddle => Back;
            public override Color ImageMarginGradientEnd => Back;
            public override Color MenuBorder => Edge;
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color MenuItemPressedGradientBegin => Hover;
            public override Color MenuItemPressedGradientEnd => Hover;
            public override Color SeparatorDark => Edge;
            public override Color SeparatorLight => Back;
        }
    }
}
