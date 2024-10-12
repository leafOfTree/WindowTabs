namespace Bemo
open System
open System.Drawing
open System.IO
open System.Windows.Forms
open Bemo.Win32
open Bemo.Win32.Forms
open System.Resources
open System.Reflection


type HotKeyView() =
    let settingsProperty name =
        {
            new IProperty<'a> with
                member x.value
                    with get() = unbox<'a>(Services.settings.getValue(name))
                    and set(value) = Services.settings.setValue(name, box(value))
        }
        
    let resources = new ResourceManager("Properties.Resources", Assembly.GetExecutingAssembly());

    let checkBox (prop:IProperty<bool>) = 
        let checkbox = BoolEditor() :> IPropEditor
        checkbox.value <- box(prop.value)
        checkbox.changed.Add <| fun() -> prop.value <- unbox<bool>(checkbox.value)
        checkbox.control

    let settingsCheckbox key = checkBox(settingsProperty(key))

    let basicForm = 
        let fields = List2([
            ("runAtStartup", settingsCheckbox "runAtStartup")
            ("hideInactiveTabs", settingsCheckbox "hideInactiveTabs")
            ("isTabbingEnabledForAllProcessesByDefault", checkBox(prop<IFilterService, bool>(Services.filter, "isTabbingEnabledForAllProcessesByDefault")))
        ])
        "Basics", UIHelper.form fields

    let taskForm = 
        let fields = List2([
            ("combineIconsInTaskbar", settingsCheckbox "combineIconsInTaskbar")
            ("replaceAltTab", settingsCheckbox "replaceAltTab")
            ("groupWindowsInSwitcher", settingsCheckbox "groupWindowsInSwitcher")
        ])
        "Tasks", UIHelper.form fields

    let switchTabs =
        let hotKeys = List2([
            ("nextTab", "nextTab")
            ("prevTab", "prevTab")
        ])

        let editors = hotKeys.enumerate.fold (Map2()) <| fun editors (i,(key, text)) ->
            let caption = resources.GetString text
            let label = UIHelper.label caption
            let editor = HotKeyEditor() :> IPropEditor
            editor.control.Margin <- Padding(0,5,0,5)
            label.Margin <- Padding(0,5,0,5)
            editors.add key editor

        hotKeys.iter <| fun (key,_) ->
            let editor = editors.find key
            editor.value <- Services.program.getHotKey(key)
            editor.changed.Add <| fun() ->
                Services.program.setHotKey key (unbox<int>(editor.value))
        
        let checkBox (prop:IProperty<bool>) = 
            let checkbox = BoolEditor() :> IPropEditor
            checkbox.value <- box(prop.value)
            checkbox.changed.Add <| fun() -> prop.value <- unbox<bool>(checkbox.value)
            checkbox.control

        let settingsCheckbox key = checkBox(settingsProperty(key))

        let fields = hotKeys.map <| fun(key,text) ->
            let editor = editors.find key
            text, editor.control

        let fields = fields.prependList(List2([
            ("enableCtrlNumberHotKey", settingsCheckbox "enableCtrlNumberHotKey")
        ]))

        "Switch Tabs", UIHelper.form fields

    let sections = List2([
        basicForm
        taskForm
        switchTabs
        ])

    let table = 
        let font = Font(resources.GetString("Font"), 10f)
        let controls = sections.map <| fun(text,control) ->
            control.Dock <- DockStyle.Fill
            let group = GroupBox()
            group.Dock <- DockStyle.Top
            group.Margin <- Padding(10)
            group.AutoSize <- true
            group.Text <- text
            group.Font <- font
            group.Controls.Add(control)
            group :> Control
        let table = UIHelper.vbox controls
        table.Dock <- DockStyle.Fill
        table

    interface ISettingsView with
        member x.key = SettingsViewType.HotKeySettings
        member x.title = resources.GetString("Behavior")
        member x.control = table :> Control

