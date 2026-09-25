namespace Bemo
open System

type AnimationWindow(os:OS) as this =
    let scope = CellScope()
    let location = Cell<Pt>(scope, Pt.empty, (=))
    let isWindowVisible = Cell(scope, false, (=))
    let isVisible = Cell<bool>(scope, false, (=))
    let image = Cell<_>(scope, None)
    let alpha = Cell(scope, byte(0xFF), (=))
    let window : IWindow = 
        let styles = WindowsStyles.WS_POPUP
        let stylesExe =
            WindowsExtendedStyles.WS_EX_LAYERED ||| 
            WindowsExtendedStyles.WS_EX_TOOLWINDOW |||
            WindowsExtendedStyles.WS_EX_TOPMOST |||
            WindowsExtendedStyles.WS_EX_TRANSPARENT 
        os.createWindow (fun msg -> msg.def()) styles stylesExe
        
    do  scope.listen <| fun() ->
            let window = os.windowFromHwnd(window.hwnd)
            match isWindowVisible.value, isVisible.value with
            | true, true -> 
                window.updateLocation(location.value)
            | true, false -> 
                window.hide()
                isWindowVisible.set(false)
            | false, true -> 
                match image.value with
                | Some(image) ->
                    window.update(image, location.value, alpha.value)
                    isWindowVisible.set(true)
                | None -> ()
            | false, false -> ()
           
    member this.setIsVisible(value) = isVisible.set(value)
    member this.hasImage = image.value.IsSome
    member this.setImage(value) = image.set(Some(value))
    member this.setLocation(value) = location.set(value)
    member this.setAlpha = alpha.set
    member this.hwnd = window.hwnd
    member this.Dispose() = (window :?> IDisposable).Dispose()