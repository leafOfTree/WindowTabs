namespace Bemo
open System
open System.Drawing
open System.Windows.Forms
open System.Runtime.InteropServices

/// muted: secondary text and marks beside the main text, as a palette row's program or a list's
/// column heading. description: a setting's description and the other explanations on a page,
/// a shade further from the names, so colour as well as size tells the two apart.
type SettingsPalette = {
    background:Color; surface:Color; text:Color; muted:Color; description:Color; disabledText:Color
    border:Color; hover:Color; accent:Color; onAccent:Color; accentText:Color; selection:Color }

module SettingsColors =
    let current() =
        if SystemInformation.HighContrast then
            { background=SystemColors.Window; surface=SystemColors.Window; text=SystemColors.WindowText
              muted=SystemColors.WindowText; description=SystemColors.WindowText; disabledText=SystemColors.GrayText; border=SystemColors.WindowText; hover=SystemColors.Control
              accent=SystemColors.Highlight; onAccent=SystemColors.HighlightText; accentText=SystemColors.Highlight; selection=SystemColors.Highlight }
        // The accent follows Windows; on dark pages it is a light shade, so what sits on it is black.
        // Secondary and disabled text in WinUI's own greys over the page, light enough to read on
        // the dark layers the popups are. Descriptions a shade darker: in the secondary grey they
        // read as nearly the names' own colour on dark pages.
        // The greys are Windows Settings' own, neutral: a warm tint read as yellowed beside it.
        elif ThemeService.currentIsDark() then
            { background=Color.FromRGB(0x202020); surface=Color.FromRGB(0x2B2B2B); text=Color.White
              muted=Color.FromRGB(0xCCCCCC); description=Color.FromRGB(0x9D9D9D); disabledText=Color.FromRGB(0x757575); border=Color.FromRGB(0x353535); hover=Color.FromRGB(0x323232)
              accent=ThemeService.accent true; onAccent=Color.Black; accentText=ThemeService.accentText true; selection=Color.FromRGB(0x383838) }
        else
            { background=Color.White; surface=Color.White; text=Color.FromRGB(0x1B1B1B)
              muted=Color.FromRGB(0x6B6B6B); description=Color.FromRGB(0x6B6B6B); disabledText=Color.FromRGB(0xBDBDBD); border=Color.FromRGB(0xE5E5E5); hover=Color.FromRGB(0xF3F3F3)
              accent=ThemeService.accent false; onAccent=Color.White; accentText=ThemeService.accentText false; selection=Color.FromRGB(0xE3E3E3) }

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
