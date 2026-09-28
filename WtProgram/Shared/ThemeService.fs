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

    // Tab groups run on their own UI threads and read the appearance while handling a
    // theme change. Reading it through Services.settings waits on the main thread, which
    // may itself be waiting to deliver that same change to them. Settings publishes every
    // change here from its own thread; other threads read that copy without waiting.
    let mutable private published : (int * AppearancePreferences) option = None
    let publishPreferences (value:AppearancePreferences) =
        System.Threading.Volatile.Write(&published,Some(System.Threading.Thread.CurrentThread.ManagedThreadId,value))
    let private currentPreferences() =
        match System.Threading.Volatile.Read(&published) with
        | Some(owner,value) when owner<>System.Threading.Thread.CurrentThread.ManagedThreadId -> value
        | _ -> Services.settings.appearance

    let currentIsDark() =
        Theme.usesDark (currentPreferences()).mode (cachedSystemDark())

    let currentAppearance() =
        let settings = currentPreferences()
        Theme.resolve settings.mode (cachedSystemDark()) SystemInformation.HighContrast
            settings.useCustomColors settings.geometry settings.lightPalette settings.darkPalette

    /// SystemEvents raises each notification from the thread that owns its hidden window and
    /// waits while every subscribing thread handles it - WinForms controls on the tab group
    /// threads included. That owner is the first STA thread to touch SystemEvents, i.e. the
    /// main thread, so a group thread waiting on the main thread deadlocked a theme change.
    /// Touched first from an MTA thread, SystemEvents runs its own thread. Call before any
    /// control is created.
    let moveSystemEventsOffMainThread() =
        let thread = System.Threading.Thread(fun () -> SystemEvents.InvokeOnEventsThread(Action ignore))
        thread.SetApartmentState(System.Threading.ApartmentState.MTA)
        thread.IsBackground <- true
        thread.Start()
        thread.Join()

    let startMonitoring() =
        refreshSystemTheme()
        let handler = UserPreferenceChangedEventHandler(fun _ _ -> refreshSystemTheme(); notifyChanged())
        SystemEvents.UserPreferenceChanged.AddHandler(handler)
        { new IDisposable with
            member _.Dispose() = SystemEvents.UserPreferenceChanged.RemoveHandler(handler) }
