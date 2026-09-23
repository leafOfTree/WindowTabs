namespace Bemo

open System
open System.Drawing
open System.Windows.Forms
open Microsoft.Win32

/// Colour selection is separate from geometry and from the taskbar's theme.
module Theme =
    let private changedEvent = Event<unit>()
    let changed = changedEvent.Publish
    let notifyChanged() = changedEvent.Trigger()

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

    let withColors (colors:TabAppearanceInfo) (geometry:TabAppearanceInfo) =
        { geometry with
            tabTextColor=colors.tabTextColor
            tabNormalBgColor=colors.tabNormalBgColor
            tabHighlightBgColor=colors.tabHighlightBgColor
            tabActiveBgColor=colors.tabActiveBgColor
            tabBorderColor=colors.tabBorderColor
            tabFlashBgColor=colors.tabFlashBgColor }

    let sameColors (a:TabAppearanceInfo) (b:TabAppearanceInfo) =
        let values (c:TabAppearanceInfo) =
            [c.tabTextColor;c.tabNormalBgColor;c.tabHighlightBgColor;c.tabActiveBgColor;c.tabBorderColor;c.tabFlashBgColor]
            |> List.map (fun color -> color.ToArgb())
        values a = values b

    let normalizeMode mode =
        match mode with "light" | "dark" -> mode | _ -> "system"

    let usesDark mode systemDark =
        match normalizeMode mode with
        | "dark" -> true
        | "light" -> false
        | _ -> systemDark

    let systemIsDark() =
        try
            use key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
            if isNull key then false
            else
                match key.GetValue("AppsUseLightTheme") with
                | :? int as value -> value = 0
                | _ -> false
        with _ -> false

    let resolve mode systemDark highContrast custom geometry lightColors darkColors =
        let colors =
            if highContrast then
                { light with
                    tabTextColor=SystemColors.WindowText
                    tabNormalBgColor=SystemColors.Window
                    tabActiveBgColor=SystemColors.Window
                    tabHighlightBgColor=SystemColors.Control
                    tabBorderColor=SystemColors.WindowText
                    tabFlashBgColor=SystemColors.Control }
            elif usesDark mode systemDark then if custom then darkColors else dark
            else if custom then lightColors else light
        withColors colors geometry

    // Read the registry on system notifications, never from a control's paint callback.
    let mutable private systemDark = if systemIsDark() then 1 else 0
    let private cachedSystemDark() = System.Threading.Volatile.Read(&systemDark)=1
    let private refreshSystemTheme() =
        System.Threading.Volatile.Write(&systemDark,if systemIsDark() then 1 else 0)

    let currentIsDark() =
        usesDark (Services.settings.getValue("tabThemeMode") :?> string) (cachedSystemDark())

    let currentAppearance() =
        let get key = Services.settings.getValue(key)
        resolve (get "tabThemeMode" :?> string) (cachedSystemDark()) SystemInformation.HighContrast
            (get "tabUseCustomColors" :?> bool) (get "tabAppearance" :?> TabAppearanceInfo)
            (get "tabLightColors" :?> TabAppearanceInfo) (get "tabDarkColors" :?> TabAppearanceInfo)

    let startMonitoring() =
        refreshSystemTheme()
        let handler = UserPreferenceChangedEventHandler(fun _ _ -> refreshSystemTheme(); notifyChanged())
        SystemEvents.UserPreferenceChanged.AddHandler(handler)
        { new IDisposable with
            member _.Dispose() = SystemEvents.UserPreferenceChanged.RemoveHandler(handler) }

    /// All UI subscriptions have the same lifetime as their owning control.
    let watch (control:Control) action =
        let refresh() =
            if not control.IsDisposed && control.IsHandleCreated then
                try
                    control.BeginInvoke(Action(fun () ->
                        if not control.IsDisposed then action())) |> ignore
                with :? InvalidOperationException -> ()
        let subscription = changed.Subscribe(fun () -> refresh())
        control.HandleCreated.Add(fun _ -> action())
        control.Disposed.Add(fun _ -> subscription.Dispose())