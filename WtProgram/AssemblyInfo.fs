module Bemo.AssemblyInfo

open System
open System.Reflection
open System.Runtime.InteropServices

// The version lives here and nowhere else. Program and the crash logger read it
// back at runtime, so cutting a release means editing the three lines below
// (and the README changelog).
//
// AssemblyVersion/AssemblyFileVersion must be numeric, so "2025.06.30" becomes
// 2025.6.30.0 there. AssemblyInformationalVersion keeps the display string.

[<assembly: AssemblyTitle("WindowTabs")>]
[<assembly: AssemblyProduct("WindowTabs")>]
[<assembly: AssemblyDescription("Brings browser-style tabbed window management to the desktop")>]
[<assembly: AssemblyCompany("WindowTabs contributors")>]
[<assembly: AssemblyCopyright("Licensed under the terms in LICENSE")>]

[<assembly: AssemblyVersion("2025.6.30.0")>]
[<assembly: AssemblyFileVersion("2025.6.30.0")>]
[<assembly: AssemblyInformationalVersion("2025.06.30")>]

[<assembly: ComVisible(false)>]

do ()

// Fallback keeps startup safe: ProgramVersion parses this string with
// Int32.Parse, so it must never come back empty.
let private fallbackVersion = "2025.06.30"

let informationalVersion =
    try
        let attrs = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof<AssemblyInformationalVersionAttribute>, false)
        if attrs.Length = 0 then fallbackVersion
        else
            let value = (attrs.[0] :?> AssemblyInformationalVersionAttribute).InformationalVersion
            if String.IsNullOrWhiteSpace(value) then fallbackVersion else value
    with _ -> fallbackVersion
