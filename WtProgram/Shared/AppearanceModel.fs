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