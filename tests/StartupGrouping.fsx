#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.IO
open System.Threading
open System.Windows.Forms
open Bemo

let check condition message = if not condition then failwith message
let pumpUntil description predicate =
    let clock = Diagnostics.Stopwatch.StartNew()
    let mutable satisfied = predicate()
    while not satisfied && clock.ElapsedMilliseconds<10000L do
        Application.DoEvents()
        Thread.Sleep(5)
        satisfied <- predicate()
    check satisfied description

let main() =
    // Exercise production initialization and the settings editor through its posted adapter.
    // Own-process fixture HWNDs are explicitly whitelisted instead of inspecting user apps.
    let runtimeDirectory = Path.Combine(__SOURCE_DIRECTORY__,"Debug","runtime-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(runtimeDirectory) |> ignore
    let previousDirectory = Environment.CurrentDirectory
    Environment.CurrentDirectory <- runtimeDirectory
    try
        let runtimeSettings = new Settings(true,saveDelay=0)
        let api = runtimeSettings :> ISettings
        api.setValue("replaceAltTab",box false)
        api.setValue("enableTabbingByDefault",box false)
        api.setValue("combineIconsInTaskbar",box false)
        api.setValue("autoHideMode",box "Never")
        let path = Diagnostics.Process.GetCurrentProcess().MainModule.FileName
        api.setValue("includedPaths",box(Set2(List2([path]))))
        use lifetime = new LifetimeScope(fun error -> raise error)
        let program = Program.Program(lifetime,settings=runtimeSettings)
        let scansBefore,_,_ = RuntimeMetrics.snapshot()
        let input = {new IStartupInputPlugin with
                        member _.init() =
                            let scans,_,_ = RuntimeMetrics.snapshot()
                            check (scans>scansBefore) "Production startup installed input hooks before its queued discovery ran"} :> IPlugin
        program.initializePlugins(List2([input]))
        use first = new Form(ShowInTaskbar=false)
        use second = new Form(ShowInTaskbar=false)
        let handles = [first.Handle;second.Handle]
        let mutable discovering = true
        let filter = FilterService() :> IFilterService
        Services.registerLocal<IFilterService>({new IFilterService with
            member _.isAppWindow hwnd = List.contains hwnd handles
            member _.isAppWindowStyle hwnd = List.contains hwnd handles
            member _.isTabbableWindow hwnd = discovering && List.contains hwnd handles && filter.getIsTabbingEnabledForProcess path
            member _.isTabbingEnabledForAllProcessesByDefault with get()=false and set _=()
            member _.setIsTabbingEnabledForProcess value enabled = filter.setIsTabbingEnabledForProcess value enabled
            member _.setIsTabbingEnabledForProcesses values enabled = filter.setIsTabbingEnabledForProcesses values enabled
            member _.getIsTabbingEnabledForProcess value = filter.getIsTabbingEnabledForProcess value})
        let dispatcher = InvokerService.invoker :> IDispatcher
        let nativeWindows (group:GroupInfo) =
            let mutable result = None
            group.invokeGroup(fun () ->
                let windows = group.group.windows.items.list
                dispatcher.Post(fun () -> result <- Some windows))
            pumpUntil "Runtime group did not answer its membership request" (fun () -> result.IsSome || group.isExited)
            result |> Option.defaultValue []
        let manual = Services.desktop.createGroup(false) :?> GroupInfo
        for hwnd in handles do (manual :> IGroup).addWindow(hwnd,false)
        pumpUntil "Runtime manual group did not settle" (fun () -> nativeWindows manual |> List.length = 2)
        try
            let mutable disabledDuringRegroup = false
            use tabChoice = api.notifyValue "excludedPaths" (fun value ->
                if (value :?> Set2<string>).contains path then disabledDuringRegroup <- true)
            use form = new Form(ClientSize=Drawing.Size(900,320),ShowInTaskbar=false)
            let editor = new AppGroupingRulesView(Dock=DockStyle.Fill)
            form.Controls.Add(editor)
            form.Show()
            let rec controls (control:Control) = seq {
                yield control
                for child in control.Controls do yield! controls child }
            let click text =
                controls editor |> Seq.pick(function :? Button as button when button.Text=text -> Some button | _ -> None) |> fun button -> button.PerformClick()
            let picker = controls editor |> Seq.pick(function :? GroupingApplicationPicker as picker -> Some picker | _ -> None)
            let selectApp() =
                picker.UpdateApplications [path]
                let combo = picker.Controls |> Seq.cast<Control> |> Seq.pick(function :? SettingsCombo as combo -> Some combo | _ -> None)
                combo.SelectedIndex <- 1+Array.findIndex ((=)path) picker.Applications
            let waitForTarget target =
                pumpUntil "Editing a rule did not regroup existing HWNDs through the production adapter" (fun () ->
                    let groups = Services.desktop.groups.list |> List.map(fun group -> group :?> GroupInfo)
                    let matching = groups |> List.filter(fun group -> group.ruleGroupId=Some target)
                    let moved = matching |> List.collect nativeWindows
                    handles |> List.forall(fun hwnd -> List.contains hwnd moved && (groups |> List.forall(fun group ->
                        not((group :> IGroup).windows.contains((=)hwnd)) || group.ruleGroupId=Some target))))
            click (tr Strings.AppRules.addGroup)
            let firstTarget = (api.getValue("appGroupingTargets") :?> AppGroupingTarget list).Head.id
            selectApp()
            click (tr Strings.AppRules.addApplication)
            waitForTarget firstTarget
            click (tr Strings.AppRules.addGroup)
            let secondTarget = (api.getValue("appGroupingTargets") :?> AppGroupingTarget list) |> List.last |> fun group -> group.id
            selectApp()
            click (tr Strings.AppRules.addApplication)
            waitForTarget secondTarget
            check (not disabledDuringRegroup && filter.getIsTabbingEnabledForProcess path) "Rule editing transiently disabled the application's tabs"
            check (handles |> List.forall WinUserApi.IsWindow) "Rule editing destroyed an application HWND"
            form.Close()
        finally
            discovering <- false
            program.refresh()
            pumpUntil "Runtime review fixture left group STAs running" (fun () -> Services.desktop.groups.isEmpty)
    finally Environment.CurrentDirectory <- previousDirectory
    printfn "PASS: production startup waits for discovery and rule edits regroup existing HWNDs after native cleanup without disabling tabs."

TestInit.run main
