namespace Bemo
open System

module NumberShortcutRules =
    let allows mode (paths:Set2<string>) path =
        let listed = paths.items.list |> List.exists(fun item -> String.Equals(item,path,StringComparison.OrdinalIgnoreCase))
        if mode="OnlyListed" then listed else not listed
    let enabled path =
        allows (Services.settings.getValue("numberShortcutAppMode") :?> string) (Services.settings.getValue("numberShortcutPaths") :?> Set2<string>) path
    let setEnabled path enabled =
        let mode = Services.settings.getValue("numberShortcutAppMode") :?> string
        let paths = Services.settings.getValue("numberShortcutPaths") :?> Set2<string>
        let remaining = Set2(paths.items.where(fun item -> not (String.Equals(item,path,StringComparison.OrdinalIgnoreCase))))
        Services.settings.setValue("numberShortcutPaths",box(if enabled=(mode="OnlyListed") then remaining.add path else remaining))

