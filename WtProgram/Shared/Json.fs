namespace Bemo
open System
open System.Collections.Generic
open Newtonsoft.Json
open Newtonsoft.Json.Linq

[<AutoOpen>]
module JObjectHelper =
    type System.Collections.Generic.IDictionary<'k,'v> with
        member this.tryFind(key) =
            if this.ContainsKey(key) then Some(this.Item(key)) else None

    type JObject with 
        member this.items = List2(this:>IDictionary<_,_>).map(fun pair -> pair.Key,pair.Value)
        member this.getString(key) = this.tryFind(key).map(fun t -> unbox<string>((t :?> JValue).Value))
        member this.getBool(key) = this.tryFind(key).map(fun t -> unbox<bool>((t :?> JValue).Value))
        // Parsed JSON holds Int64, but values written back in code (JValue(int)) hold Int32.
        member this.getInt32(key) = this.tryFind(key).map(fun t -> Convert.ToInt32((t :?> JValue).Value))
        member this.getIntPtr(key) = this.tryFind(key).map(fun t -> IntPtr(Convert.ToInt64((t :?> JValue).Value)))
        member this.getPt(key) = Pt(this.getInt32("x").Value, this.getInt32("y").Value)
        member this.getSz(key) = Sz(this.getInt32("width").Value, this.getInt32("height").Value)
        member this.getRect(key) = Rect(this.getPt("location"), this.getSz("size"))
        member this.getArray<'t>(key) =
            let parse(token:JToken) =
                List2(token :?> JArray).map(fun t -> unbox<'t>((t :?> JValue).Value))
            this.tryFind(key).map(parse)
        member this.getObjectArray(key) = 
            let parse(token:JToken) =
                List2(token :?> JArray).map(fun t -> t :?> JObject)
            this.tryFind(key).map(parse)
        member this.getStringArray(key) = this.getArray<string>(key)
        member this.getInt32Array(key) = this.tryFind(key).map(fun token -> List2(token :?> JArray).map(fun t -> Convert.ToInt32((t :?> JValue).Value)))
        member this.getObject(key) = this.tryFind(key).map(fun t -> unbox<JObject>(t))
        member this.update(key:string, token:JToken option) =
            match token with
            | Some(token) ->
                if (this :> IDictionary<_,_>).ContainsKey(key) then this.Remove(key).ignore
                this.Add(key, token)
            | None -> this.Remove(key).ignore
        member this.addOrUpdate(key:string, token:JToken) =
            this.update(key, Some(token))

        member this.setValue(key, value:obj) =
            this.addOrUpdate(key, JValue(value))
        member this.setValueArray(key, values:List2<_>) =
            this.addOrUpdate(key, JArray(values.map(fun value -> box(JValue(box(value)))).toArray))
        member this.setObject(key, value:JObject) =
            this.addOrUpdate(key, value)
        member this.setObjectArray(key, values:List2<JObject>) =
            this.addOrUpdate(key, JArray(values.map(fun value -> box(value)).toArray))

        member this.setStringArray(key, value:List2<string>) = 
            this.setValueArray(key, value)
        member this.setInt32Array(key, value:List2<Int32>) = 
            this.setValueArray(key, value)
        member this.setBool(key, value:bool) =
            this.setValue(key, value)
        member this.setInt32(key, value:int32) =
            this.setValue(key, value)
        member this.setInt64(key, value:int64) =
            this.setValue(key, value)
        member this.setIntPtr(key, value:IntPtr) =
            this.setValue(key, value.ToInt64())
        member this.setString(key, value:string) =
            this.setValue(key, value)
        member this.setPt(key, value:Pt) =
            this.setInt32("x", value.x)
            this.setInt32("y", value.y)
        member this.setSz(key, value:Sz) =
            this.setInt32("width", value.width)
            this.setInt32("height", value.height)
        member this.setRect(key, value:Rect) =
            this.setPt("location", value.location)
            this.setSz("size", value.size)
            