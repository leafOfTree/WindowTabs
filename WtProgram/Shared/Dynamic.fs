namespace Bemo
open System
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.Reflection

[<AutoOpen>]
module DynamicHelpers =
    let inline asMethodBase(a:#MethodBase) = a :> MethodBase

[<AllowNullLiteral>]
type Dynamic(?target:obj) as this =
    let values = Dictionary<string, obj>()
    let _target = defaultArg target (box(this))
    // Various flags that specify what members can be called 
    // NOTE: Remove 'BindingFlags.NonPublic' if you want a version
    // that can call only public methods of classes
    let staticFlags = BindingFlags.NonPublic ||| BindingFlags.Public ||| BindingFlags.Static 
    let instanceFlags = BindingFlags.NonPublic ||| BindingFlags.Public ||| BindingFlags.Instance 
    let ctorFlags = instanceFlags
    
    member this.target = _target
    member this.targetType = this.target.GetType()
    member this.interfaces = this.targetType.GetInterfaces().list
    member this.properties =
        let interfaceProperties = this.interfaces.collect <| fun(iType) ->
            iType.GetProperties().list
        let typeProperties = this.targetType.GetProperties().list
        typeProperties.appendList(interfaceProperties)

    member this.hasExpandoValue name =
        values.ContainsKey(name)

    member this.getExpandoValue name =
        values.Item(name)

    member this.setExpandoValue name value =
        values.Item(name) <- value
    
    member this.makeArgs(fType, args) =
        // Get arguments (from a tuple) and their types
        let argType, _ = FSharpType.GetFunctionElements(fType)
        
        // If argument is unit, we treat it as no arguments,
        // if it is not a tuple, we create singleton array,
        // otherwise we get all elements of the tuple
        if argType = typeof<unit> then [| |]
        elif not(FSharpType.IsTuple(argType)) then [| args |]
        else FSharpValue.GetTupleFields(args)

    member this.isMatchingMethod(m:MethodBase, name, args:obj[]) =
        m.Name = name && m.GetParameters().Length = args.Length

    member this.findMethodInInterface(name, args:obj[]) =
        this.interfaces.tryPick <| fun iType ->
            let map = this.targetType.GetInterfaceMap(iType)
            map.TargetMethods.list.zip(map.InterfaceMethods.list).tryPick <| fun (tm, im) ->
                if this.isMatchingMethod(im, name, args) then
                    Some(tm)
                else
                    None

    member this.findMethodInType(name, args:obj[]) =
        this.targetType.GetMethods(instanceFlags).list.tryFind <| fun m ->
            this.isMatchingMethod(m, name, args)

    member this.findMethod(name, args:obj[]) =
        let m = this.findMethodInType(name, args)
        if m.IsNone then
            let m = this.findMethodInInterface(name, args)
            m
        else
            m

    member this.findProperty(name) =
        this.properties.tryFind(fun(p) -> p.Name = name).def(null)

    member this.hasProp(name) = this.findProperty(name) <> null

    member this.dispatchFunction name : 'R =
        // Construct an F# function as the result (and cast it to the
        // expected function type specified by 'R)
        let fType = typeof<'R>
        let doInvoke args =
            let args = this.makeArgs(fType, args) 
            let method = this.findMethod(name, args).def(null)
            method.Invoke(this.target, args)
        FSharpValue.MakeFunction(fType, doInvoke).cast<'R>()

    member this.dispatchGet name : 'R =
        // Find a property that we can call and get the value
        let prop = this.findProperty(name)
        if prop <> null then
            // Call property and return result if we found some
            let meth = prop.GetGetMethod(true)
            if prop = null then failwithf "Property '%s' found, but doesn't have 'get' method." name
            try meth.Invoke(this.target, [| |]) |> unbox<'R>
            with _ -> failwithf "Failed to get value of '%s' property (of type '%s')" name this.targetType.Name
        else
            let fi = this.targetType.GetField(name)
            if fi <> null then
                fi.GetValue(this.target) |> unbox<'R>
            else
                box(null).cast<'R>()

    member this.dynamicGet name : 'R =
        // The return type is a function, which means that we want to invoke a method
        if this.hasExpandoValue(name) then
            this.getExpandoValue(name).cast<'R>()
        elif FSharpType.IsFunction(typeof<'R>) then
            this.dispatchFunction name
        else
            this.dispatchGet name

    member this.dynamicSet name (value:'b) =
        // The result type is not an F# function, so we're getting a property
        // When the 'o' object is 'System.Type', we access static properties
        let typ, flags, instance = 
          if (typeof<System.Type>).IsAssignableFrom(this.target.GetType()) 
            then unbox this.target, staticFlags, null
            else this.target.GetType(), instanceFlags, this.target
      
        // Find a property that we can call and get the value
        let prop = this.findProperty(name)
        let fi = typ.GetField(name)
        if prop <> null then
            // Call property and return result if we found some
            let meth = prop.GetSetMethod(true)
            if meth = null then failwithf "Property '%s' found, but doesn't have 'set' method." name
            try meth.Invoke(instance, [|value|]).ignore
            with _ -> failwithf "Failed to set value of '%s' property (of type '%s')" name typ.Name
        elif fi <> null then
            let fi = typ.GetField(name)
            fi.SetValue(instance, value).ignore
        else
            this.setExpandoValue name value

[<AutoOpen>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Dynamic =
    let inline asMethodBase(a:#MethodBase) = a :> MethodBase
//    let (?) (o:Dynamic) name : 'R = o.dynamicGet name
//    let (?<-) (o:Dynamic) name (value:'b) = o.dynamicSet name value
    let (?) (o:obj) name : 'r = 
        let d = 
            match o with
            | :? Dynamic as d -> d
            | _ -> Dynamic(o)
        d.dynamicGet name
    let (?<-) (o:obj) name (value:'b) = 
        let d = 
            match o with
            | :? Dynamic as d -> d
            | _ -> Dynamic(o)
        d.dynamicSet name value