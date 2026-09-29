namespace Bemo
open System
open System.Drawing
open System.Reflection
open System.Collections.Generic

type ServiceProvider() =
    [<DefaultValue>]
    [<ThreadStatic>]
    static val mutable private _localServices : Dictionary<Type, obj>

    let services = new Dictionary<Type, obj>()
    
    static member localServices
        with get() =
            if ServiceProvider._localServices = null then
                ServiceProvider._localServices <- new Dictionary<Type, obj>()
            ServiceProvider._localServices

    /// Services that other threads call are registered as Dispatched* wrappers, which
    /// marshal each member onto the owning thread explicitly.
    member this.register(service:'a) = services.Add(typeof<'a>, box service)

    member this.registerLocal(service:'a) = 
        ServiceProvider.localServices.Add(typeof<'a>, service)

    member this.get<'a>() =
        let t = typeof<'a>
        let service = 
            if ServiceProvider.localServices.ContainsKey(t) then
                ServiceProvider.localServices.Item(t)
            else
                services.Item(t)
        unbox<'a>(service)

    member this.has<'a>() =
        let t = typeof<'a>
        ServiceProvider.localServices.ContainsKey(t) || services.ContainsKey(t)

/// Typed service boundaries: each member is marshalled to the owning thread explicitly.
type DispatchedSettings(inner:ISettings, dispatcher:IDispatcher, ?published:Collections.Concurrent.ConcurrentDictionary<string,obj>) =
    interface ISettings with
        member _.appearance = dispatcher.Send(fun () -> inner.appearance)
        member _.updateAppearance change = dispatcher.Send(fun () -> inner.updateAppearance change)
        // Tab strips read settings on mouse moves. Off the owner thread a value is fetched once
        // and kept in published, which the owner clears on every write; storing it inside the
        // Send orders the store with those clears, so a stale value is never kept.
        member _.getValue key =
            match published with
            | Some cache when not dispatcher.CheckAccess ->
                match cache.TryGetValue key with
                | true,value -> value
                | _ -> dispatcher.Send(fun () -> let value = inner.getValue key in cache.[key] <- value; value)
            | _ -> dispatcher.Send(fun () -> inner.getValue key)
        member _.setValue value = dispatcher.Send(fun () -> inner.setValue value)
        member _.notifyValue key callback = dispatcher.Send(fun () -> inner.notifyValue key callback)
        member _.hotKey key = dispatcher.Send(fun () -> inner.hotKey key)
        member _.setHotKey key value = dispatcher.Send(fun () -> inner.setHotKey key value)
        member _.path = inner.path
        member _.root
            with get() = dispatcher.Send(fun () -> inner.root)
            and set value = dispatcher.Send(fun () -> inner.root <- value)

type DispatchedDesktop(inner:IDesktop, dispatcher:IDispatcher) =
    interface IDesktop with
        member _.isDragging = dispatcher.Send(fun () -> inner.isDragging)
        member _.isEmpty = dispatcher.Send(fun () -> inner.isEmpty)
        member _.createGroup enabled = dispatcher.Send(fun () -> inner.createGroup enabled)
        member _.restartGroup(hwnd, enabled) = dispatcher.Post(fun () -> inner.restartGroup(hwnd, enabled))
        member _.groups = dispatcher.Send(fun () -> inner.groups)
        member _.groupExited = dispatcher.Send(fun () -> inner.groupExited)
        member _.groupRemoved = dispatcher.Send(fun () -> inner.groupRemoved)
        member _.foregroundGroup = dispatcher.Send(fun () -> inner.foregroundGroup)

type DispatchedProgram(inner:IProgram, dispatcher:IDispatcher) =
    interface IProgram with
        member _.version = dispatcher.Send(fun () -> inner.version)
        member _.isUpgrade = dispatcher.Send(fun () -> inner.isUpgrade)
        member _.isFirstRun = dispatcher.Send(fun () -> inner.isFirstRun)
        // Fire-and-forget: callers must not wait on a refresh or on shutdown.
        member _.refresh() = dispatcher.Post(fun () -> inner.refresh())
        member _.shutdown() = dispatcher.Post(fun () -> inner.shutdown())
        member _.setWindowNameOverride value = dispatcher.Send(fun () -> inner.setWindowNameOverride value)
        // Reads an immutable snapshot; called by tab strips on every title change.
        member _.getWindowNameOverride hwnd = inner.getWindowNameOverride hwnd
        member _.appWindows = dispatcher.Send(fun () -> inner.appWindows)
        member _.getAutoGroupingEnabled path = dispatcher.Send(fun () -> inner.getAutoGroupingEnabled path)
        member _.setAutoGroupingEnabled path enabled = dispatcher.Send(fun () -> inner.setAutoGroupingEnabled path enabled)
        member _.tabAppearanceInfo = dispatcher.Send(fun () -> inner.tabAppearanceInfo)
        member _.setHotKey key value = dispatcher.Send(fun () -> inner.setHotKey key value)
        member _.getHotKey key = dispatcher.Send(fun () -> inner.getHotKey key)
        member _.suspendTabMonitoring() = dispatcher.Send(fun () -> inner.suspendTabMonitoring())
        member _.resumeTabMonitoring() = dispatcher.Send(fun () -> inner.resumeTabMonitoring())
        member _.llMouse = dispatcher.Send(fun () -> inner.llMouse)

type DispatchedFilterService(inner:IFilterService, dispatcher:IDispatcher) =
    interface IFilterService with
        member _.isAppWindow hwnd = dispatcher.Send(fun () -> inner.isAppWindow hwnd)
        member _.isAppWindowStyle hwnd = dispatcher.Send(fun () -> inner.isAppWindowStyle hwnd)
        member _.isTabbableWindow hwnd = dispatcher.Send(fun () -> inner.isTabbableWindow hwnd)
        member _.isTabbingEnabledForAllProcessesByDefault
            with get() = dispatcher.Send(fun () -> inner.isTabbingEnabledForAllProcessesByDefault)
            and set value = dispatcher.Send(fun () -> inner.isTabbingEnabledForAllProcessesByDefault <- value)
        member _.setIsTabbingEnabledForProcess path enabled = dispatcher.Send(fun () -> inner.setIsTabbingEnabledForProcess path enabled)
        member _.getIsTabbingEnabledForProcess path = dispatcher.Send(fun () -> inner.getIsTabbingEnabledForProcess path)

type DispatchedManagerView(inner:IManagerView, dispatcher:IDispatcher) =
    interface IManagerView with
        member _.show() = dispatcher.Send(fun () -> inner.show())
        member _.show(view:SettingsViewType) = dispatcher.Send(fun () -> inner.show(view))

type WtServiceProvider() =
    inherit ServiceProvider()
    member this.program = this.get<IProgram>()
    member this.desktop = this.get<IDesktop>()
    member this.managerView = this.get<IManagerView>()
    member this.filter = this.get<IFilterService>()
    member this.settings = this.get<ISettings>()
    member this.dragDrop = this.get<IDragDrop>()
    member this.openResource(name) = typeof<WtServiceProvider>.Assembly.GetManifestResourceStream(name)
    member this.openIcon(name) = new Icon(this.openResource(name))
    member this.openImage(name) = System.Drawing.Image.FromStream(this.openResource(name))

[<AutoOpen>]
module GS =
    let Services = WtServiceProvider()
