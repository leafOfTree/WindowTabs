namespace Bemo

open System
open System.Drawing
open System.Windows.Forms


/// Colour selection is separate from geometry and from the taskbar's theme.
module Theme =


    let light : TabAppearanceInfo = {
        tabHeight=25; tabMaxWidth=200; tabOverlap=0
        tabTextColor=Color.FromRGB(0x1F1F1F)
        tabNormalBgColor=Color.FromRGB(0xCCCCCC)
        tabHighlightBgColor=Color.FromRGB(0xE4E4E4)
        tabActiveBgColor=Color.White
        tabBorderColor=Color.FromRGB(0xA8A8A8)
        tabFlashBgColor=Color.FromRGB(0xFFBBBB)
        tabHeightOffset=1; tabIndentFlipped=80; tabIndentNormal=3 }

    let dark = { light with
                    tabTextColor=Color.FromRGB(0xF3F3F3)
                    tabNormalBgColor=Color.FromRGB(0x202020)
                    tabHighlightBgColor=Color.FromRGB(0x343434)
                    tabActiveBgColor=Color.FromRGB(0x454545)
                    tabBorderColor=Color.FromRGB(0x747474)
                    tabFlashBgColor=Color.FromRGB(0x772222) }

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
            tabTextColor=Color.FromRGB(0xE0E0E0); tabNormalBgColor=Color.FromRGB(0x111827)
            tabHighlightBgColor=Color.FromRGB(0x4B5970); tabActiveBgColor=Color.FromRGB(0x273548)
            tabBorderColor=Color.FromRGB(0x374151); tabFlashBgColor=Color.FromRGB(0x991B1B) }

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
