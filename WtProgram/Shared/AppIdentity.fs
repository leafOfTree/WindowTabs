namespace Bemo
open System
open System.IO

/// Executable names identify app rules across installations and updates; paths locate icons.
module AppIdentity =
    let key path =
        if String.IsNullOrWhiteSpace path then ""
        else
            try Path.GetFileName(path).ToUpperInvariant() with :? ArgumentException -> ""
    let same left right =
        let name = key left
        name<>"" && name=key right
    let contains (paths:Set2<string>) path = paths.items.any(fun saved -> same saved path)
    let without (paths:Set2<string>) path = Set2(paths.items.where(fun saved -> not(same saved path)))
