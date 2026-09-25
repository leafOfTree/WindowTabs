namespace Bemo
open System.Collections.Generic


type ICellOutput<'a> =
    abstract value : 'a
    abstract changed : IEvent<unit>

type ICellInput<'a> =
    abstract value : 'a with set

type ICellBinding<'a> =
    abstract init : unit -> unit
    abstract update : unit -> unit
    inherit ICellOutput<'a>

type CellScope(?checkForSetDuringUpdate,?checkTid) =
    let _tid = WinBaseApi.GetCurrentThreadId()
    let mutable _version = 0
    let mutable _executionStack = Stack<CellComputation>()
    let mutable _objCache = Dictionary<_, _>()
    let mutable _listeners = List<_>()
    let mutable _inUpdate = 0

    member this.tid = 
        if checkTid.exists(id) then Some(_tid)
        else None
    
    member this.checkForSetDuringUpdate = checkForSetDuringUpdate.def(false)

    member this.version = _version

    member this.nextVersion() = 
        _version <- _version + 1
        _version

    member this.executionStack
        with get() = _executionStack
        and set value = _executionStack <- value

    member this.inComputation = _executionStack.Count > 0

    
    member this.cacheBy (fKey : 'a -> 'key)  (f : 'a -> 'b) : ('a -> 'b) =
        let cells = Dictionary<_, _>()
        //not allowed to pass "null" to Dictionary<>
        let nullObj = obj()

        fun (arg:'a) ->
            let boxedArg = box(fKey(arg))
            let boxedArg = if boxedArg = null then nullObj else boxedArg
            let cell = 
                if cells.ContainsKey(boxedArg) then cells.Item(boxedArg)
                else 
                    let cell = CellComputation(this, fun() -> box(f arg))
                    cells.Add(boxedArg, cell)
                    cell   
            unbox<'b>(cell.value)

    member this.cache f = this.cacheBy id f

    member this.cacheThisBy (o:obj) (fKey: 'a -> 'key) (f : 'a -> 'b) : ('a -> 'b) =
        let key = o.GetHashCode(),f.GetType().GetHashCode()
        let cacheF =
            if _objCache.ContainsKey(key) then _objCache.Item(key)
            else
                let item = box(this.cacheBy fKey f)
                _objCache.Add(key, box(item))
                item
        unbox<'a->'b>(cacheF)

    member this.cacheThis (o:obj) (f : 'a -> 'b) = this.cacheThisBy o id f

    member this.cacheProp (o:obj) (f : unit -> 'a) : 'a =
        (this.cacheThis o f)()  

    member this.cacheOnce (o:obj) (f : unit -> 'a) : 'a =
        let key = o.GetHashCode(),f.GetType().GetHashCode()
        let item =
            if _objCache.ContainsKey(key) then _objCache.Item(key)
            else
                let item = box(f())
                _objCache.Add(key, item)
                item
        unbox<'a>(item)

    member this.beginUpdate() =
        _inUpdate <- _inUpdate + 1

    member this.endUpdate() =
        if _inUpdate = 0 then
            failwith "not in update"
        _inUpdate <- _inUpdate - 1
        this.callListeners()

    member this.listen (f: unit -> unit) : unit =
        let computation = CellComputation(this, fun() -> box(f()))
        _listeners.Add(computation)
        computation.value.ignore
        
    member this.callListeners() =
        if _inUpdate = 0 && this.inComputation.not then
            for l in _listeners do l.value.ignore

    member this.clearThis (o:obj) =
        let hash = o.GetHashCode()
        _objCache.Keys.list.where(fst >> (=) hash).iter(fun key -> _objCache.Remove(key).ignore)

    member this.create init = Cell<_>(this, init)

    member this.export (f: unit -> 'a) : ICellBinding<'a> =
        let value = ref None
        let changed = Event<_>()
        let update() = 
            value := Some(f())
            changed.Trigger()
        { new ICellBinding<'a> with
            member x.init() = 
                this.listen update
            member x.update() = update()
          interface ICellOutput<'a> with
            member x.changed = changed.Publish
            member x.value = value.Value.Value
        }

    member this.poll (f: unit -> 'a) (sub:(unit->unit) -> unit) : Cell<'a> =
        let cell = this.create(f())
        sub <| fun() ->
            cell.set(f())
        cell
    member this.import (cell:ICellOutput<'a>) : Cell<'a> =
        this.poll (fun() -> cell.value) (cell.changed.Add)

and IDependency =
    abstract member version : int with get
   
and Cell<'a>(scope:CellScope, init:'a, ?checkEq) as this=
    let mutable _prev = None
    let mutable _value = init
    let mutable _version = 0
    let mutable changed = Event<_>()

    member this.ensureScopeTid() =
        match scope.tid with
        | Some(tid) ->
            if WinBaseApi.GetCurrentThreadId() <> tid then
                failwith "accessing cell on wrong thread"
        | None -> ()

    member this.prev = _prev

    member this.setBase(newValue:'a, checkForUpdate:bool):unit =
        this.ensureScopeTid()
        if scope.checkForSetDuringUpdate && checkForUpdate && scope.inComputation then failwith "detected cell change during computation"
        if checkEq.exists(fun f -> f newValue _value).not then
            _prev <- Some(_value)
            _version <- scope.nextVersion()
            _value <- newValue
            scope.callListeners()
            changed.Trigger()

    member this.set(newValue:'a):unit =
        this.setBase(newValue, true)

    member this.value 
        with get() = 
            this.ensureScopeTid()
            for computation in scope.executionStack do
                computation.addDependency(this :> IDependency)
            _value
            
        and set newValue = this.set(newValue)

    member this.map(f) = this.set(f(this.value))
    interface IDependency with
        member this.version = _version

    interface ICellOutput<'a> with
        member x.value = this.value
        member x.changed = changed.Publish

    interface ICellInput<'a> with
        member x.value with set(value) = this.set(value)

and CellComputation(scope:CellScope, compute) =
    let mutable _cachedValue = box null
    let mutable _version = -1
    let _dependencies = ref(Set2<IDependency>())
    let mutable _computing = false

    member this.cachedValue
        with get() = _cachedValue
        and set value = _cachedValue <- value
    
    member this.version
        with get() = _version
        and set value = _version <- value

    member this.addDependency dependency = _dependencies := _dependencies.Value.add(dependency)

    member this.clearDependencies() = _dependencies := Set2()

    member this.isStale =
        let invalidated = _dependencies.Value.items.any(fun input -> input.version > this.version)
        this.version = -1 || invalidated

    member this.value =
        if _computing then 
            failwith "re-entrant computation detected"
        _computing <- true
        if this.isStale then
            scope.executionStack.Push(this)
            this.clearDependencies()
            this.version <- scope.nextVersion()
            this.cachedValue <- compute()
            scope.executionStack.Pop() |> ignore
        _computing <- false
        this.cachedValue
