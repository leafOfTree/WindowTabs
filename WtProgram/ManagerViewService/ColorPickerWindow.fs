namespace Bemo
open System
open System.Drawing
open System.Windows.Forms

/// The settings colour picker on its own, for a Custom… choice in a menu or the palette, so every
/// place a colour is picked looks the same rather than opening the system's colour box. A window
/// of its own rather than a drop-down: a drop-down never hears of a click in another app, so it
/// stayed open over the window it was colouring. A click elsewhere keeps the colour, as in Settings.
type ColorPickerWindow(initial:Color) =
    inherit Form(FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,
                 TopMost=true,KeyPreview=true,Font=SettingsUi.bodyFont())
    let finished = Event<bool>()
    let picker = new SettingsColorPicker(Color=initial)
    let mutable ended = false
    let mutable wired = false
    let finish keep =
        if not ended then
            ended <- true
            // Enter in the code's box keeps what was typed there.
            if keep then picker.CommitCode()
            picker.Flush()
            finished.Trigger keep
    /// The window it is centred over, kept on that window's screen.
    member val Area = Rectangle.Empty with get,set
    /// Each colour as it is picked or typed.
    member _.Changed = picker.Changed
    /// Once, as it closes: whether the colour is kept.
    member _.Finished = finished.Publish
    member _.Picker = picker
    /// Enter or a click elsewhere keep the colour; Esc does not.
    member this.Finish keep =
        finish keep
        this.Close()
    override this.ProcessCmdKey(msg:byref<Message>,keys:Keys) =
        match keys with
        | Keys.Enter -> this.Finish true; true
        | Keys.Escape -> this.Finish false; true
        | _ -> base.ProcessCmdKey(&msg,keys)
    override _.CreateParams =
        let cp = base.CreateParams
        cp.ExStyle <- cp.ExStyle ||| WindowsExtendedStyles.WS_EX_TOOLWINDOW
        cp.ClassStyle <- cp.ClassStyle ||| SystemCorners.shadowClassStyle()
        cp
    /// Laid out and wired here rather than in the constructor: a self reference there puts an
    /// initialization check on every member, which a finalizer's Dispose can fail.
    override this.OnLoad(e) =
        if not wired then
            wired <- true
            let p = SettingsColors.current()
            this.BackColor <- p.surface
            this.ForeColor <- p.text
            this.Controls.Add(picker)
            picker.ShowCode()
            picker.BackColor <- p.surface
            picker.ApplyTheme()
            // The picker keeps 6px round its parts; 8 more make the window's margin 14 all round.
            let margin = Dpi.scale 8
            picker.Location <- Point(margin,margin)
            this.ClientSize <- Size(picker.Right+margin,picker.Bottom+margin)
            this.Deactivate.Add(fun _ -> this.Finish true)
            SystemCorners.keep this (fun () -> (SettingsColors.current()).border) |> ignore
            if not this.Area.IsEmpty then
                let screen = Screen.FromRectangle(this.Area).WorkingArea
                let x = this.Area.Left+(this.Area.Width-this.Width)/2 |> min (screen.Right-this.Width) |> max screen.Left
                let y = this.Area.Top+(this.Area.Height-this.Height)/2 |> min (screen.Bottom-this.Height) |> max screen.Top
                this.Location <- Point(x,y)
        base.OnLoad(e)
    override this.OnShown(e) =
        base.OnShown(e)
        this.Activate()
        picker.Focus() |> ignore
    /// Closed some other way, such as Alt+F4, it keeps the colour too.
    override this.OnFormClosed(e) =
        finish true
        base.OnFormClosed(e)
    override this.OnPaint(e) =
        base.OnPaint(e)
        // Windows 11 draws the outline with the rounded corners.
        if not SystemCorners.supported.Value then
            use pen = new Pen((SettingsColors.current()).border)
            e.Graphics.DrawRectangle(pen,0,0,this.ClientSize.Width-1,this.ClientSize.Height-1)

module ColorPickerPopup =
    let show (area:Rectangle) (initial:Color) (changed:Color -> unit) (closed:bool -> unit) =
        let window = new ColorPickerWindow(initial,Area=area)
        window.Changed.Add(changed)
        window.Finished.Add(closed)
        window.Show()
