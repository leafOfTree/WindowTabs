namespace Bemo
open System
open System.Collections
open System.Collections.Generic
open System.IO

type ModelObject() as this =
    let mutable values = Dictionary<string, obj>()
    let changedEvent = Event<_>()

    member this.keys = values.Keys :> IEnumerable<string>

    member this.set(key, value:obj) = 
        if values.ContainsKey(key) then
            if value = null then
                values.Remove(key).ignore
            else
                values.Item(key) <- value
            changedEvent.Trigger(key)
        else
            values.Add(key, value)

    member this.get(key) = 
        if values.ContainsKey(key) then
            values.tryFind(key).Value
        else
            null

    member this.remove(key) = this.set(key, null)
    member this.removeAll() = this.keys.list.iter(this.remove)
    member this.changed = changedEvent.Publish

    
type ModelCollection() as this =
    inherit ModelObject()
        
    member this.indicies = this.keys.list.map(int)
    member this.length = 
        if this.indicies.isEmpty then 0 else this.indicies.maxBy 0 id + 1
    
    member this.getAt(index:int) = this.get(index.ToString())
    member this.setAt(index:int, value) = this.set(index.ToString(), value)

    member this.insert(index:int, value) =
        this.indicies.where((>=) index).iter <| fun i ->
            this.setAt(i+1, this.getAt(i))
        this.setAt(index, value)

    member this.append(value) = this.insert(this.length, value)
    
    member this.prepend(value) = this.insert(0, value)

    member this.remove(index:int) = this.set(index.ToString(), null)

    member this.items 
        with get() = List2([0..this.length]).map(this.getAt)
        and set(value:List2<obj>) = 
            this.removeAll()
            value.iter(this.append)