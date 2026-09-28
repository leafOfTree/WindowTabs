namespace Bemo
open System

type SettingBinding =
    | Toggle of key:string * freshDefault:bool * existingDefault:bool
    | Number of minimum:int * maximum:int
    | Choice of key:string * values:string list * defaultValue:string
    | Shortcut of key:string * defaultCode:int
    | Colour
    | Navigation

type SettingDefinition = {
    id:string
    page:SettingsViewType
    caption:string * string
    description:string * string
    keywords:string
    binding:SettingBinding }

module SettingsCatalog =
    let localize (en,zh) = Localization.text en zh
    let all = [
        { id="theme"; page=AppearanceSettings; caption=("Theme","主题"); description=("Choose system, light, or dark mode.","选择跟随系统、浅色或深色模式。"); keywords="theme system light dark 系统 浅色 深色"; binding=Choice("tabThemeMode",["system";"light";"dark"],"system") }
        { id="language"; page=GeneralSettings; caption=("Language","语言"); description=("Follow Windows or choose a language.","跟随 Windows 或指定语言。"); keywords="language locale english chinese japanese 语言 中文 英文 日语 日本語 言語"; binding=Choice("language",["system";"en";"zh";"ja"],"system") }
        { id="launch-at-sign-in"; page=GeneralSettings; caption=("Start with Windows","随 Windows 启动"); description=("Start WindowTabs when you sign in to Windows.","登录 Windows 时自动启动 WindowTabs。"); keywords="launch-at-sign-in startup autostart 开机 自启动"; binding=Toggle("runAtStartup",true,false) }
        { id="enable-tabs-for-new-apps"; page=GeneralSettings; caption=("Enable tabs by default","默认启用标签"); description=("Applies to apps without a rule in App rules.","适用于未在“应用规则”中单独设置的应用。"); keywords="enable-tabs-for-new-apps"; binding=Toggle("enableTabbingByDefault",true,false) }
        { id="dim-inactive-groups"; page=GeneralSettings; caption=("Dim inactive groups","淡化非活动分组"); description=("Make tabs in inactive groups translucent.","使非活动分组中的标签半透明。"); keywords="dim-inactive-groups"; binding=Toggle("hideInactiveTabs",true,false) }
        { id="auto-hide-maximized-tabs"; page=GeneralSettings; caption=("Auto-hide tabs when maximized","最大化时自动隐藏标签"); description=("Move the pointer to the top of the window to show the tabs.","将鼠标移到窗口顶部即可显示标签。"); keywords="auto-hide-maximized-tabs"; binding=Toggle("autoHide",true,true) }
        { id="tab-alignment"; page=GeneralSettings; caption=("Tab position","标签位置"); description=("Choose where tabs appear above the window.","选择标签在窗口顶部的位置。"); keywords="tab-alignment"; binding=Choice("alignment",["Left";"Center";"Right"],"Center") }
        { id="combine-taskbar-icons"; page=GeneralSettings; caption=("Combine taskbar icons","合并任务栏图标"); description=("",""); keywords="combine-taskbar-icons"; binding=Toggle("combineIconsInTaskbar",false,true) }
        { id="use-windowtabs-for-alt-tab"; page=GeneralSettings; caption=("Use WindowTabs for Alt+Tab","使用 WindowTabs 切换 Alt+Tab"); description=("",""); keywords="use-windowtabs-for-alt-tab"; binding=Toggle("replaceAltTab",false,false) }
        { id="group-windows-in-the-switcher"; page=GeneralSettings; caption=("Group windows in the switcher","在切换器中合并分组"); description=("",""); keywords="group-windows-in-the-switcher"; binding=Toggle("groupWindowsInSwitcher",false,false) }
        { id="next-tab"; page=HotKeySettings; caption=("Next tab","下一个标签"); description=("Switch to the next window in the group.","切换到当前分组中的下一个窗口。"); keywords="next-tab"; binding=Shortcut("nextTab",3623) }
        { id="previous-tab"; page=HotKeySettings; caption=("Previous tab","上一个标签"); description=("Switch to the previous window in the group.","切换到当前分组中的上一个窗口。"); keywords="previous-tab"; binding=Shortcut("prevTab",3621) }
        { id="switch-tabs-by-number"; page=HotKeySettings; caption=("Switch tabs by number","按数字切换标签"); description=("Use Ctrl + number to select a tab. Restart WindowTabs after changing this option.","使用 Ctrl + 数字选择标签。修改此项后需重启 WindowTabs。"); keywords="switch-tabs-by-number"; binding=Toggle("enableCtrlNumberHotKey",true,true) }
        { id="activate-on-hover"; page=HotKeySettings; caption=("Activate on hover","悬停时激活"); description=("Switch windows when the pointer rests on a tab.","鼠标悬停在标签上时切换窗口。"); keywords="activate-on-hover"; binding=Toggle("enableHoverActivate",false,false) }
        { id="shift-scroll"; page=HotKeySettings; caption=("Shift + scroll to switch tabs","Shift + 滚轮切换标签"); description=("Hold Shift and scroll over a grouped window to switch tabs. You can also scroll over the tab strip without holding Shift.","在分组窗口上按住 Shift 并滚动滚轮即可切换标签。在标签条上滚动时无需按 Shift。"); keywords="shift-scroll"; binding=Toggle("enableShiftScroll",true,true) }
        { id="tabTextColor"; page=AppearanceSettings; caption=("Text and close button","文字与关闭按钮"); description=("",""); keywords="tabTextColor"; binding=Colour }
        { id="tabNormalBgColor"; page=AppearanceSettings; caption=("Inactive tab","非活动标签"); description=("",""); keywords="tabNormalBgColor"; binding=Colour }
        { id="tabActiveBgColor"; page=AppearanceSettings; caption=("Active tab","活动标签"); description=("",""); keywords="tabActiveBgColor"; binding=Colour }
        { id="tabHighlightBgColor"; page=AppearanceSettings; caption=("Hovered tab","悬停标签"); description=("",""); keywords="tabHighlightBgColor"; binding=Colour }
        { id="tabBorderColor"; page=AppearanceSettings; caption=("Separator","分隔线"); description=("",""); keywords="tabBorderColor"; binding=Colour }
        { id="tabFlashBgColor"; page=AppearanceSettings; caption=("Flashing tab","闪烁标签"); description=("",""); keywords="tabFlashBgColor"; binding=Colour }
        { id="tabHeight"; page=AppearanceSettings; caption=("Tab height","标签高度"); description=("",""); keywords="tabHeight"; binding=Number(12,120) }
        { id="tabMaxWidth"; page=AppearanceSettings; caption=("Maximum tab width","标签最大宽度"); description=("",""); keywords="tabMaxWidth"; binding=Number(60,1000) }
        { id="tabOverlap"; page=AppearanceSettings; caption=("Tab spacing","标签间距"); description=("Space between neighbouring tabs.","相邻标签之间的空隙。"); keywords="tabOverlap gap spacing 间距 间隔"; binding=Number(0,100) }
        { id="tabIndentNormal"; page=AppearanceSettings; caption=("Side margin","两侧边距"); description=("Space between the tabs and the window edges. With centered tabs, this takes effect when the tabs fill the row.","标签与窗口两侧边缘的距离。标签居中时，只有排满整行后才会生效。"); keywords="tabIndentNormal tabIndentFlipped inset margin indent maximized 边距 缩进 最大化"; binding=Number(0,1000) }
        { id="app-rules"; page=ProgramSettings; caption=("App rules","应用规则"); description=("Choose apps for tabs and automatic grouping.","选择启用标签和自动分组的应用。"); keywords="process application exe 程序 进程"; binding=Navigation }
        { id="workspaces"; page=LayoutSettings; caption=("Workspaces","工作区"); description=("Save and restore window layouts.","保存和恢复窗口布局。"); keywords="workspace layout 工作区 布局"; binding=Navigation }
        { id="diagnostics"; page=DiagnosticsSettings; caption=("About & diagnostics","关于与诊断"); description=("Version and troubleshooting information.","版本与故障排查信息。"); keywords="version diagnostic log 版本 日志 诊断"; binding=Navigation }
    ]
    let find id = all |> List.find(fun item -> item.id=id)
    let toggleKey id = match (find id).binding with Toggle(key,_,_) -> key | _ -> invalidArg "id" "Not a toggle"
    let toggleDefault key existing =
        all |> List.pick(fun item -> match item.binding with Toggle(k,fresh,old) when k=key -> Some(if existing then old else fresh) | _ -> None)
    /// Ctrl+Alt+Right / Ctrl+Alt+Left, in hotkey-control encoding.
    let shortcutDefault key =
        all |> List.pick(fun item -> match item.binding with Shortcut(k,code) when k=key -> Some code | _ -> None)
    let range id = match (find id).binding with Number(low,high) -> low,high | _ -> invalidArg "id" "Not a numeric setting"
    let normalizeNumber id value =
        let low,high = range id
        max low (min high value)
    let normalizeChoice key value =
        all |> List.pick(fun item ->
            match item.binding with
            | Choice(k,values,fallback) when k=key -> Some(if List.contains value values then value else fallback)
            | _ -> None)
    let title id = (find id).caption |> localize
    let matches (query:string) item =
        let en,zh = item.caption
        let detailEn,detailZh = item.description
        let pageWords =
            match item.page with
            | GeneralSettings -> "general 常规"
            | AppearanceSettings -> "appearance 外观"
            | HotKeySettings -> "shortcuts keyboard mouse 快捷键 键盘 鼠标"
            | ProgramSettings -> "app rules 应用规则"
            | LayoutSettings -> "workspaces 工作区"
            | _ -> "diagnostics 诊断"
        let haystack = String.concat " " [en;zh;detailEn;detailZh;item.keywords;pageWords]
        query.Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries)
        |> Array.forall(fun term -> haystack.IndexOf(term,StringComparison.CurrentCultureIgnoreCase)>=0)
