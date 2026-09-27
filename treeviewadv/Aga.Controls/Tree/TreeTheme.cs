using System.Drawing;

namespace Aga.Controls.Tree
{
	/// <summary>
	/// Optional colours for hosts that draw their own light/dark theme (WindowTabs addition).
	/// When <see cref="TreeViewAdv.Theme"/> is null the tree draws with system colours as upstream does.
	/// </summary>
	public class TreeTheme
	{
		public Color Back { get; set; }
		public Color Text { get; set; }
		public Color MutedText { get; set; }
		public Color Selection { get; set; }
		public Color SelectionText { get; set; }
		public Color HeaderBack { get; set; }
		public Color HeaderText { get; set; }
		public Color Line { get; set; }
		public Color Accent { get; set; }
		/// <summary>Use the dark native scrollbars (Windows 10 1809 and later).</summary>
		public bool Dark { get; set; }
	}
}
