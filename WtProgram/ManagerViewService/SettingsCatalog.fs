namespace Bemo
open System

type SettingDefinition = {
    id:string
    page:SettingsViewType
    caption:string * string
    description:string * string
    keywords:string }

module SettingsCatalog =
    let localize (en,zh) =
        if Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName="zh" then zh else en
    let all = [
        { id="theme"; page=AppearanceSettings; caption=("Theme","主题"); description=("Choose system, light, or dark mode.","选择跟随系统、浅色或深色模式。"); keywords="theme system light dark 系统 浅色 深色" }
        { id="launch-at-sign-in"; page=GeneralSettings; caption=("Launch at sign-in","登录时启动"); description=("Keep WindowTabs available when Windows starts.","登录 Windows 后自动运行 WindowTabs。"); keywords="launch-at-sign-in startup autostart 开机 自启动" }
        { id="enable-tabs-for-new-apps"; page=GeneralSettings; caption=("Enable tabs for new apps","为新应用启用标签"); description=("Individual overrides are available in App rules.","可在“应用规则”中单独调整每个应用。"); keywords="enable-tabs-for-new-apps" }
        { id="dim-inactive-groups"; page=GeneralSettings; caption=("Dim inactive groups","淡化非活动分组"); description=("Reduce the opacity of tabs outside the active group.","降低非活动分组标签的透明度。"); keywords="dim-inactive-groups" }
        { id="auto-hide-maximized-tabs"; page=GeneralSettings; caption=("Auto-hide maximized tabs","最大化时自动收起标签"); description=("Reveal the tab strip when the pointer reaches it.","鼠标移到标签条时展开。"); keywords="auto-hide-maximized-tabs" }
        { id="tab-alignment"; page=GeneralSettings; caption=("Tab alignment","标签位置"); description=("Position the strip above the window.","设置标签条相对于窗口的位置。"); keywords="tab-alignment" }
        { id="combine-taskbar-icons"; page=GeneralSettings; caption=("Combine taskbar icons","合并任务栏图标"); description=("",""); keywords="combine-taskbar-icons" }
        { id="use-windowtabs-for-alt-tab"; page=GeneralSettings; caption=("Use WindowTabs for Alt+Tab","使用 WindowTabs 切换器"); description=("",""); keywords="use-windowtabs-for-alt-tab" }
        { id="group-windows-in-the-switcher"; page=GeneralSettings; caption=("Group windows in the switcher","在切换器中合并分组"); description=("",""); keywords="group-windows-in-the-switcher" }
        { id="next-tab"; page=HotKeySettings; caption=("Next tab","下一个标签"); description=("Switch to the next window in the group.","切换到当前分组中的下一个窗口。"); keywords="next-tab" }
        { id="previous-tab"; page=HotKeySettings; caption=("Previous tab","上一个标签"); description=("Switch to the previous window in the group.","切换到当前分组中的上一个窗口。"); keywords="previous-tab" }
        { id="switch-tabs-by-number"; page=HotKeySettings; caption=("Switch tabs by number","按数字切换标签"); description=("Use Ctrl + number to select a tab. Restart WindowTabs after changing this option.","使用 Ctrl + 数字选择标签。修改此项后需重启 WindowTabs。"); keywords="switch-tabs-by-number" }
        { id="activate-on-hover"; page=HotKeySettings; caption=("Activate on hover","悬停时激活"); description=("Switch windows when the pointer rests on a tab.","鼠标悬停在标签上时切换窗口。"); keywords="activate-on-hover" }
        { id="shift-scroll"; page=HotKeySettings; caption=("Shift + scroll","Shift + 滚轮"); description=("Hold Shift and scroll over a grouped window to switch tabs.","在分组窗口内按住 Shift 并滚动滚轮以切换标签。"); keywords="shift-scroll" }
        { id="tabTextColor"; page=AppearanceSettings; caption=("Text and close button","文字与关闭按钮"); description=("",""); keywords="tabTextColor" }
        { id="tabNormalBgColor"; page=AppearanceSettings; caption=("Inactive tab","非活动标签"); description=("",""); keywords="tabNormalBgColor" }
        { id="tabActiveBgColor"; page=AppearanceSettings; caption=("Active tab","活动标签"); description=("",""); keywords="tabActiveBgColor" }
        { id="tabHighlightBgColor"; page=AppearanceSettings; caption=("Hovered tab","悬停标签"); description=("",""); keywords="tabHighlightBgColor" }
        { id="tabBorderColor"; page=AppearanceSettings; caption=("Separator","分隔线"); description=("",""); keywords="tabBorderColor" }
        { id="tabFlashBgColor"; page=AppearanceSettings; caption=("Attention","提醒颜色"); description=("",""); keywords="tabFlashBgColor" }
        { id="tabHeight"; page=AppearanceSettings; caption=("Tab height","标签高度"); description=("",""); keywords="tabHeight" }
        { id="tabMaxWidth"; page=AppearanceSettings; caption=("Maximum width","标签最大宽度"); description=("",""); keywords="tabMaxWidth" }
        { id="tabOverlap"; page=AppearanceSettings; caption=("Tab overlap / gap","标签重叠／间距"); description=("",""); keywords="tabOverlap" }
        { id="tabIndentNormal"; page=AppearanceSettings; caption=("Window inset","普通窗口缩进"); description=("",""); keywords="tabIndentNormal" }
        { id="tabIndentFlipped"; page=AppearanceSettings; caption=("Maximized inset","最大化窗口缩进"); description=("",""); keywords="tabIndentFlipped" }
        { id="app-rules"; page=ProgramSettings; caption=("App rules","应用规则"); description=("Choose apps for tabs and automatic grouping.","选择启用标签和自动分组的应用。"); keywords="process application exe 程序 进程" }
        { id="workspaces"; page=LayoutSettings; caption=("Workspaces","工作区"); description=("Save and restore window layouts.","保存和恢复窗口布局。"); keywords="workspace layout 工作区 布局" }
        { id="diagnostics"; page=DiagnosticsSettings; caption=("About & diagnostics","关于与诊断"); description=("Version and troubleshooting information.","版本与故障排查信息。"); keywords="version diagnostic log 版本 日志 诊断" }
    ]
    let find id = all |> List.find(fun item -> item.id=id)
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
