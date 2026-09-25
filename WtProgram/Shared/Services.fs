namespace Bemo
open System
open System.Drawing
open System.Reflection
open System.Collections.Generic
open System.Runtime.Remoting.Proxies
open System.Runtime.Remoting.Messaging

type ServiceAsyncResult() as this =
    let returnInvoker = InvokerService.invoker
    let mutable cachedResult = None
    let mutable cachedfCompleted = None
    let mutable completed = false

    member this.complete(result) =
        returnInvoker.asyncInvoke <| fun() ->
            cachedResult <- Some(result)
            this.tryToComplete()

    member this.tryToComplete() =
        if completed.not && cachedResult.IsSome && cachedfCompleted.IsSome then
            completed <- true
            cachedfCompleted.Value(cachedResult.Value)

    interface IServiceAsyncResult with
        member x.onCompleted fCompleted =
            returnInvoker.asyncInvoke <| fun() ->
                cachedfCompleted <- Some(fCompleted)
                this.tryToComplete()

type ServiceProxy<'a>(service:'a, dispatcher:IDispatcher) =
    inherit RealProxy(typeof<'a>)
    let attributeCache = new Dictionary<int, ServiceMethodAttribute>()

    
    member private this.returnMessage(msg : IMessage, result : obj) =
        let mcm = msg :?> IMethodCallMessage
        ReturnMessage(result, null, 0, mcm.LogicalCallContext, mcm) :> IMessage

    member private this.methodInfo(msg: IMessage) =
        let mcm = msg :?> IMethodCallMessage
        mcm.MethodBase :?> MethodInfo
    
    member private this.serviceMethodAttributeCached(mi:MethodInfo) =
        let key = mi.MetadataToken
        lock this <| fun() ->
            if attributeCache.ContainsKey(key).not then
                let attributes = List2(mi.GetCustomAttributes(typeof<ServiceMethodAttribute>, true))
                let sma = attributes.map(fun(attr) -> attr.cast<ServiceMethodAttribute>()).tryHead.def(ServiceMethodAttribute())
                attributeCache.Add(key, sma)
            attributeCache.Item(key)

    member private this.serviceMethodAttribute<'s>(msg: IMessage) =
        let mi = this.methodInfo(msg)
        this.serviceMethodAttributeCached mi

    member private this.invokeMethod(msg : IMessage) =
        let mcm = msg :?> IMethodCallMessage
        let mi = this.methodInfo(msg)
        mi.Invoke(service, mcm.InArgs)

    member private this.isUnitReturnType(msg: IMessage) =
        this.methodInfo(msg).ReturnType = typeof<unit>

    member private this.doSyncInvoke(msg: IMessage) =
        dispatcher.Send <| fun() ->
            this.invokeMethod(msg)

    member private this.doAsyncInvoke(msg: IMessage) =  
        let asyncResult = ServiceAsyncResult()

        dispatcher.Post <| fun() ->
            let result = this.invokeMethod(msg)
            asyncResult.complete(result)

        if this.isUnitReturnType(msg) then null else box(asyncResult)

    override this.Invoke(msg) =
        let sma = this.serviceMethodAttribute(msg)
        let result = 
            if sma.async then
                this.doAsyncInvoke(msg)
            else
                this.doSyncInvoke(msg)
        this.returnMessage(msg, result)

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

    member this.register(service:'a, wrap) =
        let service = 
            if wrap then
                let rp = new ServiceProxy<'a>(service, InvokerService.invoker :> IDispatcher)
                rp.GetTransparentProxy()
            else
                box(unbox<'a>(service))
        services.Add(typeof<'a>, service)

    member this.register(service:'a) = this.register(service, true)

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

/// Typed settings boundary: callers no longer rely on reflection/remoting for settings.
type DispatchedSettings(inner:ISettings, dispatcher:IDispatcher) =
    interface ISettings with
        member _.appearance = dispatcher.Send(fun () -> inner.appearance)
        member _.updateAppearance change = dispatcher.Send(fun () -> inner.updateAppearance change)
        member _.getValue key = dispatcher.Send(fun () -> inner.getValue key)
        member _.setValue value = dispatcher.Send(fun () -> inner.setValue value)
        member _.notifyValue key callback = dispatcher.Send(fun () -> inner.notifyValue key callback)
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
