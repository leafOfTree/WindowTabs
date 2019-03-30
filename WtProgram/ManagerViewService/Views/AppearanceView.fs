namespace Bemo
open System
open System.Drawing
open System.IO
open System.Windows.Forms
open Bemo.Win32
open Bemo.Win32.Forms
open Microsoft.FSharp.Reflection
open System.Resources
open System.Reflection

type AppearanceProperty = {
    displayText : string
    key: string
    propertyType : AppearancePropertyType
    }

and AppearancePropertyType =
    | HotKeyProperty
    | IntProperty
    | ColorProperty

type AppearanceView() as this =
    let colorConfig key displayText = 
        { displayText=displayText; key=key; propertyType=ColorProperty }

    let intConfig key displayText = 
        { displayText=displayText; key=key; propertyType=IntProperty }
    
    let hkConfig key displayText = 
        { displayText=displayText; key=key; propertyType=HotKeyProperty }
        
    let resources = new ResourceManager("Properties.Resources", Assembly.GetExecutingAssembly());

    let properties = List2([
        intConfig "tabHeight" "Height"
        intConfig "tabMaxWidth" "Max Width"
        intConfig "tabOverlap" "Overlap"
        colorConfig "tabNormalBgColor" "Background Normal"
        colorConfig "tabHighlightBgColor" "Background Highlight"
        colorConfig "tabActiveBgColor" "Background Active"
        colorConfig "tabFlashBgColor" "Background Flash"
        colorConfig "tabBorderColor" "Border"
        intConfig "tabIndentNormal" "Indent Normal"
        intConfig "tabIndentFlipped" "Indent Flipped"
        ])

    let panel = 
        let panel = TableLayoutPanel()
        panel.AutoScroll <- true
        panel.Dock <- DockStyle.Fill
        panel.GrowStyle <- TableLayoutPanelGrowStyle.FixedSize
        panel.Padding <- Padding(10)
        panel.RowCount <- properties.length + 1
        List2([0..properties.length]).iter <| fun row ->
            panel.RowStyles.Add(RowStyle(SizeType.Absolute, 35.0f)).ignore
        panel.ColumnCount <- 2
        panel

   
    let editors = properties.enumerate.fold (Map2()) <| fun editors (i,prop) ->
        let label =
            let label = Label()
            label.AutoSize <- true
            label.Text <- resources.GetString(prop.displayText)
            label.TextAlign <- ContentAlignment.MiddleLeft
            label
        let editor = 
            match prop.propertyType with
            | ColorProperty -> ColorEditor() :> IPropEditor
            | IntProperty -> IntEditor() :> IPropEditor
            | HotKeyProperty -> HotKeyEditor() :> IPropEditor

        editor.control.Dock <- DockStyle.Fill
        editor.control.Margin <- Padding(0,5,0,5)
        label.Margin <- Padding(0,5,0,5)
        panel.Controls.Add(label)
        panel.Controls.Add(editor.control)
        panel.SetRow(label, i)
        panel.SetColumn(label, 0)
        panel.SetRow(editor.control, i)
        panel.SetColumn(editor.control, 1)
        editors.add prop.key editor

    let setEditorValues appearance =
        properties.iter <| fun prop ->
            let editor = editors.find prop.key
            try
                editor.value <- Serialize.readField appearance prop.key
            with | _ ->()

    let appearance = Services.program.tabAppearanceInfo

    let buttonPanel =
        let btn = Button()
        btn.Text <- resources.GetString("Reset")
        btn.Click.Add <| fun _ ->
            let appearance = Services.program.defaultTabAppearanceInfo
            setEditorValues appearance
            this.applyAppearance()
        btn

    do  
        panel.Controls.Add(buttonPanel)
        let btnRow = properties.length
        panel.SetRow(buttonPanel, btnRow)
        panel.SetColumn(buttonPanel, 1)
        buttonPanel.Anchor <- AnchorStyles.Top ||| AnchorStyles.Right
        setEditorValues appearance
        editors.items.map(snd).iter <| fun editor ->
            editor.changed.Add <| fun() -> this.applyAppearance()
        
    member this.applyAppearance() =
        let appearance = properties.fold appearance <| fun appearance property ->
            let value = (editors.find property.key).value
            (Serialize.writeField appearance property.key value) :?> TabAppearanceInfo
        Services.settings.setValue("tabAppearance", box(appearance))
        
    interface ISettingsView with
        member x.key = SettingsViewType.AppearanceSettings
        member x.title = resources.GetString("Appearance")
        member x.control = panel :> Control

