namespace Bemo
open System
open System.Windows.Forms
open Bemo.Json

/// A palette colour is kept by its place in the palette, so it follows the light or dark theme.
type TabColorChoice =
    | PaletteColor of int
    | CustomColor of Drawing.Color

type AppGroupingTarget = { id:string; name:string }

type AppGroupingRule = {
    id: string
    enabled: bool
    processPath: string
    title: string
    /// Retained for reading older exports; application mappings normalize this to -1.
    matchType: int
    groupId: string
    groupName: string
}

type SettingsRec = {
    includedPaths: Set2<string>
    excludedPaths: Set2<string>
    autoGroupingPaths : Set2<string>
    appGroupingRules: AppGroupingRule list
    appGroupingTargets: AppGroupingTarget list
    version: string
    appearance: AppearancePreferences
    runAtStartup: bool
    hideInactiveTabs: bool
    enableTabbingByDefault: bool
    replaceAltTab: bool
    groupWindowsInSwitcher: bool
    enableCtrlNumberHotKey: bool
    enableNumberLeader: bool
    appTabColors: Map<string,string>
    tabColorMode: string
    tabColorStyle: string
    numberLeaderKeys: string
    numberHotKeyModifier: string
    disabledNumberShortcutPaths: Set2<string>
    enabledNumberShortcutPaths: Set2<string>
    combineIconsInTaskbar: bool
    enableHoverActivate: bool
    /// Tab titles in bold.
    tabTextBold: bool
    /// Percent the tabs are solid while the pointer is elsewhere.
    tabOpacity: int
    /// "Never", "Maximized" or "Always".
    autoHideMode: string
    /// Off, Windows (groups, positions and splits among the windows open) or Apps (their apps started too).
    sessionRestore: string
    /// Expand auto-hidden tabs for a moment after switching tabs.
    showTabsOnSwitch: bool
    enableShiftScroll: bool
    /// Ctrl + scroll over the tabs moves the current tab.
    enableCtrlScroll: bool
    enableMiddleClickClose: bool
    /// A picture of a tab's window while the pointer rests on the tab.
    tabHoverPreview: bool
    /// "Icons" (large icons in a row) or "List" (window titles in a column).
    switcherStyle: string
    tabSearchScope: string
    alignment: string
    tabPosition: string
    tabColumnWidth: int
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
    /// Opens the window on the page of a setting, scrolled to it, as choosing it in the search does.
    abstract member showSetting : string -> unit

/// Which rows the palette lists: the active group's tabs, every window, or one of its pages.
type PaletteScope =
    | GroupTabs
    | AllTabs
    /// The commands and the settings, as one list.
    | ActionScope

type IProgram =
    abstract member version : string
    abstract member isFirstRun : bool
    /// Posted to the UI thread; returns before the work runs.
    abstract member refresh : unit -> unit
    /// Regroup the selected apps on the main STA without transient tab-setting changes.
    abstract member regroupApplications : string list -> unit
    /// Posted to the UI thread; returns before the work runs.
    abstract member shutdown : unit -> unit
    abstract member setWindowNameOverride : (IntPtr * Option<string>) -> unit
    abstract member getWindowNameOverride : IntPtr -> Option<string>
    abstract member getTabColorOverride : IntPtr -> TabColorChoice option
    abstract member setTabColorOverride : (IntPtr * TabColorChoice option) -> unit
    abstract member getTabColor : IntPtr * IntPtr list -> TabColorChoice option
    /// Every tab's colour in one call, for a group whose tabs changed.
    abstract member getTabColors : IntPtr list * IntPtr list -> TabColorChoice option list
    /// Groups made by dragging tabs, as remembered: each one's id and its windows' programs (the
    /// path last seen running, else the executable name), titles and, for a window that is open,
    /// its handle (zero when closed).
    abstract member handMadeGroups : unit -> (int * (string * string * IntPtr) list) list
    /// Forgets a group made by hand and takes its open windows apart, to be grouped as rules say.
    abstract member ungroupHandMade : int -> unit
    /// The picture chosen for the taskbar button of the group a window is in, if any.
    abstract member groupIcon : IntPtr -> string option
    /// Chooses, or with None clears, the picture for the taskbar button of a window's group.
    abstract member setGroupIcon : IntPtr * string option -> unit
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
    /// Windows an empty pane can take, on this desktop: handle, tab name and program.
    abstract member paneCandidates : unit -> (IntPtr * string * string) list
    /// Opens the palette on a scope, the windows when none is given, or closes it when it is
    /// open on that scope already; open on another, it switches. Posted to the UI thread.
    abstract member toggleSearch : PaletteScope option -> unit
    /// Saves the windows as a new workspace, as the Save layout button does. Posted to the UI thread.
    abstract member saveWorkspace : unit -> unit
    /// Brings back the workspace last saved or restored. Posted to the UI thread.
    abstract member restoreWorkspace : unit -> unit
    /// Brings back the workspace of that name. Posted to the UI thread.
    abstract member restoreWorkspaceNamed : string -> unit
    /// Takes a tab out of its group into a group of its own, as dragging it out does.
    abstract member detachTab : IntPtr -> unit
    /// The keys given to windows from the search box.
    abstract member windowShortcuts : unit -> WindowShortcut list
    /// Gives a window its key, or with a code of 0 takes it away; false when another program
    /// holds the key.
    abstract member setWindowShortcut : WindowShortcut -> bool

/// Whether a group set to combine its taskbar icons has its one button, and if not, why its
/// windows keep their own: else the setting looked on while nothing was combined.
type TaskbarCombineState =
    | Combined
    | SingleWindow
    /// WindowTabs and a window run with different rights, such as one as administrator.
    | DifferentPermissions
    /// Windows has not told which virtual desktop the windows are on.
    | DesktopUnknown
    /// Windows did not let the group's button move to the windows' virtual desktop.
    | PlacementFailed

/// Each group's state by its strip's handle, written on the group's thread and read from any.
module TaskbarCombineStates =
    let private states = Collections.Concurrent.ConcurrentDictionary<IntPtr,TaskbarCombineState>()
    let set (group:IntPtr) state = states.[group] <- state
    let tryGet (group:IntPtr) = match states.TryGetValue group with | true,state -> Some state | _ -> None
    let remove (group:IntPtr) = states.TryRemove(group) |> ignore
    /// Not combined although the group should be, for a reason the user can act on.
    let isBlocked = function DifferentPermissions | DesktopUnknown | PlacementFailed -> true | Combined | SingleWindow -> false

/// What the tab menu does, asked for from outside the group, such as from the palette or a
/// hotkey; the window is the tab's, where one is needed.
type GroupCommand =
    | RenameTab of IntPtr
    | ResetTabName of IntPtr
    | CloseOtherTabs of IntPtr
    /// Every tab of the window's program.
    | CloseAppTabs of IntPtr
    | CloseAllTabs
    | SortTabs
    /// By that many places along the strip, backwards when negative.
    | MoveTab of IntPtr * int
    | SetTabColor of IntPtr * TabColorChoice option
    | SetTabPinned of IntPtr * bool
    | ToggleIconsOnly
    | SetTabDock of TabDock
    | SetTabAlignment of Bemo.TabAlignment
    /// "Never", "Maximized" or "Always", for this group.
    | SetAutoHide of string

type IGroup =
    abstract member hwnd : IntPtr
    /// A tab menu command, run on the group's thread.
    abstract member run : GroupCommand -> unit
    abstract member addWindow: IntPtr * bool -> unit
    abstract member removeWindow: IntPtr -> unit
    abstract member switchWindow: bool * bool -> unit
    /// Split-view commands from outside the group, such as program hotkeys.
    abstract member executeSplit: SplitCommand -> unit
    /// Lists the windows to put on that side of the tab in front, as the tab menu does.
    abstract member pickPane: SplitAxis * Side -> unit
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
    /// A group's windows, their titles, or whether it was made by hand changed. Main thread.
    abstract member groupChanged: IEvent<IGroup>
    abstract member foregroundGroup: IGroup option
    /// Puts a window, grouped elsewhere or not at all, into the empty pane at the path of the
    /// group whose strip is given.
    abstract member adoptIntoPane: IntPtr * IntPtr * PanePath -> unit
    /// Puts a window, grouped elsewhere or not at all, beside the partner tab of the group whose
    /// strip is given, as the drop says.
    abstract member adoptBeside: IntPtr * IntPtr * LayoutDrop * IntPtr -> unit

type IPlugin =
    abstract member init: unit -> unit

/// Global input hooks must not be active while initial discovery waits for group STAs.
type IStartupInputPlugin =
    inherit IPlugin

module StartupPlugins =
    let initialize (plugins:IPlugin list) discover =
        let input,other = plugins |> List.partition(fun plugin -> plugin :? IStartupInputPlugin)
        other |> List.iter(fun plugin -> plugin.init())
        discover()
        input |> List.iter(fun plugin -> plugin.init())
