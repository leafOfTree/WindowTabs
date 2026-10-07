// Copyright 2009 Bemo Software, Inc. All Rights Reserved.

using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace Bemo
{
	#region Structures
    /// <summary>
    /// A win32 rectangle.
    /// </summary>
    [Serializable]
	[StructLayout(LayoutKind.Sequential)]
    public struct RECT
	{
        public RECT(POINT location, SIZE size)
        {
            Left = location.X;
            Top = location.Y;
            Right = Left + size.cx;
            Bottom = Top + size.cy;
        }
        /// <summary>
        /// The left coordinate.
        /// </summary>
		public int Left;
        /// <summary>
        /// The top coordinate.
        /// </summary>
		public int Top;
        /// <summary>
        /// The right coordinate.
        /// </summary>
		public int Right;
        /// <summary>
        /// The bottom coordiante.
        /// </summary>
		public int Bottom;
		/// <summary>
		/// Gets a <see cref="RECT"/> from a <see cref="Rectangle"/>.
		/// </summary>
		public static RECT FromRectangle(Rectangle rectangle)
		{
			RECT rect = new RECT();
			rect.Left = rectangle.Left;
			rect.Top = rectangle.Top;
			rect.Right = rectangle.Right;
			rect.Bottom = rectangle.Bottom;
			return rect;
		}
        /// Gets a <see cref="Rectangle"/> from a <see cref="RECT"/>.
        /// </summary>
        /// <returns></returns>
		public Rectangle ToRectangle()
		{
			return new Rectangle(Left, Top, Right - Left, Bottom - Top);
		}
        public RectangleF ToRectangleF()
        {
            return new RectangleF((float)Left, (float)Top, (float)Right - Left, (float)Bottom - Top);
        }
		public override string ToString()
		{
			return String.Format(CultureInfo.CurrentCulture, "X={0}, Y={1}, Width={2}, Height={3}", Left, Top, Right - Left, Bottom - Top);
		}
        [XmlIgnore]
        public int X
        {
            get { return Left; }
            set 
            {
                int width = Width;
                Left = value;
                Right = Left + width;
            }
        }
        [XmlIgnore]
        public int Y
        {
            get { return Top; }
            set 
            {
                int height = Height;
                Top = value;
                Bottom = Top + height;
            }
        }
        [XmlIgnore]
        public int Width
        {
            get { return Right - Left; }
        }
        [XmlIgnore]
        public int Height
        {
            get { return Bottom - Top; }
        }
        [XmlIgnore]
        public POINT Location
        {
            get { return new POINT(X, Y); }
            set
            {
                X = value.X;
                Y = value.Y;
            }
        }
        [XmlIgnore]
        public SIZE Size
        {
            get { return new SIZE(Width, Height); }
        }        
        /// <summary>
    }

	[StructLayout(LayoutKind.Sequential)]
	public struct RECTL       /* rcl */
	{
		public int left;
		public int top;
		public int right;
		public int bottom;
	}

    /// <summary>
    /// A win32 point.
    /// </summary>
    [Serializable]
	[StructLayout(LayoutKind.Sequential)]
    public struct POINT
	{
        /// <summary>
        /// The horizontal component.
        /// </summary>
		public int X;
        /// <summary>
        /// The vertical component.
        /// </summary>
		public int Y;
        /// <summary>
        /// Initializes a new <see cref="POINT"/>.
        /// </summary>
		public POINT(int x, int y)
		{
			this.X = x;
			this.Y = y;
		}
        /// <summary>
        /// Gets an empty <see cref="POINT"/>.
        /// </summary>
		public static POINT Empty
		{
			get {return new POINT(0, 0);}
		}
        /// <summary>
        /// Gets a <see cref="Point"/> from this <see cref="POINT"/>
        /// instance.
        /// </summary>
		public Point ToPoint()
		{
			return new Point(X, Y);
		}
        /// <summary>
        /// Gets a <see cref="POINT"/> from a <see cref="Point"/>
        /// instance.
        /// </summary>
		public static POINT FromPoint(Point point)
		{
			return new POINT(point.X, point.Y);
		}
        /// <summary>
        /// Converts the <see cref="SIZE"/> instance
        /// into a single int suitable for sending
        /// as a win32 message parameter.
        /// </summary>
        public IntPtr ToParam()
        {
            return (IntPtr)((Y << 16) | (X & 0xffff));
        }
        /// <summary>
        /// Converts a win32 message parameter into a
        /// <see cref="SIZE"/> instance.
        /// </summary>
        public static POINT FromParam(IntPtr param)
        {
            POINT point = new POINT();
            point.X = WinDefApi.LOWORD(param);
            point.Y = WinDefApi.HIWORD(param);
            return point;
        }
		public override string ToString()
		{
			return String.Format(CultureInfo.CurrentCulture, "{0}, {1}", X, Y);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
    public struct POINTL      /* ptl  */
	{
		public int x;
		public int y;
	}
    /// <summary>
    /// A win32 size structure.
    /// </summary>
	[StructLayout(LayoutKind.Sequential)]
    public struct SIZE
	{
        /// <summary>
        /// The width component.
        /// </summary>
		public int cx;
        /// <summary>
        /// The height component.
        /// </summary>
		public int cy;
        /// <summary>
        /// Initializes a new <see cref="SIZE"/>.
        /// </summary>
		public SIZE(int cx, int cy)
		{
			this.cx = cx;
			this.cy = cy;
		}
        /// <summary>
        /// Gets a <see cref="Size"/> from this
        /// <see cref="SIZE"/> instance.
        /// </summary>
		public Size ToSize()
		{
			return new Size(cx, cy);
		}
        /// <summary>
        /// Gets a <see cref="SIZE"/> from a
        /// <see cref="Size"/> instance.
        /// </summary>
		public static SIZE FromSize(Size size)
		{
			return new SIZE(size.Width, size.Height);
		}
        /// <summary>
        /// Converts the <see cref="SIZE"/> instance
        /// into a single int suitable for sending
        /// as a win32 message parameter.
        /// </summary>
        public IntPtr ToParam()
        {
            return (IntPtr) ((cy << 16) | (cx & 0xffff));
        }
        /// <summary>
        /// Converts a win32 message parameter into a
        /// <see cref="SIZE"/> instance.
        /// </summary>
        public static SIZE FromParam(IntPtr param)
        {
            SIZE size = new SIZE();
            size.cx = WinDefApi.LOWORD(param);
            size.cy = WinDefApi.HIWORD(param);
            return size;
        }
	}

	[StructLayout(LayoutKind.Sequential)]
    public struct POINTS
	{
		public short x;
		public short y;
	}

	//
	//  File System time stamps are represented with the following structure:
	//

	[StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
	{
		public int dwLowDateTime;
		public int dwHighDateTime;
	}
	#endregion

	#region Constants
    public sealed class DeviceModes
	{
		/* mode selections for the device mode function */
		public const int DM_UPDATE           = 1;
		public const int DM_COPY             = 2;
		public const int DM_PROMPT           = 4;
		public const int DM_MODIFY           = 8;

		public const int DM_IN_BUFFER        = DM_MODIFY;
		public const int DM_IN_PROMPT        = DM_PROMPT;
		public const int DM_OUT_BUFFER       = DM_COPY;
		public const int DM_OUT_DEFAULT      = DM_UPDATE;
	}

    public sealed class DeviceCapabilities
	{
		/* device capabilities indices */
		public const int DC_FIELDS           = 1;
		public const int DC_PAPERS           = 2;
		public const int DC_PAPERSIZE        = 3;
		public const int DC_MINEXTENT        = 4;
		public const int DC_MAXEXTENT        = 5;
		public const int DC_BINS             = 6;
		public const int DC_DUPLEX           = 7;
		public const int DC_SIZE             = 8;
		public const int DC_EXTRA            = 9;
		public const int DC_VERSION          = 10;
		public const int DC_DRIVER           = 11;
		public const int DC_BINNAMES         = 12;
		public const int DC_ENUMRESOLUTIONS  = 13;
		public const int DC_FILEDEPENDENCIES = 14;
		public const int DC_TRUETYPE         = 15;
		public const int DC_PAPERNAMES       = 16;
		public const int DC_ORIENTATION      = 17;
		public const int DC_COPIES           = 18;
	}
	#endregion

	#region Delegates
	#endregion

    public class WinDefApi
    {
        public static short MAKEWORD(int low, int high)
        {
            return (short) unchecked((high << 0x8) | (low & 0xff));
        }
        public static int MAKELONG(int low, int high)
        {
            return unchecked((high << 0x10) | (low & 0xffff));
        }
        public static short HIWORD(int n)
        {
            return (short)((n >> 0x10) & 0xffff);
        }
        public static short HIWORD(IntPtr n)
        {
            return HIWORD(unchecked((int)n));
        }
        public static short LOWORD(int n)
        {
            return (short)(n & 0xffff);
        }
        public static short LOWORD(IntPtr n)
        {
            return LOWORD(unchecked((int)n));
        }
        public static byte LOBYTE(short s)
        {
            return (byte)(s & 0xff);
        }
        public static byte HIBYTE(short s)
        {
            return (byte)(s >> 0x8);
        }
        public static bool BOOL(int n)
        {
            return n != 0;
        }
        public static bool BOOL(IntPtr n)
        {
            return n != IntPtr.Zero;
        }
    }
}
