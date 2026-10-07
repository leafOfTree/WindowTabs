namespace Bemo
open System

type FilterService() as this =
    let os = OS()
    let blackListedExeNames = 
        let names = Set2(List2(["taskmgr.exe"]))
        if System.Diagnostics.Debugger.IsAttached then names.add("devenv.exe") else names

    member this.includedPaths 
        with get() = Services.settings.getValue("includedPaths").cast<Set2<string>>()
        and set(value) = Services.settings.setValue("includedPaths", box(value))

     member this.excludedPaths 
        with get() = Services.settings.getValue("excludedPaths").cast<Set2<string>>()
        and set(value) = Services.settings.setValue("excludedPaths", box(value))
           
    member this.isTabbingEnabledForAllProcessesByDefault 
        with get() = Services.settings.getValue("enableTabbingByDefault").cast<bool>()
        and set(value) = 
            Services.settings.setValue("enableTabbingByDefault", box(value))
            Services.program.refresh().ignore

    member this.isBanned (window:Window) =
        blackListedExeNames.contains(window.pid.exeName)

    member this.isAppWindowStyle (window:Window) = 
        let style = IntPtr( 
            WindowsStyles.WS_OVERLAPPEDWINDOW &&&
            ~~~WindowsStyles.WS_CAPTION &&&
            ~~~WindowsStyles.WS_THICKFRAME &&&
            ~~~WindowsStyles.WS_SYSMENU
        )
        Win32Helper.IntPtrAnd(window.styleEx, IntPtr(WindowsExtendedStyles.WS_EX_TOOLWINDOW)) = IntPtr.Zero &&
        Win32Helper.IntPtrAnd(window.style, style) = style
    
    member this.isAppWindow (window:Window) =
        let tests = List2([
            fun() -> window.pid.canQueryProcess
            fun() -> this.isAppWindowStyle(window)
            fun() -> window.pid.isCurrentProcess.not
            fun() -> window.isWindow
            fun() -> window.isVisible
            fun() -> window.isTopMost.not
            fun() -> this.isValidOwner(window)
            //Win32 Dialogue class
            fun() -> window.className <> "#32770"
            fun() -> (window.className <> "ApplicationFrameWindow") || not(String.IsNullOrEmpty window.text)
            fun() -> this.isBanned(window).not
            fun() -> this.isOnScreenOrMinimized(window)
            // Background UWP apps keep a "visible" but cloaked frame window.
            fun() -> not (WindowCloaking.IsHiddenOnCurrentDesktop window.hwnd)
            ])
        tests.all(fun pred -> pred()) 

    member this.isValidOwner (window:Window) =
        let owner = window.parent
        if owner.hwnd = IntPtr.Zero then true
        // Delphi and MFC have owner windows w/ zero size.
        else owner.bounds.width = 0

    member this.screenRegion = os.screenRegion

    member this.isOnScreenOrMinimized(window:Window) =
        if window.isMinimized then true
        else
            use region = this.screenRegion
            region.containsRect(window.bounds)
    
    /// An app turned on or off in the app rules keeps that choice; the default is for the rest.
    member this.getIsTabbingEnabledForProcess(processPath) =
        match this.includedPaths.contains processPath, this.excludedPaths.contains processPath with
        // Each list once applied only under its own default, so an app could be in both: the
        // list of the current default wins, as it did then.
        | true, true -> this.isTabbingEnabledForAllProcessesByDefault.not
        | true, false -> true
        | false, true -> false
        | false, false -> this.isTabbingEnabledForAllProcessesByDefault

    /// Records the choice for each app, then applies it to open windows once.
    member this.setIsTabbingEnabledForProcesses (processPaths:string list) enabled =
        let add (paths:Set2<string>) = processPaths |> List.fold (fun (paths:Set2<string>) path -> paths.add path) paths
        let remove (paths:Set2<string>) = processPaths |> List.fold (fun (paths:Set2<string>) path -> paths.remove path) paths
        if enabled then
            this.includedPaths <- add this.includedPaths
            this.excludedPaths <- remove this.excludedPaths
        else
            this.excludedPaths <- add this.excludedPaths
            this.includedPaths <- remove this.includedPaths
        Services.program.refresh().ignore

    member this.isTabbableWindow(window:Window) = 
        this.getIsTabbingEnabledForProcess(window.pid.processPath) && this.isAppWindow(window)

    interface IFilterService with
        
        member x.isAppWindow(hwnd) =
            let window = os.windowFromHwnd(hwnd)
            this.isAppWindow(window)

        member x.isAppWindowStyle(hwnd) =
            let window = os.windowFromHwnd(hwnd)
            this.isAppWindowStyle(window)

        member x.isTabbableWindow(hwnd) =
            let window = os.windowFromHwnd(hwnd)
            this.isTabbableWindow(window)

        member x.isTabbingEnabledForAllProcessesByDefault
            with get() = this.isTabbingEnabledForAllProcessesByDefault
            and set(value) = this.isTabbingEnabledForAllProcessesByDefault <- value

        member x.setIsTabbingEnabledForProcess processPath enabled =
            this.setIsTabbingEnabledForProcesses [processPath] enabled

        member x.setIsTabbingEnabledForProcesses processPaths enabled =
            this.setIsTabbingEnabledForProcesses processPaths enabled

        member x.getIsTabbingEnabledForProcess(processPath) = 
            this.getIsTabbingEnabledForProcess(processPath)

