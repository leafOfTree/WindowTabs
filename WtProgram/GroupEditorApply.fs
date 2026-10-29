namespace Bemo
open System

/// What applying a plan did, told once every group has settled.
type GroupEditorOutcome = {
    /// Windows that joined a group.
    moved: int
    /// Groups made.
    made: int
    /// Windows taken out to stand on their own.
    detached: int
    /// Windows that were to join a group and could not: gone meanwhile, or their group was.
    skipped: IntPtr list }

/// Save in the group editor: the plan's steps on the main thread, which owns membership; each
/// group's order and layout on its own thread once its windows have arrived. The main thread
/// never waits on a group's; the groups post back through the main invoker.
module GroupEditorApply =
    /// The desktop's groups still at work.
    let private liveGroups() =
        Services.desktop.groups.list |> List.choose(fun group ->
            match group with
            | :? GroupInfo as info when not info.isExited && not info.isRetiring -> Some info
            | _ -> None)
    let private groupOf (groups:GroupInfo list) hwnd = groups |> List.tryFind(fun info -> (info :> IGroup).windows.contains((=) hwnd))

    /// Runs the plan; `finished` is called on the main thread once every group has settled.
    let run (plan:DesktopPlan) (finished:GroupEditorOutcome -> unit) =
        let main = InvokerService.invoker
        let program = Services.program
        let groups = liveGroups()
        let skipped = Collections.Generic.List<IntPtr>()
        let moved = ref 0
        let made = ref 0
        // The run itself, then one per group with work queued.
        let pending = ref 1
        program.suspendTabMonitoring()
        let settle() =
            pending.Value <- pending.Value-1
            if pending.Value=0 then
                program.resumeTabMonitoring()
                finished {moved=moved.Value; made=made.Value; detached=plan.detached.Length; skipped=List.ofSeq skipped}
        for hwnd in plan.detached do program.detachTab hwnd
        for step in plan.groups do
            let joining,gone = step.joining |> List.partition WinUserApi.IsWindow
            skipped.AddRange gone
            let target =
                match step.group with
                | Some strip -> groups |> List.tryFind(fun info -> info.hwnd=strip)
                | None when joining.IsEmpty -> None
                | None ->
                    match Services.desktop.createGroup(Services.settings.getValue("combineIconsInTaskbar") :?> bool) with
                    | :? GroupInfo as info -> made.Value <- made.Value+1; Some info
                    | _ -> None
            match target with
            | None -> skipped.AddRange joining
            | Some target ->
                pending.Value <- pending.Value+1
                // Once every window handed over from another group is in: the order and the
                // layout, queued behind the windows added from no group.
                let apply() =
                    target.invokeGroup(fun () ->
                        if not target.isExited then
                            if not joining.IsEmpty then target.group.isManual <- true
                            if step.reorder then target.group.run(SetTabOrder step.tabs)
                            step.layout |> Option.iter(fun layout -> target.group.execute(ReplaceLayout(SplitView.layoutOf layout)))
                        main.asyncInvoke settle)
                let waiting = ref 0
                let arrived hwnd added =
                    main.asyncInvoke(fun () ->
                        if added then moved.Value <- moved.Value+1 else skipped.Add hwnd
                        waiting.Value <- waiting.Value-1
                        if waiting.Value=0 then apply())
                for hwnd in joining do
                    match groupOf groups hwnd with
                    | Some source when source.hwnd<>target.hwnd ->
                        waiting.Value <- waiting.Value+1
                        target.addAfterDetaching(source,hwnd,afterAdd=arrived hwnd)
                    | Some _ -> ()
                    | None ->
                        moved.Value <- moved.Value+1
                        (target :> IGroup).addWindow(hwnd,false)
                if waiting.Value=0 then apply()
        settle()
