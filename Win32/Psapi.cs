using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Bemo
{
    public class Psapi
    {
        [DllImport("psapi.dll", CharSet = CharSet.Auto)]
        public static extern int GetProcessImageFileName(IntPtr hProcess, StringBuilder lpImageFileName, int nSize);
    }
}
