namespace Bemo
open System
open System.Windows.Forms
open Newtonsoft.Json
open Newtonsoft.Json.Linq

type SettingsRec = {
    includedPaths: Set2<string>
    excludedPaths: Set2<string>
    autoGroupingPaths : Set2<string>
    version: string
    appearance: AppearancePreferences
    runAtStartup: bool
    hideInactiveTabs: bool
    enableTabbingByDefault: bool
    replaceAltTab: bool
    groupWindowsInSwitcher: bool
    enableCtrlNumberHotKey: bool
    numberHotKeyModifier: string
    combineIconsInTaskbar: bool
    enableHoverActivate: bool
    /// "Never", "Maximized" or "Always".
    autoHideMode: string
    /// Expand auto-hidden tabs for a moment after switching tabs.
    showTabsOnSwitch: bool
    enableShiftScroll: bool
    /// "Icons" (large icons in a row) or "List" (window titles in a column).
    switcherStyle: string
    alignment: string
    /// "system", "en", "zh" or "ja".
    language: string
    }

type ISettings =
    abstract member setValue: (string * obj) -> unit
    abstract member getValue: string -> obj
    abstract member notifyValue: string -> (obj -> unit) -> IDisposable
    abstract member appearance: AppearancePreferences
    abstract member updateAppearance: (AppearancePreferences -> AppearancePreferences) -> unit
    abstract member root : JObject with get,set
    /// The settings file in use: next to WindowTabs.exe for a portable copy, else in AppData.
    abstract member path : string
    /// The user's shortcut for a program hotkey (hotkey-control encoding; 0 = none), if changed.
    abstract member hotKey: string -> int option
    abstract member setHotKey: string -> int -> unit

type IFilterService =
    abstract member isAppWindow : IntPtr -> bool
    abstract member isAppWindowStyle : IntPtr -> bool
    abstract member isTabbableWindow : IntPtr -> bool
    abstract member isTabbingEnabledForAllProcessesByDefault : bool with get, set
    abstract member setIsTabbingEnabledForProcess : string -> bool -> unit
    abstract member setIsTabbingEnabledForProcesses : string list -> bool -> unit
    abstract member getIsTabbingEnabledForProcess : string -> bool

type SettingsViewType =
    | GeneralSettings
    | ProgramSettings
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
    abstract member isFirstRun : bool
    /// Posted to the UI thread; returns before the work runs.
    abstract member refresh : unit -> unit
    /// Posted to the UI thread; returns before the work runs.
    abstract member shutdown : unit -> unit
    abstract member setWindowNameOverride : (IntPtr * Option<string>) -> unit
    abstract member getWindowNameOverride : IntPtr -> Option<string>
    abstract member appWindows : List2<IntPtr>
    abstract member getAutoGroupingEnabled : string -> bool
    abstract member setAutoGroupingEnabled : string -> bool -> unit
    abstract member tabAppearanceInfo : TabAppearanceInfo
    abstract member setHotKey: string -> int -> bool
    abstract member getHotKey: string -> int
    /// Starts the tab's program again. Posted to the UI thread; returns before the work runs.
    abstract member newTab : IntPtr -> unit
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
