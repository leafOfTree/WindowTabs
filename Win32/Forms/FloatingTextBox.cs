using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using System.Runtime.InteropServices;


namespace Bemo.Win32.Forms
{
    public partial class FloatingTextBox : Form
    {
        private Size size;
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const int WM_GETMINMAXINFO = 0x0024;
        public FloatingTextBox()
        {
            InitializeComponent();
            this.textBox.AutoSize = false;
        }
        public void SetSize(Size newSize)
        {
            this.size = newSize;
            this.Size = newSize;
            this.textBox.Size = newSize;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_WINDOWPOSCHANGING)
            {
                WindowPos windowPos = (WindowPos)m.GetLParam(typeof(WindowPos));

                // Make changes to windowPos

                // Then marshal the changes back to the message
                Marshal.StructureToPtr(windowPos, m.LParam, true);
            }

            base.WndProc(ref m);

            // Make changes to WM_GETMINMAXINFO after it has been handled by the underlying
            // WndProc, so we only need to repopulate the minimum size constraints
            if (m.Msg == WM_GETMINMAXINFO)
            {
                MinMaxInfo minMaxInfo = (MinMaxInfo)m.GetLParam(typeof(MinMaxInfo));
                minMaxInfo.ptMinTrackSize.x = this.size.Width;
                minMaxInfo.ptMinTrackSize.y = this.size.Height;
                Marshal.StructureToPtr(minMaxInfo, m.LParam, true);
            }
        }
    }

    
    struct WindowPos
    {
         public IntPtr hwnd;
         public IntPtr hwndInsertAfter;
         public int x;
         public int y;
         public int width;
         public int height;
         public uint flags;
    }

    struct POINT
    {
        public int x;
        public int y;
    }

    struct MinMaxInfo
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }
}
