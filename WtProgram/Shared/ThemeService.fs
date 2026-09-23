namespace Bemo
open System
open System.Windows.Forms
open Microsoft.Win32

module ThemeService =
    let private changedEvent = Event<unit>()
    let changed = changedEvent.Publish
    let notifyChanged() = changedEvent.Trigger()
    let systemIsDark() =
        try
            use key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
            if isNull key then false
            else
                match key.GetValue("AppsUseLightTheme") with
                | :? int as value -> value = 0
                | _ -> false
        with _ -> false

    // Read the registry on system notifications, never from a control's paint callback.
    let mutable private systemDark = if systemIsDark() then 1 else 0
    let private cachedSystemDark() = System.Threading.Volatile.Read(&systemDark)=1
    let private refreshSystemTheme() =
        System.Threading.Volatile.Write(&systemDark,if systemIsDark() then 1 else 0)

    let currentIsDark() =
        Theme.usesDark Services.settings.appearance.mode (cachedSystemDark())

    let currentAppearance() =
        let settings = Services.settings.appearance
        Theme.resolve settings.mode (cachedSystemDark()) SystemInformation.HighContrast
            settings.useCustomColors settings.geometry settings.lightPalette settings.darkPalette
    let startMonitoring() =
        refreshSystemTheme()
        let handler = UserPreferenceChangedEventHandler(fun _ _ -> refreshSystemTheme(); notifyChanged())
        SystemEvents.UserPreferenceChanged.AddHandler(handler)
        { new IDisposable with
            member _.Dispose() = SystemEvents.UserPreferenceChanged.RemoveHandler(handler) }
