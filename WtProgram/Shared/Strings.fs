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
        let top = { en="Top"; zh="靠上"; ja="上" }
        let bottom = { en="Bottom"; zh="靠下"; ja="下" }
        let center = { en="Center"; zh="居中"; ja="中央" }
        let right = { en="Right"; zh="右侧"; ja="右" }
        let never = { en="Never"; zh="从不"; ja="しない" }
        let whenMaximized = { en="Maximized"; zh="最大化时"; ja="最大化時" }
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
        let split = { en="Split view"; zh="组内分屏"; ja="分割表示" }
        let splitHorizontal = { en="Split side by side"; zh="左右分屏"; ja="左右に分割" }
        let splitVertical = { en="Split top and bottom"; zh="上下分屏"; ja="上下に分割" }
        let splitEmptyRight = { en="New empty pane on the right"; zh="在右侧新建空白区域"; ja="右側に空のペインを作成" }
        let splitEmptyBelow = { en="New empty pane below"; zh="在下方新建空白区域"; ja="下側に空のペインを作成" }
        let splitEmpty = { en="Drop a tab or window here"; zh="将标签或窗口拖到这里"; ja="ここにタブまたはウィンドウをドロップ" }
        let splitChoose = { en="Choose a window…"; zh="选择窗口…"; ja="ウィンドウを選択…" }
        let splitOr = { en="or"; zh="或"; ja="または" }
        let splitSearch = { en="Type a title or app name to fill the pane"; zh="输入标题或应用名，放入这一侧"; ja="タイトルやアプリ名を入力してペインに配置" }
        let splitShow = { en="Show split view"; zh="显示分屏"; ja="分割表示に戻す" }
        let splitNoWindows = { en="No other windows"; zh="没有其他窗口"; ja="他のウィンドウはありません" }
        let splitMerge = { en="End split view"; zh="结束分屏"; ja="分割表示を終了" }
        let splitMakeStacked = { en="Switch to top and bottom"; zh="切换为上下"; ja="上下に切り替え" }
        let splitMakeSideBySide = { en="Switch to side by side"; zh="切换为左右"; ja="左右に切り替え" }
        let splitSwap = { en="Swap panes"; zh="交换两侧"; ja="ペインを入れ替え" }
        let splitEqual = { en="Equal panes"; zh="恢复均分"; ja="均等に分割" }
        let splitLess = { en="Shrink first pane"; zh="缩小左侧／上侧"; ja="左側／上側を縮小" }
        let splitMore = { en="Grow first pane"; zh="扩大左侧／上侧"; ja="左側／上側を拡大" }
        let splitFill = { en="Fill work area / restore split"; zh="分屏铺满／还原大小"; ja="作業領域に拡大／元に戻す" }
        let splitRatio = { en="Split divider"; zh="分屏分隔条"; ja="分割境界" }
        let splitFailed = { en="These windows could not fit the split layout. Split view has ended."; zh="窗口无法适应分屏尺寸，已退出分屏。"; ja="ウィンドウを分割領域に配置できないため、分割を終了しました。" }
        let position = { en="Tab position"; zh="标签位置"; ja="タブの位置" }
        let top = { en="Top"; zh="顶部"; ja="上部" }
        let showTabTitles = { en="Show tab titles"; zh="显示标签标题"; ja="タブのタイトルを表示" }
        let showIconsOnly = { en="Show icons only"; zh="仅显示图标"; ja="アイコンのみ表示" }
        let newTab = { en="New tab"; zh="新建标签"; ja="新しいタブ" }
        let renameTab = { en="Rename"; zh="重命名"; ja="名前を変更" }
        let restoreTabName = { en="Reset name"; zh="重置名称"; ja="名前をリセット" }
        let sortTabs = { en="Sort by app and title"; zh="按应用和标题排序"; ja="アプリとタイトル順に並べ替え" }
        let combineTaskbarIcons = { en="Combine taskbar icons"; zh="合并任务栏图标"; ja="タスクバーアイコンをまとめる" }
        /// For the group's own taskbar button, shown while its icons are combined.
        let changeTaskbarIcon = { en="Change taskbar icon…"; zh="更换任务栏图标…"; ja="タスクバーアイコンを変更…" }
        let restoreTaskbarIcon = { en="Restore taskbar icon"; zh="恢复默认任务栏图标"; ja="タスクバーアイコンを元に戻す" }
        /// The kinds named with their extensions, so programs are seen to be taken too.
        let taskbarIconFiles = { en="Icon files (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|All files (*.*)|*.*"
                                 zh="图标文件 (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|所有文件 (*.*)|*.*"
                                 ja="アイコン ファイル (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|すべてのファイル (*.*)|*.*" }
        let numberShortcut = { en="Switch by number"; zh="数字键切换"; ja="番号で切り替え" }
        let enableTabs = { en="Enable tabs"; zh="启用标签"; ja="タブを有効にする" }
        let autoGroupWindows = { en="Auto-group windows"; zh="自动分组"; ja="ウィンドウを自動グループ化" }
        let close = { en="Close"; zh="关闭"; ja="閉じる" }
        let closeOthers = { en="Close others"; zh="关闭其他"; ja="他を閉じる" }
        let closeAllOf exe = { en=sprintf "Close all '%s' windows" exe; zh=sprintf "关闭所有“%s”窗口" exe; ja=sprintf "「%s」のウィンドウをすべて閉じる" exe }
        let closeAll = { en="Close all"; zh="关闭全部"; ja="すべて閉じる" }
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
        let restoreSession = { caption={ en="Restore the last session after sign-in"; zh="登录后恢复上次会话"; ja="サインイン後に前回のセッションを復元" }
                               description={ en="Groups, positions and splits come back among what is open, or the apps are launched too. Kept up as things change."
                                             zh="按上次的分组、位置和分屏恢复已打开的内容，或连应用一起启动。记录随变化自动更新。"
                                             ja="開いているものをグループ、位置、分割ごとに戻すか、アプリも起動します。記録は変化に合わせて更新されます。" }
                               keywords={ en="session reboot"; zh="会话 重启"; ja="セッション 再起動" } }
        let enableTabsByDefault = { caption={ en="Use tabs for new apps"; zh="新应用自动启用标签"; ja="新しいアプリでタブを使う" }
                                    description={ en="Only apps first discovered after this change. Existing apps keep their tab setting."; zh="只影响此后首次出现的应用。已有应用保留原来的标签设置。"; ja="変更後に初めて検出するアプリに適用します。既存のアプリのタブ設定は維持します。" }
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
        let tabPosition = { caption={ en="Position tabs"; zh="放置标签"; ja="タブを配置" }
                            description=none
                            keywords={ en="vertical horizontal sidebar left right"; zh="垂直 横向 侧栏 左 右"; ja="縦 横 サイドバー 左 右" } }
        let tabColumnWidthHint = { en="For left/right tabs. Drag a column edge to resize one group, down to an icon rail."; zh="用于左／右标签。拖动侧栏边缘可单独调整分组宽度，最窄可只显示图标。"; ja="左・右のタブ用です。列の端をドラッグしてグループの幅を変更できます。最小幅ではアイコンのみを表示します。" }
        let tabColumnWidth = { caption={ en="Vertical tab width"; zh="垂直标签宽度"; ja="縦タブ幅" }
                               description=none
                               keywords={ en="vertical sidebar width size"; zh="垂直 侧栏 宽度 大小"; ja="縦 サイドバー 幅 サイズ" } }
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
                                       description={ en="Each group appears as one item, with the icon its taskbar button has."; zh="每个分组只显示一项，图标与任务栏按钮相同。"; ja="各グループを、タスク バーのボタンと同じアイコンの 1 項目として表示します。" }
                                       keywords={ en="switcher"; zh="切换器"; ja="切り替え" } }
        let nextTab = { caption={ en="Switch to next tab"; zh="切换到下一个标签"; ja="次のタブに切り替え" }
                        description=none
                        keywords=hotkeyWords }
        let previousTab = { caption={ en="Switch to previous tab"; zh="切换到上一个标签"; ja="前のタブに切り替え" }
                            description=none
                            keywords=hotkeyWords }
        let searchTabs = { caption={ en="Switch windows"; zh="切换窗口"; ja="ウィンドウを切り替え" }
                           description={ en="Switch to a window by title or app name."; zh="按标题或应用名查找并切换窗口。"; ja="タイトルやアプリ名でウィンドウを探して切り替えます。" }
                           keywords={ en="find"; zh="查找"; ja="検索" } }
        let newTab = { caption={ en="New tab"; zh="新建标签"; ja="新しいタブ" }
                       description={ en="Starts the current tab's app again."
                                     zh="再次启动当前标签的应用。"
                                     ja="現在のタブのアプリをもう一度起動します。" }
                       keywords={ en="new window"; zh="新建窗口"; ja="新しいウィンドウ" } }
        let closeTab = { caption={ en="Close tab"; zh="关闭标签"; ja="タブを閉じる" }
                         description={ en="Closes the window of the tab in front."
                                       zh="关闭当前标签的窗口。"
                                       ja="前面のタブのウィンドウを閉じます。" }
                         keywords={ en="close window"; zh="关闭窗口"; ja="ウィンドウを閉じる" } }
        let private splitWords = { en="split view pane"; zh="分屏 窗格"; ja="分割 ペイン" }
        let splitSideBySide = { caption={ en="Split side by side with next tab"; zh="与下一个标签左右分屏"; ja="次のタブと左右に分割" }
                                description={ en="A lone tab opens an empty pane instead."; zh="只有一个标签时改为新建空白区域。"; ja="タブが 1 つだけなら空のペインを開きます。" }
                                keywords=splitWords }
        let splitStacked = { caption={ en="Split top and bottom with next tab"; zh="与下一个标签上下分屏"; ja="次のタブと上下に分割" }
                             description=none
                             keywords=splitWords }
        let splitEmptyRight = { caption={ en="New empty pane on the right"; zh="在右侧新建空白区域"; ja="右側に空のペインを作成" }
                                description={ en="Splits the focused pane again. Drop a tab or window into it."; zh="再拆一次焦点格，之后把标签或窗口拖进去。"; ja="フォーカス中のペインをさらに分割します。タブやウィンドウをそこにドロップします。" }
                                keywords=splitWords }
        let splitEmptyBelow = { caption={ en="New empty pane below"; zh="在下方新建空白区域"; ja="下側に空のペインを作成" }
                                description=none
                                keywords=splitWords }
        let splitFocusOther = { caption={ en="Focus the next pane"; zh="焦点切到下一格"; ja="次のペインにフォーカス" }
                                description={ en="Brings parked panes back as well."; zh="分屏被暂存时也会恢复显示。"; ja="待機中のペインも表示に戻します。" }
                                keywords=splitWords }
        let splitFocusLeft = { caption={ en="Focus the pane to the left"; zh="焦点切到左边的格"; ja="左のペインにフォーカス" }
                               description=none
                               keywords=splitWords }
        let splitFocusRight = { caption={ en="Focus the pane to the right"; zh="焦点切到右边的格"; ja="右のペインにフォーカス" }
                                description=none
                                keywords=splitWords }
        let splitFocusUp = { caption={ en="Focus the pane above"; zh="焦点切到上边的格"; ja="上のペインにフォーカス" }
                             description=none
                             keywords=splitWords }
        let splitFocusDown = { caption={ en="Focus the pane below"; zh="焦点切到下边的格"; ja="下のペインにフォーカス" }
                               description=none
                               keywords=splitWords }
        let splitSwap = { caption={ en="Swap panes"; zh="交换两侧"; ja="ペインを入れ替え" }
                          description={ en="The focused pane and its neighbour change places."; zh="焦点格和它旁边的格互换位置。"; ja="フォーカス中のペインと隣のペインを入れ替えます。" }
                          keywords=splitWords }
        let splitClosePane = { caption={ en="Close the focused pane"; zh="关闭焦点格"; ja="フォーカス中のペインを閉じる" }
                               description={ en="Its neighbour takes the room; the window stays a tab."; zh="旁边的格补上空位，窗口仍保留为标签。"; ja="隣のペインがその場所を使い、ウィンドウはタブとして残ります。" }
                               keywords=splitWords }
        let splitGrow = { caption={ en="Grow first pane"; zh="扩大左侧／上侧"; ja="左側／上側を拡大" }
                          description=none
                          keywords=splitWords }
        let splitShrink = { caption={ en="Shrink first pane"; zh="缩小左侧／上侧"; ja="左側／上側を縮小" }
                            description=none
                            keywords=splitWords }
        let splitEqualize = { caption={ en="Equal panes"; zh="恢复均分"; ja="均等に分割" }
                              description=none
                              keywords=splitWords }
        let splitEnd = { caption={ en="End split view"; zh="结束分屏"; ja="分割表示を終了" }
                         description={ en="The focused pane fills the group."; zh="焦点侧占满整个组。"; ja="フォーカス中のペインがグループ全体を占めます。" }
                         keywords=splitWords }
        let switchTabsByNumber = { caption={ en="Switch tabs by number"; zh="按数字切换标签"; ja="番号でタブを切り替え" }
                                   description={ en="Hold a modifier key and press 1–9."; zh="按住修饰键再按 1–9。"; ja="修飾キーを押しながら 1～9 を押します。" }
                                   keywords={ en="digit"; zh="数字"; ja="数字" } }
        let numberShortcut = { caption={ en="Modifier key for 1–9"; zh="数字键搭配的修饰键"; ja="1～9 と組み合わせるキー" }
                               description=none
                               keywords={ en="number"; zh="数字"; ja="番号" } }
        let enableNumberLeader = { caption={ en="Switch tabs with a leader key"; zh="用引导键切换标签"; ja="リーダーキーでタブを切り替え" }; description={ en="Press the leader, then a selection key. Esc cancels."; zh="先按引导键，再按选择键。Esc 取消。"; ja="リーダーキーの後、選択キーを押します。Esc で解除。" }; keywords=none }
        let leaderKeys = { caption={en="Selection keys";zh="选择键";ja="選択キー"}; description={en="Use unique letters or digits, in tab order.";zh="按标签顺序，使用不重复的字母或数字。";ja="タブ順に、重複しない英字または数字を指定します。"}; keywords={en="leader keys letters home row";zh="引导键 字母 主键区";ja="リーダーキー 英字 ホームポジション"} }
        let leaderKeyPresets = [| {en="123456789";zh="123456789";ja="123456789"}; {en="QWERTYUIOP";zh="QWERTYUIOP";ja="QWERTYUIOP"}; {en="ASDFGHJKL;";zh="ASDFGHJKL;";ja="ASDFGHJKL;"} |]
        let invalidLeaderKeys = {en="Use at least one unique letter, digit or unshifted punctuation key.";zh="请使用至少一个不重复的字母、数字或无需 Shift 的标点键。";ja="重複しない英字、数字、Shift 不要の記号を 1 つ以上指定してください。"}
        let numberLeader = { caption={ en="Tab selection leader"; zh="标签选择引导键"; ja="タブ選択のリーダーキー" }; description=none; keywords=none }
        let tabColors = { en="Color"; zh="颜色"; ja="色" }
        let customTabColor = { en="Custom…"; zh="自定义…"; ja="カスタム…" }
        let clearTabColor = { en="Clear color"; zh="清除颜色"; ja="色を解除" }
        let rememberTabColor = { en="Remember color for current app"; zh="为当前应用记住颜色"; ja="現在のアプリの色を記憶" }
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
        /// The choices of Switch tabs by number: off, or the key held with 1–9.
        let numberShortcutOff = { en="Off"; zh="关闭"; ja="オフ" }
        let numberShortcutWith key = { en=sprintf "%s + 1–9" key; zh=sprintf "%s + 1–9" key; ja=sprintf "%s + 1～9" key }
        let numberShortcutCtrl = { en="Ctrl"; zh="Ctrl"; ja="Ctrl" }
        let numberShortcutAlt = { en="Alt"; zh="Alt"; ja="Alt" }
        let activateOnHover = { caption={ en="Switch tabs on hover"; zh="悬停切换标签"; ja="ホバーでタブを切り替え" }
                                description={ en="Rest the pointer on a tab instead of clicking."; zh="鼠标停在标签上即可，无需点击。"; ja="クリックしなくても、タブにポインターを置くだけで済みます。" }
                                keywords={ en="mouse over"; zh="悬停"; ja="マウスオーバー" } }
        let shiftScroll = { caption={ en="Switch tabs with Shift + scroll"; zh="用 Shift + 滚轮切换标签"; ja="Shift + スクロールでタブを切り替え" }
                            description={ en="Works anywhere over a grouped window. Over the tab bar, Shift isn't needed."
                                          zh="在分组窗口的任意位置均可使用；在标签栏上滚动时无需按 Shift。"
                                          ja="グループ化されたウィンドウ上ならどこでも使えます。タブバー上では Shift は不要です。" }
                            keywords={ en="wheel"; zh="滚轮"; ja="ホイール" } }
        let ctrlScroll = { caption={ en="Move tabs with Ctrl + scroll"; zh="用 Ctrl + 滚轮移动标签"; ja="Ctrl + スクロールでタブを移動" }
                           description={ en="Over the tab bar, moves the current tab left or right."
                                         zh="在标签栏上滚动，将当前标签向左或向右移动。"
                                         ja="タブバー上で、現在のタブを左右に移動します。" }
                           keywords={ en="wheel reorder"; zh="滚轮 排序"; ja="ホイール 並べ替え" } }
        let middleClickClose = { caption={ en="Middle-click closes a tab"; zh="按下滚轮关闭标签"; ja="中クリックでタブを閉じる" }
                                 description={ en="Press the wheel on a tab to close its window."
                                               zh="在标签上按下滚轮，关闭它的窗口。"
                                               ja="タブ上でホイールを押すと、そのウィンドウを閉じます。" }
                                 keywords={ en="wheel click"; zh="中键 滚轮"; ja="ホイール クリック" } }
        let tabTextColor = { caption={ en="Text and close button"; zh="文字与关闭按钮"; ja="文字と閉じるボタン" }; description=none; keywords=colorWords }
        let tabNormalBgColor = { caption={ en="Inactive tab"; zh="非活动标签"; ja="非アクティブなタブ" }; description=none; keywords=colorWords }
        let tabActiveBgColor = { caption={ en="Active tab"; zh="活动标签"; ja="アクティブなタブ" }; description=none; keywords=colorWords }
        let tabHighlightBgColor = { caption={ en="Hovered tab"; zh="悬停标签"; ja="ホバー中のタブ" }; description=none; keywords=colorWords }
        let tabStyle = { caption={ en="Tab style"; zh="标签样式"; ja="タブのスタイル" }
                         description={ en="How the active tab stands out from the rest."; zh="活动标签以何种方式与其他标签区分。"; ja="アクティブなタブをほかのタブとどう見分けるかを選びます。" }
                         keywords={ en="shape folder pill"; zh="形状 文件夹 胶囊"; ja="形 フォルダー ピル" } }
        let tabHeightHint = { en="Left/right tabs have a minimum height of 32 px."; zh="左／右标签的实际高度最低为 32px。"; ja="左・右のタブの最小高さは 32px です。" }
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

    /// What letting go of a dragged tab here would do, on the preview.
    module Drop =
        let fillEmpty = { en="Fill the empty pane"; zh="放入空白窗格"; ja="空のペインに配置" }
        let columnLeft = { en="New column on the left"; zh="整组左侧新开一列"; ja="左に新しい列" }
        let columnRight = { en="New column on the right"; zh="整组右侧新开一列"; ja="右に新しい列" }
        let rowAbove = { en="New row above"; zh="整组上方新开一行"; ja="上に新しい行" }
        let rowBelow = { en="New row below"; zh="整组下方新开一行"; ja="下に新しい行" }
        /// Beside the named window, the one shown or the pane under the pointer; without a name,
        /// the split alone.
        let leftOf name = if name="" then { en="Split: left"; zh="分屏：左侧"; ja="分割: 左" } else { en=sprintf "Left of %s" name; zh=sprintf "放到 %s 的左侧" name; ja=sprintf "%s の左に" name }
        let rightOf name = if name="" then { en="Split: right"; zh="分屏：右侧"; ja="分割: 右" } else { en=sprintf "Right of %s" name; zh=sprintf "放到 %s 的右侧" name; ja=sprintf "%s の右に" name }
        let above name = if name="" then { en="Split: above"; zh="分屏：上方"; ja="分割: 上" } else { en=sprintf "Above %s" name; zh=sprintf "放到 %s 的上方" name; ja=sprintf "%s の上に" name }
        let below name = if name="" then { en="Split: below"; zh="分屏：下方"; ja="分割: 下" } else { en=sprintf "Below %s" name; zh=sprintf "放到 %s 的下方" name; ja=sprintf "%s の下に" name }

    module Session =
        let restoring = { en="Restoring the last session…"; zh="正在恢复上次会话…"; ja="前回のセッションを復元しています…" }
        let launching count =
            { en=(if count=1 then "Starting an app for the last session…" else sprintf "Starting %d apps for the last session…" count)
              zh=sprintf "正在为上次会话启动 %d 个应用…" count
              ja=sprintf "前回のセッションのためにアプリを %d 個起動しています…" count }
        let restored restored missing =
            { en=(sprintf "Session restored: %d windows" restored)+(if missing=0 then "" else sprintf ", %d not found" missing)
              zh=(sprintf "已恢复上次会话：%d 个窗口" restored)+(if missing=0 then "" else sprintf "，%d 个未找到" missing)
              ja=(sprintf "前回のセッションを復元しました: %d 個のウィンドウ" restored)+(if missing=0 then "" else sprintf "、%d 個が見つかりません" missing) }

    module General =
        /// The choices of Restore the last session at start.
        let sessionOff = { en="Off"; zh="关闭"; ja="オフ" }
        let sessionWindows = { en="Open windows"; zh="已打开的窗口"; ja="開いているウィンドウ" }
        let sessionApps = { en="Open windows, and start their apps"; zh="已打开的窗口，并启动缺少的应用"; ja="開いているウィンドウと、アプリの起動" }
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
        let prompt = { en="Type a title or app name to switch windows"; zh="输入标题或应用名，切换窗口"; ja="タイトルやアプリ名を入力してウィンドウを切り替え" }
        let noMatches = { en="No matching windows."; zh="没有匹配的窗口。"; ja="一致するウィンドウはありません。" }
        /// Beside a window on another virtual desktop that has no name of its own.
        let desktop number = { en=sprintf "Desktop %d" number; zh=sprintf "桌面 %d" number; ja=sprintf "デスクトップ %d" number }
        let otherDesktop = { en="Another desktop"; zh="其他桌面"; ja="別のデスクトップ" }
        let groupTabs count = { en=sprintf "This group · %d" count; zh=sprintf "当前分组 · %d" count; ja=sprintf "このグループ · %d" count }
        let allTabs count = { en=sprintf "All windows · %d" count; zh=sprintf "全局窗口 · %d" count; ja=sprintf "すべてのウィンドウ · %d" count }

    module AppRules =
        let addApplication = { en="Add application"; zh="添加应用"; ja="アプリを追加" }
        let deleteRule = { en="Delete"; zh="删除"; ja="削除" }
        let applicationRuleExists = { en="This application already has a grouping rule."; zh="这个应用已有分组规则，请编辑已有行。"; ja="このアプリには既にグループ化ルールがあります。既存の行を編集してください。" }
        /// Several apps sharing one named group, as opposed to each app's windows grouping by themselves.
        let groupingRules = { en="Groups across apps"; zh="跨应用分组"; ja="アプリをまたぐグループ" }
        let applications = { en="Applications"; zh="应用"; ja="アプリ" }
        let addGroup = { en="Add group"; zh="添加组"; ja="グループを追加" }
        /// Shown while Groups across apps is empty.
        let noGroups = { en="Add a group to put several apps together, or drag tabs onto each other: the groups show here"
                         zh="点“添加组”把多个应用放在一起，或拖拽标签合并窗口，分组会显示在这里"
                         ja="「グループを追加」で複数のアプリをまとめるか、タブをドラッグして重ねると、ここに表示されます" }
        /// The name a new group starts with.
        let newGroup number = { en=sprintf "Group %d" number; zh=sprintf "组 %d" number; ja=sprintf "グループ %d" number }
        let selectRule = { en="Select a group or rule to edit here. Changes save automatically."; zh="选中组或规则即可在此编辑，修改会自动保存。"; ja="グループかルールを選択して編集します。変更は自動保存されます。" }
        let newRule = { en="New rule"; zh="新规则"; ja="新しいルール" }
        let groupNameRequired = { en="Enter a group name."; zh="请填写组名。"; ja="グループ名を入力してください。" }
        let groupNameExists = { en="A group with this name already exists."; zh="已有同名组，请使用其他名称。"; ja="同じ名前のグループが存在します。" }
        let groupRulesHelp = { en="Put windows of different apps into one named group. Apps added here always have tabs, leave the list above and don't use Auto-group. Windows you group by dragging tabs together show here too."
                               zh="把不同应用的窗口放进同一个命名组。加入这里的应用会始终启用标签，不再出现在上面的列表里，也不再使用“自动分组”。拖拽标签合并形成的窗口分组也会显示在这里。"
                               ja="異なるアプリのウィンドウを名前付きの一つのグループにまとめます。ここに追加したアプリは常にタブを使い、上の一覧からは外れ、「自動グループ化」も使われません。タブをドラッグしてまとめたウィンドウのグループもここに表示されます。" }
        let application = { en="Application"; zh="应用"; ja="アプリ" }
        let chooseApplication = { en="Add an app"; zh="添加应用"; ja="アプリを追加" }
        /// Hover tips on a group's buttons.
        let ungroup = { en="Ungroup"; zh="解除分组"; ja="グループを解除" }
        let removeFromGroup = { en="Remove from group"; zh="从分组中移除"; ja="グループから外す" }
        let browseApplication = { en="Browse…"; zh="浏览…"; ja="参照…" }
        let applicationFiles = { en="Applications (*.exe)|*.exe"; zh="应用程序 (*.exe)|*.exe"; ja="アプリ (*.exe)|*.exe" }
        let applicationOnly = { en="Application only"; zh="仅按应用"; ja="アプリのみ" }
        let targetGroup = { en="Target group"; zh="目标组"; ja="宛先グループ" }
        let chooseGroup = { en="Existing group"; zh="选择已有组"; ja="既存のグループ" }
        let enabled = { en="Enabled"; zh="启用"; ja="有効" }
        let addRule = { en="Add rule"; zh="添加规则"; ja="ルールを追加" }
        let invalidRule = { en="Invalid grouping rule"; zh="分组规则无效"; ja="無効なグループ化ルール" }
        let requiredFields = { en="Choose an application and enter a target group."; zh="请选择应用并填写目标组。"; ja="アプリを選択して宛先グループを入力してください。" }
        let requiredTitle = { en="Enter a title pattern, or choose Application only."; zh="请填写标题条件，或选择“仅按应用”。"; ja="タイトル条件を入力するか「アプリのみ」を選択してください。" }
        let description = { en="Choose which apps get tabs. Auto-group puts the windows of an app into one group. Expand an app to see its windows."
                            zh="选择哪些应用使用标签。勾选“自动分组”后，同一应用的窗口会合在一个组里。展开应用可查看它的窗口。"
                            ja="タブを使うアプリを選びます。「自動グループ化」をオンにすると、同じアプリのウィンドウが一つのグループに入ります。展開するとウィンドウを表示します。" }
        let tabs = { en="Tabs"; zh="标签"; ja="タブ" }
        /// Each app's windows join one group of their own; named as in the tab menu.
        let autoGroup = { en="Auto-group"; zh="自动分组"; ja="自動グループ化" }
        /// ApplicationFrameHost.exe, which hosts every Store (UWP) app: Settings, Calculator, Photos.
        let windowsApps = { en="Windows apps"; zh="Windows 应用"; ja="Windows アプリ" }
        /// Said on hover over the icon of a group made by dragging tabs together, shown read-only
        /// after the named groups.
        let handMadeGroups = { en="Windows grouped by dragging"; zh="拖拽形成的窗口分组"; ja="ドラッグでまとめたウィンドウのグループ" }
        let scanning = { en="Scanning…"; zh="正在扫描…"; ja="スキャン中…" }
        let scanFailed details = { en="Scan failed: " + details; zh="扫描失败：" + details; ja="スキャンに失敗しました: " + details }

    module Workspaces =
        let description = { en="Save and restore window groups and positions."; zh="保存和恢复窗口分组与位置。"; ja="ウィンドウのグループと位置を保存・復元します。" }
        let help = { en="How to use\n1  Group and position your windows, then click Save layout.\n2  Select a workspace and click Restore selected.\n3  To change an item's name or window title matching, double-click it, or point at it and click its edit button.\nA window whose title has changed since it was saved is still matched by its app.\nRestore uses open windows. If an app is not running, the result offers to launch it."
                     zh="使用说明\n1  将窗口分组并调整位置，然后点击“保存布局”。\n2  选中工作区，点击“恢复所选”。\n3  双击任意项，或将鼠标移到该项上点击编辑按钮，可修改名称或窗口标题的匹配方式。\n保存后标题发生变化的窗口，仍会按其所属应用匹配。\n恢复使用已打开的窗口；应用未运行时，可在结果中启动它。"
                     ja="使い方\n1  ウィンドウをグループ化して配置し、「配置を保存」をクリックします。\n2  ワークスペースを選び、「選択したものを復元」をクリックします。\n3  項目をダブルクリックするか、項目にポインターを合わせて編集ボタンをクリックすると、名前やタイトルの一致方法を変更できます。\n保存後にタイトルが変わったウィンドウも、アプリで照合されます。\n復元は開いているウィンドウを使います。アプリが起動していない場合は、結果から起動できます。" }
        /// Saving makes a new workspace of the windows as they are; restoring acts on the selected one.
        let saveCurrent = { en="Save layout"; zh="保存布局"; ja="配置を保存" }
        let restoreSelected = { en="Restore selected"; zh="恢复所选"; ja="選択したものを復元" }
        let editTitle name = { en=sprintf "Edit %s" name; zh=sprintf "编辑 %s" name; ja=sprintf "%s を編集" name }
        let matchMethod = { en="Match method"; zh="匹配方式"; ja="一致方法" }
        let exactMatch = { en="Exact match"; zh="完全匹配"; ja="完全一致" }
        let startsWith = { en="Starts with"; zh="开头匹配"; ja="前方一致" }
        let endsWith = { en="Ends with"; zh="结尾匹配"; ja="後方一致" }
        let contains = { en="Contains"; zh="包含"; ja="部分一致" }
        let regularExpression = { en="Regular expression"; zh="正则表达式"; ja="正規表現" }
        let restoreTitle = { en="Workspace restore"; zh="恢复工作区"; ja="ワークスペースの復元" }
        /// The result counts windows in three states, each with its mark.
        let restoredState = { en="Restored"; zh="已恢复"; ja="復元済み" }
        let notFoundState = { en="Not found"; zh="未找到"; ja="見つからない" }
        let failedState = { en="Failed"; zh="失败"; ja="失敗" }
        /// Windows are matched by app when their title changed, so one not found is closed.
        let notFoundHint = { en="These windows are closed, or their app is not running."; zh="这些窗口已关闭，或其应用未运行。"; ja="これらのウィンドウは閉じられているか、アプリが起動していません。" }
        let launchApps count =
            { en=(if count=1 then "Launch the app and retry" else sprintf "Launch %d apps and retry" count)
              zh=sprintf "启动 %d 个应用并重试" count
              ja=sprintf "%d 個のアプリを起動して再試行" count }
        let launchingApps count =
            { en=(if count=1 then "Launching the app…" else sprintf "Launching %d apps…" count)
              zh=sprintf "正在启动 %d 个应用…" count
              ja=sprintf "%d 個のアプリを起動中…" count }
        let invalidSetting = { en="Invalid workspace setting"; zh="工作区设置无效"; ja="ワークスペースの設定が無効です" }
        let dataWarnings = { en="Workspace data"; zh="工作区数据"; ja="ワークスペースのデータ" }
        let titleInvalid = { en="Window title is missing or exceeds 4,096 characters."; zh="窗口标题缺失或超过 4,096 个字符。"; ja="ウィンドウのタイトルがないか、4,096 文字を超えています。" }
        let unknownMatchMethod = { en="Unknown title match method."; zh="未知的标题匹配方式。"; ja="タイトルの一致方法が不明です。" }
        let expectedObject = { en="Expected an object."; zh="应为对象。"; ja="オブジェクトが必要です。" }
        let expectedList = { en=": expected a list."; zh="：应为列表。"; ja="：リストが必要です。" }
        let missingPlacement = { en="Missing placement."; zh="缺少窗口位置。"; ja="ウィンドウの配置がありません。" }
        let invalidTabLayout = { en="Invalid saved tab layout; the group will use current defaults."; zh="保存的标签布局无效；该分组将使用当前默认设置。"; ja="保存されたタブ配置が無効です。グループには現在の既定値を使用します。" }
        let invalidSplit = { en="Invalid saved split view; the group will restore without it."; zh="保存的分屏无效；该分组恢复时不带分屏。"; ja="保存された分割表示が無効です。グループは分割なしで復元されます。" }
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
