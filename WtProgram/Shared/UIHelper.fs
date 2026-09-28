namespace Bemo
open System
open System.Drawing
open System.Drawing.Text
open System.Windows.Forms
open Bemo.Win32

[<AllowNullLiteral>]
type INode =
    abstract member showSettings : bool

type IntEditor() =
    let control = 
        let control = new NumericUpDown()
        control.Minimum <- decimal(1)
        control.Maximum <- decimal(1000)
        control.Margin <- Padding(0)
        control
    interface IPropEditor with
        member x.value 
            with get() = box(int(control.Value))
            and set(newValue) = control.Value <- decimal(unbox<int>(newValue))
        member x.control = control :> Control
        member x.changed = control.ValueChanged |> Event.map ignore

type TextEditor() =
    let control = 
        let control = new TextBox()
        control
    interface IPropEditor with
        member x.value 
            with get() = box(control.Text)
            and set(newValue) = control.Text <- unbox<string>(newValue)
        member x.control = control :> Control
        member x.changed = control.TextChanged |> Event.map ignore

type BoolEditor() =
    let control = new CheckBox()
    interface IPropEditor with
        member x.value
            with get() = box(control.Checked)
            and set(newValue) = control.Checked <- unbox<bool>(newValue)
        member x.control = control :> Control
        member x.changed = control.CheckedChanged |> Event.map ignore

type EnumEditor<'e when 'e :> Enum>() as this =
    let control = new ComboBox()
    let mutable cachedValue = null
    do this.init()

    member this.init() =
        for tag in Enum.GetValues(typeof<'e>) do
            let tag = tag.cast<'e>()
            control.Items.Add(tag.ToString()).ignore
        control.SelectedValueChanged.Add <| fun _ ->
            cachedValue <- control.SelectedItem

    member this.value
        with get() = 
            let value = cachedValue
            Enum.Parse(typeof<'e>, string(value)).cast<'e>()
        and set(value) =
            control.SelectedItem <- value.ToString()

    interface IPropEditor with
        member x.value
            with get() = this.value.cast<obj>()
            and set(value) = this.value <- value.cast<'e>()
        member x.control = control :> Control
        member x.changed = control.SelectedValueChanged |> Event.map ignore

type ColorEditor() as this =
    let changedEvent = Event<_>()
    let chooserButton = 
        let btn = new Button()
        btn.Tag <- "color-swatch"
        btn.Width <- btn.Height
        btn.Click.Add <| fun _ -> 
            let dlg = new System.Windows.Forms.ColorDialog()
            dlg.Color <- this.color
            dlg.FullOpen <- true
            dlg.ShowHelp <- false
            if dlg.ShowDialog() = DialogResult.OK then
                (this :> IPropEditor).value <- dlg.Color
                changedEvent.Trigger()
        btn.Padding <- Padding(0)
        btn.Margin <- Padding(0)
        btn.Dock <- DockStyle.Right
        btn
        
    let textBox = 
        let tb = new TextBox()
        let maxLen = 6
        let save() =
            (this :> IPropEditor).value <- this.colorFromTb
            changedEvent.Trigger()
        tb.Dock <- DockStyle.Fill
        tb.CharacterCasing <- CharacterCasing.Upper
        tb.Margin <- Padding(0)
        tb.KeyPress.Add <| fun e ->
            try
                if e.KeyChar = (char)Keys.Enter then
                    e.Handled <- true
                    save()
                elif Char.IsControl(e.KeyChar).not then
                    if tb.Text.Length + 1 - tb.SelectionLength  > maxLen then raise (Exception())
                    Int32.Parse(e.KeyChar.ToString(), Globalization.NumberStyles.HexNumber).ignore
            with ex -> 
                e.Handled <- true            
        tb.Validating.Add <| fun e ->
            try
                if tb.Text.Length > maxLen then raise (Exception())
                this.colorFromTb.ignore
            with ex -> 
                e.Cancel <- true
                tb.SelectAll()
                MessageBox.Show(Localization.text "Invalid colour. Enter six hexadecimal digits." "颜色无效，请输入六位十六进制数。").ignore

        tb.Validated.Add <| fun e -> save()
        tb

    let panel = 
        let panel = new TableLayoutPanel()
        panel.GrowStyle <- TableLayoutPanelGrowStyle.FixedSize
        panel.RowCount <- 1
        panel.ColumnCount <- 2
        panel.RowStyles.Add(RowStyle(SizeType.Absolute, 25.0f)).ignore
        panel.ColumnStyles.Add(ColumnStyle(SizeType.Percent, 0.9f)).ignore
        panel.ColumnStyles.Add(ColumnStyle(SizeType.Absolute, 25.0f)).ignore
        panel.AutoSize <- true
        panel.Padding <- Padding(0)
        panel.Margin <- Padding(0)
        panel.Controls.Add(textBox)
        panel.Controls.Add(chooserButton)
        panel.SetRow(textBox, 0)
        panel.SetColumn(textBox, 0)
        panel.SetRow(chooserButton, 0)
        panel.SetColumn(chooserButton, 1)
        panel
    member this.colorFromTb =
        let text = textBox.Text
        let value = Int32.Parse(text, Globalization.NumberStyles.HexNumber)
        Color.FromRGB(value)

    member this.color = chooserButton.BackColor
    interface IPropEditor with
        member x.value 
            with get() = 
                let text = textBox.Text
                let value = Int32.Parse(text, Globalization.NumberStyles.HexNumber)
                box(Color.FromRGB(value))
            and set(newColor) = 
                let color = unbox<Color>(newColor)
                chooserButton.BackColor <- color
                textBox.Text <- sprintf "%X" (color.ToRGB())
        member x.control = panel :> Control
        member x.changed = changedEvent.Publish


module UIHelper =

    let label text =
        let label = new Label()
        label.AutoSize <- true
        label.Text <- text
        label.TextAlign <- ContentAlignment.MiddleLeft
        label
        
    

    let form (fields:List2<_>) =
        let panel = 
            let t = new TableLayoutPanel()
            t.AutoScroll <- true
            t.AutoSize <- true
            t.Dock <- DockStyle.Fill
            //t.Padding <- Padding(10)
            t.RowCount <- fields.length
            t.ColumnCount <- 2
            // Make control align right
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10f)) |> ignore
            t

        fields.enumerate.iter <| fun (i,(text, control:Control)) ->
            let label = label text
            control.Dock <- DockStyle.Fill
            label.Margin <- Padding(0,5,0,5)
            panel.Controls.Add(label)
            panel.Controls.Add(control)
            panel.SetRow(label, i)
            panel.SetColumn(label, 0)
            panel.SetRow(control, i)
            panel.SetColumn(control, 1)
        panel
              
    let vbox (controls:List2<Control>) =
        let t = 
            let t = new TableLayoutPanel()
            t.AutoScroll <- true
            t.AutoSize <- true
            t.RowCount <- controls.length
            t.ColumnCount <- 1
            t
        controls.enumerate.iter <| fun(i,control) ->
            t.Controls.Add(control)
            t.SetRow(control, i)
            t.SetColumn(control, 0)
            t.RowStyles.Add(RowStyle()).ignore
        t  

    let hbox (controls:List2<Control>) =
        let t = 
            let t = new TableLayoutPanel()
            t.AutoScroll <- true
            t.AutoSize <- true
            t.RowCount <- 1
            t.ColumnCount <- controls.length
            t
        controls.enumerate.iter <| fun(i,control) ->
            t.Controls.Add(control)
            t.SetRow(control, 0)
            t.SetColumn(control, i)
            t.ColumnStyles.Add(ColumnStyle()).ignore
        t  

    let okCancelForm control =
        let form = new Form()
        form.Padding <- Padding(12)
        
        let okButton = new Button()
        okButton.Text <- Localization.text3 "OK" "确定" "OK"
        okButton.Click.Add <| fun _ ->
            form.DialogResult <- DialogResult.OK

        let cancelButton = new Button()
        cancelButton.Text <- Localization.text3 "Cancel" "取消" "キャンセル"
        
        cancelButton.Click.Add <| fun _ ->
            form.DialogResult <- DialogResult.Cancel

        let buttonPanel = hbox (List2([okButton.cast<Control>(); cancelButton.cast<Control>()]))
        let vboxLayout = vbox (List2([control; buttonPanel.cast<Control>()]))
        vboxLayout.RowStyles.Item(0).SizeType <- SizeType.AutoSize
        vboxLayout.RowStyles.Item(1).SizeType <- SizeType.Absolute
        buttonPanel.Anchor <- AnchorStyles.Bottom ||| AnchorStyles.Right
        vboxLayout.Dock <- DockStyle.Fill
        form.Controls.Add(vboxLayout)
        form
