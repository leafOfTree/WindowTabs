namespace Bemo
open System
open System.Drawing

module ThemePresets =
    let private order = [|0;1;4;2;5;6;7;8;3|]
    let names = order |> Array.map(fun index -> Strings.Appearance.presets.[index])
    /// Stable names for storing a preset choice and its edits, whatever the display language.
    let keys =
        let stored = [|"Default";"Ocean";"Forest";"Slate";"Teal";"Sand";"Amber";"Rose";"Plum"|]
        order |> Array.map(fun index -> stored.[index])
    /// Stored as the preset choice when the user's own palette is chosen.
    let customKey = "custom"
    /// Small preview dots need more contrast than a full tab background.
    let previewColor dark (color:Color) =
        if not dark || System.Windows.Forms.SystemInformation.HighContrast then color
        else
            let peak = float (max color.R (max color.G color.B))
            let factor = max 1.0 (175.0/max 1.0 peak)
            let channel value = min 255 (int(Math.Round(float value*factor)))
            Color.FromArgb(channel color.R,channel color.G,channel color.B)
    let palettes dark =
        let make (basis:TabPalette) text active hover inactive border =
            { basis with tabTextColor=Color.FromRGB(text);tabActiveBgColor=Color.FromRGB(active)
                         tabHighlightBgColor=Color.FromRGB(hover);tabNormalBgColor=Color.FromRGB(inactive)
                         tabBorderColor=Color.FromRGB(border) }
        let stored =
            if dark then
                [|Theme.darkPalette;Theme.bluePalette
                  make Theme.darkPalette 0xE4EEE6 0x19251D 0x2C3E31 0x455C4B 0x718778
                  make Theme.darkPalette 0xEDF1F5 0x20262E 0x323C48 0x485564 0x7B8B9E
                  make Theme.darkPalette 0xDFF5F0 0x122B29 0x22413E 0x365B56 0x6F9991
                  make Theme.darkPalette 0xF3EDE2 0x2A251F 0x40382D 0x5B5041 0x99866D
                  make Theme.darkPalette 0xFFF0DC 0x302217 0x493321 0x66492E 0xAB8254
                  make Theme.darkPalette 0xFBE7EB 0x301D24 0x472D37 0x61424E 0xA27686
                  make Theme.darkPalette 0xF1E8FA 0x281E33 0x3D2F4C 0x554267 0x927BA6|]
            else
                [|Theme.lightPalette
                  make Theme.lightPalette 0x17324D 0xF5FAFF 0xE0ECF7 0xBCD0E3 0x8BA8C2
                  make Theme.lightPalette 0x213C2B 0xF6FBF5 0xE1EEDF 0xC0D5BE 0x8CA889
                  make Theme.lightPalette 0x293440 0xF7F9FC 0xE3E9F0 0xC4CEDA 0x919FAF
                  make Theme.lightPalette 0x163E38 0xF2FCF9 0xDCEFE9 0xB5D8CE 0x7DA99D
                  make Theme.lightPalette 0x44382B 0xFFFCF5 0xF0E9D9 0xDCD0B6 0xAA9876
                  make Theme.lightPalette 0x50351C 0xFFFAF1 0xFBEACF 0xEED2A9 0xBC9765
                  make Theme.lightPalette 0x502A37 0xFFF7F9 0xF6E1E8 0xE6C3CF 0xB58C9B
                  make Theme.lightPalette 0x402B52 0xFCF8FF 0xEEE2F6 0xD8C4E6 0xA28BB4|]
        order |> Array.map(fun index -> stored.[index])
