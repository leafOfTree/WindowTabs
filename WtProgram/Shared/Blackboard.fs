namespace Bemo
open System

type BlackboardItem(key:string) =
    let valueRef = ref None
    let subscriptionsRef = ref (Map2())

    member this.value 
        with get() : Option<obj> = valueRef.Value
        and set(newValue) = 
            valueRef := newValue
            subscriptionsRef.Value.items.iter <| fun (_,fNotify) -> fNotify()

    member this.subscribe(fNotify) =
        let id = Guid.NewGuid()
        subscriptionsRef := subscriptionsRef.Value.add id fNotify
        { new IDisposable with
            member this.Dispose() = 
                subscriptionsRef := subscriptionsRef.Value.remove id
        }

type Blackboard()=
    let itemsRef = ref (Map2())
    member private this.item(key) =
        match itemsRef.Value.tryFind(key) with
        | Some(item) -> item
        | None ->
            let item = BlackboardItem(key)
            itemsRef := itemsRef.Value.add key item
            item
    member this.write(key, value) = this.item(key).value <- Some(box(value))
    member this.read(key, def:'a) = unbox<'a>(this.item(key).value.def(def))
    member this.disposableSubscribe key fNotify = this.item(key).subscribe(fNotify)
    member this.subscribe key fNotify = this.disposableSubscribe key fNotify |> ignore