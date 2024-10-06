namespace Bemo
open System
open System.Drawing
open System.Runtime.InteropServices
open System.Windows.Forms
open Bemo.Win32
open Aga.Controls
open Aga.Controls.Tree

type ITaskSwitchGroup =
    abstract member hwnd : IntPtr
    abstract member windows : Set2<IntPtr>
    
type ITaskSwitchDesktop =
    abstract member groups : List2<ITaskSwitchGroup>

type TaskWindowItem = TaskWindowItem of IntPtr * bool

    
type ITaskSwitchListControl =
    abstract member select : int -> unit
    abstract member control : Control
    abstract member onShow : Form -> unit

type TaskWindowNode(item) as this=
    inherit Node()
    let os = OS()
    let (TaskWindowItem(hwnd,isGroup)) = item
    let window = os.windowFromHwnd(hwnd)
    let image = 
        let icon = if window.iconBig.Width > window.iconSmall.Width then window.iconBig else window.iconSmall
        let image = Img(icon.ToBitmap()).resize(Sz(32,32))
        if isGroup then
            let badge = Img(Services.openIcon("Bemo.ico").ToBitmap()).resize(Sz(16,16)).bitmap
            let g = image.graphics
            g.DrawImage(badge, Point(16,16))
        image.bitmap
    do
        this.Text <- window.text
    member this.IconImage with get() = image

type TaskSwitchTreeViewControl(windows:List2<TaskWindowItem>) =
    let nameColumn = TreeColumn("Name", 200)
        
    let nodes = windows.map <| fun window -> TaskWindowNode(window)
    let tree,model = 
        let tree = TreeViewAdv()
        let model = TreeModel()
        tree.FullRowSelect <- true
        tree.UseColumns <- false
        tree.ShowLines <- false
        tree.ShowPlusMinus <- false
        tree.Columns.Add(nameColumn)
        tree.RowHeight <- 36
        tree.NodeControls.Add(
            let control = NodeControls.NodeIcon()
            control.ParentColumn <- nameColumn
            control.LeftMargin <- 3
            control.DataPropertyName <- "IconImage"
            control)
        tree.NodeControls.Add(
            let control = NodeControls.NodeTextBox()
            control.Trimming <- StringTrimming.EllipsisCharacter
            control.DisplayHiddenContentInToolTip <- true
            control.ParentColumn <- nameColumn
            control.DataPropertyName <- "Text"
            control.LeftMargin <- 3
            control)
        nodes.iter(model.Nodes.Add)
        tree.Model <- model
        tree,model

    interface ITaskSwitchListControl with
        member this.select index =  
            tree.SelectedNode <- tree.Root.Children.Item(index)
        member this.control = tree :> Control
        member this.onShow form = 
            let scrollBarWidth = 40
            nameColumn.Width <- form.Width - scrollBarWidth

type TaskSwitchForm(control:ITaskSwitchListControl) as this =
    let os = OS()
    let form = 
        let f = { 
            new Form() with
                override this.CreateParams with get() =
                    let params = base.CreateParams
                    params.ExStyle <- params.ExStyle ||| 
                        WindowsExtendedStyles.WS_EX_DLGMODALFRAME |||
                        WindowsExtendedStyles.WS_EX_TOPMOST
                    params
        }
        let formSize = Size(600,400)
        let screenSize= Screen.PrimaryScreen.Bounds.Size
        f.AutoScroll <- true
        f.ShowInTaskbar <- false
        f.StartPosition <- FormStartPosition.Manual
        f.Location <- Point((screenSize.Width - formSize.Width) / 2,  (screenSize.Height - formSize.Height) / 2)
        f.Size <- formSize
        f.ControlBox <- false
        f.Controls.Add(control.control)
        let window = os.windowFromHwnd(f.Handle)
        f    

    member this.hwnd = form.Handle

    member this.show() = 
        form.Show()
        control.control.Dock <- DockStyle.Fill
        control.onShow(form)
        let os = OS()
        os.windowFromHwnd(form.Handle).setForegroundOrRestore(true)
                
    member this.hide() = form.Hide()

    member this.select(index) =
        control.select(index)

    member this.inputControl = control.control

type TaskSwitchAction(windows:List2<TaskWindowItem>) as this =
    let os = OS()
    let Cell = CellScope()        
    let switchIndex = Cell.create(0)
    let form = TaskSwitchForm(TaskSwitchTreeViewControl(windows))
    let endedEvent = Event<_>()

    let setIndex index =
        switchIndex.set(index)
        form.select(index)

    let doSwitch next =
        let len = windows.length
        if len > 0 then
            let index = 
                let index = switchIndex.value + (if next then -1 else 1)
                if index < 0 then len - 1
                elif index > len - 1 then 0
                else index
            setIndex index

    do
        form.show()
        setIndex 0
        
        form.inputControl.LostFocus.Add <| fun e ->
            this.switchEnd(true)

        form.inputControl.KeyDown.Add <| fun e ->
            this.processKeys(e)
            
        form.inputControl.KeyUp.Add <| fun e ->
            this.processKeys(e)


    member this.processKeys(e:KeyEventArgs) =
        e.Handled <- true

    member this.switchNext() = doSwitch true
    member this.switchPrev() = doSwitch false
    member this.switchEnd(cancel:bool) =
        if cancel.not then
            let (TaskWindowItem(hwnd,_)) = windows.at(switchIndex.value)
            os.windowFromHwnd(hwnd).setForegroundOrRestore(false)
        form.hide()
        endedEvent.Trigger()

    member this.selectedHwnd =
        let (TaskWindowItem(hwnd,_)) = windows.at(switchIndex.value)
        hwnd

    member this.ended = endedEvent.Publish

type TaskSwitcher(settings:Settings, desktop:ITaskSwitchDesktop) as this=
    let os = OS()
    let Cell = CellScope()
    let hotKeyManager = HotKeyManager()
    let switcherCell = Cell.create(None:TaskSwitchAction option)
    let doTaskSwitch prev =
        if switcherCell.value.IsNone then
            let switcher = TaskSwitchAction(this.windows)
            switcher.ended.Add <| fun() ->
                switcherCell.set(None)
            switcherCell.set(Some(switcher))
        let switcher = switcherCell.value.Value    
        if prev then switcher.switchPrev() else switcher.switchNext()

    let hook = os.registerKeyboardLLHook <| fun(wParam:IntPtr, hookStruct) ->
        let wParam = int(wParam)
        let isAltReleased() =
            switcherCell.value.IsSome &&
            (Set2(List2([
                WindowMessages.WM_KEYDOWN
                WindowMessages.WM_KEYUP
                WindowMessages.WM_SYSKEYDOWN
                WindowMessages.WM_SYSKEYUP])).contains(wParam)) &&
            (hookStruct.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) = 0

        let isAltCmd(key) =
            wParam = WindowMessages.WM_SYSKEYDOWN &&
            (hookStruct.flags &&& LlKeyboardHookFlags.LLKHF_ALTDOWN) <> 0 &&
            (hookStruct.flags &&& LlKeyboardHookFlags.KF_UP) = 0 &&
            (hookStruct.vkCode = key)

        if isAltReleased() then
            switcherCell.value.iter <| fun switcher -> switcher.switchEnd(false)
            None
        elif isAltCmd(VirtualKeyCodes.VK_TAB) then
            let prev = not(Win32Helper.IsKeyPressed(VirtualKeyCodes.VK_SHIFT))
            doTaskSwitch prev
            Some(1)
        elif isAltCmd(VirtualKeyCodes.VK_ESCAPE) then
            switcherCell.value.iter <| fun switcher -> switcher.switchEnd(true)
            None
        else
            None

    member this.windows =
        let windowsInZorder = os.windowsInZorder.where(fun w -> w.isAltTabWindow && w.pid.isCurrentProcess.not)
        let groupWindowsInSwitcher = settings.settings.groupWindowsInSwitcher
        if groupWindowsInSwitcher then
            let hwndToGroup =
                let zorder  = 
                    let zorders = Map2(windowsInZorder.enumerate.map(fun(i,w) -> w.hwnd,i))
                    fun hwnd ->
                        zorders.tryFind(hwnd).def(Int32.MaxValue)
                Map2(desktop.groups.where(fun g -> g.windows.count > 1).collect(fun g -> 
                    let windows = g.windows.items.sortBy(zorder)
                    List2([(windows.head, (true, g))]).appendList(windows.tail.map(fun hwnd -> (hwnd, (false, g))))
                    ))
            windowsInZorder.map(fun w ->
                let hwnd = w.hwnd
                match hwndToGroup.tryFind hwnd with
                | Some(isTop, group) -> if isTop then Some(TaskWindowItem(hwnd, true)) else None
                | None -> Some(TaskWindowItem(hwnd, false))
            ).choose(id)
        else
            windowsInZorder.map(fun w -> TaskWindowItem(w.hwnd, false))

    interface IDisposable with
        member this.Dispose() = hook.Dispose()
