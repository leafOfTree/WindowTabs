namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

type SettingsPalette = {
    background:Color; surface:Color; text:Color; muted:Color
    border:Color; hover:Color; accent:Color; selection:Color }

module SettingsColors =
    let current() =
        if SystemInformation.HighContrast then
            { background=SystemColors.Window; surface=SystemColors.Window; text=SystemColors.WindowText
              muted=SystemColors.WindowText; border=SystemColors.WindowText; hover=SystemColors.Control
              accent=SystemColors.Highlight; selection=SystemColors.Highlight }
        elif ThemeService.currentIsDark() then
            { background=Color.FromRGB(0x181818); surface=Color.FromRGB(0x232323); text=Color.FromRGB(0xF3F3F3)
              muted=Color.FromRGB(0xAFAFAF); border=Color.FromRGB(0x363636); hover=Color.FromRGB(0x2C2C2C)
              accent=Color.FromRGB(0x3B82F6); selection=Color.FromRGB(0x303136) }
        else
            { background=Color.FromRGB(0xFAFAFA); surface=Color.White; text=Color.FromRGB(0x202020)
              muted=Color.FromRGB(0x626262); border=Color.FromRGB(0xE1E1E1); hover=Color.FromRGB(0xEAEAEA)
              accent=Color.FromRGB(0x0067C0); selection=Color.FromRGB(0xE8E8E8) }

module SettingsShapes =
    let rounded (rect:RectangleF) radius =
        let path = new Drawing2D.GraphicsPath()
        let d = min (radius*2.0f) (min rect.Width rect.Height)
        path.AddArc(rect.Left,rect.Top,d,d,180.0f,90.0f)
        path.AddArc(rect.Right-d,rect.Top,d,d,270.0f,90.0f)
        path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0.0f,90.0f)
        path.AddArc(rect.Left,rect.Bottom-d,d,d,90.0f,90.0f)
        path.CloseFigure()
        path
