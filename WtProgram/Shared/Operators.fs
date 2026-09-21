namespace Bemo
open System
open System.Collections
open System.Collections.Generic
open System.Reflection
open System.IO
open System.Text.RegularExpressions
open System.Threading
open System.Windows.Forms
open System.Drawing
open Microsoft.FSharp.Collections.Tagged

type IProperty<'a> =
    abstract member value : 'a with get,set

[<AutoOpen>]
module BaseExtensions =

    type System.Drawing.Icon with
        member this.clone() =
            let stream = new MemoryStream()
            this.Save(stream)
            let bytes = stream.ToArray()
            stream.Position <- 0L
            new Icon(stream)


    type System.IntPtr with
        member this.hasFlag (f:IntPtr) = this &&& f = f

    type System.Byte with
        member this.int32 = int32(this)

    type System.String with
        static member fromByteArray(bytes:byte[]) =
            let hex = BitConverter.ToString(bytes)
            hex.Replace("-","")

        member this.toByteArray =
            let numChars = this.Length
            let numBytes = numChars / 2
            [0..(numBytes-1)]
            |> List.map((*)2)
            |> List.map(fun i -> Convert.ToByte(this.Substring(i,2), 16))
            |> List.toArray

        member this.wildCardToRegex =
            "^" + Regex.Escape(this)
                      .Replace(@"\*", ".*")
                      .Replace(@"\?", ".")
               + "$"

        member this.tryToInt() =
            try
                Some(Int32.Parse(this))
            with
            | _ -> None

    type System.Int16 with
        member this.int32 = int32(this)
        member this.float = float(this)

    type System.Int32 with
        member this.float = float(this)
        member this.float32 = float32(this)
        member this.IntPtr = IntPtr(this)
        member this.hasFlag (f:int) = this.IntPtr.hasFlag(f.IntPtr)
    
    type System.Int64 with
        member this.float = float(this)
        member this.float32 = float32(this)
        member this.Int32 = int(this)

    type System.Double with
        member this.Int32 = int(this)

    type System.Boolean with
        member this.not = not(this)

    type Microsoft.FSharp.Core.Option<'a> with
        member this.map f = Option.map f this
        member this.bind f = Option.bind f this
        member this.def value = defaultArg this value
        member this.tryDef value = if this.IsNone then value else this
        member this.exists f = Option.exists f this
        member this.where f =
            match this with
            | Some(value) -> if f value then Some(value) else None
            | None -> None
        member this.iter f = Option.iter f this

    type System.Object with
        member this.ignore = ()
        member this.cast<'a>() = unbox<'a>(this)

    type System.Drawing.Color with
        static member FromRGB value = Color.FromArgb(0xFF000000 ||| value)
        member this.ToRGB() = this.ToArgb() &&& 0x00FFFFFF

    type System.Collections.IEnumerable with
        member this.seq =
            seq {
                for item in this do
                    yield item
            }


type MaybeBuilder() =
    member b.Return(x) = Some(x)
    member b.ReturnFrom(p) = p
    member b.Bind(p,rest) = Option.bind rest p 
    member b.Delay(f) = f()
    member b.Let(p,rest) : Option<'a> = rest p
 
[<AutoOpen>]
module Maybe =
    let maybe = new MaybeBuilder()

[<AutoOpen>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Assert =
    let always value = fun _ -> value
    let Assert pred =
        if not pred then raise(new System.Exception())


type List2<'a>(?items) =
    let items : 'a list= List.ofSeq(defaultArg items Seq.empty)
    member this.list = items
    member this.toArray = Array.ofList(items)
    member this.prepend item = List2(item::items)
    member this.append item = this.appendList(List2([item]))
    member this.appendList (l:List2<'a>) = List2(this.list@l.list)
    member this.prependList (l:List2<'a>) = List2(l.list@this.list)
    member this.where f = List2(List.filter f items)
    member this.any f = List.exists f items    
    member this.all f = List.forall f items    
    member this.sortBy f = List2(List.sortBy f items)
    member this.length = items.Length
    member this.isEmpty = items.IsEmpty
    member this.head = items.Head
    member this.tryHead = if items.IsEmpty then None else Some(items.Head)
    member this.tail = if this.isEmpty then List2() else List2(items.Tail)
    member this.iter f = List.iter f items
    member this.iteri f = List.iteri f items
    member this.count = List.length items
    member this.fold state f = List.fold f state items
    member this.reverse = List2(List.rev items)
    member this.reduce f = List.reduce f this.list
    member this.tryPick f = List.tryPick f items
    member this.pick f = (List.tryPick f items).Value
    member this.find f = List.find f items
    member this.tryFind f = List.tryFind f items
    member this.contains f = (this.tryFind f).IsSome
    member this.tryFindIndex f = List.tryFindIndex f items
    member this.findIndex f = List.findIndex f items
    member this.maxBy def f = 
        if this.isEmpty then def else List.maxBy f items
    member this.sumBy (f:'a -> int) = List.sumBy f items
    member this.minBy f = List.minBy f items
    member this.take count = List2(Seq.toList(Seq.take count items))
    member this.skip count = List2(Seq.toList(Seq.skip count items))
    member this.splitn idx = this.take idx, this.skip idx

type Comparer<'a>()=
    interface IComparer<'a> with
        member x.Compare(a,b) = (box(a)).GetHashCode().CompareTo(box(b).GetHashCode())


[<AutoOpen>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module List2 =
    type List2<'a> with
        //this does not work if its part of List2 class, constrains return to List2<'a>
        member this.map f = List2<'b>(List.map f this.list)
        member this.choose f = List2<'b>(List.choose f this.list)
        member this.collect f = List2<'b>(List.collect (f >> (fun (l:List2<_>) -> l.list)) this.list)
        member this.enumerate = List2<int*'a>(List.mapi (fun i item -> (i,item)) this.list)
        member this.at index = this.list.Item(index)
        member this.tryAt index = 
            if index < 0 || index >= this.length then None else Some(this.at(index))

        member this.pairwise = List2(Seq.pairwise this.list)
        member this.moveToEnd f = 
            match this.tryFind(f) with
            | Some(item) -> this.where(f >> not).prepend(item)
            | None -> this
        member this.moveToBegining f = 
            match this.tryFind(f) with
            | Some(item) -> this.reverse.where(f >> not).prepend(item).reverse
            | None -> this
        member this.move(f, index) =
            match this.tryFind(f) with
            | Some(item) ->
                let whereNotItem (this:List2<_>) = this.where (f >> not)
                let itemExists (this:List2<_>) = this.any f
                let this = this.moveToBegining(f)
                let idx = this.findIndex f
                let i = max 0 (min index (this.length - 1))
                let h = whereNotItem (this.take(i))
                let t = whereNotItem (this.skip(i))
                t.prepend(item).prependList(h)
            | None -> this
        member this.zip (l2:List2<_>) = List2(List.zip this.list l2.list)

    type System.Collections.Generic.IEnumerable<'a> with
        member this.list = List2(this)

    let distinct (this:List2<_>) = List2(Seq.toList(Seq.distinct (this.list)))    
     
type Map2<'a, 'b> when 'a : equality (map) =
    new(?l:List2<_>) = Map2<'a, 'b>(Tagged.Map<'a, 'b, Comparer<'a>>.Create(Comparer<'a>(),(defaultArg l (List2())).list))
    member this.items : List2<'a * 'b> = List2(map.ToList())
    member this.add key value = Map2(map.Add(key, value))
    member this.remove key = Map2(map.Remove(key))
    member this.removeWhereValue pred = Map2(this.items.choose(fun(key,value) -> if pred value then None else Some(key, value)))
    member this.tryFind key = map.TryFind(key)
    member this.contains key = map.ContainsKey(key)
    member this.find key = map.Item(key)
    member this.keys = this.items.map(fst)
    member this.values = this.items.map(snd)

type Set2<'a when 'a : equality>(set) =
    new(?l:List2<_>) = Set2<'a>(Tagged.Set<'a, Comparer<'a>>.Create(Comparer<'a>(),(defaultArg l (List2())).list))
    member this.innerSet = set
    member this.items = List2(set.ToList())
    member this.add item = Set2(set.Add(item))
    member this.remove item = Set2(set.Remove(item))
    member this.contains item = set.Contains(item)
    member this.isEmpty = this.items.isEmpty
    member this.toggle item = if this.contains item then this.remove item else this.add item
    member this.count = set.Count
    member this.sub (setToRemove:Set2<_>)=
        let t,f = set.Partition(setToRemove.contains)
        Set2(f)
    member this.union (setToAdd:Set2<'a>) = 
        let s = Set<'a, Comparer<'a>>.Union(this.innerSet, setToAdd.innerSet)
        Set2(s)
    member this.map f = Set2(this.items.map(f))

module Seq =
    let splitn idx s = 
        let h = s |> Seq.take idx
        let t = s |> Seq.skip idx
        (h,t)
    let split pred s = 
        let h = s |> Seq.takeWhile pred
        let t = s |> Seq.skipWhile pred
        (h,t)

    let trySingle pred s =
        let s = Seq.filter pred s
        Assert (Seq.length(s) <= 1)
        if Seq.isEmpty s then None
        else Some(Seq.head s)

    let single pred =
        trySingle pred >> Option.get

    let fromEnumerable (e:IEnumerable) =
        let e = e.GetEnumerator()
        seq {
            while e.MoveNext() do
                yield e.Current
        }

module Event =
    let scanl f =
        Event.scan (fun u t -> f t u)
 
[<AutoOpen>]
module Helper =
    let prop<'obj, 'value>(o:obj, propertyName) =
        let pi = typeof<'obj>.GetProperty(propertyName)
        let oTyped = unbox<'obj>(o)
        {
            new IProperty<'value> with
                member x.value
                    with get() = 
                        unbox<'value>(pi.GetValue(box(oTyped), null))
                    and set(value) = 
                        pi.SetValue(box(oTyped), box(value), null)
        }
    let memoize f =
        let cache = Dictionary<_,_>()
        fun arg ->
            if cache.ContainsKey(arg).not then
                cache.Add(arg, f(arg))
            cache.Item(arg)

    let update where values list =
        let map row =
            if where row then values row
            else row
        List.map map list

    let between (min, max) x = if x < min then min elif x > max then max else x

    let modifiersVkList = List2([
        (Keys.Control,Keys.ControlKey)
        (Keys.Alt,Keys.Menu)
        (Keys.Shift,Keys.ShiftKey)
    ])
    let keysToVkList keys =
        modifiersVkList.choose <| fun(modifier,key) ->
            if keys &&& modifier = modifier then Some(int(key)) else None
    let vkListToKeys (vKeys:List2<int>) =
        vKeys.fold Keys.None <| fun keys vk ->
            match modifiersVkList.tryFind(fun(_,key) -> int(key) = vk) with
            | Some(modifier,_) -> keys ||| modifier
            | None -> keys

    let conflate interval f =
        let lastRef = ref None
        fun() ->
            let now = DateTime.Now
            let last = lastRef.Value
            lastRef := Some(now)
            match last with
            | Some(last) -> if now.Subtract last > interval then f()
            | None -> f()
