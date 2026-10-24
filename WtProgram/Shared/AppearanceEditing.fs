namespace Bemo
open System
open System.Drawing

/// Editing the tab colours of the light or dark look, as the Appearance page and the command
/// palette both do: a change to a preset's colours is kept as that preset's edit, and a change
/// to the user's own colours makes them the custom palette. Pure functions of the preferences.
module PaletteEditing =
    let paletteForProfile dark (s:AppearancePreferences) = if dark then s.darkPalette else s.lightPalette
    let customForProfile dark (s:AppearancePreferences) = if dark then s.darkCustomPalette else s.lightCustomPalette
    let activePalette dark (s:AppearancePreferences) =
        if s.useCustomColors then paletteForProfile dark s
        elif dark then Theme.darkPalette else Theme.lightPalette
    let setActive dark palette (s:AppearancePreferences) =
        if dark then {s with darkPalette=palette;useCustomColors=true}
        else {s with lightPalette=palette;useCustomColors=true}
    let setCustom dark palette (s:AppearancePreferences) =
        if dark then {s with darkCustomPalette=palette} else {s with lightCustomPalette=palette}
    let values (p:TabPalette) =
        [p.tabTextColor;p.tabActiveBgColor;p.tabHighlightBgColor;p.tabNormalBgColor;p.tabBorderColor;p.tabFlashBgColor]
        |> List.map(fun c -> c.ToArgb())
    let original dark index = (ThemePresets.palettes dark).[index]
    let editKey dark index = (if dark then "dark:" else "light:")+ThemePresets.keys.[index]
    /// A preset as the user left it: its own colours with their edits.
    let presetPalette dark (s:AppearancePreferences) index = s.presetEdits.TryFind(editKey dark index) |> Option.defaultValue (original dark index)
    let setPreset dark key (s:AppearancePreferences) = if dark then {s with darkPreset=key} else {s with lightPreset=key}
    /// The chosen entry: Some preset, or None for Custom. Settings from before the choice was
    /// stored are matched by their colours.
    let selection dark (s:AppearancePreferences) =
        match if dark then s.darkPreset else s.lightPreset with
        | key when key=ThemePresets.customKey -> None
        | key when Array.contains key ThemePresets.keys -> Some(Array.findIndex ((=) key) ThemePresets.keys)
        | _ -> ThemePresets.palettes dark |> Array.tryFindIndex(fun candidate -> values candidate=values (activePalette dark s))
    /// A colour edit changes the chosen entry: the preset keeps it as its own edit, or Custom
    /// takes it. An edit back to a preset's own colours is no longer an edit.
    let edit dark change (s:AppearancePreferences) =
        let current = activePalette dark s
        let next = change current |> Theme.followSeparator (selection dark s |> Option.map (original dark)) current
        if values next=values current then s
        else
            match selection dark s with
            | None -> s |> setActive dark next |> setCustom dark next |> setPreset dark ThemePresets.customKey
            | Some index ->
                let edits = if values next=values (original dark index) then s.presetEdits.Remove(editKey dark index) else s.presetEdits.Add(editKey dark index,next)
                {(setActive dark next s) with presetEdits=edits} |> setPreset dark ThemePresets.keys.[index]
    /// The colours a setting names, read and written on a palette. The separator follows the text
    /// and inactive tab colours, so it has no setting of its own.
    let colorFields : (string * (TabPalette -> Color) * (Color -> TabPalette -> TabPalette)) list = [
        "tabTextColor",(fun p -> p.tabTextColor),(fun v p -> {p with tabTextColor=v})
        "tabActiveBgColor",(fun p -> p.tabActiveBgColor),(fun v p -> {p with tabActiveBgColor=v})
        "tabHighlightBgColor",(fun p -> p.tabHighlightBgColor),(fun v p -> {p with tabHighlightBgColor=v})
        "tabNormalBgColor",(fun p -> p.tabNormalBgColor),(fun v p -> {p with tabNormalBgColor=v})
        // A tab whose window flashes for attention, as its taskbar button does.
        "tabFlashBgColor",(fun p -> p.tabFlashBgColor),(fun v p -> {p with tabFlashBgColor=v}) ]

/// A colour remembered for an app, in `appTabColors` by its program's path: the tab menu and the
/// palette both read and change it here.
module AppTabColors =
    let private colors() = Services.settings.getValue("appTabColors") :?> Map<string,string>
    let remembered path = colors() |> Map.exists(fun saved _ -> AppIdentity.same saved path)
    /// Remembers a colour for the app, or with None forgets it.
    let save path (color:TabColorChoice option) =
        let others = colors() |> Map.filter(fun saved _ -> not (AppIdentity.same saved path))
        let next = match color with Some value -> others.Add(path,Theme.formatTabColor value) | None -> others
        Services.settings.setValue("appTabColors",box next)
    /// A colour chosen for one of the app's tabs: the app's own too, while it remembers one.
    let chosen path color = if remembered path then save path (Some color)
