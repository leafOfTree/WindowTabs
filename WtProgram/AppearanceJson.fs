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
        "tabIndentNormal",(fun g -> g.indentNormal),(fun v g -> {g with indentNormal=v})
        "tabIndentFlipped",(fun g -> g.indentFlipped),(fun v g -> {g with indentFlipped=v}) ]
    let readPalette (json:JObject) fallback =
        paletteFields |> List.fold(fun palette (key,_,set) ->
            try
                match json.[key] with
                | :? JValue as value -> set (Color.FromRGB(Int32.Parse(string value.Value,Globalization.NumberStyles.HexNumber))) palette
                | _ -> palette
            with _ -> palette) fallback
    let readGeometry (json:JObject) fallback =
        geometryFields |> List.fold(fun geometry (key,_,set) ->
            try
                match json.[key] with
                | :? JValue as value when not(isNull value.Value) -> set (Convert.ToInt32(value.Value)) geometry
                | _ -> geometry
            with _ -> geometry) fallback
    let writePalette palette =
        let json = JObject()
        for key,get,_ in paletteFields do json.setString(key,sprintf "%X" ((get palette).ToRGB()))
        json
    let writeLegacy geometry palette =
        let json = writePalette palette
        for key,get,_ in geometryFields do json.setInt64(key,int64(get geometry))
        json