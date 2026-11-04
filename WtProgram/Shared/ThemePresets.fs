namespace Bemo
open System
open System.Drawing

/// The accent colour chosen in Windows' Settings, read on system notifications and kept, as a
/// control's paint must not read the registry.
module SystemAccent =
    /// The shades Windows derives from the accent, lightest first: Light3, Light2, Light1, the
    /// accent, Dark1, Dark2, Dark3. None when Windows has none saved.
    let read() =
        try
            use key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent")
            match (if isNull key then null else key.GetValue("AccentPalette")) with
            | :? (byte[]) as bytes when bytes.Length>=28 ->
                Some(Array.init 7 (fun index -> Color.FromArgb(int bytes.[index*4],int bytes.[index*4+1],int bytes.[index*4+2])))
            | _ -> None
        with _ -> None
    let mutable private shades = read()
    let refresh() = System.Threading.Volatile.Write(&shades,read())
    let private shade index fallback =
        match System.Threading.Volatile.Read(&shades) with
        | Some all -> all.[index]
        | None -> Color.FromRGB(fallback)
    /// The accent as WinUI fills with it: Dark1 on light pages, Light2 on dark ones, which keep
    /// text and marks on it readable. Windows' default blue when no accent is saved.
    let fill dark = if dark then shade 1 0x60CDFF else shade 4 0x005FB8
    /// The accent as WinUI writes text in it: Dark2 on light pages, Light3 on dark ones, a step
    /// further from the page than the fill, so a light accent such as gold stays legible.
    let text dark = if dark then shade 0 0x99EBFF else shade 5 0x003E92
    /// The accent itself, as chosen.
    let chosen() = shade 3 0x0078D4

module ThemePresets =
    let private order = [|0;9;1;4;2;5;6;7;8;3|]
    let names = order |> Array.map(fun index -> Strings.Appearance.presets.[index])
    /// Stable names for storing a preset choice and its edits, whatever the display language.
    let keys =
        let stored = [|"Default";"Ocean";"Forest";"Slate";"Teal";"Sand";"Amber";"Rose";"Plum";"Accent"|]
        order |> Array.map(fun index -> stored.[index])
    /// Stored as the preset choice when the user's own palette is chosen.
    let customKey = "custom"
    /// The preset made from Windows' accent colour.
    let accentKey = "Accent"
    /// Tabs in tints of the accent, as the other presets are tints of their colour: near white
    /// for the front tab on light pages, deeper for those behind and their edge, the text a dark
    /// shade of it; on dark pages the same steps from a near-black.
    let accentTints dark (accent:Color) =
        let mix (from:Color) (t:float) =
            let step (a:byte) (b:byte) = int(Math.Round(float a+(float b-float a)*t))
            Color.FromArgb(step from.R accent.R,step from.G accent.G,step from.B accent.B)
        if dark then
            let ground = Color.FromRGB(0x1E1E1E)
            { Theme.darkPalette with tabTextColor=mix Color.White 0.10;tabActiveBgColor=mix ground 0.10
                                     tabHighlightBgColor=mix ground 0.22;tabNormalBgColor=mix ground 0.36;tabBorderColor=mix ground 0.62 }
        else
            { Theme.lightPalette with tabTextColor=mix Color.Black 0.30;tabActiveBgColor=mix Color.White 0.04
                                      tabHighlightBgColor=mix Color.White 0.12;tabNormalBgColor=mix Color.White 0.28;tabBorderColor=mix Color.White 0.50 }
    let private accentPalette dark = accentTints dark (SystemAccent.chosen())
    /// A preset's dot in the list: its hue, at a lightness that shows on the list, with about three
    /// times the colour of the muted tab it stands for. Only brightened, the dots of dark presets
    /// came out grey-white and hard to tell apart. A grey preset stays grey.
    let previewColor dark (color:Color) =
        if System.Windows.Forms.SystemInformation.HighContrast then color
        else
            let _,a,b = Theme.OkLab.ofColor color
            let chroma = sqrt(a*a+b*b)
            let shown = if chroma<0.01 then 0.0 else min 0.13 (max 0.07 (chroma*3.0))
            Theme.OkLab.ofLch (if dark then 0.76 else 0.70) shown (atan2 b a)
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
                  make Theme.darkPalette 0xFCE7E8 0x311D21 0x482D32 0x634248 0xA4767E
                  make Theme.darkPalette 0xF1E8FA 0x281E33 0x3D2F4C 0x554267 0x927BA6
                  accentPalette true|]
            else
                [|Theme.lightPalette
                  make Theme.lightPalette 0x17324D 0xF5FAFF 0xE0ECF7 0xBCD0E3 0x8BA8C2
                  make Theme.lightPalette 0x213C2B 0xF6FBF5 0xE1EEDF 0xC0D5BE 0x8CA889
                  make Theme.lightPalette 0x293440 0xF7F9FC 0xE3E9F0 0xC4CEDA 0x919FAF
                  make Theme.lightPalette 0x163E38 0xF2FCF9 0xDCEFE9 0xB5D8CE 0x7DA99D
                  make Theme.lightPalette 0x44382B 0xFFFCF5 0xF0E9D9 0xDCD0B6 0xAA9876
                  make Theme.lightPalette 0x50351C 0xFFFAF1 0xFBEACF 0xEED2A9 0xBC9765
                  make Theme.lightPalette 0x522A30 0xFEF8F8 0xF7E1E4 0xE8C3C9 0xB78C94
                  make Theme.lightPalette 0x402B52 0xFCF8FF 0xEEE2F6 0xD8C4E6 0xA28BB4
                  accentPalette false|]
        order |> Array.map(fun index -> stored.[index])
    /// The colours a look's tabs take: the accent preset's worked out afresh, so the tabs follow
    /// Windows when its accent changes; any other choice, or the accent preset once its colours
    /// are edited, as stored.
    let following dark (s:AppearancePreferences) (stored:TabPalette) =
        let key = if dark then s.darkPreset else s.lightPreset
        if s.useCustomColors && key=accentKey && not (s.presetEdits.ContainsKey((if dark then "dark:" else "light:")+accentKey))
        then accentPalette dark
        else stored
