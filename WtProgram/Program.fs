//namespace Bemo
open Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Diagnostics
open System.IO
open System.Text
open System.Reflection
open System.Runtime.InteropServices
open System.Threading
open System.Windows.Forms
open Microsoft.FSharp.Reflection
open Bemo.Win32
//open Bemo.Licensing
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open Microsoft.Win32

type ProgramInput =
    | WinEvent of (IntPtr * WinEvent)
    | ShellEvent of (IntPtr * ShellEvent)
    | Timer

type ProgramVersion(parts:List2<int>)=
    new(versionString:string) =
        ProgramVersion(List2(versionString.Split([|'.'|])).map(Int32.Parse))

    member this.parts = parts

    member this.compare(v2:ProgramVersion) =
        let maxLen = max this.parts.length v2.parts.length
        let zeroPad (parts:List2<_>) =
            if parts.length < maxLen then parts.appendList(List2(Seq.init (maxLen - parts.length) (fun _ -> 0)))
            else parts
        let v1 = zeroPad this.parts
        let v2 = zeroPad v2.parts
        v1.zip(v2).tryPick(fun(v1,v2) -> 
            if v1 > v2 then Some(1)
            elif v2 > v1 then Some(-1)
            else None).def(0)

    member this.isNewerThan(v2:ProgramVersion) = 
        this.compare(v2) > 0

type Program() as this =
    let version = "2023.10.15"
    let isStandAlone = System.Diagnostics.Debugger.IsAttached 

    let mutex = new Mutex(false, "BemoSoftware.WindowTabs")
    let Cell = CellScope()
    let os = OS()
    let invoker = InvokerService.invoker
    let taskSwitchCell = Cell.create(None)
    let isTabMonitoringSuspendedCell = Cell.create(false)
    let llMouseEvent = Event<_>()

    // case 727 outlook calendar items appear behind outlook main window
    let delayTabExeNames = Set2(List2(["outlook.exe"]))

    let settingsManager = Settings(isStandAlone)

    let keepAliveCell = Cell.create(List2())
    let keepAlive (obj:obj) =
        keepAliveCell.map(fun l -> l.append(obj))
    let lastPing = Cell.create(DateTime.MinValue)
    let notifiedOfUpgrade = Cell.create(false)
    let inShutdown = Cell.create(false)
    let isSubscribed = Cell.create(Map2<IntPtr,IDisposable>())
    let isDroppedAndAwaitingGrouping = Cell.create(Set2())
    let windowNameOverride = Cell.create(Map2())
    let notifyNewVersionEvt = Event<_>()
    let launcher = Launcher()
   
    let isFirstRun = settingsManager.fileExists.not

    let originalVersion = 
        let original = settingsManager.settings.version
        settingsManager.update <| fun s -> { s with version = version }
        original 

    let registerShellHooks =
        os.registerShellHooks <| fun (hwnd, shellEvent) ->
            match shellEvent with
            | ShellEvent.HSHELL_WINDOWCREATED -> this.receive(ShellEvent(hwnd, shellEvent))
            | ShellEvent.HSHELL_WINDOWDESTROYED -> this.receive(ShellEvent(hwnd, shellEvent))
            | _ -> ()


    let hotKeyInfo = Map2(List2([
        ("prevTab", (3621, fun (g:IGroup) -> g.switchWindow(false, false)))
        ("nextTab", (3623, fun g -> g.switchWindow(true, false)))
        ]))
        
    let hotKeyManager = HotKeyManager()

    do
        Desktop(this :> IDesktopNotification).ignore
        this.registerHotKeys()
        this.updateTaskSwitcher(Services.settings.getValue("replaceAltTab"))
        Services.settings.notifyValue "runAtStartup" this.updateRunAtStartup
        Services.settings.notifyValue "replaceAltTab" this.updateTaskSwitcher
        Services.desktop.groupExited.Add <| fun _ -> invoker.asyncInvoke(fun() -> this.updateAppWindows())
        Services.desktop.groupRemoved.Add <| fun _ -> invoker.asyncInvoke(fun() -> this.updateAppWindows())
    
    member this.desktop = Services.desktop
    member this.isTabMonitoringSuspended
        with get() = isTabMonitoringSuspendedCell.value
        and set(value) = isTabMonitoringSuspendedCell.set(value)

    member this.updateRunAtStartup(value)=
        let runAtStartup = value.cast<bool>()
        let key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)
        let keyName = "WindowTabs"
        if runAtStartup then
            let entryAssembly = System.Reflection.Assembly.GetEntryAssembly()
            let exeUri = Uri(entryAssembly.CodeBase)
            key.SetValue(keyName, sprintf "\"%s\"" exeUri.LocalPath)
        else
            key.DeleteValue(keyName, false)

    member this.isAppWindow(window:Window) =
        Services.filter.isAppWindow(window.hwnd)

    member this.isTabbableWindow(window:Window) = 
        Services.filter.isTabbableWindow(window.hwnd)

    member this.isAppWindowStyle(window:Window) =
        Services.filter.isAppWindowStyle(window.hwnd)

    member this.tryDropped(window:Window) =
        if isDroppedAndAwaitingGrouping.value.contains(window.hwnd) then Some(None) else None

    member this.tryAutoGroup(window:Window) =
        if (this :> IProgram).getAutoGroupingEnabled(window.pid.processPath) then
            let hwndZorders = this.hwndZorders()
            let groups = this.desktop.groups
            let groups = 
                match this.cast<IProgram>().tabLimit with
                | Some(tabLimit) -> groups.where(fun g -> g.windows.count < tabLimit)
                | None -> groups
            let groups = groups.where(fun g-> g.windows.count > 0).sortBy(fun g -> g.windows.map(fun hwnd -> hwndZorders.tryFind(hwnd).def(Int32.MaxValue)).minBy(id))
            let group = groups.tryFind(fun g -> g.windows.map(fun hwnd -> os.windowFromHwnd(hwnd).pid.processPath).contains((=) window.pid.processPath))
            Some(group)
        else None

    member this.updateAppWindows() =
        if this.desktop.isDragging.not then
            if inShutdown.value.not then
                os.windowsInZorder.iter <| fun window ->
                    this.ensureWindowIsSubscribed(window)
                    if this.isTabMonitoringSuspended.not then
                        this.ensureWindowIsGrouped(window)
            this.destroyEmptyGroups()
            this.removeUntabableWindows()

        this.exitIfNeeded()

    member this.ensureWindowIsSubscribed(window:Window) =
        let hwnd = window.hwnd
        if  isSubscribed.value.contains(hwnd).not &&
            window.pid.isCurrentProcess.not &&
            this.isAppWindowStyle(window)
            then
            let registerEvent evt =
                window.setWinEventHook evt (fun() -> this.receive(WinEvent(hwnd, evt)))
            let hooks = List2([WinEvent.EVENT_OBJECT_SHOW;WinEvent.EVENT_OBJECT_HIDE]).map(registerEvent)
            let dispose = {
                new IDisposable with
                    member this.Dispose() =
                        hooks.iter(fun h -> h.Dispose())
                }
            isSubscribed.map(fun s -> s.add hwnd dispose)

    member this.ensureWindowIsGrouped(window) =
        if this.isTabbableWindow(window) && this.isInGroup(window.hwnd).not then
            this.addWindowToGroup(window)

    member this.destroyEmptyGroups() =
        this.desktop.groups.iter <| fun gi ->
        if gi.windows.isEmpty && launcher.isLaunching(gi).not then
            gi.destroy()

    member this.removeUntabableWindows() =
        this.desktop.groups.iter <| fun gi ->
            gi.windows.iter <| fun hwnd ->
                if this.isTabbableWindow(os.windowFromHwnd(hwnd)).not then gi.removeWindow hwnd

    member this.findGroupForWindow(window:Window) =
        let handlers = List2([
            this.tryDropped
            launcher.findGroup
            this.tryAutoGroup
            ])
        handlers.tryPick(fun f -> f(window)).def(None)

    member this.addWindowToGroup(window:Window) =
        let hwnd = window.hwnd
        let group,isNewGroup = 
            match this.findGroupForWindow(window) with
            | Some(group) -> (group, false)
            | None -> (Services.desktop.createGroup(Services.settings.getValue("combineIconsInTaskbar").cast<bool>()), true)
        let isDropped = isDroppedAndAwaitingGrouping.value.contains(hwnd)
        //need to add this now so we don't end up creating another group for it while waiting for the WgnWindowAdded notification
        isDroppedAndAwaitingGrouping.map(fun s -> s.remove hwnd)
        let withDelay = not isDropped && isNewGroup && delayTabExeNames.contains(window.pid.exeName)
        group.addWindow(hwnd, withDelay)

    member this.receive message =
        match message with
        | WinEvent(hwnd, evt) -> ()
        | ShellEvent(hwnd, evt) ->
            match evt with
            | ShellEvent.HSHELL_WINDOWDESTROYED ->
                isSubscribed.value.tryFind(hwnd).iter <| fun dispose -> dispose.Dispose()
            | _ ->()
        | Timer -> ()
                  
        this.updateAppWindows()

    member this.exitIfNeeded() =
        if inShutdown.value then
            if this.desktop.isEmpty then Application.ExitThread()

    member this.saveSettingsAndUpdateAppWindows(f) =
        settingsManager.update f
        this.updateAppWindows()

    member this.updateTaskSwitcher(value) =
        let replaceAltTab = value.cast<bool>()
        if replaceAltTab then
            if taskSwitchCell.value.IsNone then
                let tsDesktop = {
                    new ITaskSwitchDesktop with
                        member x.groups = this.desktop.groups.map <| fun(gi) ->
                            { new ITaskSwitchGroup with
                                member y.hwnd = gi.hwnd
                                member y.windows = Set2(gi.windows)
                            }
                }
                taskSwitchCell.set(Some(TaskSwitcher(settingsManager, tsDesktop)))
        else
            taskSwitchCell.value.iter <| fun s -> (s :> IDisposable).Dispose()
            taskSwitchCell.set(None)
        

    //needed to keep hook alive
    member this.keepAliveReference = keepAliveCell.value

    member this.foregroundGroup = this.desktop.foregroundGroup

    member this.registerHotKeys() =
        hotKeyInfo.items.iter <| fun(key,(_,f)) ->
            let f() =
                this.foregroundGroup.iter <| fun group -> 
                    f(group)
            let shortcut = this.cast<IProgram>().getHotKey(key)
            let shortcut = HotKeyShortcut(HotKeyControlCode=int16(shortcut))
            hotKeyManager.register key (shortcut.RegisterHotKeyModifierFlags, shortcut.RegisterHotKeyVirtualKeyCode) f |> ignore

   
    member this.hwndZorders() : Map2<IntPtr, int>= Map2(os.windowsInZorder.enumerate.map(fun(i,w) -> w.hwnd,i))
    
    member this.isInGroup hwnd : bool =
        this.desktop.groups.any(fun group -> group.windows.contains((=)hwnd))

    member this.notifyNewVersion = notifyNewVersionEvt.Publish
    
    member this.refresh() = this.receive(Timer)

    interface IProgram with
        member x.version = version
        member x.isUpgrade = version <> originalVersion
        member x.isFirstRun = isFirstRun
        member x.refresh() = this.refresh()
        member x.suspendTabMonitoring() = 
            this.isTabMonitoringSuspended <- true

        member x.resumeTabMonitoring() = 
            this.isTabMonitoringSuspended <- false
            this.refresh()

        member x.shutdown() =
            inShutdown.set(true)
            this.desktop.groups.iter <| fun gi ->
                gi.windows.iter <| fun window ->
                    gi.removeWindow window
            this.updateAppWindows()
                   
        member x.tabLimit = None
     
        member x.setWindowNameOverride((hwnd, name)) = 
            windowNameOverride.set(windowNameOverride.value.add hwnd name)

        member x.getWindowNameOverride(hwnd) =
            windowNameOverride.value.tryFind(hwnd).bind(id)

        member x.appWindows = 
            os.windowsInZorder.where(this.isAppWindow).map(fun w -> w.hwnd)

        member x.getAutoGroupingEnabled procPath =
            settingsManager.settings.autoGroupingPaths.contains(procPath)

        member x.setAutoGroupingEnabled procPath enabled =
            if enabled then 
                this.saveSettingsAndUpdateAppWindows <| fun s -> { s with autoGroupingPaths = s.autoGroupingPaths.add procPath }                        
               //toggle tabbing for the process to force regrouping
                Services.filter.setIsTabbingEnabledForProcess procPath false
                this.refresh()
                Services.filter.setIsTabbingEnabledForProcess procPath true
                this.refresh()
            else
                this.saveSettingsAndUpdateAppWindows <| fun s -> { s with autoGroupingPaths = s.autoGroupingPaths.remove procPath }
  
        member x.tabAppearanceInfo = 
            settingsManager.settings.tabAppearance

        member x.defaultTabAppearanceInfo = settingsManager.defaultTabAppearance
            
        member x.getHotKey key = 
            let hotKeys = settingsManager.settingsJson.getObject("hotKeys").def(JObject())
            match hotKeys.getInt32(key) with
            | Some(value) -> value
            | None -> 
                let shortcut, _ = hotKeyInfo.find(key)
                int(shortcut)

        member x.setHotKey key value = 
            let settings = settingsManager.settingsJson
            let hotKeys = settings.getObject("hotKeys").def(JObject())
            hotKeys.setInt32(key, value)
            settings.setObject("hotKeys", hotKeys)
            settingsManager.settingsJson <- settings
            this.registerHotKeys()

        member x.ping() = 
            ()

        member x.notifyNewVersion() = notifyNewVersionEvt.Trigger()
        member x.newVersion = notifyNewVersionEvt.Publish
        member x.llMouse = llMouseEvent.Publish

    interface IDesktopNotification with
        member x.dragDrop(hwnd) =
            isDroppedAndAwaitingGrouping.map <| fun s -> s.add hwnd

        member x.dragEnd() = 
            this.updateAppWindows()
            

    member this.run(plugins:List2<IPlugin>) =  
        
        if System.Diagnostics.Debugger.IsAttached.not then
            if mutex.WaitOne(TimeSpan.FromSeconds(0.5), false).not then
                MessageBox.Show("Another instance of WindowTabs is running, please close it before running this instance.", "WindowTabs is already running.").ignore
                exit(0)

        Application.EnableVisualStyles()

        Services.register(this :> IProgram)
        Services.register(FilterService() :> IFilterService)
        Services.register(ManagerViewService() :> IManagerView)
        Services.program.refresh()

        plugins.iter <| fun p -> p.init()

        Application.Run()

        plugins.iter <| fun p ->
            match p with
            | :? IDisposable as d -> d.Dispose()
            | _ -> ()

let program = Program()
program.run(List2<obj>([
    InputManagerPlugin(Set2(List2([WindowMessages.WM_MOUSEWHEEL])))
    NotifyIconPlugin()
    ExceptionHandlerPlugin()
]).map(fun o -> o.cast<IPlugin>()))
