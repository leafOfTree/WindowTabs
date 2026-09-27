using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Aga.Controls.Tree
{
	// WindowTabs addition: host-supplied colours, see TreeTheme.
	public partial class TreeViewAdv
	{
		[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
		private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

		private TreeTheme _theme;
		[Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public TreeTheme Theme
		{
			get { return _theme; }
			set
			{
				_theme = value;
				if (value != null)
				{
					BackColor = value.Back;
					LineColor = value.Line;
				}
				ApplyScrollBarTheme();
				Invalidate();
			}
		}

		private void ApplyScrollBarTheme()
		{
			string theme = _theme != null && _theme.Dark ? "DarkMode_Explorer" : "Explorer";
			foreach (var bar in new System.Windows.Forms.Control[] { _vScrollBar, _hScrollBar })
				if (bar != null && bar.IsHandleCreated)
					try { SetWindowTheme(bar.Handle, theme, null); bar.Invalidate(); } catch (EntryPointNotFoundException) { }
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			foreach (var bar in new System.Windows.Forms.Control[] { _vScrollBar, _hScrollBar })
				if (bar != null) bar.HandleCreated += (s, a) => ApplyScrollBarTheme();
			ApplyScrollBarTheme();
		}
	}
}
