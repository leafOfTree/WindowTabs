namespace Bemo

/// Caption, description and extra search words (synonyms not already in the caption or
/// description) of one entry in SettingsCatalog.
type SettingText = { caption:LocalizedText; description:LocalizedText; keywords:LocalizedText }

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
        let never = { en="Never"; zh="从不"; ja="しない" }
        let whenMaximized = { en="When maximized"; zh="最大化时"; ja="最大化時" }
        let always = { en="Always"; zh="始终"; ja="常に" }
        let selectedChoice (name:LocalizedText) = { en=name.en+"  ✓"; zh=name.zh+"  ✓"; ja=name.ja+"  ✓" }
        let operationFailed = { en="Operation failed"; zh="操作失败"; ja="操作に失敗しました" }

    module Messages =
        let alreadyRunning = { en="WindowTabs is already running."; zh="WindowTabs 已在运行。"; ja="WindowTabs は既に実行中です。" }
        let couldNotContinue = { en="WindowTabs could not continue"; zh="WindowTabs 无法继续运行"; ja="WindowTabs を続行できません" }
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

    /// Tray icon menu.
    module Tray =
        let exit = { en="Exit WindowTabs"; zh="退出 WindowTabs"; ja="WindowTabs を終了" }
        let errorTitle = { en="WindowTabs ran into a problem"; zh="WindowTabs 遇到了问题"; ja="WindowTabs で問題が発生しました" }
        let errorText = { en="It kept running. The details are in the crash log; click to show it."
                          zh="程序仍在运行。详细信息已写入崩溃日志，点击查看。"
                          ja="動作は続いています。詳細はクラッシュログにあります。クリックすると表示します。" }

    /// Tab context menu.
    module TabMenu =
        let showTabTitles = { en="Show tab titles"; zh="显示标签标题"; ja="タブのタイトルを表示" }
        let showIconsOnly = { en="Show icons only"; zh="仅显示图标"; ja="アイコンのみ表示" }
        let newTab = { en="Open new tab"; zh="新建标签"; ja="新しいタブを開く" }
        let renameTab = { en="Rename tab"; zh="重命名标签"; ja="タブ名を変更" }
        let restoreTabName = { en="Restore tab name"; zh="恢复标签名称"; ja="タブ名を元に戻す" }
        let sortTabs = { en="Sort tabs by app then title"; zh="按应用和标题排序标签"; ja="アプリとタイトルの順にタブを並べ替え" }
        let enableTabsFor exe = { en=sprintf "Enable tabs for %s" exe; zh=sprintf "为 %s 启用标签" exe; ja=sprintf "%s のタブを有効にする" exe }
        let autoGroupWindowsOf exe = { en=sprintf "Enable auto-grouping for %s" exe; zh=sprintf "为 %s 启用自动分组" exe; ja=sprintf "%s の自動グループ化を有効にする" exe }
        let close = { en="Close"; zh="关闭"; ja="閉じる" }
        let closeOthers = { en="Close others"; zh="关闭其他"; ja="他を閉じる" }
        let closeAllOf exe = { en=sprintf "Close all '%s' windows" exe; zh=sprintf "关闭所有“%s”窗口" exe; ja=sprintf "「%s」のウィンドウをすべて閉じる" exe }
        let closeAll = { en="Close all"; zh="全部关闭"; ja="すべて閉じる" }
        let settings = { en="Settings..."; zh="设置..."; ja="設定..." }

    /// Menu shown when a tab is dropped with the right mouse button.
    module DropMenu =
        let copy = { en="Copy"; zh="复制"; ja="コピー" }
        let move = { en="Move"; zh="移动"; ja="移動" }
        /// Under the strip while files are dragged over an Explorer tab; %1 is the folder's name.
        let moveTo = { en="Move to %1"; zh="移动到 %1"; ja="%1 へ移動" }
        let copyTo = { en="Copy to %1"; zh="复制到 %1"; ja="%1 へコピー" }

    /// Settings window chrome and page titles.
    module SettingsWindow =
        let title = { en="WindowTabs Settings"; zh="WindowTabs 设置"; ja="WindowTabs の設定" }
        let search = { en="Search"; zh="搜索"; ja="検索" }
        let searchSettings = { en="Search settings"; zh="搜索设置"; ja="設定を検索" }
        let searchSuggestions = { en="Search suggestions"; zh="搜索建议"; ja="検索候補" }
        /// Where a search result lives when it is on no page: the language picker.
        let sidebar = { en="Sidebar"; zh="侧边栏"; ja="サイドバー" }
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
        let diagnostics = { en="Support"; zh="支持"; ja="サポート" }
        let title page =
            match page with
            | GeneralSettings -> general
            | AppearanceSettings -> appearance
            | HotKeySettings -> shortcuts
            | ProgramSettings -> appRules
            | LayoutSettings -> workspaces
            | DiagnosticsSettings -> diagnostics

    /// Captions, descriptions and search keywords of SettingsCatalog entries.
    module Settings =
        let private colorWords = { en="color colour"; zh="颜色 配色"; ja="色 カラー" }
        let private sizeWords = { en="size"; zh="尺寸 大小"; ja="サイズ 大きさ" }
        let private hotkeyWords = { en="hotkey"; zh="热键"; ja="ホットキー" }
        let theme = { caption={ en="Theme"; zh="主题"; ja="テーマ" }
                      description={ en="System follows your Windows app mode."; zh="“跟随系统”与 Windows 的应用模式保持一致。"; ja="「システム」は Windows のアプリ モードに合わせます。" }
                      keywords={ en="dark mode"; zh="暗色 深色模式"; ja="ダークモード" } }
        let language = { caption={ en="Language"; zh="语言"; ja="言語" }
                         description={ en="Follow Windows uses your Windows display language."; zh="“跟随系统”使用 Windows 的显示语言。"; ja="「Windows に従う」では Windows の表示言語を使います。" }
                         keywords={ en="locale"; zh="界面语言"; ja="表示言語" } }
        let launchAtSignIn = { caption={ en="Start with Windows"; zh="随 Windows 启动"; ja="Windows と同時に起動" }
                               description={ en="Runs in the system tray after you sign in."; zh="登录后在系统托盘中运行。"; ja="サインイン後にシステムトレイで実行します。" }
                               keywords={ en="startup autostart"; zh="开机启动 自启动"; ja="スタートアップ 自動起動" } }
        let enableTabsByDefault = { caption={ en="Use tabs for new apps"; zh="新应用自动启用标签"; ja="新しいアプリでタブを使う" }
                                    description={ en="Apps you haven't set in the list."; zh="适用于列表中还没设置过的应用。"; ja="一覧でまだ設定していないアプリに適用されます。" }
                                    keywords={ en=""; zh=""; ja="" } }
        let dimInactiveGroups = { caption={ en="Dim tabs in inactive groups"; zh="淡化非活动分组的标签"; ja="非アクティブなグループのタブの色を薄くする" }
                                  description={ en="Make the active group easier to spot."; zh="让当前活动的分组更容易辨认。"; ja="アクティブなグループを見分けやすくします。" }
                                  keywords={ en="fade"; zh="变暗"; ja="薄く 暗く" } }
        let autoHide = { caption={ en="Auto-hide tabs"; zh="自动隐藏标签"; ja="タブを自動的に隠す" }
                         description={ en="Show a thin strip until hover."; zh="只显示细栏，悬停展开。"; ja="細いバーだけ表示し、ホバーで展開します。" }
                         keywords={ en="collapse"; zh="收起"; ja="折りたたみ" } }
        let showTabsOnSwitch = { caption={ en="Show tabs briefly after switching"; zh="切换后短暂显示标签"; ja="切り替え後にタブを一時的に表示" }
                                 description={ en="For about a second, so you can see where you are."; zh="显示约一秒，方便确认当前位置。"; ja="約 1 秒間表示して、今いる位置を確認できます。" }
                                 keywords={ en="peek"; zh="短暂显示"; ja="一時表示" } }
        let tabAlignment = { caption={ en="Align tabs"; zh="对齐标签"; ja="タブを揃える" }
                             description=none
                             keywords={ en="alignment position"; zh="对齐 位置"; ja="配置 位置" } }
        let combineTaskbarIcons = { caption={ en="Combine taskbar icons per group"; zh="合并每个分组的任务栏图标"; ja="グループのタスクバーアイコンをまとめる" }
                                    description=none
                                    keywords={ en="merge"; zh="合并"; ja="結合" } }
        let replaceAltTab = { caption={ en="Use WindowTabs for Alt+Tab"; zh="使用 WindowTabs 窗口切换器"; ja="Alt+Tab に WindowTabs を使う" }
                              description={ en="Replaces the Windows switcher."; zh="替换 Windows 自带的窗口切换器。"; ja="Windows 標準のウィンドウ切り替えを置き換えます。" }
                              keywords={ en="task switcher"; zh="任务切换"; ja="タスク切り替え" } }
        let switcherStyle = { caption={ en="Switcher style"; zh="切换器样式"; ja="スイッチャーの表示" }
                              description={ en="Large icons in a row, or a list with full window titles."; zh="一排大图标，或显示完整窗口标题的列表。"; ja="大きなアイコンを横に並べるか、ウィンドウ タイトルを一覧で表示します。" }
                              keywords={ en="layout"; zh="布局"; ja="レイアウト" } }
        let groupWindowsInSwitcher = { caption={ en="Group windows in Alt+Tab"; zh="在 Alt+Tab 中按分组显示"; ja="Alt+Tab でグループをまとめる" }
                                       description={ en="Each group appears as one item."; zh="每个分组只显示一项。"; ja="各グループを 1 項目として表示します。" }
                                       keywords={ en="switcher"; zh="切换器"; ja="切り替え" } }
        let nextTab = { caption={ en="Switch to next tab"; zh="切换到下一个标签"; ja="次のタブに切り替え" }
                        description={ en="Within the current group."; zh="在当前分组内切换。"; ja="現在のグループ内で切り替えます。" }
                        keywords=hotkeyWords }
        let previousTab = { caption={ en="Switch to previous tab"; zh="切换到上一个标签"; ja="前のタブに切り替え" }
                            description={ en="Within the current group."; zh="在当前分组内切换。"; ja="現在のグループ内で切り替えます。" }
                            keywords=hotkeyWords }
        let searchTabs = { caption={ en="Search tabs"; zh="搜索标签"; ja="タブを検索" }
                           description={ en="By title or app name, current group first."; zh="按标题或应用名查找，优先当前分组。"; ja="タイトルやアプリ名で探します。現在のグループを優先。" }
                           keywords={ en="find"; zh="查找"; ja="検索" } }
        let newTab = { caption={ en="Open new tab"; zh="新建标签"; ja="新しいタブを開く" }
                       description={ en="Starts the current tab's app again."
                                     zh="再次启动当前标签的应用。"
                                     ja="現在のタブのアプリをもう一度起動します。" }
                       keywords={ en="new window"; zh="新建窗口"; ja="新しいウィンドウ" } }
        let switchTabsByNumber = { caption={ en="Switch tabs by number"; zh="按数字切换标签"; ja="番号でタブを切り替え" }
                                   description={ en="Hold a modifier key and press 1–9."; zh="按住修饰键再按 1–9。"; ja="修飾キーを押しながら 1～9 を押します。" }
                                   keywords={ en="digit"; zh="数字"; ja="数字" } }
        let numberShortcut = { caption={ en="Modifier key for 1–9"; zh="数字键搭配的修饰键"; ja="1～9 と組み合わせるキー" }
                               description=none
                               keywords={ en="number"; zh="数字"; ja="番号" } }
        let enableNumberLeader = { caption={ en="Switch tabs with a leader key"; zh="用引导键切换标签"; ja="リーダーキーでタブを切り替え" }; description={ en="Press the leader, then a selection key. Escape cancels."; zh="先按引导键，再按选择键。Esc 取消。"; ja="リーダーキーの後、選択キーを押します。Esc で解除。" }; keywords=none }
        let leaderKeys = { caption={en="Selection keys";zh="选择键";ja="選択キー"}; description={en="Use unique letters or digits, in tab order.";zh="按标签顺序，使用不重复的字母或数字。";ja="タブ順に、重複しない英字または数字を指定します。"}; keywords={en="leader keys letters home row";zh="引导键 字母 主键区";ja="リーダーキー 英字 ホームポジション"} }
        let leaderKeyPresets = [| {en="123456789";zh="123456789";ja="123456789"}; {en="QWERTYUIOP";zh="QWERTYUIOP";ja="QWERTYUIOP"}; {en="ASDFGHJKL;";zh="ASDFGHJKL;";ja="ASDFGHJKL;"} |]
        let invalidLeaderKeys = {en="Use at least one unique letter, digit or unshifted punctuation key.";zh="请使用至少一个不重复的字母、数字或无需 Shift 的标点键。";ja="重複しない英字、数字、Shift 不要の記号を 1 つ以上指定してください。"}
        let numberLeader = { caption={ en="Tab selection leader"; zh="标签选择引导键"; ja="タブ選択のリーダーキー" }; description=none; keywords=none }
        let tabColors = { en="Tab color"; zh="标签着色"; ja="タブの色分け" }
        let customTabColor = { en="Custom…"; zh="自定义…"; ja="カスタム…" }
        let clearTabColor = { en="Clear color"; zh="清除颜色"; ja="色を解除" }
        let rememberTabColor name = { en=sprintf "Remember color for %s" name; zh=sprintf "记住 %s 的颜色" name; ja=sprintf "%s の色を記憶" name }
        let tabColorNames = [|
            { en="Grey";zh="灰色";ja="グレー" }; { en="Blue";zh="蓝色";ja="青" }
            { en="Red";zh="红色";ja="赤" }; { en="Yellow";zh="黄色";ja="黄" }
            { en="Green";zh="绿色";ja="緑" }; { en="Pink";zh="粉色";ja="ピンク" }
            { en="Purple";zh="紫色";ja="紫" }; { en="Cyan";zh="青色";ja="シアン" }
            { en="Orange";zh="橙色";ja="オレンジ" }; { en="Lavender";zh="薰衣草色";ja="ラベンダー" }
            { en="Olive";zh="橄榄色";ja="オリーブ" }; { en="Rose";zh="玫红色";ja="ローズ" }
            { en="Lime";zh="青柠色";ja="ライム" }; { en="Navy";zh="藏青色";ja="ネイビー" }
            { en="Teal";zh="蓝绿色";ja="ティール" }; { en="Coral";zh="珊瑚色";ja="コーラル" } |]
        let tabColorMode = { caption={ en="Tab color coding"; zh="标签着色"; ja="タブの色分け" }
                             description={ en="Auto-assign colors by window or app."
                                           zh="按窗口或应用自动分配颜色。"
                                           ja="ウィンドウ・アプリ別に色を自動で割り当てます。" }
                             keywords={ en="colorize rainbow"; zh="着色 彩虹"; ja="色分け" } }
        let colorsOff = { en="Off"; zh="关闭"; ja="オフ" }
        let byWindow = { en="By window"; zh="按窗口"; ja="ウィンドウ別" }
        let byApp = { en="By app"; zh="按应用"; ja="アプリ別" }
        let tabColorMenuHint = { en="Change it for one tab from the tab menu. That color comes first, and shows even when this is off."
                                 zh="可在标签菜单中为单个标签选色。它优先于这里的设置，关闭时也有效。"
                                 ja="タブメニューでタブごとに色を選べます。こちらの設定より優先され、オフでも表示されます。" }
        let tabColorStyle = { caption={ en="Show colors as"; zh="着色方式"; ja="色の付け方" }
                              description=none
                              keywords={ en="stripe fill"; zh="色条 填充"; ja="ライン 塗りつぶし" } }
        let stripe = { en="Stripe"; zh="色条"; ja="色のライン" }
        let fill = { en="Fill"; zh="填充"; ja="塗りつぶし" }
        let numberShortcutMenuHint = { en="Enable or disable it for one app from the tab menu."; zh="可在标签右键菜单中为单个应用启用或禁用数字快捷键。"; ja="タブメニューでアプリごとに番号ショートカットを有効・無効にできます。" }
        let numberShortcutFor name = { en=sprintf "Switch tabs by number in %s" name; zh=sprintf "在 %s 中按数字切换标签" name; ja=sprintf "%s のタブを番号で切り替え" name }
        let numberShortcutCtrl = { en="Ctrl"; zh="Ctrl"; ja="Ctrl" }
        let numberShortcutAlt = { en="Alt"; zh="Alt"; ja="Alt" }
        let activateOnHover = { caption={ en="Switch tabs on hover"; zh="悬停切换标签"; ja="ホバーでタブを切り替え" }
                                description={ en="Rest the pointer on a tab instead of clicking."; zh="鼠标停在标签上即可，无需点击。"; ja="クリックしなくても、タブにポインターを置くだけで済みます。" }
                                keywords={ en="mouse over"; zh="悬停"; ja="マウスオーバー" } }
        let shiftScroll = { caption={ en="Switch tabs with Shift + scroll"; zh="用 Shift + 滚轮切换标签"; ja="Shift + スクロールでタブを切り替え" }
                            description={ en="Works anywhere over a grouped window. Over the tab strip, Shift isn't needed."
                                          zh="在分组窗口的任意位置均可使用；在标签条上滚动时无需按 Shift。"
                                          ja="グループ化されたウィンドウ上ならどこでも使えます。タブバー上では Shift は不要です。" }
                            keywords={ en="wheel"; zh="滚轮"; ja="ホイール" } }
        let tabTextColor = { caption={ en="Text and close button"; zh="文字与关闭按钮"; ja="文字と閉じるボタン" }; description=none; keywords=colorWords }
        let tabNormalBgColor = { caption={ en="Inactive tab"; zh="非活动标签"; ja="非アクティブなタブ" }; description=none; keywords=colorWords }
        let tabActiveBgColor = { caption={ en="Active tab"; zh="活动标签"; ja="アクティブなタブ" }; description=none; keywords=colorWords }
        let tabHighlightBgColor = { caption={ en="Hovered tab"; zh="悬停标签"; ja="ホバー中のタブ" }; description=none; keywords=colorWords }
        let tabStyle = { caption={ en="Tab style"; zh="标签样式"; ja="タブのスタイル" }
                         description={ en="How the active tab stands out from the rest."; zh="活动标签以何种方式与其他标签区分。"; ja="アクティブなタブをほかのタブとどう見分けるかを選びます。" }
                         keywords={ en="shape folder pill"; zh="形状 文件夹 胶囊"; ja="形 フォルダー ピル" } }
        let tabHeight = { caption={ en="Tab height"; zh="标签高度"; ja="タブの高さ" }; description=none; keywords=sizeWords }
        let tabMaxWidth = { caption={ en="Maximum tab width"; zh="标签最大宽度"; ja="タブの最大幅" }; description=none; keywords=sizeWords }
        let tabOverlap = { caption={ en="Tab spacing"; zh="标签间距"; ja="タブの間隔" }
                           description=none
                           keywords={ en="gap"; zh="间隙"; ja="隙間" } }
        let tabIndent = { caption={ en="Side margin"; zh="两侧边距"; ja="左右の余白" }
                          description={ en="Distance from the window edges."
                                        zh="与窗口两侧的距离。"
                                        ja="ウィンドウの左右端からの距離です。" }
                          keywords={ en="indent padding"; zh="缩进 边距"; ja="インデント 余白" } }
        let appRules = { caption=Pages.appRules
                         description={ en="Choose apps for tabs and automatic grouping."; zh="选择启用标签和自动分组的应用。"; ja="タブと自動グループ化を使うアプリを選択します。" }
                         keywords={ en="process program"; zh="程序 进程"; ja="プロセス プログラム" } }
        let workspaces = { caption=Pages.workspaces
                           description={ en="Save and restore window layouts."; zh="保存和恢复窗口布局。"; ja="ウィンドウの配置を保存して復元します。" }
                           keywords={ en="session"; zh="会话"; ja="セッション" } }
        let diagnostics = { caption=Pages.diagnostics
                            description={ en="Troubleshooting report and where to get help."; zh="故障排查报告与获取帮助的途径。"; ja="トラブルシューティング用のレポートとヘルプの入手先。" }
                            keywords={ en="about version log"; zh="关于 版本 日志"; ja="バージョン ログ" } }
        let settingsLocation = { caption={ en="Location"; zh="位置"; ja="場所" }
                                 description=none
                                 keywords={ en="folder path portable"; zh="文件夹 路径 便携"; ja="フォルダー パス ポータブル" } }
        let settingsBackup = { caption={ en="Back up and restore"; zh="备份与恢复"; ja="バックアップと復元" }
                               description={ en="Export all settings to a file, or import them and restart."; zh="将所有设置导出为文件，或从文件导入并重新启动。"; ja="すべての設定をファイルにエクスポートするか、インポートして再起動します。" }
                               keywords={ en="export import"; zh="导出 导入"; ja="エクスポート インポート" } }
        let settingsReset = { caption={ en="Reset to defaults"; zh="恢复默认设置"; ja="既定値に戻す" }
                              description={ en="Then restarts. App rules and workspaces can be kept."; zh="完成后重新启动，可保留应用规则和工作区。"; ja="その後再起動します。アプリのルールとワークスペースは残せます。" }
                              keywords={ en="factory"; zh="恢复默认 出厂"; ja="初期化" } }

    module General =
        let settingsFile = { en="Settings file"; zh="设置文件"; ja="設定ファイル" }
        let openFolder = { en="Open folder"; zh="打开文件夹"; ja="フォルダーを開く" }
        let export = { en="Export…"; zh="导出…"; ja="エクスポート…" }
        let import = { en="Import…"; zh="导入…"; ja="インポート…" }
        let settingsFileHelp = { en="If the WindowTabs.exe folder has a settings file, WindowTabs uses it (portable mode); otherwise it uses the one in AppData. Older versions named it WindowTabsSettings.txt; it is now WindowTabsSettings.json, and an old .txt file is still read until the .json one exists."
                                 zh="如果 WindowTabs.exe 所在文件夹里有设置文件，WindowTabs 就使用它（便携模式）；否则使用 AppData 中的。旧版本的设置文件名为 WindowTabsSettings.txt，现在改为 WindowTabsSettings.json；在 .json 文件生成之前，仍会读取旧的 .txt 文件。"
                                 ja="WindowTabs.exe のフォルダーに設定ファイルがあれば、WindowTabs はそれを使います（ポータブル モード）。なければ AppData のものを使います。以前のバージョンでは WindowTabsSettings.txt という名前でしたが、現在は WindowTabsSettings.json です。.json ファイルができるまでは、古い .txt ファイルも読み込まれます。" }
        let exported = { en="Settings exported."; zh="设置已导出。"; ja="設定をエクスポートしました。" }
        let reset = { en="Reset…"; zh="重置…"; ja="リセット…" }
        let resetConfirm = { en="Reset"; zh="重置"; ja="リセット" }
        let resetTitle = { en="Reset settings"; zh="重置设置"; ja="設定のリセット" }
        let resetMessage = { en="Restore every setting to its default and restart WindowTabs? The current settings are saved first, so you can import them back."
                             zh="将所有设置恢复为默认值并重新启动 WindowTabs？当前设置会先保存下来，之后可以再导入。"
                             ja="すべての設定を既定値に戻して WindowTabs を再起動しますか？現在の設定は先に保存されるので、後でインポートして戻せます。" }
        let resetAlsoClear = { en="Also clear:"; zh="同时清除："; ja="次も消去する："}
        let resetRestarting path =
            { en=sprintf "Settings were reset. WindowTabs will now restart.\n\nYour previous settings were saved to:\n%s" path
              zh=sprintf "设置已重置。WindowTabs 将重新启动。\n\n原来的设置已保存到：\n%s" path
              ja=sprintf "設定をリセットしました。WindowTabs を再起動します。\n\n以前の設定の保存先:\n%s" path }
        let importTitle = { en="Import settings"; zh="导入设置"; ja="設定のインポート" }
        let notSettingsFile = { en="This file does not contain WindowTabs settings."; zh="此文件不包含 WindowTabs 设置。"; ja="このファイルには WindowTabs の設定が含まれていません。" }
        let importedRestarting path =
            { en=sprintf "Settings imported. WindowTabs will now restart to apply them.\n\nYour previous settings were saved to:\n%s" path
              zh=sprintf "设置已导入。WindowTabs 将重新启动以应用这些设置。\n\n原来的设置已保存到：\n%s" path
              ja=sprintf "設定をインポートしました。適用するために WindowTabs を再起動します。\n\n以前の設定の保存先:\n%s" path }
        let tabMenuHint = { en="Change it for one group from the tab menu."; zh="可在标签菜单中为单个分组修改。"; ja="タブメニューでグループごとに変更できます。" }
        let startup = { en="Startup"; zh="启动"; ja="起動" }
        let tabBehavior = { en="Tab behavior"; zh="标签行为"; ja="タブの動作" }
        let taskbar = { en="Taskbar"; zh="任务栏"; ja="タスクバー" }
        let windowSwitcher = { en="Window switcher"; zh="窗口切换器"; ja="ウィンドウ切り替え" }

    module Appearance =
        let system = { en="System"; zh="跟随系统"; ja="システム" }
        let light = { en="Light"; zh="浅色"; ja="ライト" }
        let dark = { en="Dark"; zh="深色"; ja="ダーク" }
        let explorerPreview = { en="File Explorer theme preview"; zh="文件资源管理器主题预览"; ja="エクスプローラーのテーマのプレビュー" }
        let tabColors = { en="Tab colors"; zh="标签配色"; ja="タブの色" }
        let colorPreset = { en="Color preset"; zh="配色预设"; ja="配色プリセット" }
        let custom = { en="Custom"; zh="自定义"; ja="カスタム" }
        /// Switcher styles: large icons in a row, or window titles in a column.
        let switcherHorizontal = { en="Horizontal"; zh="横向"; ja="横" }
        let switcherVertical = { en="Vertical"; zh="纵向"; ja="縦" }
        /// Tab styles, in TabStyle.names order.
        let tabStyles =
            [| { en="Joined"; zh="连贯"; ja="連結" }
               { en="Folder"; zh="文件夹"; ja="フォルダー型" }
               { en="Floating"; zh="悬浮"; ja="フローティング" } |]
        /// The tab colours on which the text is shown darker or lighter than chosen.
        /// Samples of the tab text as chosen and as shown, side by side.
        let textBefore = { en="Before"; zh="调整前"; ja="調整前" }
        let textAfter = { en="After"; zh="调整后"; ja="調整後" }
        let colorsByCoding = { en="Set automatically while Tab color coding is By window or By app with Fill."
                               zh="标签着色为“按窗口”或“按应用”且选择“填充”时，这里自动配置。"
                               ja="タブの色分けが「ウィンドウ別」または「アプリ別」で「塗りつぶし」のときは、ここは自動で設定されます。" }
        let textAdjusted = { en="Text color is adjusted on some tabs to stay readable."; zh="部分标签上的文字颜色已调整，以保持清晰。"; ja="一部のタブでは、読みやすさのため文字の色を調整しています。" }
        /// A colour preset whose colours the user has changed.
        let editedPreset name = { en=sprintf "%s*" name; zh=sprintf "%s*" name; ja=sprintf "%s*" name }
        let resetColors = { en="Reset colors"; zh="重置配色"; ja="配色をリセット" }
        let tabLayout = { en="Tab layout"; zh="标签布局"; ja="タブのレイアウト" }
        let sizesStayTheSame = { en="Sizes stay the same when you switch themes."; zh="切换主题不会改变这些尺寸。"; ja="テーマを切り替えてもサイズは変わりません。" }
        let resetTabLayout = { en="Reset tab layout"; zh="重置标签布局"; ja="タブのレイアウトをリセット" }
        /// Stable source order; ThemePresets arranges the displayed colour families.
        let presets =
            [| { en="Default"; zh="默认"; ja="既定" }
               Settings.tabColorNames.[1]
               Settings.tabColorNames.[4]
               { en="Slate"; zh="石板灰"; ja="スレート" }
               Settings.tabColorNames.[14]
               { en="Sand"; zh="沙色"; ja="サンド" }
               { en="Amber"; zh="琥珀橙"; ja="アンバー" }
               Settings.tabColorNames.[11]
               Settings.tabColorNames.[6] |]
        let quickColorNames = [|
            { en="Black"; zh="黑色"; ja="黒" }
            { en="White"; zh="白色"; ja="白" }
            { en="Gray"; zh="灰色"; ja="グレー" }
            { en="Red"; zh="红色"; ja="赤" }
            { en="Orange"; zh="橙色"; ja="オレンジ" }
            { en="Yellow"; zh="黄色"; ja="黄" }
            { en="Green"; zh="绿色"; ja="緑" }
            { en="Blue"; zh="蓝色"; ja="青" } |]
        let pickerHelp = { en="Color picker: arrow keys adjust saturation and brightness; Shift + arrow keys adjust hue"
                           zh="颜色选择器：方向键调整饱和度和亮度；Shift + 方向键调整色相"
                           ja="色の選択：方向キーで彩度と明るさを調整し、Shift + 方向キーで色相を調整します" }
        let chooseColor = { en="Choose color"; zh="选择颜色"; ja="色を選択" }
        let decrease = { en="Decrease"; zh="减小"; ja="減らす" }
        let increase = { en="Increase"; zh="增大"; ja="増やす" }

    module Shortcuts =
        let keyboard = { en="Keyboard"; zh="键盘"; ja="キーボード" }
        let mouse = { en="Mouse"; zh="鼠标"; ja="マウス" }
        let keyboardNote = { en="Click a shortcut, then press the new combination. Esc cancels."
                             zh="点击快捷键后按下新的组合键。按 Esc 取消。"
                             ja="ショートカットをクリックして、新しいキーの組み合わせを押します。Esc でキャンセルします。" }
        let restoreDefaults = { en="Restore default shortcuts"; zh="恢复默认快捷键"; ja="既定のショートカットに戻す" }
        let inUse = { en="Shortcut already in use"; zh="快捷键已被占用"; ja="ショートカットは使用中です" }
        let usedBy name = { en=sprintf "Used by %s" name; zh=sprintf "已用于“%s”" name; ja=sprintf "「%s」で使用中" name }
        // Shortcut field messages are kept short to fit.
        let alreadySet = { en="Already set"; zh="已是当前快捷键"; ja="設定済み" }
        let includeCtrlOrAlt = { en="Include Ctrl or Alt"; zh="需要包含 Ctrl 或 Alt"; ja="Ctrl か Alt が必要です" }
        let pressShortcut = { en="Press a shortcut…"; zh="按下组合键…"; ja="キーを押してください…" }
        let notSet = { en="Not set"; zh="未设置"; ja="未設定" }

    /// The tab search box.
    module TabSearch =
        let prompt = { en="Search tabs by title or app"; zh="按标题或应用搜索标签"; ja="タイトルやアプリでタブを検索" }
        let noMatches = { en="No matching tabs."; zh="没有匹配的标签。"; ja="一致するタブはありません。" }
        let groupTabs count = { en=sprintf "This group · %d" count; zh=sprintf "当前分组 · %d" count; ja=sprintf "このグループ · %d" count }
        let allTabs count = { en=sprintf "All tabs · %d" count; zh=sprintf "全部标签 · %d" count; ja=sprintf "すべてのタブ · %d" count }

    module AppRules =
        let description = { en="Choose which apps use tabs and automatic grouping. Turning off auto-grouping keeps existing groups. Expand an app to see its windows."
                            zh="为应用设置标签和自动分组。关闭自动分组不会拆散已有分组。展开应用可查看其窗口。"
                            ja="アプリごとにタブと自動グループ化を設定します。自動グループ化をオフにしても既存のグループは維持されます。展開するとウィンドウが表示されます。" }
        let tabs = { en="Tabs"; zh="标签"; ja="タブ" }
        let autoGroup = { en="Auto-group"; zh="自动分组"; ja="自動グループ化" }
        /// The first row: its boxes turn a column on or off for every app below.
        let allApps = { en="All apps"; zh="全部应用"; ja="すべてのアプリ" }
        let appCount count =
            { en=(if count=1 then "1 app" else sprintf "%d apps" count); zh=sprintf "%d 个应用" count; ja=sprintf "%d 個のアプリ" count }
        let scanning = { en="Scanning…"; zh="正在扫描…"; ja="スキャン中…" }
        let scanFailed details = { en="Scan failed: " + details; zh="扫描失败：" + details; ja="スキャンに失敗しました: " + details }

    module Workspaces =
        let description = { en="Save and restore window groups and positions."; zh="保存和恢复窗口分组与位置。"; ja="ウィンドウのグループと位置を保存・復元します。" }
        let help = { en="How to use\n1  Group and position your windows, then click Save.\n2  Select a workspace and click Restore.\n3  To change an item's name or window title matching, double-click it, or point at it and click its edit button.\nRestore only uses open windows. It does not launch apps."
                     zh="使用说明\n1  将窗口分组并调整位置，然后点击“保存”。\n2  选中工作区，点击“恢复”。\n3  双击任意项，或将鼠标移到该项上点击编辑按钮，可修改名称或窗口标题的匹配方式。\n恢复仅适用于已打开的窗口，不会启动应用。"
                     ja="使い方\n1  ウィンドウをグループ化して配置し、「保存」をクリックします。\n2  ワークスペースを選び、「復元」をクリックします。\n3  項目をダブルクリックするか、項目にポインターを合わせて編集ボタンをクリックすると、名前やタイトルの一致方法を変更できます。\n復元は開いているウィンドウのみが対象です。アプリは起動しません。" }
        let restore = { en="Restore"; zh="恢复"; ja="復元" }
        let editTitle name = { en=sprintf "Edit %s" name; zh=sprintf "编辑 %s" name; ja=sprintf "%s を編集" name }
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
        let description = { en="Safe to share: no window titles, file paths or other personal information."
                            zh="可放心分享：不含窗口标题、文件路径等任何个人信息。"
                            ja="安心して共有できます：ウィンドウのタイトルやファイルのパスなどの個人情報は含まれません。" }
        let copyReport = { en="Copy the report, to paste into an issue"; zh="复制报告，以便粘贴到问题报告中"; ja="レポートをコピー（問題の報告に貼り付け）" }
        let includeWindowsHelp = { en="Include window details: each window's program, class and whether it gets tabs. Never titles."
                                   zh="包含窗口详情：每个窗口的程序、窗口类，以及是否会加上标签。不含标题。"
                                   ja="ウィンドウの詳細を含める：各ウィンドウのプログラム、クラス、タブが付くかどうか。タイトルは含みません。" }
        let refreshReport = { en="Refresh the report"; zh="刷新报告"; ja="レポートを更新" }
        let reportCopied = { en="Report copied."; zh="报告已复制。"; ja="レポートをコピーしました。" }
        let reportRefreshed = { en="Report refreshed."; zh="报告已刷新。"; ja="レポートを更新しました。" }
        let reportTitle = { en="Troubleshooting report"; zh="故障排查报告"; ja="トラブルシューティング レポート" }
        let projectPage = { en="Project page"; zh="项目主页"; ja="プロジェクトページ" }
        let reportIssue = { en="Report an issue"; zh="报告问题"; ja="問題を報告" }
        let releases = { en="Check Releases"; zh="查看发布版本"; ja="リリースを確認" }
        let releasesHint = { en="Check Releases ↗"; zh="查看发布版本 ↗"; ja="リリースを確認 ↗" }
        let openCrashLog = { en="Crash log"; zh="崩溃日志"; ja="クラッシュログ" }
