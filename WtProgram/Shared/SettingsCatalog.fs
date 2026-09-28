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
    text:SettingText
    keywords:string
    binding:SettingBinding }

module SettingsCatalog =
    let all = [
        { id="theme"; page=AppearanceSettings; text=Strings.Settings.theme; keywords="theme system light dark 系统 浅色 深色"; binding=Choice("tabThemeMode",["system";"light";"dark"],"system") }
        { id="language"; page=GeneralSettings; text=Strings.Settings.language; keywords="language locale english chinese japanese 语言 中文 英文 日语 日本語 言語"; binding=Choice("language",Localization.preferences,"system") }
        { id="launch-at-sign-in"; page=GeneralSettings; text=Strings.Settings.launchAtSignIn; keywords="launch-at-sign-in startup autostart 开机 自启动"; binding=Toggle("runAtStartup",true,false) }
        { id="enable-tabs-for-new-apps"; page=GeneralSettings; text=Strings.Settings.enableTabsByDefault; keywords="enable-tabs-for-new-apps"; binding=Toggle("enableTabbingByDefault",true,false) }
        { id="dim-inactive-groups"; page=GeneralSettings; text=Strings.Settings.dimInactiveGroups; keywords="dim-inactive-groups"; binding=Toggle("hideInactiveTabs",true,false) }
        { id="auto-hide-maximized-tabs"; page=GeneralSettings; text=Strings.Settings.autoHideMaximized; keywords="auto-hide-maximized-tabs"; binding=Toggle("autoHide",true,true) }
        { id="minimal-mode"; page=GeneralSettings; text=Strings.Settings.minimalMode; keywords="minimal-mode minimalMode compact auto hide bar hover 极简 自动隐藏 细栏 悬停"; binding=Toggle("minimalMode",false,false) }
        { id="tab-alignment"; page=GeneralSettings; text=Strings.Settings.tabAlignment; keywords="tab-alignment"; binding=Choice("alignment",["Left";"Center";"Right"],"Center") }
        { id="combine-taskbar-icons"; page=GeneralSettings; text=Strings.Settings.combineTaskbarIcons; keywords="combine-taskbar-icons"; binding=Toggle("combineIconsInTaskbar",false,true) }
        { id="use-windowtabs-for-alt-tab"; page=GeneralSettings; text=Strings.Settings.replaceAltTab; keywords="use-windowtabs-for-alt-tab"; binding=Toggle("replaceAltTab",false,false) }
        { id="group-windows-in-the-switcher"; page=GeneralSettings; text=Strings.Settings.groupWindowsInSwitcher; keywords="group-windows-in-the-switcher"; binding=Toggle("groupWindowsInSwitcher",false,false) }
        { id="next-tab"; page=HotKeySettings; text=Strings.Settings.nextTab; keywords="next-tab"; binding=Shortcut("nextTab",3623) }
        { id="previous-tab"; page=HotKeySettings; text=Strings.Settings.previousTab; keywords="previous-tab"; binding=Shortcut("prevTab",3621) }
        { id="switch-tabs-by-number"; page=HotKeySettings; text=Strings.Settings.switchTabsByNumber; keywords="switch-tabs-by-number"; binding=Toggle("enableCtrlNumberHotKey",true,true) }
        { id="activate-on-hover"; page=HotKeySettings; text=Strings.Settings.activateOnHover; keywords="activate-on-hover"; binding=Toggle("enableHoverActivate",false,false) }
        { id="shift-scroll"; page=HotKeySettings; text=Strings.Settings.shiftScroll; keywords="shift-scroll"; binding=Toggle("enableShiftScroll",true,true) }
        { id="tabTextColor"; page=AppearanceSettings; text=Strings.Settings.tabTextColor; keywords="tabTextColor"; binding=Colour }
        { id="tabNormalBgColor"; page=AppearanceSettings; text=Strings.Settings.tabNormalBgColor; keywords="tabNormalBgColor"; binding=Colour }
        { id="tabActiveBgColor"; page=AppearanceSettings; text=Strings.Settings.tabActiveBgColor; keywords="tabActiveBgColor"; binding=Colour }
        { id="tabHighlightBgColor"; page=AppearanceSettings; text=Strings.Settings.tabHighlightBgColor; keywords="tabHighlightBgColor"; binding=Colour }
        { id="tabBorderColor"; page=AppearanceSettings; text=Strings.Settings.tabBorderColor; keywords="tabBorderColor"; binding=Colour }
        { id="tabFlashBgColor"; page=AppearanceSettings; text=Strings.Settings.tabFlashBgColor; keywords="tabFlashBgColor"; binding=Colour }
        { id="tabHeight"; page=AppearanceSettings; text=Strings.Settings.tabHeight; keywords="tabHeight"; binding=Number(12,120) }
        { id="tabMaxWidth"; page=AppearanceSettings; text=Strings.Settings.tabMaxWidth; keywords="tabMaxWidth"; binding=Number(60,1000) }
        { id="tabOverlap"; page=AppearanceSettings; text=Strings.Settings.tabOverlap; keywords="tabOverlap gap spacing 间距 间隔"; binding=Number(0,100) }
        { id="tabIndentNormal"; page=AppearanceSettings; text=Strings.Settings.tabIndent; keywords="tabIndentNormal tabIndentFlipped inset margin indent maximized 边距 缩进 最大化"; binding=Number(0,1000) }
        { id="app-rules"; page=ProgramSettings; text=Strings.Settings.appRules; keywords="process application exe 程序 进程"; binding=Navigation }
        { id="workspaces"; page=LayoutSettings; text=Strings.Settings.workspaces; keywords="workspace layout 工作区 布局"; binding=Navigation }
        { id="diagnostics"; page=DiagnosticsSettings; text=Strings.Settings.diagnostics; keywords="version diagnostic log 版本 日志 诊断"; binding=Navigation }
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
    let title id = tr (find id).text.caption
    /// Matches every term against the text in all languages, so any language finds a setting.
    let matches (query:string) item =
        let pageWords =
            match item.page with
            | HotKeySettings -> [Strings.Pages.shortcuts;Strings.Shortcuts.keyboard;Strings.Shortcuts.mouse]
            | page -> [Strings.Pages.title page]
        let texts = item.text.caption :: item.text.description :: pageWords
        let haystack = String.concat " " (item.keywords :: List.collect Localization.all texts)
        query.Split([|' ';'\t'|],StringSplitOptions.RemoveEmptyEntries)
        |> Array.forall(fun term -> haystack.IndexOf(term,StringComparison.CurrentCultureIgnoreCase)>=0)
