// Run through tests/Run-Tests.ps1 -Suites MouseGestures (STA + Application.Run).
// Reading strokes, the gesture table as kept and resolved, presets, the hook's handling of a
// press, and the Mouse gestures page and its dialog in light and dark, from an isolated
// settings directory.
#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.IO
open System.Drawing
open System.Drawing.Imaging
open System.Windows.Forms
open Bemo
open Bemo.Json

let check condition message = if not condition then failwith message
let pump milliseconds =
    let watch = Diagnostics.Stopwatch.StartNew()
    while watch.ElapsedMilliseconds<int64 milliseconds do
        Application.DoEvents()
        Threading.Thread.Sleep(5)

/// A stroke through the corners, a point every 4 pixels, with a wobble of up to `wobble` pixels.
let stroke (corners:(int * int) list) wobble =
    let random = Random(7)
    corners |> List.pairwise |> List.collect(fun ((x0,y0),(x1,y1)) ->
        let steps = max 1 (max (abs(x1-x0)) (abs(y1-y0))/4)
        [ for step in 0..steps-1 ->
            x0+(x1-x0)*step/steps+random.Next(-wobble,wobble+1),y0+(y1-y0)*step/steps+random.Next(-wobble,wobble+1) ])
    |> fun points -> points @ [List.last corners]

let recognizer() =
    let read corners wobble = MouseGestures.read 1.0 (stroke corners wobble)
    check (read [0,0;0,-120] 0=Gesture "U") "A stroke up was not read as up"
    check (read [0,0;0,-100;120,-100] 2=Gesture "UR") "Up then right, with a wobble, was not read"
    check (read [0,0;0,-100;-90,-100] 3=Gesture "UL") "Up then left, with a wobble, was not read"
    // Left, a short way down at the turn, then right: the small detour is dropped.
    check (read [200,0;20,10;10,40;180,48] 3=Gesture "LR") "A sloppy left-then-back stroke was not read as left, right"
    check (read [0,0;0,-100;8,-100;8,0] 2=Gesture "UD") "Up and back down beside itself was not read as up, down"
    check (read [0,0;100,6] 0=Gesture "R") "A slightly slanted line was not read as one stroke"
    check (read [0,0;10,4] 0=NoGesture) "A twitch was read as a gesture"
    check (read [0,0;0,-100;100,-100;100,0] 0=Unrecognized) "Three strokes were read as a gesture"
    // Up, the wrong way left, then back right past where it turned: the left was a slip.
    check (read [0,0;0,-100;-80,-100;70,-100] 2=Gesture "UR") (sprintf "Up, left, then back right was read as %A" (read [0,0;0,-100;-80,-100;70,-100] 2))
    check (read [0,0;-100,0;-100,90;-100,-80] 2=Gesture "LU") "Left, down, then back up was not read as left, up"
    // Turned back only part of the way, short of where it turned, the slip is not put right.
    check (read [0,0;0,-100;-100,-100;-40,-100] 0=Unrecognized) (sprintf "Up, left, then back right part of the way was read as %A" (read [0,0;0,-100;-100,-100;-40,-100] 0))
    check (read [0,0;0,-100;-100,-100;0,-100] 0=Unrecognized) "Up, left, then back right just to the turn was read as a gesture"
    // Back to the first stroke's way is a zigzag, and a wrong first stroke is not put right.
    check (read [0,0;0,-100;8,0;16,-100] 0=Unrecognized) "Up, down, up was read as a gesture"
    check (read [0,0;0,-100;0,0;100,0] 0=Unrecognized) "Up, back down, then right was read as a gesture"
    // A scribble of loops back and forth is not a gesture, however much of it points one way.
    let scribble = [228,74;85,48;75,80;90,110;280,110;430,78;435,98;280,112;200,104;175,60;228,74]
    check (read scribble 3=Unrecognized) (sprintf "A scribble was read as %A" (read scribble 3))
    // Mostly one way, with long detours dropped: too much of the path would go unread.
    let detours = [0,0;-200,0;-200,30;-160,30;-160,60;-120,60;-120,90;-80,90;-80,120]
    check (read detours 0=Unrecognized) (sprintf "A path mostly left out was read as %A" (read detours 0))
    // A curve from going up into going right is still one turn.
    check (read [0,0;0,-80;10,-100;30,-110;140,-112] 2=Gesture "UR") (sprintf "A rounded turn was read as %A" (read [0,0;0,-80;10,-100;30,-110;140,-112] 2))
    // At twice the scale the same small move is still too short.
    check (MouseGestures.read 2.0 (stroke [0,0;40,0] 0)=NoGesture) "Strokes were not measured in logical pixels"
    check (MouseGestures.all.Length=16 && MouseGestures.all |> List.forall(fun gesture -> gesture.Length<=2)) "The gestures to pick from are not the 16 of one or two strokes"

let table() =
    let chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe"
    let editor = @"C:\Tools\editor.exe"
    let back = { gesture="L"; action=GestureKeys([MouseGesturePresets.altLeft],"") }
    let close = { gesture="DR"; action=GestureKeys([MouseGesturePresets.ctrlW],"") }
    let mine = { gesture="RD"; action=GestureKeys([0x0274],"Mine") }
    // Ctrl+K, Ctrl+C, one after another.
    let comment = { gesture="RU"; action=GestureKeys([0x024B;0x0243],"") }
    let start = { MouseGestures.empty with apps=[{ path=chrome; enabled=true; bindings=[back;close] }; { path=editor; enabled=false; bindings=[] }] }
    // An app's own binding wins; others fall back to everything's; an app switched off has none.
    check (MouseGestures.resolve start chrome "L"=Some back) "Chrome's own gesture was not used"
    check (MouseGestures.resolve start "CHROME.EXE" "DR"=Some close) "An app's binding did not override everything's for the same gesture"
    check (MouseGestures.resolve start chrome "UR"=Some { gesture="UR"; action=GestureCommand "next-tab" }) "An app lost the gestures every app has"
    check (MouseGestures.resolve start editor "UR"=None && not (MouseGestures.enabledFor start editor)) "An app switched off still had gestures"
    check (MouseGestures.resolve start @"C:\other.exe" "U"=Some { gesture="U"; action=GestureCommand "search-tabs" }) "An unlisted app lacked everything's gestures"
    check ((MouseGestures.overrides start close).IsSome && (MouseGestures.overrides start back).IsNone) "Overrides were not told apart"
    // A right press is held back only where a gesture does something: with gestures for one app
    // alone, another app's right-drag reaches it.
    let one = { start with everywhere=[] }
    check (MouseGestures.takesPresses one chrome) "The one app given gestures did not take them"
    check (not (MouseGestures.takesPresses one @"C:\other.exe")) "An app with no gestures had its right-drag held back"
    check (not (MouseGestures.takesPresses start editor) && MouseGestures.takesPresses start @"C:\other.exe")
        "Presses were not held back as an app's switch and everything's gestures say"
    // Everywhere's switch off: its gestures are kept but used nowhere; listed apps keep their own.
    let paused = { start with everywhereEnabled=false }
    check (not (MouseGestures.takesPresses paused @"C:\other.exe") && MouseGestures.resolve paused @"C:\other.exe" "U"=None)
        "An unlisted app still had the gestures for all apps with their switch off"
    check (MouseGestures.resolve paused chrome "L"=Some back && MouseGestures.resolve paused chrome "UR"=None)
        "With all apps' gestures off, a listed app lost its own or still borrowed everyone's"
    check (paused.everywhere=start.everywhere) "Switching off the gestures for all apps dropped them"
    let pausedAgain = MouseGestures.readTable (MouseGestures.writeTable paused)
    check (not pausedAgain.everywhereEnabled && pausedAgain.everywhere=start.everywhere) "The switch for all apps' gestures was not kept"
    check (MouseGestures.readTable (JToken.Parse """{"global":[],"apps":[]}""")).everywhereEnabled
        "Settings kept before the switch came read as all apps' gestures off"
    let bare = { one with apps=[{ path=chrome; enabled=true; bindings=[] }] }
    check (not (MouseGestures.takesPresses bare chrome)) "A listed app with no gestures anywhere had its right-drag held back"
    // Kept and read back, field for field.
    let added = MouseGestures.updateApp start chrome (fun app -> { app with bindings=MouseGestures.bind (MouseGestures.bind app.bindings mine) comment })
    let again = MouseGestures.readTable (MouseGestures.writeTable added)
    check (again=added) (sprintf "The gesture table did not survive being written and read: %A" again)
    // One key is kept as a number, as before sequences, so an older version still reads it.
    let written = MouseGestures.writeTable added
    check (written.ToString().Contains("\"keys\":628") || written.ToString().Contains("\"keys\": 628")) (sprintf "One key was not kept as a number: %s" (written.ToString()))
    let legacy = MouseGestures.readTable (JToken.Parse """{"global":[{"gesture":"UD","keys":116,"name":""}],"apps":[],"dismissed":[]}""")
    check (legacy.everywhere=[{ gesture="UD"; action=GestureKeys([0x74],"") }]) (sprintf "A key kept as a number was read as %A" legacy.everywhere)
    check (MouseGesturePresets.keysName [0x024B;0x0243] ""="Ctrl+K, Ctrl+C") "Keys one after another were not named in order"
    check (MouseGestures.readTable null=MouseGestures.empty) "A settings file without gestures did not start with the defaults"
    // After one stroke, what a second would do there: everything's up-left and up-right.
    check (MouseGestures.continuations MouseGestures.empty @"C:\other.exe" "U" |> List.map(fun binding -> binding.gesture)=["UL";"UR"]) "The strokes that could follow up were not up-left and up-right"
    check (MouseGestures.continuations MouseGestures.empty @"C:\other.exe" "UL"=[]) "A gesture of two strokes had strokes to follow"
    check (MouseGestures.continuations start editor "U"=[]) "An app switched off had strokes to follow"
    // Binding a gesture already there replaces it in place.
    let replaced = MouseGestures.bind [back;close] { back with action=GestureCommand "next-tab" }
    check (replaced.Length=2 && replaced.Head.action=GestureCommand "next-tab") "Binding a gesture again added a second one"
    // Changing a gesture keeps its place; changed to another's gesture, that one gives way.
    let moved = MouseGestures.rebind [back;close] back.gesture { back with gesture="UR" }
    check (moved=[{ back with gesture="UR" };close]) (sprintf "A changed gesture did not keep its place: %A" moved)
    let taken = MouseGestures.rebind [back;close] back.gesture { back with gesture=close.gesture }
    check (taken=[{ back with gesture=close.gesture }]) (sprintf "A gesture changed to another's did not replace it: %A" taken)
    // Seen running, a browser joins with its preset; taken off, it stays off.
    let seen = MouseGestures.seen MouseGestures.empty @"D:\Edge\msedge.exe" (MouseGesturePresets.bindings "msedge.exe")
    check (seen |> Option.exists(fun t -> t.apps.Length=1 && t.apps.Head.bindings=MouseGesturePresets.browser)) "A browser seen running did not get its preset"
    let removed = MouseGestures.removeApp seen.Value @"D:\Edge\msedge.exe" true
    check (removed.apps.IsEmpty && removed.dismissed=["msedge.exe"]) "Removing a preset app did not remember it"
    check ((MouseGestures.seen removed @"C:\Edge\MSEDGE.EXE" (MouseGesturePresets.bindings "msedge.exe")).IsNone) "A removed preset came back when the app was seen again"
    check ((MouseGestures.addApp removed @"C:\Edge\msedge.exe" []).dismissed.IsEmpty) "Adding an app back by hand kept it dismissed"
    check ((MouseGestures.seen MouseGestures.empty editor None).IsNone) "An app without a preset joined the list on its own"
    check (MouseGesturePresets.bindings "explorer.exe"=Some MouseGesturePresets.explorer && (MouseGesturePresets.bindings "notepad.exe").IsNone) "Presets went to the wrong apps"
    // File Explorer refreshes as a browser does: up and back down sends F5.
    check (MouseGesturePresets.explorer |> List.exists(fun binding -> binding.gesture="UD" && binding.action=GestureKeys([MouseGesturePresets.f5],""))) "File Explorer's preset has no refresh"
    // Keys are named by the preset, the user's name, or else the keys.
    check (MouseGesturePresets.keysName [MouseGesturePresets.altLeft] ""=tr Strings.MouseGestures.back) "Alt+Left was not called Back"
    check (MouseGesturePresets.keysName [MouseGesturePresets.altLeft] "Mine"="Mine") "A name of the user's was not kept"
    check (MouseGesturePresets.keysName [0x0274] ""=SettingsShortcut.text 0x0274) "Keys without a name did not show as keys"
    check (tr (Strings.MouseGestures.drawn "UR")<>"" && Localization.all (Strings.MouseGestures.drawn "LD") |> List.forall(fun text -> text<>"")) "A gesture had no words"

/// A key as the box gets it before the window does, while it records.
let pressKey (control:Control) (keys:Keys) =
    let mutable msg = Message()
    let processKey = control.GetType().GetMethod("ProcessCmdKey",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Public,null,[|typeof<Message>.MakeByRefType();typeof<Keys>|],null)
    unbox<bool>(processKey.Invoke(control,[|box msg;box keys|]))

/// WindowTabs' own windows take gestures where they have a title bar and can be activated, as
/// Settings, where gestures are tried just after turning them on; not tab strips, menus or popups.
let ownWindows() =
    let caption,popup = int64 WindowsStyles.WS_CAPTION,int64 WindowsStyles.WS_POPUP
    let tool,noActivate = int64 WindowsExtendedStyles.WS_EX_TOOLWINDOW,int64 WindowsExtendedStyles.WS_EX_NOACTIVATE
    check (GestureTargets.ordinary caption 0L) "A window of WindowTabs with a title bar, as Settings, takes no gestures"
    check (not (GestureTargets.ordinary popup tool) && not (GestureTargets.ordinary popup noActivate) && not (GestureTargets.ordinary caption tool) && not (GestureTargets.ordinary popup 0L))
          "A tab strip, menu, popup or tool window of WindowTabs takes gestures"
    // Settings itself, as the app builds it, takes them.
    use form = new Form(ShowInTaskbar=false)
    let style index = WinUserApi.GetWindowLong(form.Handle,index).ToInt64()
    check (GestureTargets.ordinary (style WindowLongFieldOffset.GWL_STYLE) (style WindowLongFieldOffset.GWL_EXSTYLE)) "An ordinary window of the app takes no gestures"

/// Another button pressed while a stroke is drawn gives the gesture up and clicks nothing: its
/// press and release, and the right button's release, are all kept from the app.
let cancelling() =
    use input = new MouseGestureInput(fun _ -> "app.exe")
    let field = typeof<MouseGestureInput>.GetField("state",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic)
    for down,up in [WindowMessages.WM_LBUTTONDOWN,WindowMessages.WM_LBUTTONUP;WindowMessages.WM_MBUTTONDOWN,WindowMessages.WM_MBUTTONUP] do
        field.SetValue(input,box (Drawing(IntPtr(1),"app.exe")))
        check (input.handle(down,0,0,IntPtr.Zero)) "A button pressed while drawing reached the app"
        check (input.handle(up,0,0,IntPtr.Zero)) "The release of the button that gave the gesture up reached the app"
        check (input.handle(WindowMessages.WM_RBUTTONUP,0,0,IntPtr.Zero) && not input.busy) "The right button's release after giving up reached the app, or left it waiting"
        check (not (input.handle(up,0,0,IntPtr.Zero)) && not (input.handle(down,0,0,IntPtr.Zero))) "A later click was kept from the app"
    // Released in the other order, the right button first, the other release is still kept.
    field.SetValue(input,box (Drawing(IntPtr(1),"app.exe")))
    input.handle(WindowMessages.WM_LBUTTONDOWN,0,0,IntPtr.Zero) |> ignore
    check (input.handle(WindowMessages.WM_RBUTTONUP,0,0,IntPtr.Zero) && input.busy) "Letting the right button go first forgot the left release"
    check (input.handle(WindowMessages.WM_LBUTTONUP,0,0,IntPtr.Zero) && not input.busy) "The left release after the right one reached the app"
    // Two buttons pressed while drawing: each one's release is kept, not only the last one's.
    field.SetValue(input,box (Drawing(IntPtr(1),"app.exe")))
    input.handle(WindowMessages.WM_LBUTTONDOWN,0,0,IntPtr.Zero) |> ignore
    input.handle(WindowMessages.WM_MBUTTONDOWN,0,0,IntPtr.Zero) |> ignore
    check (input.handle(WindowMessages.WM_LBUTTONUP,0,0,IntPtr.Zero) && input.busy) "The first of two buttons pressed while drawing let its release through"
    check (input.handle(WindowMessages.WM_RBUTTONUP,0,0,IntPtr.Zero) && input.busy) "The right release forgot the second button's"
    check (input.handle(WindowMessages.WM_MBUTTONUP,0,0,IntPtr.Zero) && not input.busy) "The second button's release reached the app, or was left waiting"

let render (control:Control) name =
    use image = new Bitmap(max 1 control.Width,max 1 control.Height)
    control.DrawToBitmap(image,Rectangle(Point.Empty,image.Size))
    image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug",name+".png"),ImageFormat.Png)

let main() =
    recognizer()
    table()
    let isolated = Path.Combine(__SOURCE_DIRECTORY__,"Debug","gesture-test-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    let original = Environment.CurrentDirectory
    Environment.CurrentDirectory <- isolated
    try
        use settings = new Settings(true, saveDelay=0)
        let api = settings :> ISettings
        Services.register<IFilterService>({ new IFilterService with
            member _.isAppWindow _ = false
            member _.isAppWindowStyle _ = false
            member _.isTabbableWindow _ = false
            member _.isTabbingEnabledForAllProcessesByDefault with get() = true and set _ = ()
            member _.setIsTabbingEnabledForProcess _ _ = ()
            member _.setIsTabbingEnabledForProcesses _ _ = ()
            member _.getIsTabbingEnabledForProcess _ = true })
        check (not (api.getValue("enableMouseGestures") :?> bool)) "Mouse gestures were on before being asked for"
        check (api.getValue("showGestureHint") :?> bool) "The hint by the pointer was off to begin with"
        check (MouseGestureRules.table()=MouseGestures.empty) "A new settings file did not start with the default gestures"
        // The app's switch, as the palette turns it, lists the app with its preset.
        MouseGestureRules.setEnabled @"C:\Program Files\Mozilla Firefox\firefox.exe" false
        settings.Flush() |> ignore
        settings.clearCaches()
        let kept = MouseGestureRules.table()
        check (kept.apps.Length=1 && not kept.apps.Head.enabled && kept.apps.Head.bindings=MouseGesturePresets.browser) "Switching an app off did not keep it, with its preset, across a reload"
        check (not (MouseGestureRules.enabled "firefox.exe")) "The app's switch did not read back off"
        // The page's switch says what it does rather than repeat the page's name above it.
        check (Localization.all Strings.Settings.mouseGestures.caption |> List.forall(fun text -> not (Localization.all Strings.Pages.mouseGestures |> List.contains text)))
              "The gestures switch is called what its page is"
        check ((Commands.find "mouse-gestures-for-app").IsSome && (Commands.find "toggle-mouse-gestures").IsSome && (Commands.find "open-settings-mouse-gestures").IsSome)
              "The palette cannot reach the gestures, their switch or their page"
        // A reset keeps the gestures, as the user's own records, unless app rules are cleared.
        check (not (isNull (SettingsCatalog.resetRoot api.root false false).["mouseGestures"])) "A reset lost the mouse gestures"
        check (isNull (SettingsCatalog.resetRoot api.root true false).["mouseGestures"]) "Clearing app rules kept the mouse gestures"
        MouseGestureRules.setEnabled "firefox.exe" true
        MouseGestureRules.save (MouseGestures.addApp (MouseGestureRules.table()) @"C:\Windows\explorer.exe" MouseGesturePresets.explorer)
        cancelling()
        ownWindows()
        // The commands a gesture can run are listed as the palette lists them: the one used last
        // first, then by category, each in the order defined.
        CommandPalette.remember "close-tab"
        let listed = Commands.inPaletteOrder (Commands.forGestures()) |> List.map(fun command -> command.id)
        let byCategory =
            Commands.forGestures() |> List.filter(fun command -> command.id<>"close-tab")
            |> List.sortBy(fun command -> List.findIndex ((=) command.category) Commands.categories) |> List.map(fun command -> command.id)
        check (listed.Head="close-tab" && listed.Tail=byCategory) (sprintf "The gesture commands were not in the palette's order: %A" listed)
        check (List.findIndex ((=) "previous-tab") listed+1=List.findIndex ((=) "next-tab") listed || List.findIndex ((=) "next-tab") listed+1=List.findIndex ((=) "previous-tab") listed)
              "The next and previous tab commands were not side by side"
        for mode,name in [DarkTheme,"dark";LightTheme,"light"] do
            api.updateAppearance(fun appearance -> { appearance with mode=mode })
            use form = new Form(ClientSize=Size(900,720),StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ShowInTaskbar=false,Font=SettingsUi.bodyFont())
            let view = MouseGestureView()
            let page = (view :> ISettingsView).control
            page.Dock <- DockStyle.Fill
            form.Controls.Add(page)
            SettingsUi.apply form
            form.Show()
            pump 200
            let editor = view.Editor
            check (form.Controls.Find("mouse-gestures",true).Length=1 && form.Controls.Find("gesture-hint",true).Length=1) "The page lacks the switches the search goes to"
            check (editor.Height>Dpi.scale 300) (sprintf "The gesture editor is too short to show its table: %d" editor.Height)
            // Off, the page below the switch is greyed and takes no input; on, it is usable.
            check (not editor.AddGestureButton.Enabled && not editor.AddAppButton.Enabled) "The gestures could be edited while they were off"
            render form ("mouse-gestures-off-"+name)
            api.setValue("enableMouseGestures",box true)
            pump 100
            check (editor.AddGestureButton.Enabled && editor.AddAppButton.Enabled) "Turning gestures on left the page greyed"
            render form ("mouse-gestures-"+name)
            // After one stroke, the hint lists where a second would lead, a row each, under it.
            do
                use hint = new GestureHint()
                let pointer = Screen.PrimaryScreen.WorkingArea.Location
                hint.show(Some "U","Next tab",false,[],pointer)
                let alone = hint.Height
                // A stroke that does nothing by itself has no caption, only where it could go on to.
                hint.show(Some "U","",true,["UL","Previous tab";"UR","A much longer action name"],pointer)
                check (hint.Height>=alone+Dpi.scale 52) (sprintf "Two strokes to follow made the hint %d high, from %d" hint.Height alone)
                use font = new Font(UiFace.text "Segoe UI",10.0f)
                check (hint.Width>=TextRenderer.MeasureText("A much longer action name",font).Width+Dpi.scale 34) "A stroke to follow did not fit in the hint"
                // In the settings' face, Segoe UI Variable where Windows has it, as the page beside it.
                let hintFont = typeof<GestureHint>.GetField("font",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic).GetValue(hint) :?> Font
                check (hintFont.Name=font.Name) (sprintf "The hint is set in %s, not %s" hintFont.Name font.Name)
                // Ready to release, the action's row stands on a tint of the accent; a stroke still
                // on its way, with nothing to run yet, has none.
                let theme = name
                let shot name =
                    let image = new Bitmap(hint.Width,hint.Height)
                    hint.DrawToBitmap(image,Rectangle(Point.Empty,image.Size))
                    image.Save(Path.Combine(__SOURCE_DIRECTORY__,"Debug",sprintf "gesture-hint-%s-%s.png" name theme))
                    image
                let tinted (image:Bitmap) =
                    let surface = (SettingsColors.current()).surface
                    let y = Dpi.scale 18
                    [ for x in Dpi.scale 8..image.Width-Dpi.scale 8 -> image.GetPixel(x,y) ]
                    |> List.exists(fun pixel -> abs(int pixel.R-int surface.R)+abs(int pixel.G-int surface.G)+abs(int pixel.B-int surface.B)>12)
                hint.show(Some "U","Switch windows",false,["UL","Previous tab";"UR","Next tab"],pointer)
                use readyWithNext = shot "ready-next"
                hint.show(Some "UR","Switch to next tab",false,[],pointer)
                use ready = shot "ready"
                hint.show(Some "U","",true,["UL","Previous tab";"UR","Next tab"],pointer)
                use onItsWay = shot "on-its-way"
                check (SystemInformation.HighContrast || (tinted ready && tinted readyWithNext && not (tinted onItsWay)))
                      "The action that runs on release does not stand out from a stroke still on its way"
                render hint ("mouse-gestures-hint-"+name)
                hint.hide()
            let everywhereColumns = editor.Columns
            editor.Scope <- Some @"C:\Program Files\Mozilla Firefox\firefox.exe"
            check (editor.Columns=everywhereColumns) (sprintf "The columns moved from %A to %A when another app was picked" everywhereColumns editor.Columns)
            // Still and whole at once: nothing plays until the pointer is on a row.
            pump 100
            render form ("mouse-gestures-firefox-"+name)
            // The hint switch and Add gesture sit at the bottom of a page with room to spare.
            let footerGap = page.ClientSize.Height-page.PointToClient(editor.AddGestureButton.PointToScreen(Point(0,editor.AddGestureButton.Height))).Y
            check (footerGap>=0 && footerGap<=Dpi.scale 48) (sprintf "Add gesture sits %d px above the bottom of the page" footerGap)
            // Hovering the override mark points its tip at the mark, from below it.
            let rows = (MouseGestures.appFor (MouseGestureRules.table()) "firefox.exe").Value.bindings
            let marked = rows |> List.findIndex(fun binding -> binding.gesture="DR")
            let mark = editor.OverrideMark marked
            check (not mark.IsEmpty) "Close web tab showed no override mark"
            // On screen, so the tip is not pushed onto the nearest monitor. The pointer is not
            // really there, so the tip hides again at once; where it was put is what counts.
            form.Location <- Screen.PrimaryScreen.WorkingArea.Location
            pump 50
            let before = Application.OpenForms |> Seq.cast<Form> |> List.ofSeq
            WinUserApi.SendMessage(editor.Handle,0x200,IntPtr.Zero,IntPtr((mark.Y+mark.Height/2)*65536+mark.X+mark.Width/2)) |> ignore
            pump 100
            let opened = Application.OpenForms |> Seq.cast<Form> |> Seq.filter(fun f -> not (before |> List.exists(fun b -> obj.ReferenceEquals(b,f)))) |> List.ofSeq
            let markOnScreen = editor.RectangleToScreen(mark)
            form.Location <- Point(-20000,-20000)
            check (opened.Length=1) (sprintf "Hovering the override mark opened %d windows, not one tip" opened.Length)
            let tip = opened.Head.Bounds
            check (tip.Top>=markOnScreen.Bottom && tip.Top<=markOnScreen.Bottom+Dpi.scale 6 && tip.Left<=markOnScreen.Left && tip.Right>=markOnScreen.Right)
                  (sprintf "The override tip at %A is not under its mark at %A" tip markOnScreen)
            WinUserApi.SendMessage(editor.Handle,0x2A3,IntPtr.Zero,IntPtr.Zero) |> ignore
            pump 50
            // A gesture of the list opens in the same dialog, filled in: its gesture picked, what it
            // does set, saved rather than added, and not said to replace itself.
            do
                let table = MouseGestureRules.table()
                let command = Commands.forGestures() |> List.head
                let byCommand = { gesture="DR"; action=GestureCommand command.id }
                use changing = new MouseGestureDialog(None,{ table with everywhere=[byCommand] },byCommand)
                check (changing.Text=tr Strings.MouseGestures.editGesture && changing.AddButton.Text=tr Strings.Common.save) "Changing a gesture did not say so"
                check (changing.Grid.Selected=Some "DR" && changing.Kind.SelectedIndex=1 && changing.Command.Text=tr command.text.caption)
                      (sprintf "The gesture opened to change was not filled in: %A, %d, %s" changing.Grid.Selected changing.Kind.SelectedIndex changing.Command.Text)
                // The link to the drawing box sits at the right, under the tiles' corner.
                changing.PerformLayout()
                let toggle = changing.Controls.Find("gesture-draw-toggle",true).[0]
                check (toggle.Right=changing.Grid.Right && toggle.Left>changing.Grid.Left) "The link to draw a gesture is not at the right edge of the tiles"
                // The keys box is as wide from the start as three combinations need, so it and the
                // name beside it stay put as keys are added.
                use blank = new MouseGestureDialog(None,table)
                let empty = blank.Keys.Width
                blank.Keys.Sequence <- [0x241]
                let one = blank.Keys.Width
                blank.Keys.Sequence <- [0x241;0x242;0x243]
                check (empty=one && one=blank.Keys.Width) (sprintf "The keys box changed width as keys were added: %d, %d, %d" empty one blank.Keys.Width)
                // The box to draw in is put away until its link asks for it, and goes again.
                check (not blank.DrawingShown && blank.DrawToggle.Text=tr Strings.MouseGestures.drawInstead) "The drawing box is out as the dialog opens"
                let click = typeof<LinkLabel>.GetMethod("OnLinkClicked",Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic)
                click.Invoke(blank.DrawToggle,[|box (LinkLabelLinkClickedEventArgs(blank.DrawToggle.Links.[0]))|]) |> ignore
                check (blank.DrawingShown && blank.DrawToggle.Text=tr Strings.MouseGestures.hideDrawing) "The link did not put out the drawing box"
                click.Invoke(blank.DrawToggle,[|box (LinkLabelLinkClickedEventArgs(blank.DrawToggle.Links.[0]))|]) |> ignore
                check (not blank.DrawingShown) "The link did not put the drawing box away again"
                // A gesture an app has of its own that every app has too shows both dots: the app's
                // in its colour, every app's grey beside it.
                do
                    let app = @"C:\Apps\both.exe"
                    let both = { table with everywhere=[{ gesture="U"; action=GestureCommand "next-tab" }]
                                            apps=[{ path=app; enabled=true; bindings=[{ gesture="U"; action=GestureCommand "close-tab" }] }] }
                    use grid = new GestureGrid(Some app,both)
                    use image = new Bitmap(grid.Width,grid.Height)
                    grid.DrawToBitmap(image,Rectangle(Point.Empty,image.Size))
                    let index = MouseGestures.all |> List.findIndex ((=) "U")
                    let tileSize,gap,header = Dpi.scale 48,Dpi.scale 6,Dpi.scale 26
                    let tileRight = (index % 8)*(tileSize+gap)+tileSize
                    let tileTop = header+(index/8)*(tileSize+gap)
                    let dot = Dpi.scale 6
                    let y = tileTop+Dpi.scale 4+dot/2
                    let near (c:Color) (x:Color) = abs(int c.R-int x.R)+abs(int c.G-int x.G)+abs(int c.B-int x.B)<60
                    let own = image.GetPixel(tileRight-Dpi.scale 5-dot/2,y)
                    let every = image.GetPixel(tileRight-Dpi.scale 5-dot-Dpi.scale 3-dot/2,y)
                    check (near own (GestureUi.warning()) && near every (SettingsColors.current()).muted)
                          (sprintf "A gesture of the app and of every app does not show both dots: %A, %A" own every)
                let byKeys = { gesture="UL"; action=GestureKeys([0x254],"Find") }
                use keyed = new MouseGestureDialog(None,{ table with everywhere=[byKeys] },byKeys)
                check (keyed.Kind.SelectedIndex=0 && keyed.Keys.Sequence=[0x254] && keyed.NameBox.Text="Find")
                      (sprintf "The keys of the gesture opened to change were not filled in: %A %s" keyed.Keys.Sequence keyed.NameBox.Text)
            use dialog = new MouseGestureDialog(editor.Scope,MouseGestureRules.table())
            dialog.StartPosition <- FormStartPosition.Manual
            dialog.Location <- Point(-20000,-20000)
            dialog.Show()
            dialog.Grid.Select(Some "DR")
            pump 1500
            render dialog ("mouse-gestures-dialog-"+name)
            // What the dialog says and which kind is picked leave its buttons where they are.
            let placed() = dialog.ClientSize,dialog.PointToClient(dialog.AddButton.PointToScreen(Point.Empty))
            dialog.Choose "DR"
            pump 50
            // A shortcut to send, the first choice, is picked as the dialog opens.
            check (dialog.Kind.SelectedIndex=0 && dialog.Kind.Text=tr Strings.MouseGestures.sendKeys) "The dialog did not open on sending a shortcut, the first choice"
            let first = placed()
            dialog.Kind.SelectedIndex <- 1
            pump 50
            check (placed()=first) (sprintf "Picking a command moved the dialog from %A to %A" first (placed()))
            render dialog ("mouse-gestures-dialog-command-"+name)
            dialog.Choose "RL"
            dialog.Kind.SelectedIndex <- 0
            pump 50
            check (placed()=first) (sprintf "Another gesture moved the dialog from %A to %A" first (placed()))
            check (TextRenderer.MeasureText(tr Strings.MouseGestures.runCommand,dialog.Kind.Font).Width+Dpi.scale 60<=dialog.Kind.Width) "The kind list cuts its choices short"
            // Keys to send may be any key: Esc, Delete and Home alone are taken, not read as
            // cancel, clear or refused for lacking Ctrl or Alt, and Esc leaves the dialog open.
            for key in [Keys.Escape;Keys.Delete;Keys.Home;Keys.Enter;Keys.Back] do
                dialog.Keys.StartRecording()
                check (pressKey dialog.Keys key && dialog.Keys.Sequence=[SettingsShortcut.encode key] && dialog.Visible)
                      (sprintf "%A alone was not taken as a key to send: %A" key dialog.Keys.Sequence)
            // Keys pressed one after another make a sequence, up to three; a pause ends it, and
            // recording again starts it afresh.
            dialog.Keys.StartRecording()
            for key in [Keys.Control ||| Keys.K;Keys.Control ||| Keys.C] do pressKey dialog.Keys key |> ignore
            check (dialog.Keys.Sequence=[0x024B;0x0243] && dialog.Keys.IsRecording) (sprintf "Two keys in a row were taken as %A" dialog.Keys.Sequence)
            render dialog ("mouse-gestures-dialog-sequence-"+name)
            pump 1400
            check (not dialog.Keys.IsRecording) "A pause after the keys did not end the recording"
            dialog.Keys.StartRecording()
            for key in [Keys.A;Keys.B;Keys.C;Keys.D] do pressKey dialog.Keys key |> ignore
            check (dialog.Keys.Sequence=[0x41;0x42;0x43] && not dialog.Keys.IsRecording) (sprintf "Keys past three were taken, or kept it recording: %A" dialog.Keys.Sequence)
            check (placed()=first) (sprintf "Three keys moved the dialog from %A to %A" first (placed()))
            dialog.Keys.Clear()
            // A press on the name field's frame, round its text, puts the caret in the text there,
            // at its start on the left and its end on the right, as a native box's margin does.
            do
                let box = dialog.NameBox
                let frame = box.Parent
                box.Text <- "abc"
                let press (x:int) =
                    WinUserApi.SendMessage(frame.Handle,0x201,IntPtr(1),IntPtr((Dpi.scale 3 <<< 16) ||| x)) |> ignore
                    WinUserApi.SendMessage(box.Handle,0x202,IntPtr.Zero,IntPtr((2 <<< 16) ||| 2)) |> ignore
                    box.SelectionStart,box.SelectionLength
                check (press (frame.Width-Dpi.scale 3)=(3,0)) (sprintf "A press on the frame's right margin left the caret at %A" (box.SelectionStart,box.SelectionLength))
                check (press (Dpi.scale 3)=(0,0)) (sprintf "A press on the frame's left margin left the caret at %A" (box.SelectionStart,box.SelectionLength))
                box.Text <- ""
                // Typed into, the field's foot is the accent, as WinUI marks it; left, a plain line.
                let edge() =
                    use image = new Bitmap(frame.Width,frame.Height)
                    frame.DrawToBitmap(image,Rectangle(Point.Empty,image.Size))
                    image.GetPixel(frame.Width/2,frame.Height-2)
                box.Focus() |> ignore
                pump 50
                if box.Focused then
                    let accent = (SettingsColors.current()).accent
                    let near (a:Color) (b:Color) = abs(int a.R-int b.R)+abs(int a.G-int b.G)+abs(int a.B-int b.B)<90
                    check (near (edge()) accent) (sprintf "The name field's outline was %A while typed into" (edge()))
                    render frame ("mouse-gestures-name-focus-"+name)
                    dialog.Kind.Focus() |> ignore
                    pump 50
                    check (not (near (edge()) accent)) "The name field kept its accent outline after losing the keys"
            // The command list opens under a search box and shows ten rows at most.
            do
                use popup = dialog.Command.CreateDropDown().Value
                let input = ((popup.Items.[0] :?> ToolStripControlHost).Control :?> SettingsSearchBox).Controls.[0] :?> TextBox
                let list = ((popup.Items.[1] :?> ToolStripControlHost).Control :?> SettingsListFrame).List
                let all = list.Items.Count
                check (all>10 && popup.Height<=10*list.ItemHeight+Dpi.scale 60) (sprintf "The command list is %d px tall for %d commands" popup.Height all)
                check (string list.Items.[0]<>tr Strings.MouseGestures.chooseCommand) "The command list offered its prompt as a choice"
                let names = [ for item in list.Items -> string item ]
                let expected = Commands.inPaletteOrder (Commands.forGestures()) |> List.map(fun command -> tr command.text.caption)
                check (names=expected) (sprintf "The commands are not in the palette's order: %A" (List.truncate 5 names))
                // A wheel notch moves the rows by pixels, eased, not a row at a time.
                WinUserApi.SendMessage(list.Handle,0x20A,IntPtr(-120 <<< 16),IntPtr.Zero) |> ignore
                pump 300
                check (list.Top<0 && list.TopIndex=0) (sprintf "A wheel notch left the rows at %d (top row %d)" list.Top list.TopIndex)
                let closeTab = tr (Commands.find "close-tab").Value.text.caption
                input.Text <- closeTab.ToUpperInvariant()
                check (list.Items.Count>=1 && list.Items.Count<all && string list.Items.[0]=closeTab) (sprintf "Searching for %s left %d rows" closeTab list.Items.Count)
                input.Text <- "tab"
                render list ("mouse-gestures-command-search-"+name)
                input.Text <- ""
                check (list.Items.Count=all) "Clearing the search did not bring every command back"
            dialog.Close()
            // An app is taken off the list from over its gestures, away from its switch: a press
            // beside the switch only picks the app.
            do
                let firefox = @"C:\Program Files\Mozilla Firefox\firefox.exe"
                let press (point:Point) = WinUserApi.SendMessage(editor.Handle,0x201,IntPtr(1),IntPtr((point.Y <<< 16) ||| point.X)) |> ignore
                let listed() = (MouseGestures.appFor (MouseGestureRules.table()) firefox).IsSome
                let entry = (MouseGestureRules.table()).apps |> List.findIndex(fun app -> AppIdentity.same app.path firefox)
                let item = Rectangle(0,Dpi.scale 26+(entry+1)*Dpi.scale 46,max (Dpi.scale 200) (min (Dpi.scale 300) (editor.Width*32/100)),Dpi.scale 44)
                press (Point(item.Right-Dpi.scale 52,item.Top+item.Height/2))
                check (listed() && editor.Scope=Some firefox) "A press beside an app's switch took the app off the list"
                let button = editor.RemoveAppBounds
                press (Point(button.X+button.Width/2,button.Y+button.Height/2))
                check (not (listed()) && editor.Scope=None) "The button over an app's gestures did not take the app off the list"
                MouseGestureRules.save (MouseGestures.addApp (MouseGestureRules.table()) firefox MouseGesturePresets.browser)
            form.Close()
            api.setValue("enableMouseGestures",box false)
        // Picking a gesture over one the app has and adding it replaces that one.
        let before = (MouseGestures.appFor (MouseGestureRules.table()) "firefox.exe").Value.bindings
        MouseGestureRules.save (MouseGestures.updateApp (MouseGestureRules.table()) "firefox.exe" (fun app -> { app with bindings=MouseGestures.bind app.bindings { gesture="L"; action=GestureCommand "previous-tab" } }))
        let after = (MouseGestures.appFor (MouseGestureRules.table()) "firefox.exe").Value.bindings
        check (after.Length=before.Length && after.Head.action=GestureCommand "previous-tab") "Adding a gesture the app had did not replace it"
    finally Environment.CurrentDirectory <- original
    printfn "PASS: strokes read, gesture table kept and resolved, presets, app switch, reset, page and dialog renders."

TestInit.run main
