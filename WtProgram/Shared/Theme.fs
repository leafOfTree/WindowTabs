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
        tabHeightOffset=1; tabIndentFlipped=80; tabIndentNormal=3 }

    let dark = { light with
                    tabTextColor=Color.FromRGB(0xF3F3F3)
                    tabNormalBgColor=Color.FromRGB(0x454545)
                    tabHighlightBgColor=Color.FromRGB(0x343434)
                    tabActiveBgColor=Color.FromRGB(0x202020)
                    tabBorderColor=Color.FromRGB(0x747474)
                    tabFlashBgColor=Color.FromRGB(0x772222) }

    let tabPalette dark =
        (if dark then [|0x9AA0A6;0x8AB4F8;0xF28B82;0xFDD663;0x81C995;0xFF8BCB;0xC58AF9;0x78D9EC|]
         else [|0x70757A;0x1A73E8;0xD93025;0xE8A200;0x188038;0xD01884;0x8430CE;0x008B9A|]) |> Array.map Color.FromRGB
    let blend amount (tint:Color) (background:Color) =
        let channel a b = int(Math.Round(float b + (float a-float b)*amount))
        Color.FromArgb(255,channel tint.R background.R,channel tint.G background.G,channel tint.B background.B)
    let tabTint highContrast tint = if highContrast then None else tint

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
