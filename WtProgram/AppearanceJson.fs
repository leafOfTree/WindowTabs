namespace Bemo
open System
open System.Drawing
open Newtonsoft.Json.Linq

/// Keep the legacy JSON schema at this boundary; runtime palettes never carry layout fields.
module AppearanceJson =
    let private paletteFields : (string * (TabPalette -> Color) * (Color -> TabPalette -> TabPalette)) list = [
        "tabTextColor",(fun p -> p.tabTextColor),(fun v p -> {p with tabTextColor=v})
        "tabNormalBgColor",(fun p -> p.tabNormalBgColor),(fun v p -> {p with tabNormalBgColor=v})
        "tabHighlightBgColor",(fun p -> p.tabHighlightBgColor),(fun v p -> {p with tabHighlightBgColor=v})
        "tabActiveBgColor",(fun p -> p.tabActiveBgColor),(fun v p -> {p with tabActiveBgColor=v})
        "tabBorderColor",(fun p -> p.tabBorderColor),(fun v p -> {p with tabBorderColor=v})
        "tabFlashBgColor",(fun p -> p.tabFlashBgColor),(fun v p -> {p with tabFlashBgColor=v}) ]
    let private geometryFields : (string * (TabGeometry -> int) * (int -> TabGeometry -> TabGeometry)) list = [
        "tabHeight",(fun g -> g.height),(fun v g -> {g with height=v})
        "tabMaxWidth",(fun g -> g.maxWidth),(fun v g -> {g with maxWidth=v})
        "tabOverlap",(fun g -> g.overlap),(fun v g -> {g with overlap=v})
        "tabHeightOffset",(fun g -> g.heightOffset),(fun v g -> {g with heightOffset=v})
        "tabIndentNormal",(fun g -> g.indentNormal),(fun v g -> {g with indentNormal=v}) ]
    let readPalette (json:JObject) fallback =
        paletteFields |> List.fold(fun palette (key,_,set) ->
            try
                match json.[key] with
                | :? JValue as value -> set (Color.FromRGB(Int32.Parse(string value.Value,Globalization.NumberStyles.HexNumber))) palette
                | _ -> palette
            with _ -> palette) fallback
    /// Stored as the legacy negative overlap; the settings page shows it as a positive gap.
    let normalizeOverlap overlap =
        let low,high = SettingsCatalog.range "tabOverlap"
        max -high (min -low overlap)
    let readGeometry (json:JObject) fallback =
        let fallback =
            match json.["tabStyle"] with
            | :? JValue as value when value.Type=JTokenType.String ->
                {fallback with style=SettingsCatalog.normalizeChoice "tabStyle" (string value.Value) |> TabStyle.parse}
            | _ -> fallback
        geometryFields |> List.fold(fun geometry (key,_,set) ->
            try
                match json.[key] with
                | :? JValue as value when value.Type=JTokenType.Integer ->
                    let number = Convert.ToInt32(value.Value)
                    let normalized =
                        match key with
                        | "tabHeightOffset" -> max -120 (min 120 number)
                        | "tabOverlap" -> normalizeOverlap number
                        | _ -> SettingsCatalog.normalizeNumber key number
                    set normalized geometry
                | _ -> geometry
            with _ -> geometry) fallback
    let normalizeGeometry (geometry:TabGeometry) =
        { style=geometry.style
          height=SettingsCatalog.normalizeNumber "tabHeight" geometry.height
          maxWidth=SettingsCatalog.normalizeNumber "tabMaxWidth" geometry.maxWidth
          overlap=normalizeOverlap geometry.overlap
          heightOffset=max -120 (min 120 geometry.heightOffset)
          indentNormal=SettingsCatalog.normalizeNumber "tabIndentNormal" geometry.indentNormal }
    let writePalette palette =
        let json = JObject()
        for key,get,_ in paletteFields do json.setString(key,sprintf "%X" ((get palette).ToRGB()))
        json
    let writeLegacy geometry palette =
        let json = writePalette palette
        for key,get,_ in geometryFields do json.setInt64(key,int64(get geometry))
        json.setString("tabStyle",TabStyle.serialize geometry.style)
        json