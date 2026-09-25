module Bemo.AssemblyInfo

open System
open System.Reflection
open System.Runtime.InteropServices

// Version and product attributes are generated from WtProgram.fsproj. The release
// workflow sets the version from the git tag (v2025.06.30 -> -p:Version=2025.06.30);
// Program and the crash logger read the informational version back at runtime.

[<assembly: ComVisible(false)>]

do ()

// Keep diagnostics and settings migration version comparisons non-empty.
let private fallbackVersion = "2025.06.30"

let informationalVersion =
    try
        let attrs = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof<AssemblyInformationalVersionAttribute>, false)
        if attrs.Length = 0 then fallbackVersion
        else
            let value = (attrs.[0] :?> AssemblyInformationalVersionAttribute).InformationalVersion
            if String.IsNullOrWhiteSpace(value) then fallbackVersion else value
    with _ -> fallbackVersion
