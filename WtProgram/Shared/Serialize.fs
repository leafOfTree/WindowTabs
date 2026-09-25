namespace Bemo
open System
open System.Collections
open System.IO
open Microsoft.FSharp.Reflection

type TypeTree =
    | TypeInt32
    | TypeInt64
    | TypeBool
    | TypeString
    | TypeSequence of TypeTree * (obj -> obj seq) * (obj seq -> obj)
    | TypeProduct of (TypeTree seq) * (obj -> obj seq) * (obj seq -> obj)
    | TypeSum of (int -> TypeTree seq) * (obj -> (int * (obj seq))) * ((int * (obj seq)) -> obj)

module Serialize =
    let rec parse =
        function
        | t when FSharpType.IsTuple(t) -> 
            let types = FSharpType.GetTupleElements(t)
            let getValues = FSharpValue.GetTupleFields >> Seq.ofArray
            let setValues values = FSharpValue.MakeTuple(values |> Array.ofSeq, t)
            TypeProduct(types |> Seq.map parse, getValues, setValues)
        | t when FSharpType.IsRecord(t) -> 
            let types =
                FSharpType.GetRecordFields(t)
                |> Seq.map(fun pi -> pi.PropertyType)
            let getValues = FSharpValue.GetRecordFields >> Seq.ofArray
            let setValues values = FSharpValue.MakeRecord(t,values |> Array.ofSeq)
            TypeProduct(types |> Seq.map parse, getValues, setValues)
        | t when t.IsArray ->
            let itemType = t.GetElementType()
            let getValues (obj:obj) = 
                obj 
                :?> IEnumerable
                |> Seq.cast<obj>
                |> Seq.cache
            let setValues values =
                let values = values |> List.ofSeq
                let length = values |> Seq.length
                let arr = Array.CreateInstance(itemType, length)
                do  values 
                    |> Seq.iteri (fun i v -> do arr.SetValue(v, i))
                arr :> obj
            TypeSequence(parse itemType, getValues, setValues)
        | t when FSharpType.IsUnion(t) ->
            let getType i =
                FSharpType.GetUnionCases(t)
                |> Seq.find (fun (c:UnionCaseInfo) -> c.Tag = i)
                |> fun c -> c.GetFields()
                |> Seq.map(fun pi -> parse pi.PropertyType)
            let getValues (obj:obj) =
                let info,values = FSharpValue.GetUnionFields(obj, t)
                info.Tag, (values :> seq<_>)
            let setValues (i,values) =
                let info =
                    FSharpType.GetUnionCases(t)
                    |> Seq.find (fun (c:UnionCaseInfo) -> c.Tag = i)
                FSharpValue.MakeUnion(info, values |> Array.ofSeq)
            TypeSum(getType, getValues, setValues)
        | t when t = typeof<int32> -> TypeInt32
        | t when t = typeof<int64> -> TypeInt64
        | t when t = typeof<bool> -> TypeBool
        | t when t = typeof<string> -> TypeString
        | t -> failwith (sprintf "Type '%A' not supported for serialization" t.Name)

    let serialize stream (root:'a) =
        let writer = new BinaryWriter(stream)
        let rec serialize(t,root) =
            match t with
            | TypeProduct(types,getValues,_) -> 
                do  root
                    |> getValues
                    |> Seq.zip types
                    |> Seq.iter serialize
            | TypeSequence(t, getValues, _) ->
                do  root
                    |> getValues
                    |> Seq.length
                    |> fun l -> 
                        do writer.Write(l)
                do  root
                    |> getValues
                    |> Seq.map (fun v -> t,v)
                    |> Seq.iter serialize
            | TypeSum(getTypes, getValues, _) ->
                let tag,values =
                    root
                    |> getValues
                do  writer.Write(tag)
                do  values
                    |> Seq.zip (getTypes(tag))
                    |> Seq.iter serialize
            | TypeInt32 -> 
                do  writer.Write(root :?> int32)
            | TypeInt64 ->
                do  writer.Write(root :?> int64)
            | TypeBool ->
                do  writer.Write(root :?> bool)
            | TypeString ->
                do  writer.Write(root :?> string)
        do  serialize(parse typeof<'a>,root)

    let deserialize stream : 'a =
        let reader = new BinaryReader(stream)
        let rec deserialize t =
            match t with
            | TypeProduct(types,_,setValues) ->
                types
                |> Seq.map deserialize
                |> setValues
            | TypeSequence(t, _, setValues) ->
                let length = reader.ReadInt32()
                Seq.init length (fun _ -> deserialize t)
                |> setValues
            | TypeSum(getTypes, _, setValues) ->
                let tag = reader.ReadInt32()
                getTypes tag
                |> Seq.map deserialize
                |> fun values -> setValues(tag, values)
            | TypeInt32 -> 
                reader.ReadInt32() |> box
            | TypeInt64 ->
                reader.ReadInt64() |> box
            | TypeBool ->
                reader.ReadBoolean() |> box
            | TypeString ->
                reader.ReadString() |> box
        deserialize (parse typeof<'a>) :?> 'a

    let to_bytes value =
        let stream = new MemoryStream()
        do  serialize stream value
        stream.ToArray()

    let from_bytes (bytes:byte[]) =
        let stream = new MemoryStream(bytes)
        do  stream.Position <- 0L
        deserialize stream

    let load path = File.ReadAllBytes(path) |> from_bytes

    let save input path = 
        let bytes = to_bytes input
        do  File.WriteAllBytes(path, bytes)    
            
    let readField (record:obj) fieldName =
        let propInfo = record.GetType().GetProperty(fieldName)
        FSharpValue.GetRecordField(record, propInfo)

    let writeField (record:obj) fieldName fieldValue = 
        let t = record.GetType()
        let properties = List2(FSharpType.GetRecordFields(t))
        let values = List2(FSharpValue.GetRecordFields(record)).enumerate.map <| fun (i,value) ->
            let propInfo = properties.at(i)
            if propInfo.Name = fieldName then fieldValue
            else value
        FSharpValue.MakeRecord(t, values.toArray)

    let getFieldType (recordType:Type) fieldName =
        let properties = List2(FSharpType.GetRecordFields(recordType))
        properties.find(fun p -> p.Name = fieldName).PropertyType