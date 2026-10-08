namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

type SettingsPalette = {
    background:Color; surface:Color; text:Color; muted:Color; disabledText:Color
    border:Color; hover:Color; accent:Color; selection:Color }

module SettingsColors =
    let current() =
        if SystemInformation.HighContrast then
            { background=SystemColors.Window; surface=SystemColors.Window; text=SystemColors.WindowText
              muted=SystemColors.WindowText; disabledText=SystemColors.GrayText; border=SystemColors.WindowText; hover=SystemColors.Control
              accent=SystemColors.Highlight; selection=SystemColors.Highlight }
        elif ThemeService.currentIsDark() then
            { background=Color.FromRGB(0x1A1A19); surface=Color.FromRGB(0x252524); text=Color.FromRGB(0xF0EFEC)
              muted=Color.FromRGB(0x8C8A84); disabledText=Color.FromRGB(0x55544F); border=Color.FromRGB(0x30302E); hover=Color.FromRGB(0x30302F)
              accent=Color.FromRGB(0x3B82F6); selection=Color.FromRGB(0x373735) }
        else
            { background=Color.White; surface=Color.White; text=Color.FromRGB(0x191919)
              muted=Color.FromRGB(0x898883); disabledText=Color.FromRGB(0xBDBCB8); border=Color.FromRGB(0xE5E5E5); hover=Color.FromRGB(0xF3F3F3)
              accent=Color.FromRGB(0x2D79D7); selection=Color.FromRGB(0xE3E3E3) }
