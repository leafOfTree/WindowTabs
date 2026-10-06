module SettingsWindowPerf

open System
open System.Drawing
open System.IO
open System.Runtime.InteropServices
open System.Windows.Forms
open Bemo
open Newtonsoft.Json.Linq

[<DllImport("user32.dll")>]
extern IntPtr private SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)

/// The settings window, off screen, with isolated settings and stand-in services: opening it,
/// building a page, showing a built page again and moving to a monitor with another scale.
/// No real application windows, input or user preferences are involved.
let run measure iterations (pixels:Bitmap -> string) (results:JArray) (images:JArray) =
    let originalDirectory = Environment.CurrentDirectory
    let isolated = Path.Combine(originalDirectory,"settings-"+Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(isolated) |> ignore
    Environment.CurrentDirectory <- isolated
    try
        use settings = new Settings(true,saveDelay=0)
        Services.register<IFilterService>({ new IFilterService with
            member _.isAppWindow _ = false
            member _.isAppWindowStyle _ = false
            member _.isTabbableWindow _ = false
            member _.isTabbingEnabledForAllProcessesByDefault with get() = false and set _ = ()
            member _.setIsTabbingEnabledForProcess _ _ = ()
            member _.setIsTabbingEnabledForProcesses _ _ = ()
            member _.getIsTabbingEnabledForProcess _ = false })
        let mouse = Event<int32 * IntPtr>()
        Services.register<IProgram>({ new IProgram with
            member _.version = "benchmark"
            member _.isFirstRun = false
            member _.refresh() = ()
            member _.shutdown() = ()
            member _.setWindowNameOverride _ = ()
            member _.getWindowNameOverride _ = None
            member _.getTabColor _ = None
            member _.appWindows = List2()
            member _.getAutoGroupingEnabled _ = false
            member _.setAutoGroupingEnabled _ _ = ()
            member _.tabAppearanceInfo = ThemeService.currentAppearance()
            member _.setHotKey _ _ = true
            member _.getHotKey key = SettingsCatalog.shortcutDefault key
            member _.newTab _ = ()
            member _.suspendTabMonitoring() = ()
            member _.resumeTabMonitoring() = ()
            member _.llMouse = mouse.Publish })
        let dpi = Dpi.value()
        let other = if dpi=96 then 144 else 96
        let openWindow () =
            let frame = DesktopManagerForm(viewFactories=[
                GeneralSettings,"General",(fun () -> GeneralView() :> ISettingsView)
                AppearanceSettings,"Appearance",(fun () -> AppearanceView() :> ISettingsView)
                HotKeySettings,"Shortcuts",(fun () -> HotKeyView() :> ISettingsView) ])
            let form = frame.window
            form.ShowInTaskbar <- false
            form.StartPosition <- FormStartPosition.Manual
            form.Location <- Point(-20000,-20000)
            WinUserApi.ShowWindow(form.Handle,ShowWindowCommands.SW_SHOWNOACTIVATE) |> ignore
            form.Update()
            frame
        let close (frame:DesktopManagerForm) =
            if not (isNull (box frame)) then
                frame.window.Close()
                frame.window.Dispose()
            Application.DoEvents()
        // As a user does: the sidebar button, then the window painted.
        let show (frame:DesktopManagerForm) key =
            let rec all (control:Control) = seq { yield control; for child in control.Controls do yield! all child }
            let caption = tr (Strings.Pages.title key)
            let button = all frame.window |> Seq.pick(function :? SettingsNavigationButton as b when b.Text=caption -> Some b | _ -> None)
            button.PerformClick()
            frame.window.Update()
        let rescale (frame:DesktopManagerForm) (target:int) =
            let form = frame.window
            let bounds = form.Bounds
            let scale value = int(Math.Round(float value*float target/float(Dpi.value())))
            let rect = Marshal.AllocHGlobal(16)
            try
                [bounds.Left;bounds.Top;bounds.Left+scale bounds.Width;bounds.Top+scale bounds.Height]
                |> List.iteri(fun index value -> Marshal.WriteInt32(rect,index*4,value))
                SendMessage(form.Handle,0x02E0,IntPtr(target ||| (target <<< 16)),rect) |> ignore
            finally Marshal.FreeHGlobal(rect)
            form.Update()
        let name scenario = sprintf "settings-%ddpi-%s" dpi scenario
        let fresh _ = Application.DoEvents(); Unchecked.defaultof<DesktopManagerForm>
        let opened _ = Application.DoEvents(); openWindow()
        // The window opens on General, so opening it includes building that page.
        let mutable frame = Unchecked.defaultof<DesktopManagerForm>
        results.Add(measure (name "open-general") iterations fresh (fun _ -> frame <- openWindow()) (fun _ -> close frame))
        results.Add(measure (name "build-appearance") iterations opened (fun f -> show f AppearanceSettings) close)
        results.Add(measure (name "build-shortcuts") iterations opened (fun f -> show f HotKeySettings) close)
        let built = openWindow()
        try
            for key in [AppearanceSettings;HotKeySettings;GeneralSettings] do show built key
            let waiting _ = Application.DoEvents(); built
            results.Add(measure (name "switch-built-page") iterations waiting (fun f ->
                show f (if f.activeView=GeneralSettings then AppearanceSettings else GeneralSettings)) ignore)
            // Three built pages, one shown; each operation moves to the other scale or back, so a
            // hidden page laid out at neither.
            let mutable step = 0
            results.Add(measure (name "rescale-3-pages-back-and-forth") iterations waiting (fun f ->
                rescale f (if step%2=0 then other else dpi)
                step <- step+1) ignore)
            if step%2=1 then rescale built dpi
            Dpi.set dpi
            Application.DoEvents()
            // One move after the pages were seen, the usual case: each operation starts back at
            // this scale with every page shown since.
            let seen _ =
                if Dpi.value()<>dpi then rescale built dpi
                Dpi.set dpi
                for key in [AppearanceSettings;HotKeySettings;GeneralSettings] do show built key
                Application.DoEvents()
                built
            results.Add(measure (name "rescale-3-pages-once") iterations seen (fun f -> rescale f other) ignore)
            rescale built dpi
            Dpi.set dpi
            Application.DoEvents()
            // Whatever made pages faster must not change how they look.
            for key in [GeneralSettings;AppearanceSettings;HotKeySettings] do
                show built key
                Application.DoEvents()
                let form = built.window
                use bitmap = new Bitmap(form.Width,form.Height)
                form.DrawToBitmap(bitmap,Rectangle(Point.Empty,bitmap.Size))
                let image = sprintf "settings-%ddpi-%A" dpi key
                // Saved beside the report, to see what changed when the hashes differ.
                bitmap.Save(Path.Combine(originalDirectory,image+".png"),Imaging.ImageFormat.Png)
                images.Add(JObject(JProperty("name",image),JProperty("sha256",pixels bitmap)))
        finally close built
    finally
        Dpi.set (Dpi.system())
        Environment.CurrentDirectory <- originalDirectory
        try Directory.Delete(isolated,true) with _ -> ()
