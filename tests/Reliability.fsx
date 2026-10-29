#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Threading
open System.Collections.Concurrent
open Bemo
open Bemo.Json

let check condition message = if not condition then failwith message

module CloakProbe =
    [<System.Runtime.InteropServices.DllImport("dwmapi.dll")>]
    extern int DwmSetWindowAttribute(nativeint hwnd, int attribute, int& value, int size)
    [<System.Runtime.InteropServices.DllImport("user32.dll")>]
    extern nativeint SendMessage(nativeint hwnd, int msg, nativeint wParam, nativeint lParam)

/// Runs a check while handling a message sent from another thread, an input-synchronous call.
type SyncCallWindow(onMessage:unit -> unit) =
    inherit System.Windows.Forms.NativeWindow()
    override this.WndProc(m:System.Windows.Forms.Message byref) =
        if m.Msg = 0x8001 then onMessage() else base.WndProc(&m)

let main() =
    let queued = ResizeArray<unit -> unit>()
    let mutable desktopReads = 0
    let desktopA,desktopB = Guid.NewGuid(),Guid.NewGuid()
    let mutable queriedDesktop = desktopA
    let mutable lookupTime = DateTime.UtcNow
    let lookup = DesktopLookup((fun _ -> desktopReads<-desktopReads+1;Some queriedDesktop),queued.Add,now=(fun () -> lookupTime))
    let mutable desktopReady = 0
    use desktopSubscription = lookup.Ready.Subscribe(fun _ -> desktopReady<-desktopReady+1)
    let probeHwnd = IntPtr(123)
    for _ in 1..100 do check ((lookup.TryGet probeHwnd).IsNone) "Discovery waited for or invented an unfinished shell answer"
    check (desktopReads=0 && queued.Count=1) "Discovery ran a shell query inline or duplicated pending work"
    queued.[0]()
    check (lookup.TryGet probeHwnd=Some(Some desktopA) && desktopReady=1) "A completed desktop query did not wake discovery"
    lookupTime <- lookupTime.AddSeconds(2.0)
    check (lookup.TryGet probeHwnd=Some(Some desktopA) && queued.Count=2) "Desktop moves could not refresh a cached answer"
    lookup.Forget probeHwnd
    check ((lookup.TryGet probeHwnd).IsNone && queued.Count=3) "A reused HWND inherited a previous desktop answer"
    queued.[1]()
    check ((lookup.TryGet probeHwnd).IsNone && desktopReady=1) "An obsolete shell query republished a destroyed window"
    queriedDesktop <- desktopB
    queued.[2]()
    check (lookup.TryGet probeHwnd=Some(Some desktopB) && desktopReady=2) "A reused HWND lost its new desktop answer"
    let desktop value () = value
    let unexpectedDesktop() = failwith "A confirmed desktop unnecessarily fell back to the visible desktop"
    check (VirtualDesktop.resolveGroupingDesktop (desktop(Some desktopB)) unexpectedDesktop unexpectedDesktop unexpectedDesktop=Some desktopB)
          "A known background desktop was replaced by the current visible desktop"
    check (VirtualDesktop.resolveGroupingDesktop (desktop None) (desktop(Some desktopB)) unexpectedDesktop unexpectedDesktop=Some desktopB)
          "A grouped window lost its taskbar owner's desktop"
    check (VirtualDesktop.resolveGroupingDesktop (desktop None) (desktop None) (desktop(Some desktopA)) unexpectedDesktop=Some desktopA)
          "An unregistered grouped window lost its previously confirmed desktop after a desktop switch"
    check (VirtualDesktop.resolveGroupingDesktop (desktop None) (desktop None) (desktop None) (desktop(Some desktopA))=Some desktopA)
          "A visible unregistered application window could not resolve its desktop"
    check ((VirtualDesktop.resolveGroupingDesktop (desktop None) (desktop None) (desktop None) (desktop None)).IsNone)
          "An unconfirmed desktop became a wildcard grouping target"
    let appRule target kind title =
        { id=Guid.NewGuid().ToString("N");enabled=true;processPath = @"C:\Editor.exe";title=title;matchType=kind
          groupId=target;groupName=target }
    let broad = appRule "General" -1 ""
    let legacy = appRule "Project A" 3 "Project A"
    let browser = {broad with id="browser";processPath = @"C:\Browser.exe"}
    check (AppGroupingRules.resolve [broad] @"c:\EDITOR.exe" "Any title"=GroupingTarget "General") "Application grouping compared titles or path case"
    check (AppGroupingRules.resolve [{broad with enabled=false}] broad.processPath ""=NoGroupingRule) "Disabled application rule was applied"
    check (AppGroupingRules.resolve [broad] browser.processPath ""=NoGroupingRule) "Application rule matched another executable"
    check (AppGroupingRules.hasEnabled [broad] @"c:\editor.exe") "Custom grouping did not claim its app case-insensitively"
    check (AppGroupingRules.resolve [broad] @"D:\Updated\EDITOR.exe" ""=GroupingTarget "General") "A custom grouping rule did not apply across installation directories"
    check (AppGroupingRules.withoutAutoGrouping [broad] [@"c:\editor.exe";browser.processPath]=[browser.processPath]) "Custom grouping retained duplicate automatic grouping"
    check (AppGroupingRules.withoutAutoGrouping [{broad with enabled=false}] [broad.processPath]=[broad.processPath]) "Disabled custom grouping disabled an independent automatic choice"
    let rootRules = JObject(JProperty("appGroupingRules",AppGroupingRules.write [broad;browser]))
    check (AppGroupingRules.read rootRules=[broad;browser]) "Application grouping lost its paths or destination"
    let aliases = JObject(JProperty("appGroupingRules",AppGroupingRules.write [broad;{broad with id="alias";processPath= @"D:\Other\EDITOR.exe"}]))
    check (AppGroupingRules.read aliases=[broad]) "Imported aliases created multiple grouping destinations for one executable"
    let migrated = JObject(JProperty("appGroupingRules",AppGroupingRules.write [legacy;broad]))
    check (AppGroupingRules.read migrated=[{legacy with title="";matchType= -1}]) "Legacy title rules did not become one application mapping"
    let malformedRules = rootRules.DeepClone() :?> JObject
    (malformedRules.["appGroupingRules"] :?> JArray).Add(JObject(JProperty("processPath","broken")))
    let invalidPath = (AppGroupingRules.write [broad]).[0].DeepClone()
    invalidPath.["processPath"] <- JValue("broken\u0000.exe")
    (malformedRules.["appGroupingRules"] :?> JArray).Add(invalidPath)
    check (AppGroupingRules.read malformedRules=[broad;browser]) "Malformed import erased valid application mappings"
    check (AppGroupingRules.read (SettingsCatalog.resetRoot rootRules false false)=[broad;browser]) "Reset discarded application grouping without clearing app rules"
    check (AppGroupingRules.read (SettingsCatalog.resetRoot rootRules true false)=[]) "Clearing app rules retained application grouping"
    let desktopA,desktopB = Guid.NewGuid(),Guid.NewGuid()
    let desktopOf hwnd = if hwnd=1 then Some desktopA elif hwnd=2 then Some desktopB else None
    let groupRules = ["A",[1];"A",[2];"B",[1]]
    let pick hwnd = AppGroupingRules.pick snd (fst >> Some) desktopOf groupRules "A" hwnd
    check (pick 1=Some("A",[1]) && pick 2=Some("A",[2])) "Named grouping crossed virtual desktops"
    check (pick 3=None) "An unresolved named-rule window joined another desktop's group"
    check (AppGroupingRules.pick snd (fst >> Some) (fun _ -> None) ["A",[1]] "A" 2=None)
          "Unresolved named-rule windows were assumed to share a desktop"
    let mutable resolvedDesktop = None
    let pendingDesktop hwnd = if hwnd=1 then Some desktopA else resolvedDesktop
    let pendingPick() = AppGroupingRules.pick snd (fst >> Some) pendingDesktop ["A",[1]] "A" 2
    check ((pendingPick()).IsNone) "A pending query could pull a new window onto the first desktop"
    resolvedDesktop <- Some desktopB
    check ((pendingPick()).IsNone) "Completing a query for a different desktop allowed adoption"
    resolvedDesktop <- Some desktopA
    check (pendingPick()=Some("A",[1])) "A confirmed same-desktop window could not join its target"
    // A system theme change deadlocked when SystemEvents ran on the main thread while a tab
    // group thread waited on it for the appearance.
    ThemeService.moveSystemEventsOffMainThread()
    let mutable eventsThread = null
    use delivered = new ManualResetEventSlim(false)
    Microsoft.Win32.SystemEvents.InvokeOnEventsThread(Action(fun () -> eventsThread <- Thread.CurrentThread.Name; delivered.Set()))
    check (delivered.Wait(5000) && eventsThread=".NET SystemEvents") "SystemEvents does not have a thread of its own"
    ThemeService.publishPreferences {
        geometry=Theme.defaultGeometry; legacyPalette=Theme.lightPalette; lightPalette=Theme.lightPalette; darkPalette=Theme.darkPalette
        lightCustomPalette=Theme.lightPalette; darkCustomPalette=Theme.darkPalette; mode=ThemeMode.parse "dark"; useCustomColors=false
        lightPreset=""; darkPreset=""; presetEdits=Map.empty }
    let mutable dark = false
    let reader = Thread(fun () -> dark <- ThemeService.currentIsDark())
    reader.Start()
    check (reader.Join(5000) && dark) "Reading the appearance off the settings thread went through Services.settings"

    let events = ResizeArray<int>()
    let errors = ResizeArray<exn>()
    let scope = new LifetimeScope(errors.Add)
    for value in 1..3 do
        scope.Own({new IDisposable with member _.Dispose() = events.Add(value); if value=2 then failwith "cleanup"}) |> ignore
    (scope :> IDisposable).Dispose()
    (scope :> IDisposable).Dispose()
    check (Seq.toList events=[3;2;1] && errors.Count=1) "Cleanup did not complete once in reverse order"
    let keyboardHook = OS().registerKeyboardLLHook(fun _ -> None)
    keyboardHook.Dispose()
    keyboardHook.Dispose()
    let helperWindow = OS().createWindow (fun message -> message.def()) 0 0
    (helperWindow :?> IDisposable).Dispose()
    (helperWindow :?> IDisposable).Dispose()
    let name = "WindowTabs-Test-"+Guid.NewGuid().ToString("N")
    use instance = new SingleInstance(name)
    check (instance.TryAcquire()) "First singleton lock failed"
    let mutable duplicate = true
    let thread = Thread(ThreadStart(fun () -> use other = new SingleInstance(name) in duplicate <- other.TryAcquire()))
    thread.Start()
    thread.Join()
    check (not duplicate) "Duplicate process lock accepted"
    // Sign-in follows the copy started last: an entry naming an older copy, or none, is replaced.
    let exe = @"C:\Apps\WindowTabs\WindowTabs.exe"
    check (not(Program.RunAtStartup.isStale (Program.RunAtStartup.command exe) exe)) "The current copy's sign-in entry was taken as stale"
    check (not(Program.RunAtStartup.isStale (Program.RunAtStartup.command (exe.ToLowerInvariant())) exe)) "A sign-in entry differing only in case was taken as stale"
    check (Program.RunAtStartup.isStale (Program.RunAtStartup.command @"C:\Old\WindowTabs.exe") exe && Program.RunAtStartup.isStale null exe)
          "A sign-in entry for another copy, or none, was kept"

    let registered = Collections.Generic.HashSet<int>()
    let operations = ResizeArray<string>()
    let mutable allow = true
    let mutable invoked = 0
    use keys = new HotKeyBindings((fun (id,_,_) -> operations.Add("register"); allow && registered.Add(id)),
                                  (fun id -> operations.Add("release"); registered.Remove(id) |> ignore))
    check (keys.Register "action" (2,65) (fun () -> invoked <- invoked+1)) "Initial hotkey failed"
    let original = Seq.head registered
    allow <- false
    check (not(keys.Register "action" (2,66) ignore)) "Conflicting hotkey accepted"
    keys.Invoke original
    check (invoked=1 && registered.Contains(original)) "Failed replacement lost working hotkey"
    allow <- true
    operations.Clear()
    check (keys.Register "action" (2,66) ignore) "Replacement failed"
    check (Seq.toList operations=["register";"release"] && registered.Count=1) "Hotkey replacement was not transactional"
    keys.Register "action" (0,0) ignore |> ignore
    check (registered.Count=0) "Disabling hotkey leaked registration"
    // While a shortcut is recorded, every key is let go so the box can have it, and taken back after.
    invoked <- 0
    check (keys.Register "palette" (2,80) (fun () -> invoked <- invoked+1)) "Hotkey for recording test failed"
    let paletteId = Seq.head registered
    keys.Suspend()
    check (registered.Count=0) "Recording a shortcut kept the app's keys from the box"
    keys.Invoke paletteId
    check (invoked=0) "A key ran its command while a shortcut was recorded"
    keys.Resume()
    check (registered.Contains paletteId) "The keys were not taken back after recording"
    keys.Invoke paletteId
    check (invoked=1) "A key taken back did not run its command"
    // A shortcut set while the keys are let go, as in a full-screen game, waits to be taken with the rest.
    keys.Suspend()
    check (keys.Register "split" (2,81) ignore && registered.Count=0) "A shortcut set while the keys were let go took its key at once"
    keys.Resume()
    check (registered.Count=2) (sprintf "A shortcut set while the keys were let go was not taken with the rest: %d" registered.Count)
    // A box's recording tells the program, once for the first and once for the last.
    let seen = ResizeArray<bool>()
    use watching = ShortcutRecording.Changed.Subscribe(seen.Add)
    ShortcutRecording.start()
    ShortcutRecording.start()
    ShortcutRecording.stop()
    check (ShortcutRecording.isActive() && List.ofSeq seen=[true]) "A second recording box let the keys back too soon"
    ShortcutRecording.stop()
    ShortcutRecording.stop()
    check (not (ShortcutRecording.isActive()) && List.ofSeq seen=[true;false]) "The keys did not come back once recording ended"

    // Settings files written by older versions (with Json.NET) read and write back unchanged.
    let seen = DateTime(2026,10,16,8,30,0,DateTimeKind.Utc)
    let written = JObject(JProperty("text","quote\" slash\\ tab\t nul\u0000 line\u2028 \u00e9"),JProperty("count",3),
                          JProperty("ratio",12.0),JProperty("big",9007199254740993L),JProperty("on",true),JProperty("none",null),
                          JProperty("list",JArray([1;2])),JProperty("empty",JObject()),JProperty("seen",seen))
    let expected =
        String.Join("\r\n",["{";"  \"text\": \"quote\\\" slash\\\\ tab\\t nul\\u0000 line\\u2028 \u00e9\",";"  \"count\": 3,";"  \"ratio\": 12.0,"
                            "  \"big\": 9007199254740993,";"  \"on\": true,";"  \"none\": null,";"  \"list\": [";"    1,";"    2";"  ],"
                            "  \"empty\": {},";"  \"seen\": \"2026-10-16T08:30:00Z\"";"}"])
    check (written.ToString()=expected) "Settings JSON is no longer written as older versions wrote it"
    let reread = JObject.Parse(expected)
    check (reread.ToString()=expected) "Settings JSON did not read and write back unchanged"
    check ((written.["count"] :?> JValue).Value :? int && (reread.["count"] :?> JValue).Value :? int64) "JSON integers changed type"
    check (reread.Value<DateTime>("seen")=seen && reread.Value<DateTime>("seen").Kind=DateTimeKind.Utc) "A saved time did not read back as UTC"
    // Hand-edited files that Json.NET accepted still load: comments, quotes, bare names, trailing
    // commas, octal and hex, repeated names and the NUL padding a crash can leave.
    let edited = JObject.Parse("// note\n{'a':1, b:[1,2,], /* c */ \"c\":010, \"d\":0x1F, \"c\":2,}\u0000\u0000")
    check (edited.ToString(Formatting.None)="""{"a":1,"b":[1,2],"c":2,"d":31}""") "A hand-edited settings file no longer loads as before"
    let nested depth = String.replicate depth "{\"a\":" + "1" + String.replicate depth "}"
    check ((JObject.Parse(nested 64)).Count=1) "Deep settings JSON was rejected"
    for broken in ["";"{";"{\"a\":}";"{\"a\":1}x";"[1]";"{\"a\":\"\\q\"}";nested 65] do
        check (try JObject.Parse(broken) |> ignore; false with _ -> true) ("Malformed settings JSON was accepted: "+broken)
    // A token already in a tree is copied when added to another, so later edits stay apart.
    let shared = JObject(JProperty("inner",JObject(JProperty("x",1))))
    let other = JObject()
    other.["copy"] <- shared.["inner"]
    (other.["copy"] :?> JObject).["x"] <- JValue(2)
    check (shared.ToString(Formatting.None)="""{"inner":{"x":1}}""") "A JSON token added to a second tree was shared, not copied"

    let root = JObject.Parse("""{"workspaces":[{"name":"valid","groups":[{"placement":{"x":10,"y":20,"width":800,"height":600,"showCmd":1},"windows":[{"title":"Documents","matchType":0},{"title":"[","matchType":4}]}]},7]}""")
    let before = root.ToString()
    let workspaces,warnings,readOnly = WorkspaceData.read root
    check (workspaces.Length=1 && warnings.Length=2 && not readOnly) "Partial workspace recovery rejected valid entries"
    check (root.ToString()=before) "Workspace validation mutated saved data"
    let group = workspaces.Head.["groups"].[0] :?> JObject
    let placement = WorkspaceData.placement (group.["placement"] :?> JObject)
    check (placement.rcNormalPosition.x=10 && placement.rcNormalPosition.width=800) "Legacy placement migration lost bounds"
    let restored = WorkspaceData.placement (WorkspaceData.writePlacement placement)
    check (restored=placement) "Placement round trip lost geometry"
    // WorkspaceData.read rewrites numbers as Int32 JValues; the model must still load them.
    let loaded = Workspace.deserialize workspaces.Head
    let window = (group.["windows"].[0] :?> JObject)
    check (loaded.name="valid" && JObject(JProperty("n",JValue(7))).getInt32("n")=Some 7 && window.getInt32("matchType")=Some 0) "Normalized workspace could not be loaded"
    // New snapshots use app identity; older snapshots still match by title.
    let savedWindow title path zorder =
        let window = WorkspaceWindow()
        window.name <- "App"
        window.title <- title
        window.processPath <- path
        window.matchType <- WorkspaceWindowTitleMatchType.ExactMatch
        window.zorder <- zorder
        window
    let first = savedWindow "Shared title" @"C:\Apps\Editor.exe" 1
    let second = savedWindow "Shared title" @"C:\Apps\Browser.exe" 0
    let savedGroup = WorkspaceGroup(name="ordered",placement=placement)
    savedGroup.addWindow first
    savedGroup.addWindow second
    let roundTrip = WorkspaceGroup.deserialize(savedGroup.serialize())
    check roundTrip.tabLayout.IsNone "Legacy workspace acquired an unintended tab layout"
    for dock in [TabDockTop;TabDockLeft;TabDockRight] do
        let layout = {dock=dock;columnWidth=284;normalAlignment=TabRight;insideAlignment=TabLeft}
        savedGroup.tabLayout <- Some layout
        let restored = WorkspaceGroup.deserialize(savedGroup.serialize())
        check (restored.tabLayout=Some layout) "Workspace lost tab position, logical width or alignment"
    let invalidLayout = root.DeepClone() :?> JObject
    invalidLayout.["workspaces"].[0].["groups"].[0].["tabLayout"] <- JObject(JProperty("position","invalid"))
    let originalLayout = invalidLayout.ToString()
    let validGroups,layoutWarnings,_ = WorkspaceData.read invalidLayout
    check (validGroups.Length=1 && layoutWarnings.Length=3 && invalidLayout.ToString()=originalLayout)
          "Damaged tab layout discarded windows or changed saved recovery data"
    check ((Workspace.deserialize validGroups.Head).groups.head.cast<WorkspaceGroup>().tabLayout.IsNone)
          "Damaged tab layout did not fall back to current defaults"
    let oversized = WorkspaceData.tabLayout (JObject(JProperty("position","Right"),JProperty("columnWidth",10000)))
    check (oversized.columnWidth=snd SettingsCatalog.columnWidthRange) "Workspace width escaped the supported range"
    for schema in [1;2;3] do
        let compatible = JObject(JProperty("workspaceSchemaVersion",schema),JProperty("workspaces",JArray(savedGroup.serialize() |> fun group -> JObject(JProperty("groups",JArray(group))))))
        let entries,_,blocked = WorkspaceData.read compatible
        check (entries.Length=1 && not blocked) "A supported workspace schema became read-only"
    // A session save that finds the windows as they were, as read back from the file, does not
    // rewrite the settings; one with a title changed does, and so does the first.
    let writtenGroup = savedGroup.serialize()
    let readBack = JObject.Parse(JObject(JProperty("session",writtenGroup)).ToString()).["session"]
    check (WorkspaceModel.sameJson readBack (savedGroup.serialize())) "An unchanged session would rewrite the settings file"
    let retitled = savedGroup.serialize()
    (retitled.["windows"].[0] :?> JObject).["title"] <- JValue("Another title")
    check (not (WorkspaceModel.sameJson readBack retitled) && not (WorkspaceModel.sameJson null writtenGroup))
          "A changed or first session would not be written"
    let resolver = WindowResolver(List2([IntPtr(10),"Shared title",@"c:\apps\browser.EXE";
                                         IntPtr(20),"Shared title",@"c:\apps\editor.EXE"]))
    let restore resolver groups missing =
        (WorkspaceRestore.resolve resolver (List2(groups |> List.mapi(fun i windows -> i,windows))) missing raise).map snd
    let matched = (restore resolver [roundTrip.windows] (fun _ -> failwith "Missing saved app")).head
    check (matched.map snd |> fun handles -> handles.list=[IntPtr(20);IntPtr(10)]) "Restoring sorted tabs by z-order or matched another app"
    check ((WorkspaceRestore.zorder matched).list=[IntPtr(10);IntPtr(20)]) "Restoring tab order lost window z-order"
    // A missing window is named by its app and its whole title, however long.
    check (WorkspaceRestore.describe (first :> Dynamic)=(first.name,first.title)) "A missing window was not named by its app and full title"
    // Only apps that are not running, exist and start by their path are offered, once each.
    let launchable =
        WorkspaceRestore.launchable [@"C:\Apps\Running.exe"] (fun path -> not(path.Contains("Gone")))
            [@"C:\Apps\Editor.exe";@"c:\apps\EDITOR.exe";@"C:\apps\running.EXE";@"C:\Apps\Gone.exe";""
             @"C:\Windows\System32\ApplicationFrameHost.exe"]
    check (launchable=[@"C:\Apps\Editor.exe"]) (sprintf "Wrong apps offered to launch: %A" launchable)
    // Each window left out says why: its app open with no window for it, started without one in
    // time, would not start, a Store app, its file gone, not running, or saved without its app.
    let why = WorkspaceRestore.whyMissing [@"C:\Apps\Running.exe"] (fun path -> not(path.Contains("Gone")))
                  [@"C:\Apps\Slow.exe",None;@"C:\Apps\Denied.exe",Some "Access is denied."]
    let reasons = [@"c:\apps\running.EXE";@"C:\Apps\Slow.exe";@"C:\Apps\Denied.exe";@"C:\Windows\System32\ApplicationFrameHost.exe"
                   @"C:\Apps\Gone.exe";@"C:\Apps\Closed.exe";""] |> List.map why
    check (reasons=[NoWindowLeft;NoWindowAfterStart;StartFailed "Access is denied.";StoreApp;AppGone;NotRunning;UnknownApp])
          (sprintf "Wrong reasons for windows not found: %A" reasons)
    // After starting apps, the result knows which were started and which would not start, and
    // lists every window restored over both tries.
    let leftOut name title path =
        let window = savedWindow title path 0
        window.name <- name
        window
    let slow = leftOut "Slow.exe" "Report" @"C:\Apps\Slow.exe"
    let denied = leftOut "Denied.exe" "" @"C:\Apps\Denied.exe"
    let firstTry = { WorkspaceRestoreResult.Of("Session",1,[],[]) with windows=["Editor.exe","Notes"]
                                                                       launchable=[@"C:\Apps\Slow.exe";@"C:\Apps\Denied.exe"] }
    let secondTry = { WorkspaceRestoreResult.Of("Session",1,["Slow.exe","Report";"Denied.exe",""],[]) with
                        windows=["Other.exe","Mail"]; pending=[null,slow;null,denied] }
    let both = firstTry.Then(secondTry,["Denied.exe","Access is denied."])
    check (both.windows=["Editor.exe","Notes";"Other.exe","Mail"] && both.launches=[@"C:\Apps\Slow.exe",None;@"C:\Apps\Denied.exe",Some "Access is denied."])
          (sprintf "The second try lost the windows restored or the apps started: %A %A" both.windows both.launches)
    let notFound = both.notFound(fun _ -> true)
    check (notFound=["Slow.exe","Report",NoWindowAfterStart;"Denied.exe","",StartFailed "Access is denied."])
          (sprintf "Windows not found named the wrong reasons: %A" notFound)
    // The notice lists every window left out as an entry, its title over its app and why, or
    // its app alone over why, then the apps restored as a note.
    Localization.setPreference "en"
    let lines = { both with windows=both.windows@["notepad.exe","A";"Notepad.exe","B"] }.noticeLines(fun _ -> true)
    Localization.setPreference "system"
    check (lines=[Entry("Report","Slow.exe · App started, but showed no window in time")
                  Entry("Denied.exe","App would not start: Access is denied.")
                  Note "Restored: editor.exe, other.exe, notepad.exe ×2"])
          (sprintf "The restore notice listed the wrong lines: %A" lines)
    check ((lines |> List.map(fun line -> line.text))=["Report — Slow.exe · App started, but showed no window in time"
                                                       "Denied.exe — App would not start: Access is denied."
                                                       "Restored: editor.exe, other.exe, notepad.exe ×2"])
          (sprintf "The restore notice's lines read wrongly as text: %A" (lines |> List.map(fun line -> line.text)))
    // An app started once that opened fewer windows than were saved is started again, once for
    // each window still missing; not one that would not start, nor one that was never started.
    let again = WorkspaceRestore.startAgain [@"C:\Apps\Neovide.exe";@"C:\Apps\Running.exe";@"C:\Apps\Denied.exe"]
                    [@"c:\apps\neovide.EXE",None;@"C:\Apps\Denied.exe",Some "Access is denied.";@"C:\Apps\Slow.exe",None]
                    [@"C:\Apps\Neovide.exe";@"C:\Apps\Neovide.exe";@"C:\Apps\Running.exe";@"C:\Apps\Denied.exe";@"C:\Apps\Slow.exe";""]
    check (again=[@"C:\Apps\Neovide.exe";@"C:\Apps\Neovide.exe"]) (sprintf "Wrong apps started again: %A" again)
    let third = { both with launchable=[@"C:\Apps\Slow.exe";@"C:\Apps\Slow.exe"] }.Then({ secondTry with windows=["Slow.exe","Report"]; pending=[] },[])
    check (third.windows.Length=3 && third.launches.Length=4 && third.pending.IsEmpty)
          (sprintf "Starting apps again lost what the earlier tries did: %A %A" third.windows third.launches)
    check ((resolver.resolve(first,false)).IsNone) "A window was matched twice"
    let wrongApp = WindowResolver(List2([IntPtr(30),"Shared title",@"C:\Other.exe"]))
    check ((wrongApp.resolve(first,true)).IsNone) "A title from another app matched as the saved app"
    check (wrongApp.resolve(first,false)=Some(IntPtr(30))) "An app moved to another folder no longer matched by title"
    let legacy = WorkspaceWindow.deserialize window
    check (legacy.processPath="" && (WindowResolver(List2([IntPtr(30),"Documents",@"C:\Other.exe"]))).resolve(legacy,true)=Some(IntPtr(30))) "Old title-only workspaces stopped matching"
    let missingCount = ref 0
    let unavailable = WindowResolver(List2([IntPtr(40),"Shared title",""]))
    let fallback = restore unavailable [roundTrip.windows] (fun _ -> missingCount.Value <- missingCount.Value+1)
    check ((fallback.head.map snd).list=[IntPtr(40)] && missingCount.Value=1) "An unknown executable path did not fall back to the title or lost missing counts"
    // The editor is gone: its title fallback must not take the browser saved in a later group.
    let moved = WindowResolver(List2([IntPtr(50),"Shared title",@"C:\Other.exe";
                                      IntPtr(60),"Shared title",@"C:\Apps\Browser.exe"]))
    let split = restore moved [List2<Dynamic>([first :> Dynamic]);List2<Dynamic>([second :> Dynamic])] (fun _ -> failwith "Missing saved app")
    check (split.map(fun group -> (group.map snd).list).list=[[IntPtr(50)];[IntPtr(60)]]) "A title fallback took another saved app's window"
    let handles (groups:List2<List2<WorkspaceWindow * IntPtr>>) = groups.map(fun group -> (group.map snd).list).list
    let renamed = WindowResolver(List2([IntPtr(70),"Another document",@"c:\apps\EDITOR.exe"]))
    check (handles (restore renamed [List2<Dynamic>([first :> Dynamic])] (fun _ -> failwith "Missing saved app"))=[[IntPtr(70)]]) "A window whose title changed was not matched by its app"
    let movedApp = WindowResolver(List2([IntPtr(75),"Another document",@"D:\New\Editor.exe"]))
    check (movedApp.resolveApp(first)=Some(IntPtr(75))) "An app moved to another folder was not matched by its exe name"
    // The renamed note prefers the open "b" note by title, but a later group's title match needs it.
    let notesA = savedWindow "Notes - b2.txt" @"C:\Apps\Notes.exe" 0
    let notesB = savedWindow "Notes - b.txt" @"C:\Apps\Notes.exe" 1
    let notes () = WindowResolver(List2([IntPtr(80),"Notes - c.txt",@"C:\Apps\Notes.exe";
                                         IntPtr(90),"Notes - b.txt",@"C:\Apps\Notes.exe"]))
    let noteGroups = [List2<Dynamic>([notesA :> Dynamic]);List2<Dynamic>([notesB :> Dynamic])]
    check (handles (restore (notes()) noteGroups (fun _ -> failwith "Missing saved app"))=[[IntPtr(80)];[IntPtr(90)]]) "An app match took a window that a title match needed"
    check ((notes()).resolveApp(notesA)=Some(IntPtr(90))) "An app match did not prefer the most similar title"
    let otherMissing = ref 0
    let otherApp = WindowResolver(List2([IntPtr(100),"Something else",@"C:\Other.exe"]))
    check (handles (restore otherApp [List2<Dynamic>([first :> Dynamic])] (fun _ -> otherMissing.Value <- otherMissing.Value+1))=[[]] && otherMissing.Value=1) "A window of another app with a different title was taken"
    check ((roundTrip.windows.head :?> WorkspaceWindow).processPath=first.processPath) "Snapshot round trip lost app identity"
    // Monitor work areas are converted to WINDOWPLACEMENT's workspace coordinates.
    let screen x y w h = Rect(Pt(x,y),Sz(w,h))
    let primary = screen 0 0 1920 1080,screen 0 40 1920 1040
    let fit bounds = WorkspaceData.fitPlacement [primary] {placement with rcNormalPosition=bounds}
    check (fit placement.rcNormalPosition=placement) "An unchanged visible layout moved"
    let moved = fit (screen 2500 1500 800 600)
    check (moved.rcNormalPosition=screen 1120 440 800 600 && moved.showCmd=placement.showCmd) "Disconnected monitor layout was not fitted to the work area"
    let smaller = fit (screen -2000 -2000 3000 1800)
    check (smaller.rcNormalPosition=screen 0 0 1920 1040) "Oversized restored window is outside the screen"
    let secondary = screen -1280 0 1280 1024,screen -1280 0 1280 984
    let onSecondary = {placement with rcNormalPosition=screen -1200 30 800 600}
    check (WorkspaceData.fitPlacement [primary;secondary] onSecondary=onSecondary) "Valid negative monitor coordinates moved"
    let leftTaskbar = screen 0 0 1920 1080,screen 40 0 1880 1080
    let leftFit = WorkspaceData.fitPlacement [leftTaskbar] {placement with rcNormalPosition=screen 1800 20 800 600}
    check (leftFit.rcNormalPosition.x=1080) "Left taskbar offset was applied twice"
    let maximized = WorkspaceData.fitPlacement [primary] {placement with showCmd=3;ptMaxPosition=Pt(3000,0);rcNormalPosition=screen 3000 0 800 600}
    check (maximized.showCmd=3 && maximized.ptMaxPosition=Pt(-1,-1)) "Relocated layout kept obsolete maximize coordinates"
    let emptyGroups = JObject.Parse("""{"workspaces":[{"name":"w","groups":[{"placement":{"x":0,"y":0,"width":800,"height":600},"windows":[]},{"placement":{"x":0,"y":0,"width":800,"height":600}},{"placement":{"x":0,"y":0,"width":800,"height":600},"windows":[{"title":"Documents","matchType":0}]}]}]}""")
    let kept,emptyWarnings,_ = WorkspaceData.read emptyGroups
    check (emptyWarnings.IsEmpty && (kept.Head.["groups"] :?> JArray).Count=1) "Groups saved without windows were not dropped quietly"
    let workspace = Workspace(name="w")
    let wsGroup = WorkspaceGroup(name="g",placement=placement)
    let wsWindow = WorkspaceWindow()
    workspace.addGroup(wsGroup)
    wsGroup.addWindow(wsWindow)
    (box wsWindow :?> IWorkspaceNode).remove()
    check workspace.groups.isEmpty "Deleting a group's last window left an empty group"
    for schema in ["999";"\"bad\"";"0"] do
        let _,_,protectedData = WorkspaceData.read (JObject.Parse("{\"workspaceSchemaVersion\":"+schema+"}"))
        check protectedData "Unsupported schema was writable"
    let malicious = WindowTitleMatcher.compile 4 "^(a+)+$"
    let watch = Diagnostics.Stopwatch.StartNew()
    let mutable timedOut = false
    try malicious (String('a',100000)+"!") |> ignore
    with :? Text.RegularExpressions.RegexMatchTimeoutException -> timedOut <- true
    check (timedOut && watch.ElapsedMilliseconds<2000L) "Workspace regular expression did not time out"

    let report = RuntimeDiagnostics.report (JObject.Parse("""{"licenseKey":"SECRET","workspaces":[{"title":"SECRET"}],"path":"SECRET","runAtStartup":true,"alignment":"SECRET"}""")) 0 0
    check (not(report.ToString().Contains("SECRET"))) "Diagnostic report leaked private data"
    // Tab text stays readable on every tab colour: kept where it reads well, otherwise taken
    // darker or lighter just enough, keeping some of its own colour.
    do
        check (abs (TextContrast.ratio System.Drawing.Color.Black System.Drawing.Color.White-21.0) < 0.01) "Contrast of black on white is not 21:1"
        let pink,green = System.Drawing.Color.FromArgb(0xA9,0x79,0x79),System.Drawing.Color.FromArgb(0x71,0xD5,0x1A)
        let fixedText = TextContrast.readable pink green
        check (TextContrast.ratio fixedText green >= TextContrast.minimum && fixedText.ToArgb()<>System.Drawing.Color.Black.ToArgb() && fixedText.R>fixedText.G)
              (sprintf "Pink text on green is not made readable in its own hue: %A" fixedText)
        check (TextContrast.readable System.Drawing.Color.White System.Drawing.Color.Black = System.Drawing.Color.White) "Readable text was changed"
        // Each tab is judged on its own: a pale inactive tab does not change the text on a
        // dark active tab where the chosen colour already reads well.
        let teal,darkNavy,slate = System.Drawing.Color.FromArgb(0x00,0xB4,0xB4),System.Drawing.Color.FromArgb(0x11,0x18,0x27),System.Drawing.Color.FromArgb(0x4B,0x59,0x70)
        check (TextContrast.readable teal darkNavy = teal) "Text that reads on the active tab was adjusted for another tab"
        let onSlate = TextContrast.readable teal slate
        check (onSlate <> teal && TextContrast.ratio onSlate slate >= TextContrast.minimum) "Text on a low-contrast tab is not made readable"
        for palette in [Theme.lightPalette;Theme.darkPalette] do
            for background in [palette.tabActiveBgColor;palette.tabHighlightBgColor;palette.tabNormalBgColor] do
                check (TextContrast.readable palette.tabTextColor background = palette.tabTextColor)
                      (sprintf "A default theme's text is adjusted on %A" background)
        for dark in [false;true] do
            ThemePresets.palettes dark |> Array.iteri(fun index palette ->
                let low = [palette.tabActiveBgColor;palette.tabHighlightBgColor;palette.tabNormalBgColor;palette.tabFlashBgColor]
                          |> List.filter(fun background -> TextContrast.readable palette.tabTextColor background <> palette.tabTextColor)
                // Built-in presets read well as they are, so they look exactly as before.
                check low.IsEmpty (sprintf "Preset %s (%s) needs its text adjusted" ThemePresets.keys.[index] (if dark then "dark" else "light")))
    // A reset keeps the version, writes fresh-install toggles, and keeps app rules and
    // workspaces unless asked to clear them.
    do
        let current = JObject.Parse("""{"version":"1.2","tabThemeMode":"dark","runAtStartup":false,"hotKeys":{"nextTab":1},
                                        "includedPaths":["a.exe"],"excludedPaths":["b.exe"],"autoGroupingPaths":[],
                                        "workspaces":[{"name":"w"}],"workspaceSchemaVersion":2}""")
        let kept = SettingsCatalog.resetRoot current false false
        check (kept.["version"].ToString()="1.2" && isNull kept.["tabThemeMode"] && isNull kept.["hotKeys"]) "Reset kept a preference"
        check ((kept.["runAtStartup"] :?> JValue).Value=box true) "Reset did not write the fresh-install toggle default"
        check (not (isNull kept.["includedPaths"]) && not (isNull kept.["excludedPaths"]) && not (isNull kept.["workspaces"])
               && kept.["workspaceSchemaVersion"].ToString()="2") "Reset lost app rules or workspaces"
        let cleared = SettingsCatalog.resetRoot current true true
        check (isNull cleared.["includedPaths"] && isNull cleared.["excludedPaths"] && isNull cleared.["workspaces"]) "Reset did not clear what was asked"
        check (current.["tabThemeMode"].ToString()="dark") "Reset changed the settings it read"
    check (report.["settings"].["workspaceCount"].Value<int>()=1) "Diagnostic summary missing workspace count"
    // Most telling first; crashes (none here) would sit before the developer figures at the end.
    let order = ["version";"os";"dotNet";"settings";"otherTools";"environment";"monitors";"groups";"groupedWindows";"uptimeMinutes";"resources";"scans"]
    let keys = report.Properties() |> Seq.map(fun property -> property.Name) |> Seq.toList
    check (keys=order) (sprintf "Diagnostic report fields are missing or out of order: %A" keys)
    check ((report.["monitors"] :?> JArray).Count >= 1) "Diagnostic report lists no displays"
    IO.File.WriteAllText(IO.Path.Combine(__SOURCE_DIRECTORY__,"Debug","diagnostics-sample.json"),report.ToString())
    // Nothing that looks like a file path, whatever the machine has.
    check (not (report.ToString().Contains(":\\"))) "Diagnostic report contains a path"
    let rules = RuntimeDiagnostics.report (JObject.Parse("""{"includedPaths":["C:\\SECRET\\a.exe","b"],"excludedPaths":["c"],"tabAppearance":{"tabHeight":25,"tabMaxWidth":"SECRET"}}""")) 0 0
    check (rules.["settings"].["appRules"].["tabsOn"].Value<int>()=2 && rules.["settings"].["tabs"].["height"].Value<int>()=25) "Diagnostic summary missing rule counts or tab size"
    check (not (rules.ToString().Contains("SECRET"))) "Diagnostic summary leaked app rule paths or unexpected values"
    // The report lists the latest crashes, newest first, with their exception types and the
    // methods on their stacks, but never messages or source paths.
    let crashLog = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"WindowTabsCrash.log")
    let existingLog = if IO.File.Exists(crashLog) then Some(IO.File.ReadAllText(crashLog)) else None
    try
        // Starting the handler leaves no file behind; only a logged error writes one.
        if IO.File.Exists(crashLog) then IO.File.Delete(crashLog)
        let handler = new ExceptionHandlerPlugin()
        (handler :> IPlugin).init()
        check (not (IO.File.Exists(crashLog))) "Crash log was created before any error"
        handler.log "Test" (box (InvalidOperationException("probe")))
        (handler :> IDisposable).Dispose()
        check (IO.File.Exists(crashLog) && IO.File.ReadAllText(crashLog).Contains("probe")) "A logged error was not written to the crash log"
        let entry time kind =
            String.concat "\r\n" ["---------------------------------------------";"Time    : "+time;"Source  : Application.ThreadException"
                                  "Version : 1.2.3";"OS      : Windows 11 Pro 24H2 (26100.1) / .NET 4.8.1"
                                  kind+": Could not open C:\\SECRET\\file.txt ---> System.IO.IOException: SECRET title"
                                  "and a SECRET second message line"
                                  "   at Bemo.Somewhere() in C:\\SECRET\\Program.fs:line 12"
                                  "   在 Bemo.Elsewhere(String path) 位置 D:\\SECRET\\Other.fs:行号 3"
                                  "   --- End of inner exception stack trace ---";""]
        // An older version wrote oldest first; the report still starts with the newest.
        IO.File.WriteAllText(crashLog,entry "2026-01-01 10:00:00" "System.ArgumentException"+entry "2026-01-02 11:00:00" "System.InvalidOperationException")
        let withCrashes = RuntimeDiagnostics.report (JObject()) 0 0
        let keys = withCrashes.Properties() |> Seq.map(fun property -> property.Name) |> Seq.toList
        check (List.findIndex ((=) "crashes") keys = List.findIndex ((=) "uptimeMinutes") keys+1
               && List.findIndex ((=) "resources") keys = List.findIndex ((=) "crashes") keys+1)
              (sprintf "Crashes are not between the current state and the developer figures: %A" keys)
        let crashes = withCrashes.["crashes"]
        let latest = crashes.["latest"].[0]
        check (not (isNull crashes) && crashes.["inLog"].Value<int>()=2 && latest.["time"].Value<string>()="2026-01-02 11:00:00"
               && latest.["exception"].Value<string>()="System.InvalidOperationException ---> System.IO.IOException"
               && latest.["source"].Value<string>()="Application.ThreadException") (sprintf "Diagnostic report misread the crash log: %O" crashes)
        let frames = latest.["stack"] |> Seq.map string |> Seq.toList
        check (frames=["at Bemo.Somewhere()";"在 Bemo.Elsewhere(String path)";"--- End of inner exception stack trace ---"])
              (sprintf "Crash stack frames were not reduced to their methods: %A" frames)
        check (not (crashes.ToString().Contains("SECRET"))) "Diagnostic report copied a crash message or path"
        check (RuntimeDiagnostics.crashLogPath()=Some crashLog) "Crash log next to the exe was not found"
        // New entries go on top, old logs are put in order, and the oldest go past the limit.
        let log = RuntimeDiagnostics.CrashLog.prepend (entry "2026-01-03 09:00:00" "System.NullReferenceException") (IO.File.ReadAllText(crashLog)) 100000
        let times = RuntimeDiagnostics.CrashLog.entries log |> List.map(fun e -> e.Substring(e.IndexOf("Time    :")+10,19))
        check (log.StartsWith("-----") && log.IndexOf("2026-01-03") < log.IndexOf("2026-01-02") && log.IndexOf("2026-01-02") < log.IndexOf("2026-01-01"))
              (sprintf "Crash log is not newest first: %A" times)
        let short = RuntimeDiagnostics.CrashLog.prepend (entry "2026-01-03 09:00:00" "System.NullReferenceException") (IO.File.ReadAllText(crashLog)) 800
        check (short.Contains("2026-01-03") && not (short.Contains("2026-01-01"))) "Crash log limit did not drop the oldest entries"
        // A log left empty offers nothing to open.
        IO.File.WriteAllText(crashLog,"")
        check (RuntimeDiagnostics.crashLogPath()<>Some crashLog) "An empty crash log was offered to open"
    finally
        match existingLog with
        | Some text -> IO.File.WriteAllText(crashLog,text)
        | None -> IO.File.Delete(crashLog)
    let geometry = AppearanceJson.readGeometry (JObject.Parse("""{"tabHeight":-20,"tabMaxWidth":999999,"tabOverlap":30}""")) Theme.defaultGeometry
    check (geometry.height=12 && geometry.maxWidth=1000 && geometry.overlap=0) "Invalid persisted dimensions bypassed shared bounds"
    let gap = AppearanceJson.readGeometry (JObject.Parse("""{"tabOverlap":-30}""")) Theme.defaultGeometry
    check (gap.overlap= -30 && (AppearanceJson.normalizeGeometry {gap with overlap= -500}).overlap= -100) "Tab gap lost its stored sign or bounds"
    check (Theme.defaultGeometry.style=JoinedTabs) "Tabs no longer join into one bar by default"
    for style in [JoinedTabs;FolderTabs;PillTabs] do
        let stored = AppearanceJson.writeLegacy {Theme.defaultGeometry with style=style} Theme.lightPalette
        check ((AppearanceJson.readGeometry stored Theme.defaultGeometry).style=style) (sprintf "Tab style %A was not kept" style)
    check ((AppearanceJson.readGeometry (JObject.Parse("""{"tabStyle":"zigzag"}""")) {Theme.defaultGeometry with style=PillTabs}).style=JoinedTabs)
        "An unknown tab style was not read as the joined style"
    check (SettingsCatalog.normalizeChoice "alignment" "invalid"="Center") "Invalid choice was not normalized"
    check (SettingsCatalog.normalizeChoice "language" "fr"="system") "Unknown language was not normalized"
    // File icons must resolve for paths outside the ANSI code page (SHGetFileInfo is called as Unicode).
    let unicodeDir = IO.Path.Combine(__SOURCE_DIRECTORY__,"Debug","图标 アイコン")
    IO.Directory.CreateDirectory(unicodeDir) |> ignore
    let unicodeExe = IO.Path.Combine(unicodeDir,"WindowTabs.exe")
    IO.File.Copy(IO.Path.Combine(__SOURCE_DIRECTORY__,"Debug","WindowTabs.exe"),unicodeExe,true)
    let fileIcon = Win32Helper.GetFileIcon(unicodeExe)
    check (AppIcons.HasOwnIcon unicodeExe && not (AppIcons.IsGenericIcon fileIcon)) "File icon lost for a non-ASCII path"
    WinUserApi.DestroyIcon(fileIcon) |> ignore
    for code,expected in ["en","Close";"zh","关闭";"ja","閉じる";"ar","إغلاق"] do
        Localization.setPreference code
        check (Localization.plain Strings.TabMenu.close=expected) ("Language override ignored: "+code)
    // Arabic is shown inside a right-to-left embedding; parsed and saved values stay unmarked.
    check (tr Strings.TabMenu.close="\u202Bإغلاق\u202C") "Arabic text must read right to left"
    check ((Localization.plain Strings.TabMenu.taskbarIconFiles).EndsWith("|*.*")) "File filter must not carry direction marks"
    Localization.setPreference "fr"
    check (tr Strings.TabMenu.close="Close") "Unsupported language must fall back to English"
    Localization.setPreference "system"
    // A text is either empty in every language or translated in every language.
    let partial =
        SettingsCatalog.all
        |> List.collect(fun item -> [item.text.caption; item.text.description; item.text.keywords])
        |> List.filter(fun text -> let values = Localization.all text in List.contains "" values && List.exists ((<>) "") values)
        |> List.map(fun text -> text.en)
    check partial.IsEmpty ("Settings text with a missing translation: " + String.concat " | " partial)
    check (SettingsCatalog.all |> List.forall(fun item -> item.text.caption.en<>"")) "Setting without a caption"
    check (SettingsCatalog.all |> List.exists(fun item -> SettingsCatalog.matches "テーマ" item)) "Settings search ignores Japanese"
    check (SettingsCatalog.all |> List.exists(fun item -> SettingsCatalog.matches "السمة" item)) "Settings search ignores Arabic"
    let found query = SettingsCatalog.all |> List.filter (SettingsCatalog.matches query) |> List.map(fun item -> item.id)
    for query in ["autostart";"自启动";"スタートアップ";"runAtStartup"] do
        check (List.contains "launch-at-sign-in" (found query)) ("Search keyword missed: "+query)
    // Keywords are synonyms of the setting's own name.
    for query in ["fade";"变暗";"暗く"] do
        check (List.contains "dim-inactive-groups" (found query)) ("Search keyword missed: "+query)

    let queue = ConcurrentQueue<unit -> unit>()
    let dispatcher = {new IDispatcher with
        member _.CheckAccess = true
        member _.Send action = action()
        member _.Post action = queue.Enqueue(action)}
    let drain() =
        let mutable action = Unchecked.defaultof<unit -> unit>
        while queue.TryDequeue(&action) do action()
    let until predicate =
        let watch = Diagnostics.Stopwatch.StartNew()
        let mutable satisfied = predicate()
        while not satisfied && watch.ElapsedMilliseconds<5000L do
            Thread.Sleep(2)
            satisfied <- predicate()
        check satisfied "Worker timed out"
    let applied,discarded = ResizeArray<int>(),ConcurrentBag<int>()
    let failures = ResizeArray<exn>()
    use started = new ManualResetEventSlim(false)
    use release = new ManualResetEventSlim(false)
    let mutable active,peak,finished = 0,0,0
    let work value (token:CancellationToken) =
        let count = Interlocked.Increment(&active)
        peak <- max count peak
        try
            if value=0 then started.Set(); release.Wait()
            value
        finally
            Interlocked.Decrement(&active) |> ignore
            Interlocked.Increment(&finished) |> ignore
    use scanner = new LatestWork<int>(dispatcher,applied.Add,discarded.Add,failures.Add)
    scanner.Request(work 0)
    check (started.Wait(5000)) "Worker did not start"
    for value in 1..200 do scanner.Request(work value)
    release.Set()
    until(fun () -> finished=2 && queue.Count>=1)
    Thread.Sleep(20)
    drain()
    check (peak=1 && Seq.toList applied=[200] && (discarded |> Seq.contains 0)) "Scan replacement ran concurrently or applied stale results"
    scanner.Request(work 201)
    until(fun () -> finished=3 && queue.Count>=1)
    (scanner :> IDisposable).Dispose()
    Thread.Sleep(20)
    drain()
    check (Seq.toList applied=[200] && (discarded |> Seq.contains 201) && failures.Count=0) "Closed page received a queued result"

    let logical = Theme.dark
    let originalDpi = Dpi.value()
    try
        for _ in 1..100 do
            for dpi in [96;120;144;192;96] do
                Dpi.set dpi
                check (logical.scaled.tabHeight=Dpi.scaleAt dpi logical.tabHeight) "DPI geometry accumulated scaling"
        check (logical.scaled.tabHeight=logical.tabHeight) "DPI round trip changed logical dimensions"
    finally Dpi.set originalDpi
    // The Alt+Tab switcher builds its list inside a low-level keyboard hook, an input-synchronous
    // call in which COM cannot reach another process. Asking there whether a cloaked window is on
    // this desktop failed, so a UWP app closed to the background (Realtek Audio Console) was listed.
    // The answer from the window's last check outside the hook now stands.
    do
        use cloaked = new System.Windows.Forms.Form(ShowInTaskbar=false, Text="Cloaked probe",
                                                     StartPosition=System.Windows.Forms.FormStartPosition.Manual,
                                                     Location=System.Drawing.Point(-20000,-20000))
        cloaked.Show()
        let mutable cloak = 1
        CloakProbe.DwmSetWindowAttribute(cloaked.Handle, 13, &cloak, 4) |> ignore
        let target = cloaked.Handle
        check (not(fst(WindowCloaking.TryGetVisibleDesktopId target))) "A cloaked window inherited the visible desktop and could cross-group"
        check (WindowCloaking.IsHiddenOnCurrentDesktop target) "A cloaked window on this desktop is not hidden"
        let insideCall = ref None
        let probe = SyncCallWindow(fun () -> insideCall.Value <- Some(WindowCloaking.IsHiddenOnCurrentDesktop target))
        probe.CreateHandle(System.Windows.Forms.CreateParams())
        try
            let handle = probe.Handle
            let sender = Thread(fun () -> CloakProbe.SendMessage(handle, 0x8001, 0n, 0n) |> ignore)
            sender.Start()
            while sender.IsAlive do
                System.Windows.Forms.Application.DoEvents()
                Thread.Sleep(5)
        finally probe.DestroyHandle()
        check (insideCall.Value = Some true) (sprintf "A cloaked window counted as shown inside an input-synchronous call: %A" insideCall.Value)
    // Owned application windows bypass the ordinary Alt+Tab ownership filter, but
    // must still disappear when DWM cloaks them on another desktop.
    do
        let directory = System.IO.Path.Combine(__SOURCE_DIRECTORY__,"Debug","switch-desktop-"+Guid.NewGuid().ToString("N"))
        System.IO.Directory.CreateDirectory(directory) |> ignore
        let previous = Environment.CurrentDirectory
        Environment.CurrentDirectory <- directory
        try
            use settings = new Settings(true)
            use proxy = new System.Windows.Forms.Form(ShowInTaskbar=false)
            use current = new System.Windows.Forms.Form(Text="Current desktop probe",Owner=proxy,
                              StartPosition=System.Windows.Forms.FormStartPosition.Manual,
                              Location=System.Drawing.Point(-20000,-20000))
            use other = new System.Windows.Forms.Form(Text="Other desktop probe",Owner=proxy,
                            StartPosition=System.Windows.Forms.FormStartPosition.Manual,
                            Location=System.Drawing.Point(-20000,-20000))
            TaskbarProxies.add proxy.Handle
            try
                current.Show()
                other.Show()
                let mutable cloak = 1
                check (CloakProbe.DwmSetWindowAttribute(other.Handle,13,&cloak,4)=0) "Could not cloak switcher fixture"
                check (WindowCloaking.IsCloaked other.Handle && OS().windowFromHwnd(other.Handle).isVisible)
                      "Switcher fixture must be user32-visible but DWM-cloaked"
                let group = {new ITaskSwitchGroup with
                    member _.hwnd = proxy.Handle
                    member _.windows = Set2(List2([current.Handle;other.Handle]))}
                let desktop = {new ITaskSwitchDesktop with member _.groups = List2([group])}
                use switcher = new TaskSwitcher(settings,desktop)
                for grouped in [false;true] do
                    (settings :> ISettings).setValue("groupWindowsInSwitcher",box grouped)
                    let candidates() = switcher.windows.list |> List.map(fun (TaskWindowItem(hwnd,_)) -> hwnd)
                    check (List.contains current.Handle (candidates())) "Switcher omitted a current-desktop owned window"
                    check (not(List.contains other.Handle (candidates()))) "Switcher listed a cloaked owned window from another desktop"
                    cloak <- 0
                    check (CloakProbe.DwmSetWindowAttribute(other.Handle,13,&cloak,4)=0) "Could not uncloak switcher fixture"
                    if not grouped then
                        check (List.contains other.Handle (candidates())) "Switcher did not refresh windows after a desktop visibility change"
                    cloak <- 1
                    CloakProbe.DwmSetWindowAttribute(other.Handle,13,&cloak,4) |> ignore
            finally TaskbarProxies.remove proxy.Handle
        finally Environment.CurrentDirectory <- previous
    printfn "PASS: startup ownership, singleton, transactional shortcuts, workspace recovery and regex timeout, private diagnostics, single-flight cancellation, DPI geometry and cloaked windows inside an input-synchronous call and the switcher."

TestInit.run main
