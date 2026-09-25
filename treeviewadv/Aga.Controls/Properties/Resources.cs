using System.Drawing;
using System.IO;

namespace Aga.Controls.Properties
{
    /// <summary>
    /// Replaces the generated Resources.Designer.cs: the files under Resources\ are
    /// embedded directly (see Aga.Controls.csproj) and read here by name.
    /// </summary>
    internal static class Resources
    {
        private static byte[] Read(string file)
        {
            using (Stream stream = typeof(Resources).Assembly.GetManifestResourceStream("Aga.Controls.Resources." + file))
            using (MemoryStream copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }

        // GDI+ needs the stream for the bitmap's lifetime; a MemoryStream holds no native resources.
        private static Bitmap Image(string file)
        {
            return new Bitmap(new MemoryStream(Read(file)));
        }

        internal static Bitmap check { get { return Image("check.bmp"); } }
        internal static byte[] DVSplit { get { return Read("DVSplit.cur"); } }
        internal static Bitmap Folder { get { return Image("Folder.bmp"); } }
        internal static Bitmap FolderClosed { get { return Image("FolderClosed.bmp"); } }
        internal static Bitmap Leaf { get { return Image("Leaf.bmp"); } }
        internal static byte[] loading_icon { get { return Read("loading_icon"); } }
        internal static Bitmap minus { get { return Image("minus.bmp"); } }
        internal static Bitmap plus { get { return Image("plus.bmp"); } }
        internal static Bitmap uncheck { get { return Image("uncheck.bmp"); } }
        internal static Bitmap unknown { get { return Image("unknown.bmp"); } }
    }
}
