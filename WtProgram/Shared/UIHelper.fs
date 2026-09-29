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

/// Lists an enum's values by their display names; label defaults to the member name.
type EnumEditor<'e when 'e :> Enum>(?label:'e -> string) =
    let values = [| for value in Enum.GetValues(typeof<'e>) -> value :?> 'e |]
    let label = defaultArg label (fun value -> value.ToString())
    let control = new ComboBox(DropDownStyle=ComboBoxStyle.DropDownList)
    do control.Items.AddRange([| for value in values -> box(label value) |])

    member this.value
        with get() = values.[max 0 control.SelectedIndex]
        and set(value:'e) = control.SelectedIndex <- Array.IndexOf(values,value)

    interface IPropEditor with
        member x.value
            with get() = box x.value
            and set(value) = x.value <- value.cast<'e>()
        member x.control = control :> Control
        member x.changed = control.SelectedIndexChanged |> Event.map ignore

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
        okButton.Text <- tr Strings.Common.ok
        okButton.Click.Add <| fun _ ->
            form.DialogResult <- DialogResult.OK

        let cancelButton = new Button()
        cancelButton.Text <- tr Strings.Common.cancel
        
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
