namespace Bemo
open System
open System.Collections.Generic
open System.Drawing
open System.Windows.Forms

module GroupEditorPreview =
    let height = 96
    let width = 180
    /// Clip the destination and source together so scrolling never stretches the picture.
    let projection crop (source:Size) (target:Rectangle) (viewport:Rectangle) =
        if source.Width<=0 || source.Height<=0 || target.Width<=0 || target.Height<=0 then None
        else
            let factor = if crop then float target.Width/float source.Width else min (float target.Width/float source.Width) (float target.Height/float source.Height)
            let w,h = max 1 (int(float source.Width*factor)),max 1 (int(float source.Height*factor))
            let dest = Rectangle(target.X+(target.Width-w)/2,target.Y+(if crop then 0 else (target.Height-h)/2),w,h)
            let shown = Rectangle.Intersect(Rectangle.Intersect(dest,target),viewport)
            if shown.Width<=0 || shown.Height<=0 then None
            else
                let sx = (shown.X-dest.X)*source.Width/dest.Width
                let sy = (shown.Y-dest.Y)*source.Height/dest.Height
                let sw = max 1 (shown.Width*source.Width/dest.Width)
                let sh = max 1 (shown.Height*source.Height/dest.Height)
                Some(shown,Rectangle(sx,sy,min sw (source.Width-sx),min sh (source.Height-sy)))

/// Only visible cards own DWM registrations; no screenshots or capture workers are needed.
type GroupEditorPreviews() =
    let thumbnails = Dictionary<IntPtr,IntPtr>()
    let release hwnd =
        DwmApi.DwmUnregisterThumbnail(thumbnails.[hwnd]) |> ignore
        thumbnails.Remove hwnd |> ignore
    member _.Clear() = for hwnd in List.ofSeq thumbnails.Keys do release hwnd
    member _.Count = thumbnails.Count
    member _.Update(owner:IntPtr, crop:bool, viewport:Rectangle, cards:(IntPtr * Rectangle) list) =
        let wanted = cards |> List.filter(fun (hwnd,r) -> r.IntersectsWith viewport && WinUserApi.IsWindow hwnd && not(WinUserApi.IsIconic hwnd))
        for hwnd in List.ofSeq thumbnails.Keys do
            if not(wanted |> List.exists(fun (h,_) -> h=hwnd)) then release hwnd
        for hwnd,target in wanted do
            let thumb =
                match thumbnails.TryGetValue hwnd with
                | true,value -> Some value
                | _ ->
                    let mutable value = IntPtr.Zero
                    if DwmApi.DwmRegisterThumbnail(owner,hwnd,&value)=0 then
                        thumbnails.[hwnd] <- value
                        Some value
                    else None
            thumb |> Option.iter(fun thumb ->
                let mutable size = SIZE()
                let projection =
                    if DwmApi.DwmQueryThumbnailSourceSize(thumb,&size)=0 then
                        GroupEditorPreview.projection crop (Size(size.cx,size.cy)) target viewport
                    else None
                let mutable props = DWM_THUMBNAIL_PROPERTIES()
                props.dwFlags <- 1 ||| 2 ||| 4 ||| 8
                props.opacity <- 255uy
                match projection with
                | Some(dest,source) ->
                    props.rcDestination <- RECT.FromRectangle dest
                    props.rcSource <- RECT.FromRectangle source
                    props.fVisible <- 1
                | None -> props.fVisible <- 0
                DwmApi.DwmUpdateThumbnailProperties(thumb,&props) |> ignore)
    interface IDisposable with
        member this.Dispose() = this.Clear()

/// An owned, non-activating preview keeps the editor's focus and never catches the pointer.
type GroupEditorPreviewTip() =
    inherit Form()
    let thumbnails = new GroupEditorPreviews()
    override _.ShowWithoutActivation = true
    override this.WndProc(message:Message byref) =
        if message.Msg=0x84 then message.Result <- IntPtr(-1)
        elif message.Msg=0x21 then message.Result <- IntPtr(3)
        else base.WndProc(&message)
    override _.CreateParams =
        let cp = base.CreateParams
        cp.ExStyle <- cp.ExStyle ||| 0x08000000 ||| 0x00000080 ||| 0x00000020
        cp
    override this.OnPaint(e) =
        let p = SettingsColors.current()
        TextRenderer.DrawText(e.Graphics,this.Text,this.Font,Rectangle(Dpi.scale 8,0,this.Width-Dpi.scale 16,Dpi.scale 30),p.text,
                              TextFormatFlags.EndEllipsis ||| TextFormatFlags.SingleLine ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPrefix)
        use edge = new Pen(p.border)
        e.Graphics.DrawRectangle(edge,0,0,this.Width-1,this.Height-1)
    member this.Preview(owner:Form,hwnd:IntPtr,title:string,anchor:Rectangle,available:Rectangle) =
        this.FormBorderStyle <- FormBorderStyle.None
        this.ShowInTaskbar <- false
        this.StartPosition <- FormStartPosition.Manual
        this.BackColor <- (SettingsColors.current()).surface
        this.Font <- owner.Font
        this.Text <- title
        let w,h = min (Dpi.scale 460) available.Width,min (Dpi.scale 320) available.Height
        let x = max available.Left (min (available.Right-w) anchor.Left)
        let y = if anchor.Bottom+h<=available.Bottom then anchor.Bottom else max available.Top (anchor.Top-h)
        this.Bounds <- Rectangle(x,y,w,h)
        this.Show(owner)
        let target = Rectangle(Dpi.scale 8,Dpi.scale 30,w-Dpi.scale 16,h-Dpi.scale 38)
        thumbnails.Update(this.Handle,false,this.ClientRectangle,[hwnd,target])
        this.Invalidate()
    member this.Dismiss() =
        this.Hide()
        thumbnails.Clear()
    override this.Dispose(disposing) =
        if disposing then (thumbnails :> IDisposable).Dispose()
        base.Dispose(disposing)
