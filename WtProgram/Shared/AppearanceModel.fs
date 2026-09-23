namespace Bemo
open System.Drawing

/// Logical or scaled geometry, matching the units of the source appearance.
/// This record deliberately contains no palette fields.
type TabGeometry = {
    height:int; maxWidth:int; overlap:int; heightOffset:int
    indentNormal:int; indentFlipped:int }

module TabGeometry =
    let fromAppearance (appearance:TabAppearanceInfo) = {
        height=appearance.tabHeight; maxWidth=appearance.tabMaxWidth
        overlap=appearance.tabOverlap; heightOffset=appearance.tabHeightOffset
        indentNormal=appearance.tabIndentNormal; indentFlipped=appearance.tabIndentFlipped }
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
        tabHeightOffset=g.heightOffset; tabIndentNormal=g.indentNormal; tabIndentFlipped=g.indentFlipped
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
    mode:ThemeMode
    useCustomColors:bool }