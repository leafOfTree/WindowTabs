#r "System.Drawing"
#r "System.Windows.Forms"
#r "Debug/Newtonsoft.Json.dll"
#r "Debug/Win32.dll"
#r "Debug/WindowTabs.exe"
open System
open System.Drawing
open System.IO
open System.Windows.Forms
open System.Runtime.InteropServices
open Bemo
module Native =
    [<DllImport("user32.dll")>]
    extern IntPtr SetThreadDpiAwarenessContext(IntPtr context)
    [<DllImport("user32.dll")>]
    extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam)
let check condition message = if not condition then failwith message
let main() =
    Application.EnableVisualStyles()
    // Settings rows read the theme palette, which comes from the settings service.
    // An isolated settings directory keeps real preferences untouched.
    let originalDirectory = Environment.CurrentDirectory
    let isolated = Path.Combine(__SOURCE_DIRECTORY__,"Debug","dpi-test-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    use settings = new Settings(true, saveDelay=0)
    // The host has no .config file, like the shipped exe: TestInit supplied the option.
    let dpiHelper = typeof<Form>.Assembly.GetType("System.Windows.Forms.DpiHelper",true)
    check (dpiHelper.GetProperty("EnableDpiChangedMessageHandling",Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Static).GetValue(null) :?> bool)
          "WinForms will not rescale forms without WindowTabs.exe.config"
    let previous = Native.SetThreadDpiAwarenessContext(IntPtr(-4))
    let originalDpi = Dpi.value()
    try
        use form = new Form(AutoScaleDimensions=SizeF(float32(Dpi.value()),float32(Dpi.value())),AutoScaleMode=AutoScaleMode.Dpi,
                            ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=Point(-20000,-20000),ClientSize=Size(980,760))
        form.DpiChanged.Add(fun e -> Dpi.set e.DeviceDpiNew)
        let sidebar = new Panel(Dock=DockStyle.Left,Width=Dpi.scale 208)
        let page = new SettingsPage()
        let field = new SettingsCombo([|"System";"Light";"Dark"|])
        SettingsUi.row page.contentTable "Theme" "" field |> ignore
        form.Controls.Add(page)
        form.Controls.Add(sidebar)
        form.Show()
        Application.DoEvents()
        let start = Dpi.forWindow form.Handle
        Dpi.set start
        let width = sidebar.Width
        let memory = Marshal.AllocHGlobal(16)
        try
            for _ in 1..5 do
                for dpi in [120;144;192;96;start] do
                    let bounds = form.Bounds
                    [bounds.Left;bounds.Top;bounds.Right;bounds.Bottom] |> List.iteri(fun i value -> Marshal.WriteInt32(memory,i*4,value))
                    Native.SendMessage(form.Handle,0x02E0,IntPtr(dpi ||| (dpi <<< 16)),memory) |> ignore
                    Application.DoEvents()
                    check (Dpi.value()=dpi) (sprintf "Form did not process DPI change: expected %d got %d" dpi (Dpi.value()))
                    check (abs(sidebar.Width-Dpi.scaleAt dpi 208)<=2) (sprintf "Sidebar at %d: expected %d, got %d (start %d, initial %d)" dpi (Dpi.scaleAt dpi 208) sidebar.Width start width)
                    check (field.Width>0 && field.Height>=Dpi.scale 30) "Editor clipped after DPI transition"
            check (abs(sidebar.Width-width)<=2) "Repeated DPI changes caused layout drift"
        finally Marshal.FreeHGlobal(memory)
        form.Close()
        // A settings page first created after the window moved to a monitor with another scale
        // takes fonts for that scale, like the pages WinForms rescaled.
        Dpi.set start
        Services.register<IFilterService>({
            new IFilterService with
                member _.isAppWindow _ = false
                member _.isAppWindowStyle _ = false
                member _.isTabbableWindow _ = false
                member _.isTabbingEnabledForAllProcessesByDefault with get()=true and set _=()
                member _.setIsTabbingEnabledForProcess _ _ = ()
                member _.setIsTabbingEnabledForProcesses _ _ = ()
                member _.getIsTabbingEnabledForProcess _ = false })
        let frame = DesktopManagerForm(viewFactories=[
            GeneralSettings,"General",(fun () -> GeneralView() :> ISettingsView)
            AppearanceSettings,"Appearance",(fun () -> AppearanceView() :> ISettingsView)])
        use settingsForm = frame.window
        settingsForm.ShowInTaskbar <- false
        settingsForm.StartPosition <- FormStartPosition.Manual
        settingsForm.Location <- Point(-20000,-20000)
        settingsForm.Show()
        let rec labels (control:Control) = seq { if control :? Label then yield control
                                                 for child in control.Controls do yield! labels child }
        Application.DoEvents()
        let target = start*2
        let bounds = settingsForm.Bounds
        let memory = Marshal.AllocHGlobal(16)
        try
            [bounds.Left;bounds.Top;bounds.Right;bounds.Bottom] |> List.iteri(fun i value -> Marshal.WriteInt32(memory,i*4,value))
            Native.SendMessage(settingsForm.Handle,0x02E0,IntPtr(target ||| (target <<< 16)),memory) |> ignore
        finally Marshal.FreeHGlobal(memory)
        Application.DoEvents()
        check (Dpi.value()=target) "Settings window did not process the DPI change"
        frame.showView(AppearanceSettings)
        Application.DoEvents()
        let expected = 10.5f*float32 target/float32(Dpi.system())
        let sizes = Seq.append (labels settingsForm) (settingsForm.Controls.Find("tabStyle",true) |> Seq.cast<Control>)
                    |> Seq.filter(fun c -> c.Visible && c.Font.Style=FontStyle.Regular) |> Seq.map(fun c -> c.Font.Size) |> Seq.distinct |> List.ofSeq
        check (not sizes.IsEmpty && sizes |> List.forall(fun size -> abs(size-expected)<0.05f))
              (sprintf "Page created after the DPI change has fonts %A, expected %.2f" sizes expected)
        settingsForm.Close()
    finally
        Dpi.set originalDpi
        Native.SetThreadDpiAwarenessContext(previous) |> ignore
        Environment.CurrentDirectory <- originalDirectory
    printfn "PASS: repeated native WM_DPICHANGED transitions, sidebar sizing, editor layout and settings pages created at a new scale."
TestInit.run main
