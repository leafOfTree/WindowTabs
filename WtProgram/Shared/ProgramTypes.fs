namespace Bemo
open System
open System.Windows.Forms
open Newtonsoft.Json
open Newtonsoft.Json.Linq

[<AttributeUsage(System.AttributeTargets.Method)>]
type ServiceMethodAttribute() =
    inherit Attribute()
    let mutable _async = false

    member this.async
        with get() = _async
        and set(value) = _async <- value

type IServiceAsyncResult =
    abstract member onCompleted : (obj -> unit) -> unit

type SettingsRec = {
    licenseKey: string
    ticket: string option
    includedPaths: Set2<string>
    excludedPaths: Set2<string>
    autoGroupingPaths : Set2<string>
    version: string
    tabAppearance: TabAppearanceInfo
    runAtStartup: bool
    hideInactiveTabs: bool
    enableTabbingByDefault: bool
    replaceAltTab: bool
    groupWindowsInSwitcher: bool
    enableCtrlNumberHotKey: bool
    combineIconsInTaskbar: bool
    enableHoverActivate: bool
    }

type ILicenseManager =
    abstract member isLicensed : bool
    abstract member licenseKey : string with get,set
    abstract member setTicketString : string -> unit

type ISettings =
    abstract member setValue: (string * obj) -> unit
    abstract member getValue: string -> obj
    abstract member notifyValue: string -> (obj -> unit) -> unit
    abstract member root : JObject with get,set

type IFilterService =
    abstract member isAppWindow : IntPtr -> bool
    abstract member isAppWindowStyle : IntPtr -> bool
    abstract member isTabbableWindow : IntPtr -> bool
    abstract member isTabbingEnabledForAllProcessesByDefault : bool with get, set
    abstract member setIsTabbingEnabledForProcess : string -> bool -> unit
    abstract member getIsTabbingEnabledForProcess : string -> bool

type SettingsViewType =
    | ProgramSettings
    | LicenseSettings
    | AppearanceSettings
    | DiagnosticsSettings
    | LayoutSettings
    | HotKeySettings

type ISettingsView =
    abstract key : SettingsViewType
    abstract title : string
    abstract control : Control

type IPropEditor =
    abstract member value : obj with get,set
    abstract member control : Control
    abstract member changed : IEvent<unit>

type IManagerView =
    abstract member show : unit -> unit
    abstract member show : SettingsViewType -> unit

type IProgram =
    abstract member version : string
    abstract member isUpgrade : bool
    abstract member isFirstRun : bool
    [<ServiceMethod(async=true)>]
    abstract member refresh : unit -> unit
    [<ServiceMethod(async=true)>]
    abstract member shutdown : unit -> unit
    abstract member tabLimit : int option
    abstract member setWindowNameOverride : (IntPtr * Option<string>) -> unit
    abstract member getWindowNameOverride : IntPtr -> Option<string>
    abstract member appWindows : List2<IntPtr>
    abstract member getAutoGroupingEnabled : string -> bool
    abstract member setAutoGroupingEnabled : string -> bool -> unit
    abstract member tabAppearanceInfo : TabAppearanceInfo
    abstract member defaultTabAppearanceInfo : TabAppearanceInfo
    [<ServiceMethod(async=true)>]
    abstract member ping : unit -> unit
    abstract member setHotKey: string -> int -> unit
    abstract member getHotKey: string -> int
    [<ServiceMethod(async=true)>]
    abstract member notifyNewVersion : unit -> unit
    abstract member newVersion : IEvent<unit>
    abstract member suspendTabMonitoring : unit -> unit
    abstract member resumeTabMonitoring : unit -> unit
    abstract member llMouse : IEvent<int32 * IntPtr>

type IGroup =
    abstract member hwnd : IntPtr
    abstract member addWindow: IntPtr * bool -> unit
    abstract member removeWindow: IntPtr -> unit
    abstract member switchWindow: bool * bool -> unit
    abstract member windows: List2<IntPtr>
    abstract member destroy: unit -> unit

type IDesktop =
    abstract member isDragging : bool
    abstract member isEmpty : bool
    abstract member createGroup: bool -> IGroup
    abstract member restartGroup: IntPtr * bool -> unit
    abstract member groups : List2<IGroup>
    abstract member groupExited: IEvent<IGroup>
    abstract member groupRemoved: IEvent<IGroup>
    abstract member foregroundGroup: IGroup option

type IPlugin =
    abstract member init: unit -> unit
