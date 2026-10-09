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

module SettingsTextWrap =
    /// The text as lines no wider than width: broken at spaces, after a path separator inside a
    /// long word such as a file path (WordBreak would clip one), else between characters.
    let lines (text:string) (font:Font) (width:int) =
        let measure (part:string) = TextRenderer.MeasureText(part,font,Size(Int32.MaxValue,Int32.MaxValue),TextFormatFlags.NoPrefix).Width
        let fits (part:string) = measure (part.TrimEnd()) <= width
        // Pieces that each end where a line may break.
        let pieces (paragraph:string) =
            let result = ResizeArray<string>()
            let mutable start = 0
            for index in 0..paragraph.Length-1 do
                if paragraph.[index]=' ' || paragraph.[index]='\\' || paragraph.[index]='/' then
                    result.Add(paragraph.Substring(start,index+1-start))
                    start <- index+1
            if start<paragraph.Length then result.Add(paragraph.Substring(start))
            List.ofSeq result
        let rec characters (line:string) (piece:string) (lines:string list) =
            if piece="" then line,lines
            elif fits (line+piece.Substring(0,1)) || line="" then characters (line+piece.Substring(0,1)) (piece.Substring(1)) lines
            else characters "" piece (line.TrimEnd()::lines)
        text.Replace("\r\n","\n").Split('\n')
        |> Array.collect(fun paragraph ->
            let line,lines =
                pieces paragraph |> List.fold(fun (line:string,lines) piece ->
                    if fits (line+piece) then line+piece,lines
                    elif fits piece then piece,(if line="" then lines else line.TrimEnd()::lines)
                    else characters line piece lines) ("",[])
            line.TrimEnd()::lines |> List.rev |> Array.ofList)
        |> List.ofArray
