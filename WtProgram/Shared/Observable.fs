namespace Bemo
open System
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.Reflection


type ObservableListener(handler:obj->unit) as this =
    member this.fire(arg) = handler(arg)

type ObservableEvent(name:string) as this =
    let listeners = System.Collections.Generic.List<ObservableListener>()

    member this.listen(handler:obj->unit) =
        let listener = ObservableListener(handler)
        listeners.Add(listener)

    member this.fire(arg) =
        listeners.list.iter <| fun listener ->
            listener.fire(arg)

type Observable() as this =
    let events = System.Collections.Generic.Dictionary<string, ObservableEvent>()

    member this.event(eventName: string) =
        if events.ContainsKey(eventName) then
            events.Item(eventName)
        else
            let event = ObservableEvent(eventName)
            events.Item(eventName) <- event
            event

    member this.on(eventName:string, handler:obj->unit) =
        let event = this.event(eventName)
        event.listen(handler)
    
    member this.fireEvent(eventName:string, arg:obj) =
        let event = this.event(eventName)
        event.fire(arg)

    static member init(obj:Dynamic) =
        let observable = Observable()
        obj?on <- observable.on
        obj?fireEvent <- observable.fireEvent



    