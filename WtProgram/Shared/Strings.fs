namespace Bemo

/// Caption and description of one entry in SettingsCatalog.
type SettingText = { caption:LocalizedText; description:LocalizedText }

/// Every user-visible text, in every supported language. Show one with `tr`.
/// Text with values filled in is a function, so the format is written once per language.
module Strings =
    let private none = { en=""; zh=""; ja="" }

    module Common =
        let ok = { en="OK"; zh="确定"; ja="OK" }
        let cancel = { en="Cancel"; zh="取消"; ja="キャンセル" }
        let save = { en="Save"; zh="保存"; ja="保存" }
        let refresh = { en="Refresh"; zh="刷新"; ja="更新" }
        let name = { en="Name"; zh="名称"; ja="名称" }
        let title = { en="Title"; zh="标题"; ja="タイトル" }
        let left = { en="Left"; zh="左侧"; ja="左" }
        let center = { en="Center"; zh="居中"; ja="中央" }
        let right = { en="Right"; zh="右侧"; ja="右" }
        let operationFailed = { en="Operation failed"; zh="操作失败"; ja="操作に失敗しました" }

    module Messages =
        let alreadyRunning = { en="WindowTabs is already running."; zh="WindowTabs 已在运行。"; ja="WindowTabs は既に実行中です。" }
        let couldNotContinue = { en="WindowTabs could not continue"; zh="WindowTabs 无法继续运行"; ja="WindowTabs を続行できません" }
        let shortcutUnavailable = { en="Shortcut unavailable"; zh="快捷键不可用"; ja="ショートカットを使用できません" }
        let shortcutUnavailableFor name =
            { en=sprintf "The shortcut for %s is unavailable. Choose another shortcut in Settings." name
              zh=sprintf "“%s”的快捷键已被占用，请在设置中另选一个。" name
              ja=sprintf "「%s」のショートカットは使用できません。設定で別のショートカットを選択してください。" name }
        let settingsSaveFailed = { en="Settings save failed"; zh="设置保存失败"; ja="設定を保存できませんでした" }
        let unableToSaveSettings path details =
            { en=sprintf "Unable to save settings to %s.\nYour changes remain in memory and will be retried on the next edit or exit.\n\n%s" path details
              zh=sprintf "无法保存设置到 %s。\n更改仍保留在内存中，下次修改或退出时会重试。\n\n%s" path details
              ja=sprintf "設定を %s に保存できませんでした。\n変更はメモリに保持され、次の編集時または終了時に再試行されます。\n\n%s" path details }
        let settingsError = { en="Settings error"; zh="设置错误"; ja="設定エラー" }
        let errorLoadingSettings path details =
            { en=sprintf "Error loading settings.\n\nFix or remove the file %s.\n\nDetails: %s" path details
              zh=sprintf "读取设置出错。\n\n请修复或删除文件 %s。\n\n详细信息：%s" path details
              ja=sprintf "設定の読み込み中にエラーが発生しました。\n\nファイル %s を修正または削除してください。\n\n詳細: %s" path details }
        let invalidColor = { en="Invalid color. Enter six hexadecimal digits."; zh="颜色无效，请输入六位十六进制数。"; ja="色が無効です。16 進数 6 桁で入力してください。" }

    /// Tray icon menu.
    module Tray =
        let exit = { en="Exit WindowTabs"; zh="退出 WindowTabs"; ja="WindowTabs を終了" }

    /// Tab context menu.
    module TabMenu =
        let showTabTitles = { en="Show tab titles"; zh="显示标签标题"; ja="タブのタイトルを表示" }
        let showIconsOnly = { en="Show icons only"; zh="仅显示图标"; ja="アイコンのみ表示" }
        let newWindow = { en="New window"; zh="新建窗口"; ja="新しいウィンドウ" }
        let renameTab = { en="Rename tab"; zh="重命名标签"; ja="タブ名を変更" }
        let restoreTabName = { en="Restore tab name"; zh="恢复标签名称"; ja="タブ名を元に戻す" }
        let disableTabsFor exe = { en=sprintf "Disable tabs for %s" exe; zh=sprintf "禁用 %s 的标签" exe; ja=sprintf "%s のタブを無効にする" exe }
        let autoGroupWindowsOf exe = { en=sprintf "Auto-group %s windows" exe; zh=sprintf "自动分组 %s 的窗口" exe; ja=sprintf "%s のウィンドウを自動グループ化" exe }
        let close = { en="Close"; zh="关闭"; ja="閉じる" }
        let closeOthers = { en="Close others"; zh="关闭其他"; ja="他を閉じる" }
        let closeAllOf exe = { en=sprintf "Close all '%s' windows" exe; zh=sprintf "关闭所有“%s”窗口" exe; ja=sprintf "「%s」のウィンドウをすべて閉じる" exe }
        let closeAll = { en="Close all"; zh="全部关闭"; ja="すべて閉じる" }
        let settings = { en="Settings..."; zh="设置..."; ja="設定..." }

    /// Menu shown when a tab is dropped with the right mouse button.
    module DropMenu =
        let copy = { en="Copy"; zh="复制"; ja="コピー" }
        let move = { en="Move"; zh="移动"; ja="移動" }

    /// Settings window chrome and page titles.
    module SettingsWindow =
        let title = { en="WindowTabs Settings"; zh="WindowTabs 设置"; ja="WindowTabs の設定" }
        let search = { en="Search"; zh="搜索"; ja="検索" }
        let searchSettings = { en="Search settings"; zh="搜索设置"; ja="設定を検索" }
        let searchSuggestions = { en="Search suggestions"; zh="搜索建议"; ja="検索候補" }
        let searchResults = { en="Search results"; zh="搜索结果"; ja="検索結果" }
        let noMatches = { en="No matching settings."; zh="没有找到匹配的设置。"; ja="一致する設定はありません。" }
        let followWindows = { en="Follow Windows"; zh="跟随系统"; ja="Windows に従う" }
        let howToUse = { en="How to use"; zh="使用说明"; ja="使い方" }
        let pageScroll = { en="Page scroll"; zh="页面滚动条"; ja="ページのスクロールバー" }

    module Pages =
        let general = { en="General"; zh="常规"; ja="全般" }
        let appearance = { en="Appearance"; zh="外观"; ja="外観" }
        let shortcuts = { en="Shortcuts"; zh="快捷键"; ja="ショートカット" }
        let appRules = { en="App rules"; zh="应用规则"; ja="アプリのルール" }
        let workspaces = { en="Workspaces"; zh="工作区"; ja="ワークスペース" }
        let diagnostics = { en="About & diagnostics"; zh="关于与诊断"; ja="バージョン情報と診断" }
        let title page =
            match page with
            | GeneralSettings -> general
            | AppearanceSettings -> appearance
            | HotKeySettings -> shortcuts
            | ProgramSettings -> appRules
            | LayoutSettings -> workspaces
            | DiagnosticsSettings -> diagnostics

    /// Captions and descriptions of SettingsCatalog entries.
    module Settings =
        let theme = { caption={ en="Theme"; zh="主题"; ja="テーマ" }
                      description={ en="Choose system, light, or dark mode."; zh="选择跟随系统、浅色或深色模式。"; ja="システム、ライト、ダークから選択します。" } }
        let language = { caption={ en="Language"; zh="语言"; ja="言語" }
                         description={ en="Follow Windows or choose a language."; zh="跟随 Windows 或指定语言。"; ja="Windows の設定に従うか、言語を選択します。" } }
        let launchAtSignIn = { caption={ en="Start with Windows"; zh="随 Windows 启动"; ja="Windows と同時に起動" }
                               description={ en="Launch at startup."; zh="开机时自动运行。"; ja="起動時に自動で実行します。" } }
        let enableTabsByDefault = { caption={ en="Enable tabs by default"; zh="默认启用标签"; ja="既定でタブを有効にする" }
                                    description={ en="For apps without an app rule."; zh="适用于没有应用规则的应用。"; ja="アプリのルールがないアプリに適用します。" } }
        let dimInactiveGroups = { caption={ en="Dim tabs in inactive groups"; zh="淡化非活动分组的标签"; ja="非アクティブなグループのタブを半透明にする" }
                                  description={ en="Make tabs in inactive groups translucent."; zh="使非活动分组中的标签半透明。"; ja="アクティブでないグループのタブを半透明にします。" } }
        let autoHideMaximized = { caption={ en="Auto-hide tabs when maximized"; zh="最大化时自动隐藏标签"; ja="最大化時にタブを自動的に隠す" }
                                  description={ en="Point to the top edge to show them."; zh="鼠标移到顶部边缘时显示。"; ja="上端にポインターを移動すると表示します。" } }
        let minimalMode = { caption={ en="Minimal mode"; zh="极简模式"; ja="ミニマルモード" }
                            description={ en="Shrink tabs to a thin bar in all windows. Hover to expand."; zh="所有窗口的标签收成细栏，悬停时展开。"; ja="すべてのウィンドウでタブを細いバーにし、ポインターを重ねると展開します。" } }
        let tabAlignment = { caption={ en="Tab position"; zh="标签位置"; ja="タブの位置" }
                             description={ en="Choose where tabs appear above the window."; zh="选择标签在窗口顶部的位置。"; ja="ウィンドウ上部でのタブの位置を選択します。" } }
        let combineTaskbarIcons = { caption={ en="One taskbar icon per group"; zh="每个分组显示一个任务栏图标"; ja="グループごとにタスクバーアイコンを1つ表示" }
                                    description={ en="Applies to new groups. Change existing ones from the tab menu."; zh="适用于新分组。已有分组可在标签右键菜单中更改。"; ja="新しいグループに適用します。既存のグループはタブの右クリックメニューで変更できます。" } }
        let replaceAltTab = { caption={ en="Use WindowTabs for Alt+Tab"; zh="使用 WindowTabs 窗口切换器"; ja="Alt+Tab に WindowTabs を使う" }
                              description={ en="Replaces the Windows switcher."; zh="替换 Windows 自带的窗口切换器。"; ja="Windows 標準のウィンドウ切り替えを置き換えます。" } }
        let groupWindowsInSwitcher = { caption={ en="Group windows in Alt+Tab"; zh="Alt+Tab 中按分组显示"; ja="Alt+Tab でグループをまとめる" }
                                       description={ en="Show each window group as one item."; zh="每个窗口分组只显示一项。"; ja="ウィンドウのグループをそれぞれ 1 項目にまとめて表示します。" } }
        let nextTab = { caption={ en="Next tab"; zh="下一个标签"; ja="次のタブ" }
                        description={ en="Switch to the next window in the group."; zh="切换到当前分组中的下一个窗口。"; ja="グループ内の次のウィンドウに切り替えます。" } }
        let previousTab = { caption={ en="Previous tab"; zh="上一个标签"; ja="前のタブ" }
                            description={ en="Switch to the previous window in the group."; zh="切换到当前分组中的上一个窗口。"; ja="グループ内の前のウィンドウに切り替えます。" } }
        let switchTabsByNumber = { caption={ en="Switch tabs by number"; zh="按数字切换标签"; ja="番号でタブを切り替え" }
                                   description={ en="Use Ctrl + 1–9 to switch tabs. Restart WindowTabs to apply changes."; zh="使用 Ctrl + 1–9 切换标签。更改后需重启 WindowTabs。"; ja="Ctrl + 1～9 でタブを切り替えます。変更後は WindowTabs を再起動してください。" } }
        let activateOnHover = { caption={ en="Switch tabs on hover"; zh="悬停切换标签"; ja="ホバーでタブを切り替え" }
                                description={ en="Switch windows when the pointer rests on a tab."; zh="鼠标悬停在标签上时切换窗口。"; ja="タブにポインターを置くとウィンドウを切り替えます。" } }
        let shiftScroll = { caption={ en="Shift + scroll to switch tabs"; zh="Shift + 滚轮切换标签"; ja="Shift + スクロールでタブを切り替え" }
                            description={ en="Hold Shift and scroll over a grouped window to switch tabs. You can also scroll over the tab strip without holding Shift."
                                          zh="在分组窗口上按住 Shift 并滚动滚轮即可切换标签。在标签条上滚动时无需按 Shift。"
                                          ja="グループ化されたウィンドウ上で Shift を押しながらスクロールするとタブを切り替えます。タブバー上では Shift を押さずにスクロールできます。" } }
        let tabTextColor = { caption={ en="Text and close button"; zh="文字与关闭按钮"; ja="文字と閉じるボタン" }; description=none }
        let tabNormalBgColor = { caption={ en="Inactive tab"; zh="非活动标签"; ja="非アクティブなタブ" }; description=none }
        let tabActiveBgColor = { caption={ en="Active tab"; zh="活动标签"; ja="アクティブなタブ" }; description=none }
        let tabHighlightBgColor = { caption={ en="Hovered tab"; zh="悬停标签"; ja="ホバー中のタブ" }; description=none }
        let tabBorderColor = { caption={ en="Separator"; zh="分隔线"; ja="区切り線" }; description=none }
        let tabFlashBgColor = { caption={ en="Flashing tab"; zh="闪烁标签"; ja="点滅するタブ" }; description=none }
        let tabHeight = { caption={ en="Tab height"; zh="标签高度"; ja="タブの高さ" }; description=none }
        let tabMaxWidth = { caption={ en="Maximum tab width"; zh="标签最大宽度"; ja="タブの最大幅" }; description=none }
        let tabOverlap = { caption={ en="Tab spacing"; zh="标签间距"; ja="タブの間隔" }
                           description={ en="Space between adjacent tabs."; zh="相邻标签之间的空隙。"; ja="隣り合うタブの間隔です。" } }
        let tabIndent = { caption={ en="Side margin"; zh="两侧边距"; ja="左右の余白" }
                          description={ en="Space between the tabs and the window edges. With centered tabs, this takes effect when the tabs fill the row."
                                        zh="标签与窗口两侧边缘的距离。标签居中时，只有排满整行后才会生效。"
                                        ja="タブとウィンドウの左右の端との間隔です。中央揃えの場合は、タブが行いっぱいになったときに反映されます。" } }
        let appRules = { caption=Pages.appRules
                         description={ en="Choose apps for tabs and automatic grouping."; zh="选择启用标签和自动分组的应用。"; ja="タブと自動グループ化を使うアプリを選択します。" } }
        let workspaces = { caption=Pages.workspaces
                           description={ en="Save and restore window layouts."; zh="保存和恢复窗口布局。"; ja="ウィンドウの配置を保存して復元します。" } }
        let diagnostics = { caption=Pages.diagnostics
                            description={ en="Version and troubleshooting information."; zh="版本与故障排查信息。"; ja="バージョンとトラブルシューティングの情報。" } }

    module General =
        let startupAndDefaults = { en="Startup and defaults"; zh="启动与默认设置"; ja="起動と既定の設定" }
        let tabBehavior = { en="Tab behavior"; zh="标签行为"; ja="タブの動作" }
        let taskbar = { en="Taskbar"; zh="任务栏"; ja="タスクバー" }
        let windowSwitcher = { en="Window switcher"; zh="窗口切换器"; ja="ウィンドウ切り替え" }

    module Appearance =
        let system = { en="System"; zh="跟随系统"; ja="システム" }
        let light = { en="Light"; zh="浅色"; ja="ライト" }
        let dark = { en="Dark"; zh="深色"; ja="ダーク" }
        let explorerPreview = { en="File Explorer theme preview"; zh="文件资源管理器主题预览"; ja="エクスプローラーのテーマのプレビュー" }
        let darkThemeColors = { en="Dark theme · Tab colors"; zh="深色主题 · 标签配色"; ja="ダークテーマ · タブの色" }
        let lightThemeColors = { en="Light theme · Tab colors"; zh="浅色主题 · 标签配色"; ja="ライトテーマ · タブの色" }
        let colorPreset = { en="Color preset"; zh="配色预设"; ja="配色プリセット" }
        let custom = { en="Custom"; zh="自定义"; ja="カスタム" }
        let resetColors = { en="Reset colors"; zh="重置配色"; ja="配色をリセット" }
        let tabLayout = { en="Tab layout"; zh="标签布局"; ja="タブのレイアウト" }
        let sizesStayTheSame = { en="Sizes stay the same when you switch themes."; zh="切换主题不会改变这些尺寸。"; ja="テーマを切り替えてもサイズは変わりません。" }
        let resetTabLayout = { en="Reset tab layout"; zh="重置标签布局"; ja="タブのレイアウトをリセット" }
        /// In ThemePresets.palettes order.
        let presets =
            [| { en="Default"; zh="默认"; ja="既定" }
               { en="Ocean"; zh="海洋蓝"; ja="オーシャン" }
               { en="Forest"; zh="森林绿"; ja="フォレスト" }
               { en="Slate"; zh="石板灰"; ja="スレート" }
               { en="Teal"; zh="青碧"; ja="ティール" }
               { en="Sand"; zh="沙岩米色"; ja="サンド" }
               { en="Amber"; zh="琥珀橙"; ja="アンバー" }
               { en="Rose"; zh="玫瑰红"; ja="ローズ" }
               { en="Plum"; zh="梅紫"; ja="プラム" } |]
        let pickerHelp = { en="Color picker: arrow keys adjust saturation and brightness; Shift + arrow keys adjust hue"
                           zh="颜色选择器：方向键调整饱和度和亮度；Shift + 方向键调整色相"
                           ja="色の選択：方向キーで彩度と明るさを調整し、Shift + 方向キーで色相を調整します" }
        let chooseColor = { en="Choose color"; zh="选择颜色"; ja="色を選択" }
        let decrease = { en="Decrease"; zh="减小"; ja="減らす" }
        let increase = { en="Increase"; zh="增大"; ja="増やす" }

    module Shortcuts =
        let keyboard = { en="Keyboard"; zh="键盘"; ja="キーボード" }
        let mouse = { en="Mouse"; zh="鼠标"; ja="マウス" }
        let keyboardNote = { en="Click a shortcut, then press the new combination. Esc cancels; × or Backspace removes it."
                             zh="点击快捷键后按下新的组合键。按 Esc 取消；点 × 或按 Backspace 移除。"
                             ja="ショートカットをクリックして、新しいキーの組み合わせを押します。Esc でキャンセル、× または Backspace で削除します。" }
        let restoreDefaults = { en="Restore default shortcuts"; zh="恢复默认快捷键"; ja="既定のショートカットに戻す" }
        let inUse = { en="Shortcut already in use"; zh="快捷键已被占用"; ja="ショートカットは使用中です" }
        let usedBy name = { en=sprintf "Used by %s" name; zh=sprintf "已用于“%s”" name; ja=sprintf "「%s」で使用中" name }
        // Shortcut field messages are kept short to fit.
        let alreadySet = { en="Already set"; zh="已是当前快捷键"; ja="設定済み" }
        let includeCtrlOrAlt = { en="Include Ctrl or Alt"; zh="需要包含 Ctrl 或 Alt"; ja="Ctrl か Alt が必要です" }
        let pressShortcut = { en="Press a shortcut…"; zh="按下组合键…"; ja="キーを押してください…" }
        let notSet = { en="Not set"; zh="未设置"; ja="未設定" }

    module AppRules =
        let description = { en="Choose which apps use tabs and automatic grouping. Expand an app to see its windows."
                            zh="为应用设置标签和自动分组。展开应用可查看其窗口。"
                            ja="アプリごとにタブと自動グループ化を設定します。展開するとウィンドウが表示されます。" }
        let tabs = { en="Tabs"; zh="标签"; ja="タブ" }
        let autoGroup = { en="Auto-group"; zh="自动分组"; ja="自動グループ化" }
        let appCount count =
            { en=(if count=1 then "1 app" else sprintf "%d apps" count); zh=sprintf "%d 个应用" count; ja=sprintf "%d 個のアプリ" count }
        let scanning = { en="Scanning…"; zh="正在扫描…"; ja="スキャン中…" }
        let scanFailed details = { en="Scan failed: " + details; zh="扫描失败：" + details; ja="スキャンに失敗しました: " + details }

    module Workspaces =
        let description = { en="Save and restore window groups and positions."; zh="保存和恢复窗口分组与位置。"; ja="ウィンドウのグループと位置を保存・復元します。" }
        let help = { en="How to use\n1  Group and position your windows, then click Save.\n2  Select a workspace and click Restore.\n3  Click Edit to change names or window title matching.\nRestore only uses open windows. It does not launch apps."
                     zh="使用说明\n1  将窗口分组并调整位置，然后点击“保存”。\n2  选中工作区，点击“恢复”。\n3  点击“编辑”可修改名称或窗口标题的匹配方式。\n恢复仅适用于已打开的窗口，不会启动应用。"
                     ja="使い方\n1  ウィンドウをグループ化して配置し、「保存」をクリックします。\n2  ワークスペースを選び、「復元」をクリックします。\n3  「編集」で名前やタイトルの一致方法を変更できます。\n復元は開いているウィンドウのみが対象です。アプリは起動しません。" }
        let restore = { en="Restore"; zh="恢复"; ja="復元" }
        let delete = { en="Delete"; zh="删除"; ja="削除" }
        let edit = { en="Edit"; zh="编辑"; ja="編集" }
        let matchMethod = { en="Match method"; zh="匹配方式"; ja="一致方法" }
        let exactMatch = { en="Exact match"; zh="完全匹配"; ja="完全一致" }
        let startsWith = { en="Starts with"; zh="开头匹配"; ja="前方一致" }
        let endsWith = { en="Ends with"; zh="结尾匹配"; ja="後方一致" }
        let contains = { en="Contains"; zh="包含"; ja="部分一致" }
        let regularExpression = { en="Regular expression"; zh="正则表达式"; ja="正規表現" }
        let restoreTitle = { en="Workspace restore"; zh="恢复工作区"; ja="ワークスペースの復元" }
        let restoreSummary restored missing errors details =
            { en=sprintf "Restored: %d\nNot found: %d\nErrors: %d%s" restored missing errors details
              zh=sprintf "已恢复：%d\n未找到：%d\n错误：%d%s" restored missing errors details
              ja=sprintf "復元: %d\n見つからない: %d\nエラー: %d%s" restored missing errors details }
        let invalidSetting = { en="Invalid workspace setting"; zh="工作区设置无效"; ja="ワークスペースの設定が無効です" }
        let dataWarnings = { en="Workspace data"; zh="工作区数据"; ja="ワークスペースのデータ" }
        let titleInvalid = { en="Window title is missing or exceeds 4,096 characters."; zh="窗口标题缺失或超过 4,096 个字符。"; ja="ウィンドウのタイトルがないか、4,096 文字を超えています。" }
        let unknownMatchMethod = { en="Unknown title match method."; zh="未知的标题匹配方式。"; ja="タイトルの一致方法が不明です。" }
        let expectedObject = { en="Expected an object."; zh="应为对象。"; ja="オブジェクトが必要です。" }
        let expectedList = { en=": expected a list."; zh="：应为列表。"; ja="：リストが必要です。" }
        let missingPlacement = { en="Missing placement."; zh="缺少窗口位置。"; ja="ウィンドウの配置がありません。" }
        let noValidWindows = { en="No valid windows in this group."; zh="此分组中没有有效窗口。"; ja="このグループに有効なウィンドウがありません。" }
        let unsupportedVersion = { en="Unsupported workspace version; saved workspaces will not be modified."
                                   zh="不支持此工作区版本；已保存的工作区不会被修改。"
                                   ja="このワークスペースのバージョンには対応していません。保存済みのワークスペースは変更されません。" }

    module Diagnostics =
        let description = { en="Attach this report to bug reports. It omits window titles, paths and license data."
                            zh="提交问题时可附上此报告。报告不包含窗口标题、路径和授权信息。"
                            ja="不具合を報告するときはこのレポートを添付してください。ウィンドウのタイトル、パス、ライセンス情報は含まれません。" }
        let saved = { en="Saved."; zh="已保存。"; ja="保存しました。" }
        let copyReport = { en="Copy report"; zh="复制报告"; ja="レポートをコピー" }
        let reportCopied = { en="Report copied."; zh="报告已复制。"; ja="レポートをコピーしました。" }
        let saveReport = { en="Save report"; zh="保存报告"; ja="レポートを保存" }
        let exportSettings = { en="Export settings"; zh="导出设置"; ja="設定をエクスポート" }
