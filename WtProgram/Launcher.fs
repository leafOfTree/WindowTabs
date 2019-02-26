namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open System.Threading
open System.Windows.Forms
open Microsoft.FSharp.Reflection
open Bemo.Win32
//open Bemo.Licensing
open Newtonsoft.Json
open Newtonsoft.Json.Linq
    
type LaunchGroup(gi:IGroup, commands:List2<_>) as this =
    
    let pidsCell = ref (Set2())
    member this.group = gi
    member this.pids = pidsCell.Value
    member this.removePid(id) = pidsCell := pidsCell.Value.remove id
    member this.run(fileName, arguments:string option) =
        let psi = ProcessStartInfo()
        psi.UseShellExecute <- true
        psi.FileName <- fileName
        if arguments.IsSome then
            psi.Arguments <- arguments.Value

        let proc = Process.Start(psi)
        proc.Refresh()
        proc.Id

    member this.launch() =
        pidsCell := Set2(commands.map <| fun(cmd,args) ->
            this.run(cmd, args)
        )

type Launcher() as this =
    let Cell = CellScope()
    let groupsCell = Cell.create(Set2<LaunchGroup>())

    member this.launch(group, commands) =
        let lg = LaunchGroup(group, commands)
        groupsCell.map(fun s -> s.add lg)
        lg.launch()

    member this.isLaunching(group) = groupsCell.value.items.any(fun lg -> lg.group = group)

    member this.findGroup(window:Window) =
        let pid = window.pid.pid
        let lg = groupsCell.value.items.tryFind(fun lg -> lg.pids.contains(pid))
        match lg with
        | Some(lg) -> 
            lg.removePid(pid)
            if lg.pids.count = 0 then
                groupsCell.map(fun s -> s.remove lg)
            let group = lg.group
            Some(Some(group))
        | None -> None
