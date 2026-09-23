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
            { background=Color.FromRGB(0x1A1A19); surface=Color.FromRGB(0x252524); text=Color.FromRGB(0xF0EFEC)
              muted=Color.FromRGB(0x8C8A84); border=Color.FromRGB(0x30302E); hover=Color.FromRGB(0x30302F)
              accent=Color.FromRGB(0x3B82F6); selection=Color.FromRGB(0x373735) }
        else
            { background=Color.FromRGB(0xFAF9F6); surface=Color.White; text=Color.FromRGB(0x262624)
              muted=Color.FromRGB(0x73716B); border=Color.FromRGB(0xE5E3DE); hover=Color.FromRGB(0xEEECE7)
              accent=Color.FromRGB(0x0067C0); selection=Color.FromRGB(0xE4E2DC) }

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
