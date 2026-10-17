namespace Bemo.Json
open System
open System.Collections
open System.Collections.Generic
open System.Globalization
open System.Runtime.CompilerServices
open System.Text

/// The JSON tree WindowTabs keeps its settings in. Names, numbering and behaviour follow the parts
/// of Json.NET it replaced, so files written by older versions read and write back unchanged.
type JTokenType =
    | Object = 1
    | Array = 2
    | Property = 4
    | Integer = 6
    | Float = 7
    | String = 8
    | Boolean = 9
    | Null = 10
    | Undefined = 11
    | Date = 12

type Formatting =
    | None = 0
    | Indented = 1

type JsonReaderException(message:string, line:int, position:int) =
    inherit Exception(sprintf "%s Line %d, position %d." message line position)
    member _.LineNumber = line
    member _.LinePosition = position

[<AllowNullLiteral; AbstractClass>]
type JToken() =
    let mutable parent : JToken = null
    /// The container or property holding this token; a token added while it has one is copied.
    member _.Parent with get() = parent and internal set(value) = parent <- value
    abstract Type : JTokenType
    abstract DeepClone : unit -> JToken
    abstract Children : unit -> JToken seq
    default _.Children() = Seq.empty
    member this.HasValues = not (Seq.isEmpty (this.Children()))
    abstract Item : key:obj -> JToken with get, set
    default this.Item
        with get(_:obj) : JToken = invalidOp (sprintf "Cannot access child value on %s." (this.GetType().Name))
        and set (_:obj) (_:JToken) = invalidOp (sprintf "Cannot set child value on %s." (this.GetType().Name))
    member this.ToString(formatting:Formatting) =
        let text = StringBuilder()
        JToken.Write(text,this,(if formatting=Formatting.Indented then 0 else -1))
        text.ToString()
    override this.ToString() = this.ToString(Formatting.Indented)
    member this.ToObject<'T>() : 'T = JToken.Convert<'T> this
    /// Casts such as `int token`, as Json.NET's explicit conversions allowed.
    static member op_Explicit(token:JToken) : int = JToken.Convert<int> (JToken.Required token)
    static member op_Explicit(token:JToken) : int64 = JToken.Convert<int64> (JToken.Required token)
    static member op_Explicit(token:JToken) : float = JToken.Convert<float> (JToken.Required token)
    static member op_Explicit(token:JToken) : bool = JToken.Convert<bool> (JToken.Required token)
    static member op_Explicit(token:JToken) : DateTime = JToken.Convert<DateTime> (JToken.Required token)
    static member op_Explicit(token:JToken) : string = JToken.Convert<string> token
    static member internal Required(token:JToken) =
        if isNull token then raise(ArgumentNullException("value")) else token
    static member Parse(json:string) : JToken = JsonParser(json).ReadRoot()

    static member internal Adopt(owner:JToken, token:JToken) =
        let token = if isNull token.Parent && not (obj.ReferenceEquals(token,owner)) then token else token.DeepClone()
        token.Parent <- owner
        token

    /// An object to store: a token as it is, any other value as a JValue.
    static member internal FromContent(content:obj) : JToken =
        match content with
        | :? JToken as token -> token
        | value -> JValue(value) :> JToken

    static member internal Convert<'T>(token:JToken) : 'T =
        match box token with
        | null -> Unchecked.defaultof<'T>
        | :? 'T as same when typeof<'T> <> typeof<IComparable> && typeof<'T> <> typeof<IFormattable> -> same
        | _ ->
            match token with
            | :? JValue as value ->
                match value.Value with
                | :? 'T as same -> same
                | raw ->
                    let nullable = Nullable.GetUnderlyingType typeof<'T>
                    if not (isNull nullable) && isNull raw then Unchecked.defaultof<'T>
                    else
                        let target = if isNull nullable then typeof<'T> else nullable
                        match raw with
                        // Dates stay text when read; Json.NET turned ISO text into DateTime itself.
                        | :? string as text when target = typeof<DateTime> ->
                            unbox<'T>(box(DateTime.Parse(text,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)))
                        | _ -> unbox<'T>(Convert.ChangeType(raw,target,CultureInfo.InvariantCulture))
            | _ -> raise(InvalidCastException(sprintf "Cannot cast %s to %s." (token.GetType().Name) typeof<'T>.Name))

    static member internal WriteString(text:StringBuilder, value:string) =
        text.Append('"') |> ignore
        for c in value do
            match c with
            | '"' -> text.Append("\\\"") |> ignore
            | '\\' -> text.Append("\\\\") |> ignore
            | '\b' -> text.Append("\\b") |> ignore
            | '\t' -> text.Append("\\t") |> ignore
            | '\n' -> text.Append("\\n") |> ignore
            | '\f' -> text.Append("\\f") |> ignore
            | '\r' -> text.Append("\\r") |> ignore
            | c when c < ' ' || int c = 0x85 || int c = 0x2028 || int c = 0x2029 -> text.Append("\\u").Append((int c).ToString("x4")) |> ignore
            | c -> text.Append(c) |> ignore
        text.Append('"') |> ignore

    /// Doubles always show a decimal place, as Json.NET wrote them.
    static member internal FloatText(text:string) =
        if text.IndexOfAny([|'.';'E';'e'|]) >= 0 then text else text+".0"

    /// indent is the nesting level, or -1 for compact output.
    static member internal Write(text:StringBuilder, token:JToken, indent:int) =
        let inner = if indent>=0 then indent+1 else -1
        let line level = if level>=0 then text.Append(Environment.NewLine).Append(' ',2*level) |> ignore
        let container (start:char) (finish:char) (items:JToken seq) =
            text.Append(start) |> ignore
            let mutable first = true
            for item in items do
                if not first then text.Append(',') |> ignore
                first <- false
                line inner
                JToken.Write(text,item,inner)
            if not first then line indent
            text.Append(finish) |> ignore
        match token with
        | :? JObject as o -> container '{' '}' (o.Children())
        | :? JArray as a -> container '[' ']' (a.Children())
        | :? JProperty as p ->
            JToken.WriteString(text,p.Name)
            text.Append(if indent>=0 then ": " else ":") |> ignore
            JToken.Write(text,p.Value,indent)
        | :? JValue as v ->
            let invariant = CultureInfo.InvariantCulture
            let special (value:float) = Double.IsNaN value || Double.IsInfinity value
            match v.Value with
            | null -> text.Append(if v.Type=JTokenType.Undefined then "undefined" else "null") |> ignore
            | :? string as s -> JToken.WriteString(text,s)
            | :? bool as b -> text.Append(if b then "true" else "false") |> ignore
            | :? DateTime as d -> JToken.WriteString(text,d.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFK",invariant))
            | :? DateTimeOffset as d -> JToken.WriteString(text,d.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz",invariant))
            | :? float as f when special f -> JToken.WriteString(text,f.ToString("R",invariant))
            | :? float as f -> text.Append(JToken.FloatText(f.ToString("R",invariant))) |> ignore
            | :? float32 as f when special (float f) -> JToken.WriteString(text,f.ToString("R",invariant))
            | :? float32 as f -> text.Append(JToken.FloatText(f.ToString("R",invariant))) |> ignore
            | :? decimal as d -> text.Append(JToken.FloatText(d.ToString(invariant))) |> ignore
            | :? IFormattable as number -> text.Append(number.ToString(null,invariant)) |> ignore
            | other -> JToken.WriteString(text,string other)
        | _ -> ()

    interface IEnumerable<JToken> with
        member this.GetEnumerator() = this.Children().GetEnumerator()
    interface IEnumerable with
        member this.GetEnumerator() = (this.Children() :> IEnumerable).GetEnumerator()

and [<AllowNullLiteral>] JValue internal (value:obj, kind:JTokenType) =
    inherit JToken()
    static member internal TypeOf(value:obj) =
        match value with
        | null -> JTokenType.Null
        | :? string -> JTokenType.String
        | :? bool -> JTokenType.Boolean
        | :? int | :? int64 | :? int16 | :? sbyte | :? byte | :? uint16 | :? uint32 | :? uint64 | :? Numerics.BigInteger -> JTokenType.Integer
        | :? float | :? float32 | :? decimal -> JTokenType.Float
        | :? DateTime | :? DateTimeOffset -> JTokenType.Date
        | _ -> invalidArg "value" (sprintf "Could not determine JSON object type for type %s." (value.GetType().FullName))
    new(value:obj) = JValue(value,JValue.TypeOf value)
    new(value:string) = JValue(box value,JTokenType.String)
    new(value:bool) = JValue(box value,JTokenType.Boolean)
    new(value:int64) = JValue(box value,JTokenType.Integer)
    new(value:float) = JValue(box value,JTokenType.Float)
    new(value:DateTime) = JValue(box value,JTokenType.Date)
    new(other:JValue) = JValue(other.Value,other.Type)
    /// The stored .NET value: Int64 or Double when read from text, as given when set in code.
    member _.Value : obj = value
    override _.Type = kind
    override _.DeepClone() = JValue(value,kind) :> JToken
    override _.ToString() = if isNull value then "" else value.ToString()
    override this.Equals(other) =
        match other with
        | :? JValue as other ->
            match value,other.Value with
            | (:? IConvertible as a),(:? IConvertible as b) when kind<>JTokenType.String && kind=other.Type
                                                              && (kind=JTokenType.Integer || kind=JTokenType.Float) ->
                Convert.ToDouble(a,CultureInfo.InvariantCulture) = Convert.ToDouble(b,CultureInfo.InvariantCulture)
            | a,b -> kind=other.Type && obj.Equals(a,b)
        | _ -> false
    override _.GetHashCode() =
        match value with
        | null -> 0
        | :? IConvertible as number when kind=JTokenType.Integer || kind=JTokenType.Float ->
            Convert.ToDouble(number,CultureInfo.InvariantCulture).GetHashCode()
        | value -> value.GetHashCode()
    interface IFormattable with
        member _.ToString(format,provider) =
            match value with
            | null -> ""
            | :? IFormattable as formattable -> formattable.ToString(format,provider)
            | value -> value.ToString()

and [<AllowNullLiteral>] JProperty(name:string, content:obj) as this =
    inherit JToken()
    let mutable value : JToken = null
    do
        this.Value <-
            match content with
            | :? JToken as token -> token
            | :? string -> JToken.FromContent content
            | :? IEnumerable -> JArray(content) :> JToken
            | _ -> JToken.FromContent content
    member _.Name : string = name
    member this.Value
        with get() : JToken = value
        and set(token:JToken) =
            let token = JToken.Adopt(this,(if isNull token then JValue(null:obj) :> JToken else token))
            if not (isNull value) then value.Parent <- null
            value <- token
    override _.Type = JTokenType.Property
    override _.DeepClone() = JProperty(name,value.DeepClone()) :> JToken
    override _.Children() = Seq.singleton value

and [<AllowNullLiteral>] JObject() =
    inherit JToken()
    let properties = List<JProperty>()
    let index = Dictionary<string,JProperty>(StringComparer.Ordinal)
    new([<ParamArray>] content:obj[]) as this = JObject() then for item in content do this.Add(item)
    new(other:JObject) as this = JObject() then for p in other.Properties() do this.Add(p.DeepClone())
    static member Parse(json:string) : JObject =
        match JsonParser(json).ReadRoot() with
        | :? JObject as root -> root
        | _ -> raise(JsonReaderException("The JSON text is not an object.",1,0))
    member internal this.Insert(position:int, property:JProperty) =
        if index.ContainsKey(property.Name) then
            invalidArg "content" (sprintf "Can not add property %s to JObject. Property with the same name already exists on object." property.Name)
        let property = JToken.Adopt(this,property) :?> JProperty
        properties.Insert(position,property)
        index.[property.Name] <- property
    member this.Add(content:obj) =
        match content with
        | :? JProperty as property -> this.Insert(properties.Count,property)
        | :? JToken | :? string | null -> invalidArg "content" (sprintf "Can not add %s to JObject." (if isNull content then "null" else content.GetType().Name))
        | :? IEnumerable as items -> for item in items do this.Add(item)
        | other -> invalidArg "content" (sprintf "Can not add %s to JObject." (other.GetType().Name))
    member this.Add(name:string, value:JToken) = this.Add(JProperty(name,value))
    member this.AddFirst(content:obj) =
        match content with
        | :? JProperty as property -> this.Insert(0,property)
        | _ -> invalidArg "content" "Only a JProperty can be added to a JObject."
    member _.Count = properties.Count
    member _.ContainsKey(name:string) = index.ContainsKey(name)
    member _.Property(name:string) = match index.TryGetValue(name) with | true,p -> p | _ -> null
    member _.Properties() : JProperty seq = properties :> JProperty seq
    member _.Remove(name:string) =
        match index.TryGetValue(name) with
        | true,p ->
            index.Remove(name) |> ignore
            properties.Remove(p) |> ignore
            p.Parent <- null
            true
        | _ -> false
    member _.TryGetValue(name:string, [<Runtime.InteropServices.Out>] value:byref<JToken>) =
        match index.TryGetValue(name) with
        | true,p -> value <- p.Value; true
        | _ -> value <- null; false
    member this.Item
        with get(name:string) : JToken = match index.TryGetValue(name) with | true,p -> p.Value | _ -> null
        and set (name:string) (value:JToken) =
            match index.TryGetValue(name) with
            | true,p -> p.Value <- value
            | _ -> this.Add(JProperty(name,value))
    override this.Item
        with get(key:obj) : JToken =
            match key with
            | :? string as name -> this.[name]
            | _ -> invalidArg "key" (sprintf "Accessed JObject values with invalid key value: %O. Object property name expected." key)
        and set (key:obj) (value:JToken) =
            match key with
            | :? string as name -> this.[name] <- value
            | _ -> invalidArg "key" (sprintf "Set JObject values with invalid key value: %O. Object property name expected." key)
    member _.GetEnumerator() : IEnumerator<KeyValuePair<string,JToken>> =
        (properties |> Seq.map(fun p -> KeyValuePair(p.Name,p.Value))).GetEnumerator()
    override _.Type = JTokenType.Object
    override this.DeepClone() = JObject(this) :> JToken
    override _.Children() = properties |> Seq.cast<JToken>
    interface IDictionary<string,JToken> with
        member this.Add(key,value) = this.Add(key,value)
        member this.ContainsKey(key) = this.ContainsKey(key)
        member _.Keys = properties |> Seq.map(fun p -> p.Name) |> Seq.toArray :> ICollection<string>
        member this.Remove(key:string) = this.Remove(key)
        member this.TryGetValue(key,value) = this.TryGetValue(key,&value)
        member _.Values = properties |> Seq.map(fun p -> p.Value) |> Seq.toArray :> ICollection<JToken>
        member this.Item with get(key:string) = this.[key] and set (key:string) value = this.[key] <- value
    interface ICollection<KeyValuePair<string,JToken>> with
        member this.Add(item) = this.Add(item.Key,item.Value)
        member _.Clear() =
            for p in properties do p.Parent <- null
            properties.Clear()
            index.Clear()
        member this.Contains(item) = match index.TryGetValue(item.Key) with | true,p -> obj.ReferenceEquals(p.Value,item.Value) | _ -> false
        member this.CopyTo(target,start) = properties |> Seq.iteri(fun i p -> target.[start+i] <- KeyValuePair(p.Name,p.Value))
        member _.Count = properties.Count
        member _.IsReadOnly = false
        member this.Remove(item:KeyValuePair<string,JToken>) =
            (this :> ICollection<KeyValuePair<string,JToken>>).Contains(item) && this.Remove(item.Key)
    interface IEnumerable<KeyValuePair<string,JToken>> with
        member this.GetEnumerator() = this.GetEnumerator()

and [<AllowNullLiteral>] JArray() =
    inherit JToken()
    let items = List<JToken>()
    new([<ParamArray>] content:obj[]) as this = JArray() then for item in content do this.Add(item)
    new(other:JArray) as this = JArray() then for item in other.Children() do this.Add(item.DeepClone())
    member this.Add(content:obj) =
        match content with
        | :? JToken as token -> items.Add(JToken.Adopt(this,token))
        | :? string -> items.Add(JToken.Adopt(this,JToken.FromContent content))
        | :? IEnumerable as many -> for item in many do this.Add(item)
        | value -> items.Add(JToken.Adopt(this,JToken.FromContent value))
    member this.Insert(position:int, content:obj) =
        items.Insert(position,JToken.Adopt(this,JToken.FromContent content))
    member _.RemoveAt(position:int) =
        items.[position].Parent <- null
        items.RemoveAt(position)
    member this.Remove(item:JToken) =
        match items.IndexOf(item) with
        | -1 -> false
        | position -> this.RemoveAt(position); true
    member _.Clear() =
        for item in items do item.Parent <- null
        items.Clear()
    member _.IndexOf(item:JToken) = items.IndexOf(item)
    member _.Count = items.Count
    member this.Item
        with get(position:int) : JToken = items.[position]
        and set (position:int) (value:JToken) =
            let value = JToken.Adopt(this,(if isNull value then JValue(null:obj) :> JToken else value))
            items.[position].Parent <- null
            items.[position] <- value
    override this.Item
        with get(key:obj) : JToken =
            match key with
            | :? int as position -> this.[position]
            | _ -> invalidArg "key" (sprintf "Accessed JArray values with invalid key value: %O. Int32 array index expected." key)
        and set (key:obj) (value:JToken) =
            match key with
            | :? int as position -> this.[position] <- value
            | _ -> invalidArg "key" (sprintf "Set JArray values with invalid key value: %O. Int32 array index expected." key)
    override _.Type = JTokenType.Array
    override this.DeepClone() = JArray(this) :> JToken
    override _.Children() = items :> JToken seq
    interface IList<JToken> with
        member this.IndexOf(item) = this.IndexOf(item)
        member this.Insert(position,item) = this.Insert(position,item)
        member this.RemoveAt(position) = this.RemoveAt(position)
        member this.Item with get(position) = this.[position] and set position value = this.[position] <- value
    interface ICollection<JToken> with
        member this.Add(item) = this.Add(item)
        member this.Clear() = this.Clear()
        member _.Contains(item) = items.Contains(item)
        member _.CopyTo(target,start) = items.CopyTo(target,start)
        member _.Count = items.Count
        member _.IsReadOnly = false
        member this.Remove(item) = this.Remove(item)

/// Reads what Json.NET accepted in a settings file a person may have edited: comments, single
/// quotes, unquoted names and trailing commas. Later duplicate names replace earlier values.
and internal JsonParser(text:string) =
    static let maxDepth = 64
    let mutable pos = 0
    let fail (message:string) : 'a =
        let mutable line,column = 1,0
        for i in 0..(min pos text.Length)-1 do
            if text.[i]='\n' then (line <- line+1; column <- 0)
            elif text.[i]<>'\r' then column <- column+1
        raise(JsonReaderException(message,line,column))
    let next() = if pos<text.Length then text.[pos] else '\000'
    let atEnd() = pos>=text.Length
    /// Json.NET skipped NUL characters between tokens, as a file cut short by a crash can end in them.
    let blank c = Char.IsWhiteSpace c || int c = 0
    let skip() =
        let mutable going = true
        while going && pos<text.Length do
            let c = text.[pos]
            if blank c then pos <- pos+1
            elif c='/' && pos+1<text.Length && text.[pos+1]='/' then
                while pos<text.Length && text.[pos]<>'\n' && text.[pos]<>'\r' do pos <- pos+1
            elif c='/' && pos+1<text.Length && text.[pos+1]='*' then
                match text.IndexOf("*/",pos+2,StringComparison.Ordinal) with
                | -1 -> pos <- text.Length; fail "Unexpected end while parsing comment."
                | finish -> pos <- finish+2
            elif c='/' then fail (sprintf "Error parsing comment. Expected: *, got %c." (if pos+1<text.Length then text.[pos+1] else ' '))
            else going <- false
    let separator() =
        atEnd() || (let c = text.[pos] in blank c || c=',' || c='}' || c=']' || c='/' || c=')')
    let literal (word:string) (value:JToken) =
        if String.CompareOrdinal(text,pos,word,0,word.Length)=0 then
            pos <- pos+word.Length
            if not (separator()) then fail (sprintf "Unexpected character encountered while parsing value: %c." (next()))
            value
        else fail (sprintf "Unexpected character encountered while parsing value: %c." (next()))
    let readString (quote:char) =
        pos <- pos+1
        let result = StringBuilder()
        let mutable reading = true
        while reading do
            let stop = text.IndexOfAny([|quote;'\\'|],pos)
            if stop<0 then (pos <- text.Length; fail (sprintf "Unterminated string. Expected delimiter: %c." quote))
            result.Append(text,pos,stop-pos) |> ignore
            pos <- stop+1
            if text.[stop]=quote then reading <- false
            else
                if atEnd() then fail (sprintf "Unterminated string. Expected delimiter: %c." quote)
                let escape = text.[pos]
                pos <- pos+1
                match escape with
                | 'b' -> result.Append('\b') |> ignore
                | 't' -> result.Append('\t') |> ignore
                | 'n' -> result.Append('\n') |> ignore
                | 'f' -> result.Append('\f') |> ignore
                | 'r' -> result.Append('\r') |> ignore
                | '\\' | '"' | '\'' | '/' -> result.Append(escape) |> ignore
                | 'u' ->
                    let mutable code = 0
                    if pos+4>text.Length || not (Int32.TryParse(text.Substring(pos,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,&code)) then
                        fail "Invalid Unicode escape sequence."
                    result.Append(char code) |> ignore
                    pos <- pos+4
                | other -> fail (sprintf "Bad JSON escape sequence: \\%c." other)
        result.ToString()
    let readNumber() =
        let start = pos
        let numeric c = (c>='0' && c<='9') || (c>='a' && c<='f') || (c>='A' && c<='F') || c='-' || c='+' || c='.' || c='x' || c='X'
        while pos<text.Length && numeric text.[pos] do pos <- pos+1
        let number = text.Substring(start,pos-start)
        if not (separator()) then fail (sprintf "Unexpected character encountered while parsing number: %c." (next()))
        let invalid() : JToken = fail (sprintf "Input string '%s' is not a valid number." number)
        let digits = if number.StartsWith("-") then number.Substring(1) else number
        let mutable integer = 0L
        let mutable real = 0.0
        if number.Length>1 && number.[0]='0' && number.[1]<>'.' && number.[1]<>'e' && number.[1]<>'E' then
            // Json.NET also read JavaScript's 0x hexadecimal and leading-zero octal integers.
            try JValue(if number.StartsWith("0x",StringComparison.OrdinalIgnoreCase) then Convert.ToInt64(number,16) else Convert.ToInt64(number,8)) :> JToken
            with _ -> invalid()
        elif digits.Length>0 && digits |> Seq.forall(fun c -> c>='0' && c<='9') then
            if Int64.TryParse(number,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,&integer) then JValue(integer) :> JToken
            else JValue(Numerics.BigInteger.Parse(number,CultureInfo.InvariantCulture)) :> JToken
        elif Double.TryParse(number,NumberStyles.Float,CultureInfo.InvariantCulture,&real) then JValue(real) :> JToken
        else invalid()
    let rec readValue (depth:int) : JToken =
        skip()
        if atEnd() then fail "Unexpected end when reading JSON."
        match text.[pos] with
        | '{' -> readObject (depth+1) :> JToken
        | '[' -> readArray (depth+1) :> JToken
        | '"' | '\'' as quote -> JValue(readString quote) :> JToken
        | 't' -> literal "true" (JValue(true))
        | 'f' -> literal "false" (JValue(false))
        | 'n' -> literal "null" (JValue(null:obj))
        | 'u' -> literal "undefined" (JValue(null,JTokenType.Undefined))
        | 'N' -> literal "NaN" (JValue(Double.NaN))
        | 'I' -> literal "Infinity" (JValue(Double.PositiveInfinity))
        | '-' when pos+1<text.Length && text.[pos+1]='I' -> pos <- pos+1; literal "Infinity" (JValue(Double.NegativeInfinity))
        | c when (c>='0' && c<='9') || c='-' || c='.' -> readNumber()
        | c -> fail (sprintf "Unexpected character encountered while parsing value: %c." c)
    and readObject (depth:int) =
        if depth>maxDepth then fail (sprintf "The reader's MaxDepth of %d has been exceeded." maxDepth)
        pos <- pos+1
        let result = JObject()
        let mutable reading = true
        while reading do
            skip()
            if atEnd() then fail "Unexpected end when reading JSON."
            if text.[pos]='}' then (pos <- pos+1; reading <- false)
            else
                let name =
                    match text.[pos] with
                    | '"' | '\'' as quote -> readString quote
                    | c when Char.IsLetterOrDigit c || c='_' || c='$' ->
                        let start = pos
                        while pos<text.Length && (Char.IsLetterOrDigit text.[pos] || text.[pos]='_' || text.[pos]='$') do pos <- pos+1
                        text.Substring(start,pos-start)
                    | c -> fail (sprintf "Invalid property identifier character: %c." c)
                skip()
                if next()<>':' then fail (sprintf "Invalid character after parsing property name. Expected ':' but got: %c." (next()))
                pos <- pos+1
                let value = readValue depth
                match result.Property(name) with
                | null -> result.Add(JProperty(name,value))
                | existing -> existing.Value <- value
                skip()
                match next() with
                | ',' -> pos <- pos+1
                | '}' -> pos <- pos+1; reading <- false
                | _ when atEnd() -> fail "Unexpected end when reading JSON."
                | c -> fail (sprintf "After parsing a value an unexpected character was encountered: %c." c)
        result
    and readArray (depth:int) =
        if depth>maxDepth then fail (sprintf "The reader's MaxDepth of %d has been exceeded." maxDepth)
        pos <- pos+1
        let result = JArray()
        let mutable reading = true
        while reading do
            skip()
            if atEnd() then fail "Unexpected end when reading JSON."
            if text.[pos]=']' then (pos <- pos+1; reading <- false)
            // An empty slot, as in [1,,2], was read as undefined.
            elif text.[pos]=',' then (pos <- pos+1; result.Add(JValue(null,JTokenType.Undefined)))
            else
                result.Add(readValue depth)
                skip()
                match next() with
                | ',' -> pos <- pos+1
                | ']' -> pos <- pos+1; reading <- false
                | _ when atEnd() -> fail "Unexpected end when reading JSON."
                | c -> fail (sprintf "After parsing a value an unexpected character was encountered: %c." c)
        result
    member _.ReadRoot() : JToken =
        if isNull text then raise(ArgumentNullException("json"))
        let root = readValue 0
        skip()
        if not (atEnd()) then fail (sprintf "Additional text encountered after finished reading JSON content: %c." (next()))
        root

/// Value<'T>() reads a token as a .NET value, converting numbers and text like Json.NET.
[<Extension; AbstractClass; Sealed>]
type JTokenExtensions =
    [<Extension>]
    static member Value<'T>(token:JToken) : 'T = JToken.Convert<'T> token
    [<Extension>]
    static member Value<'T>(token:JToken, key:obj) : 'T = JToken.Convert<'T>(token.[key])
