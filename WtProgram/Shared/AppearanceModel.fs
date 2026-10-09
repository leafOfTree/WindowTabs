namespace Bemo
open System.Drawing

/// Logical or scaled geometry, matching the units of the source appearance.
/// This record deliberately contains no palette fields.
type TabGeometry = {
    style:TabStyle
    height:int; maxWidth:int; overlap:int; heightOffset:int
    indentNormal:int }

module TabGeometry =
    /// Tabs drawn inside the window sit over its title bar; this extra margin keeps
    /// them clear of three 46px caption buttons plus a small gap before minimize.
    let captionButtonsReserve = 144
    let fromAppearance (appearance:TabAppearanceInfo) = {
        style=appearance.tabStyle
        height=appearance.tabHeight; maxWidth=appearance.tabMaxWidth
        overlap=appearance.tabOverlap; heightOffset=appearance.tabHeightOffset
        indentNormal=appearance.tabIndentNormal }
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TabStyle =
    /// Stored names, in the order the settings page lists them.
    let names = ["joined";"folder";"pill"]
    let parse = function "folder" -> FolderTabs | "pill" -> PillTabs | _ -> JoinedTabs
    let serialize = function FolderTabs -> "folder" | PillTabs -> "pill" | JoinedTabs -> "joined"
type TabPalette = {
    tabTextColor:Color; tabNormalBgColor:Color; tabHighlightBgColor:Color
    tabActiveBgColor:Color; tabBorderColor:Color; tabFlashBgColor:Color }

module TabPalette =
    let fromAppearance (a:TabAppearanceInfo) : TabPalette = {
        tabTextColor=a.tabTextColor; tabNormalBgColor=a.tabNormalBgColor
        tabHighlightBgColor=a.tabHighlightBgColor; tabActiveBgColor=a.tabActiveBgColor
        tabBorderColor=a.tabBorderColor; tabFlashBgColor=a.tabFlashBgColor }
    let compose (g:TabGeometry) (p:TabPalette) : TabAppearanceInfo = {
        tabStyle=g.style; tabHeight=g.height; tabMaxWidth=g.maxWidth; tabOverlap=g.overlap
        tabHeightOffset=g.heightOffset; tabIndentNormal=g.indentNormal
        tabIndentFlipped=g.indentNormal+TabGeometry.captionButtonsReserve
        tabTextColor=p.tabTextColor; tabNormalBgColor=p.tabNormalBgColor
        tabHighlightBgColor=p.tabHighlightBgColor; tabActiveBgColor=p.tabActiveBgColor
        tabBorderColor=p.tabBorderColor; tabFlashBgColor=p.tabFlashBgColor }

/// Keeps tab text readable on each tab colour. One text colour serves the active, hovered,
/// inactive and flashing tabs, and no single choice suits every combination a user can pick,
/// so each tab is judged on its own: one that reads well keeps the colour as chosen.
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
    /// The other way round: the background taken lighter or darker, just until the text reads
    /// on it, for a background that may change where the text must not.
    let readableBackground (text:Color) (background:Color) =
        if ratio text background >= minimum then background
        else
            let target = if ratio text Color.White >= ratio text Color.Black then Color.White else Color.Black
            shades background target
            |> List.tryFind(fun candidate -> ratio text candidate >= minimum)
            |> Option.defaultValue target

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
