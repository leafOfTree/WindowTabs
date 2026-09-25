using System;
using System.Collections.Generic;
using System.Text;
using Bemo.Win32;

namespace Bemo.Win32
{
    public class HotKeyShortcut
    {
        public bool IsCtrlModifier
        {
            get { return isCtrlModifier; }
            set { isCtrlModifier = value; }
        }
        public bool IsAltModifier
        {
            get { return isAltModifier; }
            set { isAltModifier = value; }
        }
        public bool IsShiftModifier
        {
            get { return isShiftModifier; }
            set { isShiftModifier = value; }
        }
        public char VirtualKey
        {
            get { return virtualKey; }
            set { virtualKey = value; }
        }
        public short HotKeyControlCode
        {
            get
            {
                int mod = 0;
                mod |= IsCtrlModifier ? HotKeyConstants.HOTKEYF_CONTROL : 0;
                mod |= IsAltModifier ? HotKeyConstants.HOTKEYF_ALT : 0;
                mod |= IsShiftModifier ? HotKeyConstants.HOTKEYF_SHIFT : 0;
                mod |= isExtendedVk ? HotKeyConstants.HOTKEYF_EXT : 0;
                return WinDefApi.MAKEWORD((int)VirtualKey, mod);
            }
            set
            {
                int mod = WinDefApi.HIBYTE(value);
                IsCtrlModifier = (mod & HotKeyConstants.HOTKEYF_CONTROL) != 0;
                IsAltModifier = (mod & HotKeyConstants.HOTKEYF_ALT) != 0;
                IsShiftModifier = (mod & HotKeyConstants.HOTKEYF_SHIFT) != 0;
                isExtendedVk = (mod & HotKeyConstants.HOTKEYF_EXT) != 0;
                VirtualKey = (char)WinDefApi.LOBYTE(value);
            }
        }
        public int RegisterHotKeyModifierFlags
        {
            get
            {
                int mod = 0;
                mod |= IsCtrlModifier ? HotKeyConstants.MOD_CONTROL : 0;
                mod |= IsAltModifier ? HotKeyConstants.MOD_ALT : 0;
                mod |= IsShiftModifier ? HotKeyConstants.MOD_SHIFT : 0;
                return mod;
            }
            set
            {
                IsCtrlModifier = (value & HotKeyConstants.MOD_CONTROL) != 0;
                IsAltModifier = (value & HotKeyConstants.MOD_ALT) != 0;
                IsShiftModifier = (value & HotKeyConstants.MOD_SHIFT) != 0;
            }
        }
        public int RegisterHotKeyVirtualKeyCode
        {
            get
            {
                return (int)VirtualKey;
            }
            set
            {
                VirtualKey = (char)value;
            }
        }
        bool isCtrlModifier;
        bool isAltModifier;
        bool isShiftModifier;
        bool isExtendedVk;
        char virtualKey;
    }
}
