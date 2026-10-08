namespace Bemo

open System
open System.Drawing
open System.Windows.Forms


/// Colour selection is separate from geometry and from the taskbar's theme.
module Theme =


    let light : TabAppearanceInfo = {
        tabStyle=JoinedTabs; tabHeight=25; tabMaxWidth=200; tabOverlap=0
        tabTextColor=Color.FromRGB(0x1F1F1F)
        tabNormalBgColor=Color.FromRGB(0xCCCCCC)
        tabHighlightBgColor=Color.FromRGB(0xE4E4E4)
        tabActiveBgColor=Color.White
        tabBorderColor=Color.FromRGB(0xA8A8A8)
        tabFlashBgColor=Color.FromRGB(0xFFBBBB)
        tabHeightOffset=1; tabIndentFlipped=3+TabGeometry.captionButtonsReserve; tabIndentNormal=3 }

    let dark = { light with
                    tabTextColor=Color.FromRGB(0xF3F3F3)
                    tabNormalBgColor=Color.FromRGB(0x454545)
                    tabHighlightBgColor=Color.FromRGB(0x343434)
                    tabActiveBgColor=Color.FromRGB(0x202020)
                    tabBorderColor=Color.FromRGB(0x747474)
                    tabFlashBgColor=Color.FromRGB(0x772222) }

    /// Stored indices keep their colour meanings when display or allocation order changes.
    /// One set for both themes, every colour dark enough for white text, so a row of tabs reads
    /// the same everywhere. Each keeps the hue its name says; colours near in hue sit at different
    /// depths (blue and navy, pink and rose, red and coral), so any two remain easy to tell apart.
    let private palette =
        [|0x5B6167;0x0065DA;0xCF1637;0x836901;0x017031;0xBD197E;0x690CA5;0x08798D
          0xB35F04;0x6F4DDE;0x5A5503;0xA40044;0x538003;0x0B16A2;0x035E5A;0xAC3802|] |> Array.map Color.FromRGB
    let tabPalette (_dark:bool) = Array.copy palette
    let tabPaletteSize = 16
    /// Keep related colours together in the menu.
    let tabColorOrder = [1;7;14;4;3;8;2;5;11;6;9;10;12;13;15;0]
    /// Alternate distant hues so neighbouring automatic colours remain easy to distinguish.
    let tabAllocationOrder = [1;8;4;6;3;13;2;7;5;14;11;10;9;12;15;0]
    /// Whether a bar takes the lighter tints. Decided by the bar rather than the theme: a
    /// custom palette can put a dark bar in the light theme. 0.179 is where black and white
    /// text are equally readable.
    let darkBar (bar:Color) = TextContrast.luminance bar < 0.179
    /// "palette:N" for a palette colour, "#RRGGBB" for a custom one.
    let parseTabColor (value:string) =
        let invariant = Globalization.CultureInfo.InvariantCulture
        let mutable number = 0
        if isNull value then None
        elif value.StartsWith("palette:",StringComparison.Ordinal) &&
             Int32.TryParse(value.Substring(8),Globalization.NumberStyles.None,invariant,&number) && number<tabPaletteSize then
            Some(PaletteColor number)
        elif value.Length=7 && value.[0]='#' && Int32.TryParse(value.Substring(1),Globalization.NumberStyles.HexNumber,invariant,&number) then
            Some(CustomColor(Color.FromRGB number))
        else None
    let formatTabColor choice =
        match choice with
        | PaletteColor index -> sprintf "palette:%d" index
        | CustomColor color -> sprintf "#%02X%02X%02X" color.R color.G color.B
    let tabColor dark choice =
        match choice with
        | PaletteColor index -> (tabPalette dark).[index]
        | CustomColor color -> color
    /// FNV-1a over the executable name, independent of runtime hash randomisation.
    let appColorIndex (exe:string) =
        let hash = exe.ToUpperInvariant() |> Seq.fold(fun hash c -> (hash ^^^ uint32 c)*16777619u) 2166136261u
        int(hash % uint32 tabPaletteSize)
    let leastUsedColor indices =
        let counts = Array.zeroCreate tabPaletteSize
        for index in indices do if index>=0 && index<tabPaletteSize then counts.[index] <- counts.[index]+1
        // Exhaust the coloured choices before grey, then repeat in the same order.
        tabAllocationOrder |> List.mapi(fun rank index -> rank,index) |> List.minBy(fun (rank,index) -> counts.[index],rank) |> snd
    let blend amount (tint:Color) (background:Color) =
        let channel a b = int(Math.Round(float b + (float a-float b)*amount))
        Color.FromArgb(255,channel tint.R background.R,channel tint.G background.G,channel tint.B background.B)
    let tabTint highContrast tint = if highContrast then None else tint
    /// OKLab, where equal steps look equal: shades taken in it keep their hue and stay apart.
    module OkLab =
        let private linear (c:byte) = let v = float c/255.0 in if v<=0.04045 then v/12.92 else ((v+0.055)/1.055)**2.4
        let private encoded (v:float) = let v = max 0.0 (min 1.0 v) in if v<=0.0031308 then 12.92*v else 1.055*(v**(1.0/2.4))-0.055
        let private cbrt (v:float) = if v<0.0 then -((-v)**(1.0/3.0)) else v**(1.0/3.0)
        let ofColor (c:Color) =
            let r,g,b = linear c.R,linear c.G,linear c.B
            let l = cbrt(0.4122214708*r+0.5363325363*g+0.0514459929*b)
            let m = cbrt(0.2119034982*r+0.6806995451*g+0.1073969566*b)
            let s = cbrt(0.0883024619*r+0.2817188376*g+0.6299787005*b)
            0.2104542553*l+0.7936177850*m-0.0040720468*s,1.9779984951*l-2.4285922050*m+0.4505937099*s,0.0259040371*l+0.7827717662*m-0.8086757660*s
        let private linearOf (L:float,a:float,b:float) =
            let l = (L+0.3963377774*a+0.2158037573*b)**3.0
            let m = (L-0.1055613458*a-0.0638541728*b)**3.0
            let s = (L-0.0894841775*a-1.2914855480*b)**3.0
            4.0767416621*l-3.3077115913*m+0.2309699292*s,-1.2684380046*l+2.6097574011*m-0.3413193965*s,-0.0041960863*l-0.7034186147*m+1.7076147010*s
        /// Lightness, chroma and hue; chroma is given up where sRGB cannot show it.
        let ofLch (L:float) (chroma:float) (hue:float) =
            let rec fit chroma =
                let r,g,b = linearOf(L,chroma*cos hue,chroma*sin hue)
                if (r<0.0 || g<0.0 || b<0.0 || r>1.0 || g>1.0 || b>1.0) && chroma>0.002 then fit (chroma-0.002) else r,g,b
            let r,g,b = fit chroma
            let channel v = int(Math.Round(encoded v*255.0))
            Color.FromArgb(255,channel r,channel g,channel b)
        let distance (x:Color) (y:Color) =
            let (l1,a1,b1),(l2,a2,b2) = ofColor x,ofColor y
            sqrt((l1-l2)*(l1-l2)+(a1-a2)*(a1-a2)+(b1-b2)*(b1-b2))
    /// A tab behind the active one in its colour: a little darker and with less colour, so the
    /// active tab is the brightest and the rest keep their hue and stay apart.
    let tabShade (darker:float) (colourKept:float) (tint:Color) =
        let L,a,b = OkLab.ofColor tint
        OkLab.ofLch (L-darker) (sqrt(a*a+b*b)*colourKept) (atan2 b a)
    /// How much of each colour an inactive group keeps; the rest comes from its background.
    let dimAmount = 0.4
    /// Soften inactive groups without letting the window underneath show through: towards white
    /// on a light bar, and on a dark one towards the bar itself, so dark tabs grey out instead of
    /// sinking to black, where their colours could no longer be told apart.
    let dimColor bar color =
        let background = if darkBar bar then bar else Color.White
        blend dimAmount color background
    /// The separator is not chosen on its own: it follows the text and inactive tab colours, a
    /// quarter of the way from the inactive tab towards the text, as the presets draw it. Back on
    /// its preset's text and inactive colours, a palette takes the preset's own separator again.
    let followSeparator (preset:TabPalette option) (before:TabPalette) (after:TabPalette) =
        let same (a:Color) (b:Color) = a.ToArgb()=b.ToArgb()
        if same after.tabTextColor before.tabTextColor && same after.tabNormalBgColor before.tabNormalBgColor then after
        else
            match preset with
            | Some p when same p.tabTextColor after.tabTextColor && same p.tabNormalBgColor after.tabNormalBgColor ->
                {after with tabBorderColor=p.tabBorderColor}
            | _ -> {after with tabBorderColor=blend 0.25 after.tabTextColor after.tabNormalBgColor}

    let sameColors (a:TabAppearanceInfo) (b:TabAppearanceInfo) =
        let values (c:TabAppearanceInfo) =
            [c.tabTextColor;c.tabNormalBgColor;c.tabHighlightBgColor;c.tabActiveBgColor;c.tabBorderColor;c.tabFlashBgColor]
            |> List.map (fun color -> color.ToArgb())
        values a = values b

    let usesDark mode systemDark =
        match mode with
        | DarkTheme -> true
        | LightTheme -> false
        | SystemTheme -> systemDark
    let defaultGeometry = TabGeometry.fromAppearance light
    let resetLayout (settings:AppearancePreferences) = {settings with geometry=defaultGeometry}
    let lightPalette = TabPalette.fromAppearance light
    let darkPalette = TabPalette.fromAppearance dark
    let bluePalette =
        { darkPalette with
            tabTextColor=Color.FromRGB(0xE0E0E0); tabNormalBgColor=Color.FromRGB(0x4B5970)
            tabHighlightBgColor=Color.FromRGB(0x273548); tabActiveBgColor=Color.FromRGB(0x111827)
            tabBorderColor=Color.FromRGB(0x374151); tabFlashBgColor=Color.FromRGB(0x991B1B) }

    /// Upgrade only exact copies of the former presets; keep user-edited colours.
    let upgradeDarkPalette palette =
        let oldDark = { darkPalette with tabNormalBgColor=Color.FromRGB(0x202020);tabActiveBgColor=Color.FromRGB(0x454545) }
        let oldBlue = { bluePalette with tabNormalBgColor=Color.FromRGB(0x111827);tabHighlightBgColor=Color.FromRGB(0x4B5970);tabActiveBgColor=Color.FromRGB(0x273548) }
        let matches candidate = sameColors (TabPalette.compose defaultGeometry palette) (TabPalette.compose defaultGeometry candidate)
        if matches oldDark then darkPalette
        elif matches oldBlue then bluePalette
        else palette

    let resolve mode systemDark highContrast custom (geometry:TabGeometry) (lightColors:TabPalette) (darkColors:TabPalette) =
        let colors =
            if highContrast then
                { lightPalette with
                    tabTextColor=SystemColors.WindowText
                    tabNormalBgColor=SystemColors.Window
                    tabActiveBgColor=SystemColors.Window
                    tabHighlightBgColor=SystemColors.Control
                    tabBorderColor=SystemColors.WindowText
                    tabFlashBgColor=SystemColors.Control }
            elif usesDark mode systemDark then if custom then darkColors else darkPalette
            else if custom then lightColors else lightPalette
        TabPalette.compose geometry colors

/// Main-thread writes retain window identity across group transfers; reads use immutable maps.
type WindowTabColors() =
    let mutable indices : Map<IntPtr,int> = Map.empty
    let mutable overrides : Map<IntPtr,TabColorChoice> = Map.empty
    let mutable paths : Map<IntPtr,string> = Map.empty
    member _.getOverride hwnd = overrides.TryFind hwnd
    member _.setOverride hwnd color = overrides <- match color with Some color -> overrides.Add(hwnd,color) | None -> overrides.Remove hwnd
    /// Asked once per window: finding the path opens its process.
    member _.path hwnd (load:unit -> string) =
        match paths.TryFind hwnd with
        | Some path -> path
        | None ->
            let path = load()
            paths <- paths.Add(hwnd,path)
            path
    member _.remove hwnd =
        indices <- indices.Remove hwnd
        overrides <- overrides.Remove hwnd
        paths <- paths.Remove hwnd
    /// Allocate in group order once; singleton previews do not reserve a colour.
    member _.resolve hwnd (path:string) mode (remembered:Map<string,string>) (peers:IntPtr list) =
        let app = remembered |> Map.tryPick(fun key value ->
            if String.Equals(key,path,StringComparison.OrdinalIgnoreCase) then Theme.parseTabColor value else None)
        match overrides.TryFind hwnd,app with
        | Some color,_ | _,Some color -> Some color
        | _ ->
            match mode with
            | "ByWindow" ->
                if peers.Length>1 then
                    for peer in peers do
                        if not(indices.ContainsKey peer) then
                            let used = peers |> List.choose(fun peer -> indices.TryFind peer)
                            indices <- indices.Add(peer,Theme.leastUsedColor used)
                let index = indices.TryFind hwnd |> Option.defaultValue Theme.tabAllocationOrder.Head
                Some(PaletteColor index)
            | "ByApp" -> Some(PaletteColor(Theme.appColorIndex (IO.Path.GetFileName path)))
            | _ -> None
