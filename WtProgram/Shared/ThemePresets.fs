namespace Bemo
open System.Drawing

module ThemePresets =
    let palettes dark =
        let make (basis:TabPalette) text active hover inactive border =
            { basis with tabTextColor=Color.FromRGB(text);tabActiveBgColor=Color.FromRGB(active)
                         tabHighlightBgColor=Color.FromRGB(hover);tabNormalBgColor=Color.FromRGB(inactive)
                         tabBorderColor=Color.FromRGB(border) }
        if dark then
            [|Theme.darkPalette;Theme.bluePalette
              make Theme.darkPalette 0xE4EEE6 0x19251D 0x2C3E31 0x455C4B 0x718778|]
        else
            [|Theme.lightPalette
              make Theme.lightPalette 0x17324D 0xF5FAFF 0xE0ECF7 0xBCD0E3 0x8BA8C2
              make Theme.lightPalette 0x213C2B 0xF6FBF5 0xE1EEDF 0xC0D5BE 0x8CA889|]
