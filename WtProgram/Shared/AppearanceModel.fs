namespace Bemo
open System.Drawing

/// Logical or scaled geometry, matching the units of the source appearance.
/// This record deliberately contains no palette fields.
type TabGeometry = {
    height:int; maxWidth:int; overlap:int; heightOffset:int
    indentNormal:int }

module TabGeometry =
    /// Tabs drawn inside the window sit over its title bar; this extra margin keeps
    /// them clear of the caption buttons. One side margin setting covers both cases.
    let captionButtonsReserve = 77
    let fromAppearance (appearance:TabAppearanceInfo) = {
        height=appearance.tabHeight; maxWidth=appearance.tabMaxWidth
        overlap=appearance.tabOverlap; heightOffset=appearance.tabHeightOffset
        indentNormal=appearance.tabIndentNormal }
type TabPalette = {
    tabTextColor:Color; tabNormalBgColor:Color; tabHighlightBgColor:Color
    tabActiveBgColor:Color; tabBorderColor:Color; tabFlashBgColor:Color }

module TabPalette =
    let fromAppearance (a:TabAppearanceInfo) : TabPalette = {
        tabTextColor=a.tabTextColor; tabNormalBgColor=a.tabNormalBgColor
        tabHighlightBgColor=a.tabHighlightBgColor; tabActiveBgColor=a.tabActiveBgColor
        tabBorderColor=a.tabBorderColor; tabFlashBgColor=a.tabFlashBgColor }
    let compose (g:TabGeometry) (p:TabPalette) : TabAppearanceInfo = {
        tabHeight=g.height; tabMaxWidth=g.maxWidth; tabOverlap=g.overlap
        tabHeightOffset=g.heightOffset; tabIndentNormal=g.indentNormal
        tabIndentFlipped=g.indentNormal+TabGeometry.captionButtonsReserve
        tabTextColor=p.tabTextColor; tabNormalBgColor=p.tabNormalBgColor
        tabHighlightBgColor=p.tabHighlightBgColor; tabActiveBgColor=p.tabActiveBgColor
        tabBorderColor=p.tabBorderColor; tabFlashBgColor=p.tabFlashBgColor }

/// Keeps tab text readable on each tab colour. One text colour serves the active, hovered,
/// inactive and flashing tabs, and no single choice suits every combination a user can pick.
module TextContrast =
    /// WCAG 2 contrast for normal-sized text.
    let minimum = 4.5
    /// WCAG relative luminance, 0 for black to 1 for white.
    let luminance (color:Color) =
        let channel (value:byte) =
            let c = float value/255.0
            if c <= 0.03928 then c/12.92 else ((c+0.055)/1.055) ** 2.4
        0.2126*channel color.R+0.7152*channel color.G+0.0722*channel color.B
    /// From 1 (the same) to 21 (black on white).
    let ratio (a:Color) (b:Color) =
        let la,lb = luminance a,luminance b
        (max la lb+0.05)/(min la lb+0.05)
    /// The text taken a twentieth of the way towards black or white per step.
    let private shades (text:Color) (target:Color) =
        let channel (a:byte) (b:byte) t = int(System.Math.Round(float a+(float b-float a)*t))
        [1..20] |> List.map(fun step ->
            let t = float step/20.0
            Color.FromArgb(255,channel text.R target.R t,channel text.G target.G t,channel text.B target.B t))
    /// The text colour itself where it reads well on the background; otherwise the same colour
    /// taken darker or lighter, whichever the background allows, just until it does.
    let readable (text:Color) (background:Color) =
        if ratio text background >= minimum then text
        else
            let target = if ratio Color.Black background >= ratio Color.White background then Color.Black else Color.White
            shades text target
            |> List.tryFind(fun candidate -> ratio candidate background >= minimum)
            |> Option.defaultValue target
    /// One shade of the text that reads well on all the backgrounds, the nearest one to it.
    /// None when some need it darker and others lighter, as with a light active tab among
    /// dark ones.
    let shared (text:Color) (backgrounds:Color list) =
        let reads (candidate:Color) = backgrounds |> List.forall(fun background -> ratio candidate background >= minimum)
        if reads text then Some text
        else
            [Color.Black;Color.White]
            |> List.choose(fun target ->
                let candidates = shades text target
                candidates |> List.tryFindIndex reads |> Option.map(fun step -> step,candidates.[step]))
            |> List.sortBy fst |> List.tryHead |> Option.map snd
    /// The text colour on one tab. The active, hovered and inactive tabs share one shade where
    /// one reads on all three, so the text looks the same as it moves between them; otherwise,
    /// and on any other background such as a flashing tab, each gets its own.
    let onTab (text:Color) (active:Color) (hovered:Color) (inactive:Color) (background:Color) =
        readable (shared text [active;hovered;inactive] |> Option.defaultValue text) background

type ThemeMode = SystemTheme | LightTheme | DarkTheme
module ThemeMode =
    let parse = function "light" -> LightTheme | "dark" -> DarkTheme | _ -> SystemTheme
    let serialize = function LightTheme -> "light" | DarkTheme -> "dark" | SystemTheme -> "system"

type AppearancePreferences = {
    geometry:TabGeometry
    legacyPalette:TabPalette
    lightPalette:TabPalette
    darkPalette:TabPalette
    /// The user's own palettes, kept apart so picking a preset never overwrites them.
    lightCustomPalette:TabPalette
    darkCustomPalette:TabPalette
    mode:ThemeMode
    useCustomColors:bool
    /// The colour preset chosen for each profile: its English name, "custom", or "" when none
    /// has been stored yet (settings from before presets were saved), and it is worked out from
    /// the colours instead.
    lightPreset:string
    darkPreset:string
    /// Colours changed in a preset, by "light:Name" or "dark:Name"; the preset keeps them.
    presetEdits:Map<string,TabPalette> }