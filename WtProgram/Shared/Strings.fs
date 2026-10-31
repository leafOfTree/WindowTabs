namespace Bemo

/// Caption, description and extra search words (synonyms not already in the caption or
/// description) of one entry in SettingsCatalog.
type SettingText = { caption:LocalizedText; description:LocalizedText; keywords:LocalizedText }

/// Every user-visible text, in every supported language. Show one with `tr`.
/// Text with values filled in is a function, so the format is written once per language.
module Strings =
    let private none = { en=""; zh=""; ja=""; ar="" }

    module Common =
        let ok = { en="OK"; zh="确定"; ja="OK"; ar="موافق" }
        let cancel = { en="Cancel"; zh="取消"; ja="キャンセル"; ar="إلغاء" }
        let save = { en="Save"; zh="保存"; ja="保存"; ar="حفظ" }
        let refresh = { en="Refresh"; zh="刷新"; ja="更新"; ar="تحديث" }
        let name = { en="Name"; zh="名称"; ja="名称"; ar="الاسم" }
        let title = { en="Title"; zh="标题"; ja="タイトル"; ar="العنوان" }
        let left = { en="Left"; zh="左侧"; ja="左"; ar="يسار" }
        let top = { en="Top"; zh="靠上"; ja="上"; ar="أعلى" }
        let bottom = { en="Bottom"; zh="靠下"; ja="下"; ar="أسفل" }
        let center = { en="Center"; zh="居中"; ja="中央"; ar="الوسط" }
        let right = { en="Right"; zh="右侧"; ja="右"; ar="يمين" }
        let never = { en="Never"; zh="从不"; ja="しない"; ar="أبدًا" }
        let whenMaximized = { en="Maximized"; zh="最大化时"; ja="最大化時"; ar="عند التكبير" }
        let always = { en="Always"; zh="始终"; ja="常に"; ar="دائمًا" }
        let selectedChoice (name:LocalizedText) = { en=name.en+"  ✓"; zh=name.zh+"  ✓"; ja=name.ja+"  ✓"; ar=name.ar+"  ✓" }
        let operationFailed = { en="Operation failed"; zh="操作失败"; ja="操作に失敗しました"; ar="فشلت العملية" }

    module Messages =
        let alreadyRunning = { en="WindowTabs is already running."; zh="WindowTabs 已在运行。"; ja="WindowTabs は既に実行中です。"; ar="برنامج WindowTabs قيد التشغيل بالفعل." }
        let couldNotContinue = { en="WindowTabs could not continue"; zh="WindowTabs 无法继续运行"; ja="WindowTabs を続行できません"; ar="تعذّر على WindowTabs المتابعة" }
        let settingsSaveFailed = { en="Settings save failed"; zh="设置保存失败"; ja="設定を保存できませんでした"; ar="فشل حفظ الإعدادات" }
        let unableToSaveSettings path details =
            { en=sprintf "Unable to save settings to %s.\nYour changes remain in memory and will be retried on the next edit or exit.\n\n%s" path details
              zh=sprintf "无法保存设置到 %s。\n更改仍保留在内存中，下次修改或退出时会重试。\n\n%s" path details
              ja=sprintf "設定を %s に保存できませんでした。\n変更はメモリに保持され、次の編集時または終了時に再試行されます。\n\n%s" path details
              ar=sprintf "تعذّر حفظ الإعدادات في %s.\nستظل تغييراتك في الذاكرة وستتم إعادة المحاولة عند التعديل التالي أو عند الخروج.\n\n%s" path details }
        let settingsError = { en="Settings error"; zh="设置错误"; ja="設定エラー"; ar="خطأ في الإعدادات" }
        let errorLoadingSettings path details =
            { en=sprintf "Error loading settings.\n\nFix or remove the file %s.\n\nDetails: %s" path details
              zh=sprintf "读取设置出错。\n\n请修复或删除文件 %s。\n\n详细信息：%s" path details
              ja=sprintf "設定の読み込み中にエラーが発生しました。\n\nファイル %s を修正または削除してください。\n\n詳細: %s" path details
              ar=sprintf "حدث خطأ أثناء تحميل الإعدادات.\n\nأصلح الملف %s أو احذفه.\n\nالتفاصيل: %s" path details }

    /// Tray icon menu.
    module Tray =
        let exit = { en="Exit WindowTabs"; zh="退出 WindowTabs"; ja="WindowTabs を終了"; ar="إنهاء WindowTabs" }
        let errorTitle = { en="WindowTabs ran into a problem"; zh="WindowTabs 遇到了问题"; ja="WindowTabs で問題が発生しました"; ar="واجه WindowTabs مشكلة" }
        let errorText = { en="It kept running. The details are in the crash log; click to show it."
                          zh="程序仍在运行。详细信息已写入崩溃日志，点击查看。"
                          ja="動作は続いています。詳細はクラッシュログにあります。クリックすると表示します。"
                          ar="استمر البرنامج في العمل. التفاصيل موجودة في سجل الأعطال؛ انقر لعرضه." }

    /// Tab context menu.
    module TabMenu =
        let split = { en="Split view"; zh="组内分屏"; ja="分割表示"; ar="العرض المقسّم" }
        /// A window chosen from a list goes on that side of the tab.
        let splitPickRight = { en="Window on the right…"; zh="在右侧放入窗口…"; ja="右側にウィンドウを配置…"; ar="نافذة على اليمين…" }
        let splitPickBelow = { en="Window below…"; zh="在下方放入窗口…"; ja="下側にウィンドウを配置…"; ar="نافذة في الأسفل…" }
        let splitPickLeft = { en="Window on the left…"; zh="在左侧放入窗口…"; ja="左側にウィンドウを配置…"; ar="نافذة على اليسار…" }
        let splitPickAbove = { en="Window above…"; zh="在上方放入窗口…"; ja="上側にウィンドウを配置…"; ar="نافذة في الأعلى…" }
        let splitEmpty = { en="Drop a tab or window here"; zh="将标签或窗口拖到这里"; ja="ここにタブまたはウィンドウをドロップ"; ar="أفلت علامة تبويب أو نافذة هنا" }
        let splitChoose = { en="Choose a window…"; zh="选择窗口…"; ja="ウィンドウを選択…"; ar="اختر نافذة…" }
        let splitOr = { en="or"; zh="或"; ja="または"; ar="أو" }
        let splitSearch = { en="Type a title or app name to fill the pane"; zh="输入标题或应用名，放入这个窗格"; ja="タイトルやアプリ名を入力してペインに配置"; ar="اكتب عنوانًا أو اسم تطبيق لملء الجزء" }
        let splitShow = { en="Show split view"; zh="显示分屏"; ja="分割表示に戻す"; ar="إظهار العرض المقسّم" }
        let splitNoWindows = { en="No other windows"; zh="没有其他窗口"; ja="他のウィンドウはありません"; ar="لا توجد نوافذ أخرى" }
        let splitMerge = { en="End split view"; zh="结束分屏"; ja="分割表示を終了"; ar="إنهاء العرض المقسّم" }
        let splitMakeStacked = { en="Switch to top and bottom"; zh="切换为上下"; ja="上下に切り替え"; ar="التبديل إلى أعلى وأسفل" }
        let splitMakeSideBySide = { en="Switch to side by side"; zh="切换为左右"; ja="左右に切り替え"; ar="التبديل إلى جنبًا إلى جنب" }
        let splitSwap = { en="Swap panes"; zh="交换窗格"; ja="ペインを入れ替え"; ar="تبديل الأجزاء" }
        let splitEqual = { en="Equal panes"; zh="均分窗格"; ja="均等に分割"; ar="أجزاء متساوية" }
        let splitRatio = { en="Split divider"; zh="分屏分隔条"; ja="分割境界"; ar="فاصل التقسيم" }
        let splitFailed = { en="These windows could not fit the split layout. Split view has ended."; zh="窗口无法适应分屏尺寸，已退出分屏。"; ja="ウィンドウを分割領域に配置できないため、分割を終了しました。"; ar="تعذّر احتواء هذه النوافذ في تخطيط التقسيم. انتهى العرض المقسّم." }
        let top = { en="Top"; zh="顶部"; ja="上部"; ar="أعلى" }
        let showTabTitles = { en="Show tab titles"; zh="显示标签标题"; ja="タブのタイトルを表示"; ar="إظهار عناوين علامات التبويب" }
        let showIconsOnly = { en="Show icons only"; zh="仅显示图标"; ja="アイコンのみ表示"; ar="إظهار الأيقونات فقط" }
        let newTab = { en="New tab"; zh="新建标签"; ja="新しいタブ"; ar="علامة تبويب جديدة" }
        let renameTab = { en="Rename"; zh="重命名"; ja="名前を変更"; ar="إعادة تسمية" }
        let restoreTabName = { en="Reset name"; zh="重置名称"; ja="名前をリセット"; ar="إعادة تعيين الاسم" }
        let pinTab = { en="Pin"; zh="固定"; ja="固定"; ar="تثبيت" }
        let unpinTab = { en="Unpin"; zh="取消固定"; ja="固定を解除"; ar="إلغاء التثبيت" }
        let sortTabs = { en="Sort by app and title"; zh="按应用和标题排序"; ja="アプリとタイトル順に並べ替え"; ar="ترتيب حسب التطبيق والعنوان" }
        let combineTaskbarIcons = { en="Combine taskbar icons"; zh="合并任务栏图标"; ja="タスクバーアイコンをまとめる"; ar="دمج أيقونات شريط المهام" }
        /// Shown greyed out under "Combine taskbar icons" while the group's windows keep their own buttons.
        let combineBlockedPermissions = { en="Not combined: WindowTabs and these windows run with different rights (one as administrator)"
                                          zh="未合并：WindowTabs 与这些窗口的权限不同（其中一方以管理员身份运行）"
                                          ja="まとめられていません: WindowTabs とこれらのウィンドウの権限が異なります (一方が管理者として実行中)"
                                          ar="لم يتم الدمج: يعمل WindowTabs وهذه النوافذ بصلاحيات مختلفة (أحدهما كمسؤول)" }
        let combineBlockedDesktop = { en="Not combined: Windows has not told which virtual desktop these windows are on"
                                      zh="未合并：Windows 未提供这些窗口所在的虚拟桌面"
                                      ja="まとめられていません: これらのウィンドウがどの仮想デスクトップにあるか Windows から取得できません"
                                      ar="لم يتم الدمج: لم يُحدّد Windows سطح المكتب الافتراضي الذي توجد عليه هذه النوافذ" }
        let combineBlockedPlacement = { en="Not combined: Windows did not let the taskbar button move to this virtual desktop"
                                        zh="未合并：Windows 未允许将任务栏按钮移到此虚拟桌面"
                                        ja="まとめられていません: Windows がタスクバー ボタンをこの仮想デスクトップへ移動できませんでした"
                                        ar="لم يتم الدمج: لم يسمح Windows بنقل زر شريط المهام إلى سطح المكتب الافتراضي هذا" }
        /// For the group's own taskbar button, shown while its icons are combined.
        let changeTaskbarIcon = { en="Change taskbar icon…"; zh="更换任务栏图标…"; ja="タスクバーアイコンを変更…"; ar="تغيير أيقونة شريط المهام…" }
        let restoreTaskbarIcon = { en="Restore taskbar icon"; zh="恢复默认任务栏图标"; ja="タスクバーアイコンを元に戻す"; ar="استعادة أيقونة شريط المهام" }
        /// The kinds named with their extensions, so programs are seen to be taken too.
        let taskbarIconFiles = { en="Icon files (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|All files (*.*)|*.*"
                                 zh="图标文件 (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|所有文件 (*.*)|*.*"
                                 ja="アイコン ファイル (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|すべてのファイル (*.*)|*.*"
                                 ar="ملفات الأيقونات (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|كل الملفات (*.*)|*.*" }
        let numberShortcut = { en="Switch tabs by number"; zh="按数字切换标签"; ja="番号でタブを切り替え"; ar="التبديل بين علامات التبويب باستخدام الأرقام" }
        let enableTabs = { en="Use tabs"; zh="使用标签"; ja="タブを使う"; ar="استخدام علامات التبويب" }
        let autoGroupWindows = { en="Auto-group windows"; zh="自动分组"; ja="ウィンドウを自動グループ化"; ar="تجميع النوافذ تلقائيًا" }
        let close = { en="Close"; zh="关闭"; ja="閉じる"; ar="إغلاق" }
        let closeOthers = { en="Close others"; zh="关闭其他"; ja="他を閉じる"; ar="إغلاق علامات التبويب الأخرى" }
        let closeAllOf exe = { en=sprintf "Close all '%s' tabs" exe; zh=sprintf "关闭所有“%s”标签" exe; ja=sprintf "「%s」のタブをすべて閉じる" exe; ar=sprintf "إغلاق جميع علامات تبويب «%s»" exe }
        let closeAll = { en="Close all"; zh="关闭全部"; ja="すべて閉じる"; ar="إغلاق الكل" }
        let settings = { en="Settings…"; zh="设置…"; ja="設定…"; ar="الإعدادات…" }

    /// Menu shown when a tab is dropped with the right mouse button.
    module DropMenu =
        let copy = { en="Copy"; zh="复制"; ja="コピー"; ar="نسخ" }
        let move = { en="Move"; zh="移动"; ja="移動"; ar="نقل" }
        /// Under the strip while files are dragged over an Explorer tab; %1 is the folder's name.
        let moveTo = { en="Move to %1"; zh="移动到 %1"; ja="%1 へ移動"; ar="نقل إلى %1" }
        let copyTo = { en="Copy to %1"; zh="复制到 %1"; ja="%1 へコピー"; ar="نسخ إلى %1" }

    /// Settings window chrome and page titles.
    module SettingsWindow =
        let title = { en="WindowTabs Settings"; zh="WindowTabs 设置"; ja="WindowTabs の設定"; ar="إعدادات WindowTabs" }
        let search = { en="Search"; zh="搜索"; ja="検索"; ar="بحث" }
        let searchSettings = { en="Search settings"; zh="搜索设置"; ja="設定を検索"; ar="البحث في الإعدادات" }
        let searchSuggestions = { en="Search suggestions"; zh="搜索建议"; ja="検索候補"; ar="اقتراحات البحث" }
        /// Where a search result lives when it is on no page: the language picker.
        let sidebar = { en="Sidebar"; zh="侧边栏"; ja="サイドバー"; ar="الشريط الجانبي" }
        let searchResults = { en="Search results"; zh="搜索结果"; ja="検索結果"; ar="نتائج البحث" }
        let noMatches = { en="No matching settings."; zh="没有找到匹配的设置。"; ja="一致する設定はありません。"; ar="لا توجد إعدادات مطابقة." }
        let followWindows = { en="Follow Windows"; zh="跟随系统"; ja="Windows に従う"; ar="حسب Windows" }
        let howToUse = { en="How to use"; zh="使用说明"; ja="使い方"; ar="طريقة الاستخدام" }
        let pageScroll = { en="Page scroll"; zh="页面滚动条"; ja="ページのスクロールバー"; ar="شريط تمرير الصفحة" }

    module Pages =
        let general = { en="General"; zh="常规"; ja="全般"; ar="عام" }
        let appearance = { en="Appearance"; zh="外观"; ja="外観"; ar="المظهر" }
        let shortcuts = { en="Shortcuts"; zh="快捷键"; ja="ショートカット"; ar="الاختصارات" }
        let appRules = { en="App rules"; zh="应用规则"; ja="アプリのルール"; ar="قواعد التطبيقات" }
        let workspaces = { en="Workspaces"; zh="工作区"; ja="ワークスペース"; ar="مساحات العمل" }
        let diagnostics = { en="Support"; zh="支持"; ja="サポート"; ar="الدعم" }
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
        let private colorWords = { en="tab color colour"; zh="标签 颜色 配色"; ja="タブ 色 カラー"; ar="علامة تبويب اللون الألوان" }
        /// The tabs' text, found as such: the settings name it by what it colours.
        let private tabTextWords = { en="tab text title font color colour"; zh="标签文本 标签文字 标签 文本 文字 标题 字体 颜色"; ja="タブの文字 タブ テキスト 文字 タイトル フォント 色"; ar="نص علامة التبويب علامة تبويب نص عنوان خط اللون" }
        let private sizeWords = { en="size"; zh="尺寸 大小"; ja="サイズ 大きさ"; ar="الحجم" }
        let private hotkeyWords = { en="hotkey"; zh="热键"; ja="ホットキー"; ar="مفتاح الاختصار" }
        let theme = { caption={ en="Theme"; zh="主题"; ja="テーマ"; ar="السمة" }
                      description={ en="System follows your Windows app mode."; zh="“跟随系统”与 Windows 的应用模式保持一致。"; ja="「システム」は Windows のアプリ モードに合わせます。"; ar="يتبع خيار النظام وضع التطبيقات في Windows." }
                      keywords={ en="dark mode"; zh="暗色 深色模式"; ja="ダークモード"; ar="الوضع الداكن" } }
        let language = { caption={ en="Language"; zh="语言"; ja="言語"; ar="اللغة" }
                         description={ en="Follow Windows uses your Windows display language."; zh="“跟随系统”使用 Windows 的显示语言。"; ja="「Windows に従う」では Windows の表示言語を使います。"; ar="يستخدم خيار «حسب Windows» لغة العرض في Windows." }
                         keywords={ en="locale"; zh="界面语言"; ja="表示言語"; ar="الإعدادات المحلية" } }
        let launchAtSignIn = { caption={ en="Start with Windows"; zh="随 Windows 启动"; ja="Windows と同時に起動"; ar="التشغيل مع Windows" }
                               description={ en="Runs in the system tray after you sign in."; zh="登录后在系统托盘中运行。"; ja="サインイン後にシステムトレイで実行します。"; ar="يعمل في منطقة الإعلام بعد تسجيل الدخول." }
                               keywords={ en="startup autostart"; zh="开机启动 自启动"; ja="スタートアップ 自動起動"; ar="بدء التشغيل التشغيل التلقائي" } }
        let restoreSession = { caption={ en="Restore the last session after sign-in"; zh="登录后恢复上次会话"; ja="サインイン後に前回のセッションを復元"; ar="استعادة الجلسة الأخيرة بعد تسجيل الدخول" }
                               // One line beside the choice, which says whether apps are started.
                               description={ en="Puts windows back into their groups, places and splits."
                                             zh="将窗口放回原来的分组、位置和分屏。"
                                             ja="ウィンドウを元のグループ、位置、分割に戻します。"
                                             ar="يعيد النوافذ إلى مجموعاتها ومواضعها وتقسيماتها." }
                               keywords={ en="session reboot"; zh="会话 重启"; ja="セッション 再起動"; ar="جلسة إعادة التشغيل" } }
        let enableTabsByDefault = { caption={ en="Use tabs for new apps"; zh="新应用使用标签"; ja="新しいアプリでタブを使う"; ar="استخدام علامات التبويب للتطبيقات الجديدة" }
                                    description={ en="Only apps first discovered after this change. Existing apps keep their tab setting."; zh="只影响此后首次出现的应用。已有应用保留原来的标签设置。"; ja="変更後に初めて検出するアプリに適用します。既存のアプリのタブ設定は維持します。"; ar="ينطبق ذلك على التطبيقات التي يكتشفها البرنامج لأول مرة بعد هذا التغيير فقط. وتحتفظ التطبيقات الموجودة بإعداد علامات التبويب الحالي." }
                                    keywords={ en=""; zh=""; ja=""; ar="" } }
        let dimInactiveGroups = { caption={ en="Dim tabs in inactive groups"; zh="淡化非活动分组的标签"; ja="非アクティブなグループのタブの色を薄くする"; ar="تخفيف لون علامات التبويب في المجموعات غير النشطة" }
                                  description={ en="Make the active group easier to spot."; zh="让当前活动的分组更容易辨认。"; ja="アクティブなグループを見分けやすくします。"; ar="يسهّل تمييز المجموعة النشطة." }
                                  keywords={ en="fade"; zh="变暗"; ja="薄く 暗く"; ar="تعتيم" } }
        let pauseInFullScreen = { caption={ en="Pause while a full-screen app runs"; zh="全屏应用运行时暂停"; ja="全画面アプリの実行中は一時停止"; ar="الإيقاف المؤقت أثناء تشغيل تطبيق بملء الشاشة" }
                                  description={ en="Shortcuts leave their keys to the app; notices wait until it closes or leaves full screen."; zh="快捷键暂不生效，通知等退出全屏后再显示。"; ja="ショートカットはアプリにキーを譲り、通知は全画面が終わってから表示します。"; ar="تترك الاختصارات مفاتيحها للتطبيق، وتنتظر الإشعارات حتى يخرج من ملء الشاشة." }
                                  keywords={ en="fullscreen do not disturb"; zh="全屏 勿扰"; ja="フルスクリーン 応答不可"; ar="ملء الشاشة عدم الإزعاج" } }
        let autoHide = { caption={ en="Auto-hide tabs"; zh="自动隐藏标签"; ja="タブを自動的に隠す"; ar="إخفاء علامات التبويب تلقائيًا" }
                         description={ en="Show a thin strip until hover."; zh="只显示细栏，悬停展开。"; ja="細いバーだけ表示し、ホバーで展開します。"; ar="يبقى شريط رفيع فقط حتى تمرّر المؤشر فوقه." }
                         keywords={ en="collapse"; zh="收起"; ja="折りたたみ"; ar="طيّ" } }
        let iconsOnly = { caption={ en="Show icons only"; zh="仅显示图标"; ja="アイコンのみ表示"; ar="إظهار الأيقونات فقط" }
                          description={ en="Tabs show their icons without titles."; zh="标签只显示图标，不显示标题。"; ja="タブにタイトルを出さず、アイコンだけを表示します。"; ar="تعرض علامات التبويب أيقوناتها دون عناوين." }
                          keywords={ en="compact titles"; zh="紧凑 标题"; ja="コンパクト タイトル"; ar="مضغوط عناوين" } }
        let showTabsOnSwitch = { caption={ en="Show tabs briefly after switching"; zh="切换后短暂显示标签"; ja="切り替え後にタブを一時的に表示"; ar="إظهار علامات التبويب لفترة وجيزة بعد التبديل" }
                                 description={ en="For about a second, so you can see where you are."; zh="显示约一秒，方便确认当前位置。"; ja="約 1 秒間表示して、今いる位置を確認できます。"; ar="لمدة ثانية تقريبًا لتتمكن من معرفة موضعك." }
                                 keywords={ en="peek"; zh="短暂显示"; ja="一時表示"; ar="معاينة" } }
        let tabPosition = { caption={ en="Position tabs"; zh="放置标签"; ja="タブを配置"; ar="موضع علامات التبويب" }
                            description=none
                            keywords={ en="vertical horizontal sidebar left right"; zh="垂直 横向 侧栏 左 右"; ja="縦 横 サイドバー 左 右"; ar="رأسي أفقي شريط جانبي يسار يمين" } }
        let tabColumnWidthHint = { en="For left/right tabs. Drag a column edge to resize one group, down to an icon rail."; zh="用于左／右标签。拖动侧栏边缘可单独调整分组宽度，最窄可只显示图标。"; ja="左・右のタブ用です。列の端をドラッグしてグループの幅を変更できます。最小幅ではアイコンのみを表示します。"; ar="لعلامات التبويب على اليسار أو اليمين. اسحب حافة العمود لتغيير عرض مجموعة واحدة، حتى لا يبقى منها إلا شريط أيقونات." }
        let tabColumnWidth = { caption={ en="Vertical tab width"; zh="垂直标签宽度"; ja="縦タブ幅"; ar="عرض علامات التبويب الرأسية" }
                               description=none
                               keywords={ en="vertical sidebar width size"; zh="垂直 侧栏 宽度 大小"; ja="縦 サイドバー 幅 サイズ"; ar="رأسي شريط جانبي عرض حجم" } }
        let tabAlignment = { caption={ en="Align tabs"; zh="对齐标签"; ja="タブを揃える"; ar="محاذاة علامات التبويب" }
                             description=none
                             keywords={ en="alignment position"; zh="对齐 位置"; ja="配置 位置"; ar="محاذاة موضع" } }
        let combineTaskbarIcons = { caption={ en="Combine taskbar icons per group"; zh="合并每个分组的任务栏图标"; ja="グループのタスクバーアイコンをまとめる"; ar="دمج أيقونات شريط المهام لكل مجموعة" }
                                    description=none
                                    keywords={ en="merge"; zh="合并"; ja="結合"; ar="دمج" } }
        let replaceAltTab = { caption={ en="Use WindowTabs for Alt+Tab"; zh="使用 WindowTabs 窗口切换器"; ja="Alt+Tab に WindowTabs を使う"; ar="استخدام WindowTabs عند الضغط على Alt+Tab" }
                              description={ en="Replaces the Windows switcher."; zh="替换 Windows 自带的窗口切换器。"; ja="Windows 標準のウィンドウ切り替えを置き換えます。"; ar="يحل محل مبدّل النوافذ في Windows." }
                              keywords={ en="task switcher"; zh="任务切换"; ja="タスク切り替え"; ar="مبدّل المهام" } }
        let switcherStyle = { caption={ en="Switcher style"; zh="切换器样式"; ja="スイッチャーの表示"; ar="نمط المبدّل" }
                              description={ en="Large icons in a row, or a list with full window titles."; zh="一排大图标，或显示完整窗口标题的列表。"; ja="大きなアイコンを横に並べるか、ウィンドウ タイトルを一覧で表示します。"; ar="أيقونات كبيرة في صف، أو قائمة تعرض عناوين النوافذ كاملة." }
                              keywords={ en="layout"; zh="布局"; ja="レイアウト"; ar="التخطيط" } }
        let groupWindowsInSwitcher = { caption={ en="Group windows in Alt+Tab"; zh="在 Alt+Tab 中按分组显示"; ja="Alt+Tab でグループをまとめる"; ar="تجميع النوافذ في Alt+Tab" }
                                       description={ en="Each group appears as one item, with the icon its taskbar button has."; zh="每个分组只显示一项，图标与任务栏按钮相同。"; ja="各グループを、タスク バーのボタンと同じアイコンの 1 項目として表示します。"; ar="تظهر كل مجموعة كعنصر واحد بالأيقونة نفسها التي يحملها زر شريط المهام." }
                                       keywords={ en="switcher"; zh="切换器"; ja="切り替え"; ar="المبدّل" } }
        let nextTab = { caption={ en="Switch to next tab"; zh="切换到下一个标签"; ja="次のタブに切り替え"; ar="الانتقال إلى علامة التبويب التالية" }
                        description=none
                        keywords=hotkeyWords }
        let previousTab = { caption={ en="Switch to previous tab"; zh="切换到上一个标签"; ja="前のタブに切り替え"; ar="الانتقال إلى علامة التبويب السابقة" }
                            description=none
                            keywords=hotkeyWords }
        let searchTabs = { caption={ en="Switch windows"; zh="切换窗口"; ja="ウィンドウを切り替え"; ar="التبديل بين النوافذ" }
                           description={ en="Switch to a window by title or app name."; zh="按标题或应用名查找并切换窗口。"; ja="タイトルやアプリ名でウィンドウを探して切り替えます。"; ar="الانتقال إلى نافذة باستخدام عنوانها أو اسم التطبيق." }
                           keywords={ en="find"; zh="查找"; ja="検索"; ar="بحث" } }
        /// Keys inside the palette and the window search only, beside the arrows.
        let listNext = { caption={ en="Move down the list"; zh="在列表中下移"; ja="リストで下へ移動"; ar="النزول في القائمة" }
                         description={ en="Moves the choice down in the command palette and the window search, as ↓ does. Only there."
                                       zh="在命令面板和窗口搜索中把选中项下移，与 ↓ 相同。只在这两处生效。"
                                       ja="コマンドパレットとウィンドウ検索で選択を下へ移動します（↓ と同じ）。そこでのみ有効です。"
                                       ar="ينقل الاختيار لأسفل في لوحة الأوامر وبحث النوافذ، كما يفعل ↓. هناك فقط." }
                         keywords={ en="next down select move palette search"; zh="下一项 向下 选择 移动 面板 搜索"; ja="次 下 選択 移動 パレット 検索"; ar="التالي أسفل تحديد نقل لوحة بحث" } }
        let listPrevious = { caption={ en="Move up the list"; zh="在列表中上移"; ja="リストで上へ移動"; ar="الصعود في القائمة" }
                             description={ en="Moves the choice up in the command palette and the window search, as ↑ does. Only there."
                                           zh="在命令面板和窗口搜索中把选中项上移，与 ↑ 相同。只在这两处生效。"
                                           ja="コマンドパレットとウィンドウ検索で選択を上へ移動します（↑ と同じ）。そこでのみ有効です。"
                                           ar="ينقل الاختيار لأعلى في لوحة الأوامر وبحث النوافذ، كما يفعل ↑. هناك فقط." }
                             keywords={ en="previous up select move palette search"; zh="上一项 向上 选择 移动 面板 搜索"; ja="前 上 選択 移動 パレット 検索"; ar="السابق أعلى تحديد نقل لوحة بحث" } }
        let commandPalette = { caption={ en="Command palette"; zh="命令面板"; ja="コマンドパレット"; ar="لوحة الأوامر" }
                               description={ en="Run any command or setting by name."
                                             zh="按名称运行任意命令或设置。"
                                             ja="名前でコマンドや設定を実行します。"
                                             ar="شغّل أي أمر أو إعداد باسمه." }
                               keywords={ en="actions search everywhere"; zh="动作 全局搜索"; ja="アクション 検索"; ar="إجراءات بحث شامل" } }
        let newTab = { caption={ en="New tab"; zh="新建标签"; ja="新しいタブ"; ar="علامة تبويب جديدة" }
                       description={ en="Starts the current tab's app again."
                                     zh="再次启动当前标签的应用。"
                                     ja="現在のタブのアプリをもう一度起動します。"
                                     ar="يشغّل نسخة جديدة من تطبيق علامة التبويب الحالية." }
                       keywords={ en="new window"; zh="新建窗口"; ja="新しいウィンドウ"; ar="نافذة جديدة" } }
        let closeTab = { caption={ en="Close tab"; zh="关闭标签"; ja="タブを閉じる"; ar="إغلاق علامة التبويب" }
                         description={ en="Closes the window of the tab in front."
                                       zh="关闭当前标签的窗口。"
                                       ja="前面のタブのウィンドウを閉じます。"
                                       ar="يغلق نافذة علامة التبويب الأمامية." }
                         keywords={ en="close window"; zh="关闭窗口"; ja="ウィンドウを閉じる"; ar="إغلاق النافذة" } }
        let private workspaceWords = { en="workspace layout"; zh="工作区 布局"; ja="ワークスペース 配置"; ar="مساحة العمل التخطيط" }
        let saveWorkspace = { caption={ en="Save workspace"; zh="保存工作区"; ja="ワークスペースを保存"; ar="حفظ مساحة العمل" }
                              description={ en="Saves the windows as they are as a new workspace, as Save layout does."
                                            zh="将当前窗口保存为新的工作区，与“保存布局”相同。"
                                            ja="現在のウィンドウを新しいワークスペースとして保存します（「配置を保存」と同じ）。"
                                            ar="يحفظ النوافذ بحالتها الحالية كمساحة عمل جديدة، كما يفعل «حفظ التخطيط»." }
                              keywords=workspaceWords }
        let restoreWorkspace = { caption={ en="Restore workspace"; zh="恢复工作区"; ja="ワークスペースを復元"; ar="استعادة مساحة العمل" }
                                 description={ en="Restores the workspace last saved or restored."
                                               zh="恢复最近一次保存或恢复的工作区。"
                                               ja="最後に保存または復元したワークスペースを復元します。"
                                               ar="يستعيد آخر مساحة عمل تم حفظها أو استعادتها." }
                                 keywords=workspaceWords }
        let private splitWords = { en="split view pane"; zh="分屏 窗格"; ja="分割 ペイン"; ar="عرض مقسّم جزء" }
        let editGroups = { caption={ en="Edit groups…"; zh="编辑分组…"; ja="グループを編集…"; ar="تحرير المجموعات…" }
                           description={ en="A picture of the group in front to rearrange its tabs and panes, applied when saved."
                                         zh="以图示方式编辑当前分组的标签和窗格，保存后一次性生效。"
                                         ja="手前のグループのタブとペインを図で編集し、保存時にまとめて適用します。"
                                         ar="صورة للمجموعة الأمامية لإعادة ترتيب علاماتها وأجزائها، تُطبَّق عند الحفظ." }
                           keywords={ en="group editor layout rearrange organize"; zh="分组编辑器 布局 整理 重排"; ja="グループ エディター レイアウト 整理"; ar="محرر المجموعات تخطيط ترتيب" } }
        let splitPickRight = { caption={ en="Window on the right…"; zh="在右侧放入窗口…"; ja="右側にウィンドウを配置…"; ar="نافذة على اليمين…" }
                               description={ en="Lists the windows to put beside the tab in front."; zh="列出可放到当前标签旁边的窗口。"; ja="前面のタブの隣に置くウィンドウを一覧表示します。"; ar="يعرض النوافذ التي يمكن وضعها بجانب علامة التبويب الأمامية." }
                               keywords=splitWords }
        let splitPickBelow = { caption={ en="Window below…"; zh="在下方放入窗口…"; ja="下側にウィンドウを配置…"; ar="نافذة في الأسفل…" }
                               description=none
                               keywords=splitWords }
        let splitPickLeft = { caption={ en="Window on the left…"; zh="在左侧放入窗口…"; ja="左側にウィンドウを配置…"; ar="نافذة على اليسار…" }
                              description=none
                              keywords=splitWords }
        let splitPickAbove = { caption={ en="Window above…"; zh="在上方放入窗口…"; ja="上側にウィンドウを配置…"; ar="نافذة في الأعلى…" }
                               description=none
                               keywords=splitWords }
        let splitFocusLeft = { caption={ en="Focus the pane to the left"; zh="焦点切到左边的窗格"; ja="左のペインにフォーカス"; ar="التركيز على الجزء الأيسر" }
                               description=none
                               keywords=splitWords }
        let splitFocusRight = { caption={ en="Focus the pane to the right"; zh="焦点切到右边的窗格"; ja="右のペインにフォーカス"; ar="التركيز على الجزء الأيمن" }
                                description=none
                                keywords=splitWords }
        let splitFocusUp = { caption={ en="Focus the pane above"; zh="焦点切到上边的窗格"; ja="上のペインにフォーカス"; ar="التركيز على الجزء الأعلى" }
                             description=none
                             keywords=splitWords }
        let splitFocusDown = { caption={ en="Focus the pane below"; zh="焦点切到下边的窗格"; ja="下のペインにフォーカス"; ar="التركيز على الجزء الأسفل" }
                               description=none
                               keywords=splitWords }
        let switchTabsByNumber = { caption={ en="Switch tabs by number"; zh="按数字切换标签"; ja="番号でタブを切り替え"; ar="التبديل بين علامات التبويب باستخدام الأرقام" }
                                   description={ en="Hold a modifier key and press 1–9."; zh="按住修饰键再按 1–9。"; ja="修飾キーを押しながら 1～9 を押します。"; ar="اضغط باستمرار على مفتاح تعديل ثم اضغط على رقم من 1 إلى 9." }
                                   keywords={ en="digit"; zh="数字"; ja="数字"; ar="رقم" } }
        let numberShortcut = { caption={ en="Modifier key for 1–9"; zh="数字键搭配的修饰键"; ja="1～9 と組み合わせるキー"; ar="مفتاح التعديل للأرقام من 1 إلى 9" }
                               description=none
                               keywords={ en="number"; zh="数字"; ja="番号"; ar="رقم" } }
        let enableNumberLeader = { caption={ en="Switch tabs with a leader key"; zh="用引导键切换标签"; ja="リーダーキーでタブを切り替え"; ar="التبديل بين علامات التبويب بمفتاح تمهيدي" }; description={ en="Press the leader, then a selection key. Esc cancels."; zh="先按引导键，再按选择键。Esc 取消。"; ja="リーダーキーの後、選択キーを押します。Esc で解除。"; ar="اضغط المفتاح التمهيدي ثم مفتاح الاختيار. اضغط Esc للإلغاء." }; keywords=none }
        let leaderKeys = { caption={en="Selection keys";zh="选择键";ja="選択キー";ar="مفاتيح الاختيار"}; description={en="Use unique letters or digits, in tab order.";zh="按标签顺序，使用不重复的字母或数字。";ja="タブ順に、重複しない英字または数字を指定します。";ar="استخدم أحرفًا أو أرقامًا غير مكررة حسب ترتيب علامات التبويب."}; keywords={en="leader keys letters home row";zh="引导键 字母 主键区";ja="リーダーキー 英字 ホームポジション";ar="مفتاح تمهيدي أحرف الصف الأوسط"} }
        let leaderKeyPresets = [| {en="123456789";zh="123456789";ja="123456789";ar="123456789"}; {en="QWERTYUIOP";zh="QWERTYUIOP";ja="QWERTYUIOP";ar="QWERTYUIOP"}; {en="ASDFGHJKL;";zh="ASDFGHJKL;";ja="ASDFGHJKL;";ar="ASDFGHJKL;"} |]
        let invalidLeaderKeys = {en="Use at least one unique letter, digit or unshifted punctuation key.";zh="请使用至少一个不重复的字母、数字或无需 Shift 的标点键。";ja="重複しない英字、数字、Shift 不要の記号を 1 つ以上指定してください。";ar="استخدم حرفًا أو رقمًا أو علامة ترقيم واحدة على الأقل، غير مكررة ولا تحتاج إلى Shift."}
        let numberLeader = { caption={ en="Leader key"; zh="引导键"; ja="リーダーキー"; ar="المفتاح التمهيدي" }; description=none; keywords=none }
        let tabColors = { en="Color"; zh="颜色"; ja="色"; ar="اللون" }
        let customTabColor = { en="Custom…"; zh="自定义…"; ja="カスタム…"; ar="مخصص…" }
        let clearTabColor = { en="Clear color"; zh="清除颜色"; ja="色を解除"; ar="مسح اللون" }
        let rememberTabColor = { en="Remember color for this app"; zh="为此应用记住颜色"; ja="このアプリの色を記憶"; ar="تذكّر اللون لهذا التطبيق" }
        let tabColorNames = [|
            { en="Grey";zh="灰色";ja="グレー";ar="رمادي" }; { en="Blue";zh="蓝色";ja="青";ar="أزرق" }
            { en="Red";zh="红色";ja="赤";ar="أحمر" }; { en="Yellow";zh="黄色";ja="黄";ar="أصفر" }
            { en="Green";zh="绿色";ja="緑";ar="أخضر" }; { en="Pink";zh="粉色";ja="ピンク";ar="وردي" }
            { en="Purple";zh="紫色";ja="紫";ar="أرجواني" }; { en="Cyan";zh="青色";ja="シアン";ar="سماوي" }
            { en="Orange";zh="橙色";ja="オレンジ";ar="برتقالي" }; { en="Lavender";zh="薰衣草色";ja="ラベンダー";ar="خزامي" }
            { en="Olive";zh="橄榄色";ja="オリーブ";ar="زيتي" }; { en="Rose";zh="玫红色";ja="ローズ";ar="وردي داكن" }
            { en="Lime";zh="青柠色";ja="ライム";ar="ليموني" }; { en="Navy";zh="藏青色";ja="ネイビー";ar="كحلي" }
            { en="Teal";zh="蓝绿色";ja="ティール";ar="أزرق مخضر" }; { en="Coral";zh="珊瑚色";ja="コーラル";ar="مرجاني" } |]
        let tabColorMode = { caption={ en="Tab color coding"; zh="标签着色"; ja="タブの色分け"; ar="تلوين علامات التبويب" }
                             description={ en="Auto-assign colors by window or app."
                                           zh="按窗口或应用自动分配颜色。"
                                           ja="ウィンドウ・アプリ別に色を自動で割り当てます。"
                                           ar="تعيين الألوان تلقائيًا حسب النافذة أو التطبيق." }
                             keywords={ en="colorize rainbow"; zh="着色 彩虹"; ja="色分け"; ar="تلوين قوس قزح" } }
        let colorsOff = { en="Off"; zh="关闭"; ja="オフ"; ar="إيقاف" }
        let byWindow = { en="By window"; zh="按窗口"; ja="ウィンドウ別"; ar="حسب النافذة" }
        let byApp = { en="By app"; zh="按应用"; ja="アプリ別"; ar="حسب التطبيق" }
        let tabColorMenuHint = { en="Change it for one tab from the tab menu. That color comes first, and shows even when this is off."
                                 zh="可在标签菜单中为单个标签选色。它优先于这里的设置，关闭时也有效。"
                                 ja="タブメニューでタブごとに色を選べます。こちらの設定より優先され、オフでも表示されます。"
                                 ar="غيّر لون علامة تبويب واحدة من قائمتها. تكون الأولوية لهذا اللون ويظهر حتى عند إيقاف هذا الخيار." }
        let tabColorStyle = { caption={ en="Show colors as"; zh="着色方式"; ja="色の付け方"; ar="طريقة عرض الألوان" }
                              description=none
                              keywords={ en="stripe fill"; zh="色条 填充"; ja="ライン 塗りつぶし"; ar="شريط تعبئة" } }
        let stripe = { en="Stripe"; zh="色条"; ja="色のライン"; ar="شريط" }
        let fill = { en="Fill"; zh="填充"; ja="塗りつぶし"; ar="تعبئة" }
        let numberShortcutMenuHint = { en="Enable or disable it for one app from the tab menu."; zh="可在标签右键菜单中为单个应用启用或禁用数字快捷键。"; ja="タブメニューでアプリごとに番号ショートカットを有効・無効にできます。"; ar="يمكن تمكينه أو تعطيله لتطبيق واحد من قائمة علامة التبويب." }
        let numberShortcutFor name = { en=sprintf "Switch tabs by number in %s" name; zh=sprintf "在 %s 中按数字切换标签" name; ja=sprintf "%s のタブを番号で切り替え" name; ar=sprintf "التبديل بين علامات التبويب بالأرقام في %s" name }
        /// The choices of Switch tabs by number: off, or the key held with 1–9.
        let numberShortcutOff = { en="Off"; zh="关闭"; ja="オフ"; ar="إيقاف" }
        let numberShortcutWith key = { en=sprintf "%s + 1–9" key; zh=sprintf "%s + 1–9" key; ja=sprintf "%s + 1～9" key; ar=sprintf "%s + 1–9" key }
        let numberShortcutCtrl = { en="Ctrl"; zh="Ctrl"; ja="Ctrl"; ar="Ctrl" }
        let numberShortcutAlt = { en="Alt"; zh="Alt"; ja="Alt"; ar="Alt" }
        let activateOnHover = { caption={ en="Switch tabs on hover"; zh="悬停切换标签"; ja="ホバーでタブを切り替え"; ar="التبديل بين علامات التبويب عند تمرير المؤشر" }
                                description={ en="Rest the pointer on a tab instead of clicking."; zh="鼠标停在标签上即可，无需点击。"; ja="クリックしなくても、タブにポインターを置くだけで済みます。"; ar="ضع المؤشر فوق علامة تبويب بدلًا من النقر عليها." }
                                keywords={ en="mouse over"; zh="悬停"; ja="マウスオーバー"; ar="تمرير المؤشر" } }
        let tabHoverPreview = { caption={ en="Preview a tab's window on hover"; zh="悬停时预览标签窗口"; ja="ホバーでタブのウィンドウをプレビュー"; ar="معاينة نافذة علامة التبويب عند تمرير المؤشر" }
                                description={ en="Rest the pointer on a tab behind to see its window, with its full title."
                                              zh="鼠标停在后面的标签上，显示它的窗口截图和完整标题。"
                                              ja="背面のタブにポインターを置くと、そのウィンドウと完全なタイトルを表示します。"
                                              ar="ضع المؤشر فوق علامة تبويب خلفية لرؤية نافذتها مع عنوانها الكامل." }
                                keywords={ en="thumbnail screenshot picture hover card"; zh="缩略图 截图 预览 悬停"; ja="サムネイル スクリーンショット プレビュー"; ar="صورة مصغرة لقطة شاشة معاينة" } }
        let shiftScroll = { caption={ en="Switch tabs with Shift + scroll"; zh="用 Shift + 滚轮切换标签"; ja="Shift + スクロールでタブを切り替え"; ar="التبديل بين علامات التبويب باستخدام Shift والتمرير" }
                            description={ en="Works anywhere over a grouped window. Over the tab bar, Shift isn't needed."
                                          zh="在分组窗口的任意位置均可使用；在标签栏上滚动时无需按 Shift。"
                                          ja="グループ化されたウィンドウ上ならどこでも使えます。タブバー上では Shift は不要です。"
                                          ar="يعمل في أي مكان فوق نافذة مجمّعة. وفوق شريط علامات التبويب لا حاجة إلى Shift." }
                            keywords={ en="wheel"; zh="滚轮"; ja="ホイール"; ar="عجلة التمرير" } }
        let ctrlScroll = { caption={ en="Move tabs with Ctrl + scroll"; zh="用 Ctrl + 滚轮移动标签"; ja="Ctrl + スクロールでタブを移動"; ar="تحريك علامات التبويب باستخدام Ctrl والتمرير" }
                           description={ en="Over the tab bar, moves the current tab left or right."
                                         zh="在标签栏上滚动，将当前标签向左或向右移动。"
                                         ja="タブバー上で、現在のタブを左右に移動します。"
                                         ar="فوق شريط علامات التبويب، ينقل علامة التبويب الحالية إلى اليسار أو اليمين." }
                           keywords={ en="wheel reorder"; zh="滚轮 排序"; ja="ホイール 並べ替え"; ar="عجلة التمرير لإعادة الترتيب" } }
        let middleClickClose = { caption={ en="Middle-click closes a tab"; zh="按下滚轮关闭标签"; ja="中クリックでタブを閉じる"; ar="الإغلاق بالنقر الأوسط" }
                                 description={ en="Press the wheel on a tab to close its window."
                                               zh="在标签上按下滚轮，关闭它的窗口。"
                                               ja="タブ上でホイールを押すと、そのウィンドウを閉じます。"
                                               ar="اضغط بعجلة الفأرة على علامة التبويب لإغلاق نافذتها." }
                                 keywords={ en="wheel click"; zh="中键 滚轮"; ja="ホイール クリック"; ar="النقر بعجلة الفأرة" } }
        let tabTextColor = { caption={ en="Text and close button"; zh="文字与关闭按钮"; ja="文字と閉じるボタン"; ar="النص وزر الإغلاق" }; description=none; keywords=tabTextWords }
        let tabOpacity = { caption={ en="Tab opacity (%)"; zh="标签不透明度（%）"; ja="タブの不透明度 (%)"; ar="عتامة علامات التبويب (%)" }
                           description={ en="Below 100 the tabs are see-through, and solid under the pointer."; zh="低于 100 时标签半透明，鼠标指向时不透明。"; ja="100 未満でタブが半透明になり、ポインターを重ねると不透明になります。"; ar="أقل من 100 تصبح علامات التبويب شفافة، ومعتمة تحت المؤشر." }
                           keywords={ en="transparency transparent see-through alpha"; zh="透明度 透明 半透明"; ja="透明度 透明 半透明"; ar="شفافية شفاف" } }
        let tabTextBold = { caption={ en="Bold tab text"; zh="标签文字加粗"; ja="タブの文字を太字にする"; ar="نص علامات التبويب بخط عريض" }
                            description={ en="Which tab titles show in a heavier weight."; zh="哪些标签的标题以粗体显示。"; ja="どのタブのタイトルを太字で表示するか。"; ar="عناوين علامات التبويب التي تظهر بخط أثقل." }
                            keywords=tabTextWords }
        let tabCloseButton = { caption={ en="Show close buttons"; zh="显示关闭按钮"; ja="閉じるボタンを表示"; ar="إظهار أزرار الإغلاق" }
                               description={ en="Which tabs show the × that closes their window."
                                             zh="哪些标签显示用来关闭窗口的 × 按钮。"
                                             ja="ウィンドウを閉じる × をどのタブに表示するか。"
                                             ar="علامات التبويب التي يظهر عليها زر × لإغلاق نافذتها." }
                               keywords={ en="close button x hide"; zh="关闭按钮 叉 隐藏"; ja="閉じるボタン × 非表示"; ar="زر الإغلاق إخفاء" } }
        let closeOnActiveAndHover = { en="Active and on hover"; zh="当前标签和悬停时"; ja="アクティブとホバー時"; ar="النشطة وعند التمرير" }
        let closeOnActive = { en="Active tab only"; zh="仅当前标签"; ja="アクティブなタブのみ"; ar="علامة التبويب النشطة فقط" }
        let boldAllTabs = { en="All tabs"; zh="所有标签"; ja="すべてのタブ"; ar="كل علامات التبويب" }
        let tabNormalBgColor = { caption={ en="Inactive tab"; zh="非活动标签"; ja="非アクティブなタブ"; ar="علامة تبويب غير نشطة" }; description=none; keywords=colorWords }
        let tabActiveBgColor = { caption={ en="Active tab"; zh="活动标签"; ja="アクティブなタブ"; ar="علامة تبويب نشطة" }; description=none; keywords=colorWords }
        let tabHighlightBgColor = { caption={ en="Hovered tab"; zh="悬停标签"; ja="ホバー中のタブ"; ar="علامة التبويب التي يشير إليها المؤشر" }; description=none; keywords=colorWords }
        let tabFlashBgColor = { caption={ en="Tab asking for attention"; zh="请求注意的标签"; ja="注意を求めるタブ"; ar="علامة تبويب تطلب الانتباه" }
                                description={ en="While its window flashes, as its taskbar button does."; zh="其窗口闪烁提醒时（如任务栏按钮闪烁）。"; ja="ウィンドウが点滅して知らせている間（タスクバーのボタンと同様）。"; ar="أثناء وميض نافذتها، كما يومض زرها في شريط المهام." }
                                keywords={ en="tab flash attention alert danger notify blink color colour"; zh="标签 闪烁 提醒 警示 注意 危险 颜色"; ja="タブ 点滅 注意 警告 通知 色"; ar="علامة تبويب وميض انتباه تنبيه خطر لون" } }
        let tabStyle = { caption={ en="Tab style"; zh="标签样式"; ja="タブのスタイル"; ar="نمط علامة التبويب" }
                         description={ en="How the active tab stands out from the rest."; zh="活动标签以何种方式与其他标签区分。"; ja="アクティブなタブをほかのタブとどう見分けるかを選びます。"; ar="كيفية تمييز علامة التبويب النشطة عن البقية." }
                         keywords={ en="shape folder pill"; zh="形状 文件夹 胶囊"; ja="形 フォルダー ピル"; ar="شكل مجلد كبسولة" } }
        let tabHeightHint = { en="Left/right tabs have a minimum height of 32 px."; zh="左／右标签的实际高度最低为 32px。"; ja="左・右のタブの最小高さは 32px です。"; ar="يبلغ الحد الأدنى لارتفاع علامات التبويب الجانبية 32 بكسل." }
        let tabHeight = { caption={ en="Tab height"; zh="标签高度"; ja="タブの高さ"; ar="ارتفاع علامة التبويب" }; description=none; keywords=sizeWords }
        let tabMaxWidth = { caption={ en="Maximum tab width"; zh="标签最大宽度"; ja="タブの最大幅"; ar="أقصى عرض لعلامة التبويب" }; description=none; keywords=sizeWords }
        let tabOverlap = { caption={ en="Tab spacing"; zh="标签间距"; ja="タブの間隔"; ar="المسافة بين علامات التبويب" }
                           description=none
                           keywords={ en="gap"; zh="间隙"; ja="隙間"; ar="فجوة" } }
        let tabIndent = { caption={ en="Side margin"; zh="两侧边距"; ja="左右の余白"; ar="الهامش الجانبي" }
                          description={ en="Distance from the window edges."
                                        zh="与窗口两侧的距离。"
                                        ja="ウィンドウの左右端からの距離です。"
                                        ar="المسافة من حواف النافذة." }
                          keywords={ en="indent padding"; zh="缩进 边距"; ja="インデント 余白"; ar="مسافة بادئة حشو" } }
        let appRules = { caption=Pages.appRules
                         description={ en="Choose apps for tabs and automatic grouping."; zh="选择启用标签和自动分组的应用。"; ja="タブと自動グループ化を使うアプリを選択します。"; ar="اختر التطبيقات التي تستخدم علامات التبويب والتجميع التلقائي." }
                         keywords={ en="process program"; zh="程序 进程"; ja="プロセス プログラム"; ar="عملية برنامج" } }
        let workspaces = { caption=Pages.workspaces
                           description={ en="Save and restore window layouts."; zh="保存和恢复窗口布局。"; ja="ウィンドウの配置を保存して復元します。"; ar="حفظ تخطيطات النوافذ واستعادتها." }
                           keywords={ en="session"; zh="会话"; ja="セッション"; ar="جلسة" } }
        let diagnostics = { caption=Pages.diagnostics
                            description={ en="Troubleshooting report and where to get help."; zh="故障排查报告与获取帮助的途径。"; ja="トラブルシューティング用のレポートとヘルプの入手先。"; ar="تقرير استكشاف الأخطاء وإصلاحها ومكان الحصول على المساعدة." }
                            keywords={ en="about version log"; zh="关于 版本 日志"; ja="バージョン ログ"; ar="حول الإصدار السجل" } }
        let settingsLocation = { caption={ en="Location"; zh="位置"; ja="場所"; ar="الموقع" }
                                 description=none
                                 keywords={ en="folder path portable"; zh="文件夹 路径 便携"; ja="フォルダー パス ポータブル"; ar="مجلد مسار محمول" } }
        let settingsBackup = { caption={ en="Back up and restore"; zh="备份与恢复"; ja="バックアップと復元"; ar="النسخ الاحتياطي والاستعادة" }
                               description={ en="Export all settings to a file, or import them and restart."; zh="将所有设置导出为文件，或从文件导入并重新启动。"; ja="すべての設定をファイルにエクスポートするか、インポートして再起動します。"; ar="تصدير جميع الإعدادات إلى ملف، أو استيرادها ثم إعادة التشغيل." }
                               keywords={ en="export import"; zh="导出 导入"; ja="エクスポート インポート"; ar="تصدير استيراد" } }
        let settingsReset = { caption={ en="Reset to defaults"; zh="恢复默认设置"; ja="既定値に戻す"; ar="إعادة التعيين إلى الإعدادات الافتراضية" }
                              description={ en="Then restarts. App rules and workspaces can be kept."; zh="完成后重新启动，可保留应用规则和工作区。"; ja="その後再起動します。アプリのルールとワークスペースは残せます。"; ar="ثم يُعاد تشغيل البرنامج. يمكن الاحتفاظ بقواعد التطبيقات ومساحات العمل." }
                              keywords={ en="factory"; zh="恢复默认 出厂"; ja="初期化"; ar="إعدادات المصنع" } }

    /// What letting go of a dragged tab here would do, on the preview.
    /// Notices: a bold title naming what is restored or saved (the last session, or a workspace
    /// by its name), and below it what is happening to it.
    module Notices =
        let lastSession = { en="Last session"; zh="上次会话"; ja="前回のセッション"; ar="الجلسة الأخيرة" }
        let restoring = { en="Restoring…"; zh="正在恢复…"; ja="復元しています…"; ar="جارٍ الاستعادة…" }
        let launching count =
            { en=(if count=1 then "Starting an app…" else sprintf "Starting %d apps…" count)
              zh=sprintf "正在启动 %d 个应用…" count
              ja=sprintf "アプリを %d 個起動しています…" count
              ar=sprintf "جارٍ تشغيل %d من التطبيقات…" count }
        let restored restored missing =
            { en=(if restored=1 then "Restored 1 window" else sprintf "Restored %d windows" restored)+(if missing=0 then "" else sprintf ", %d not found" missing)
              zh=(sprintf "已恢复 %d 个窗口" restored)+(if missing=0 then "" else sprintf "，%d 个未找到" missing)
              ja=(sprintf "%d 個のウィンドウを復元しました" restored)+(if missing=0 then "" else sprintf "（%d 個は見つかりません）" missing)
              ar=(sprintf "تمت استعادة %d نافذة" restored)+(if missing=0 then "" else sprintf "، %d غير موجودة" missing) }
        /// An error notice, with the reason the system gave.
        let restoreFailed reason =
            { en=sprintf "Could not restore. %s" reason; zh=sprintf "恢复失败。%s" reason; ja=sprintf "復元できませんでした。%s" reason; ar=sprintf "تعذّرت الاستعادة. %s" reason }
        let saveFailed reason =
            { en=sprintf "Could not save. %s" reason; zh=sprintf "保存失败。%s" reason; ja=sprintf "保存できませんでした。%s" reason; ar=sprintf "تعذّر الحفظ. %s" reason }
        let saved windows =
            { en=(if windows=1 then "Saved with 1 window" else sprintf "Saved with %d windows" windows)
              zh=sprintf "已保存，共 %d 个窗口" windows
              ja=sprintf "保存しました（ウィンドウ %d 個）" windows
              ar=sprintf "تم الحفظ (%d نافذة)" windows }
        let deleted = { en="Deleted"; zh="已删除"; ja="削除しました"; ar="تم الحذف" }
        /// Why a saved window was not found, after its app in the restore notice and log.
        let unknownApp = { en="Saved without its app"; zh="保存时未记录应用"; ja="アプリが記録されていません"; ar="حُفظت دون تطبيقها" }
        /// The reason, then what to do about it as a sentence of its own, so the two do not read as one.
        let storeApp = { en="Store app, cannot be started. Please open it and restore again"; zh="应用商店应用无法自动启动。请手动打开后再恢复"; ja="ストア アプリは起動できません。開いてから再度復元してください"; ar="تطبيق من المتجر لا يمكن تشغيله. من فضلك افتحه ثم استعد مجددًا" }
        let noWindowLeft = { en="App is open, but no window of it matches"; zh="应用已打开，但没有可对应的窗口"; ja="アプリは開いていますが、対応するウィンドウがありません"; ar="التطبيق مفتوح، لكن لا نافذة منه تطابقها" }
        let noWindowAfterStart = { en="App started, but showed no window in time"; zh="应用已启动，但未及时出现窗口"; ja="アプリは起動しましたが、時間内にウィンドウが現れません"; ar="بدأ التطبيق، لكن لم تظهر نافذته في الوقت المحدد" }
        let startFailed reason = { en=sprintf "App would not start: %s" reason; zh=sprintf "应用启动失败：%s" reason; ja=sprintf "アプリを起動できません: %s" reason; ar=sprintf "تعذّر تشغيل التطبيق: %s" reason }
        let appGone = { en="App file no longer exists"; zh="应用文件已不存在"; ja="アプリのファイルがありません"; ar="ملف التطبيق لم يعد موجودًا" }
        let notRunning = { en="App is not running"; zh="应用未运行"; ja="アプリが実行されていません"; ar="التطبيق لا يعمل" }
        let more count = { en=sprintf "…and %d more" count; zh=sprintf "…另有 %d 个" count; ja=sprintf "…ほか %d 個" count; ar=sprintf "…و%d أخرى" count }
        /// The apps whose windows came back, each with how many, at the end of the restore notice.
        let restoredApps apps = { en="Restored: "+apps; zh="已恢复："+apps; ja="復元済み: "+apps; ar="تمت الاستعادة: "+apps }

    module General =
        /// The choices of Restore the last session at start.
        let sessionOff = { en="Off"; zh="关闭"; ja="オフ"; ar="إيقاف" }
        /// Short, so the choice leaves its row's description room for one line.
        let sessionWindows = { en="Windows only"; zh="仅窗口"; ja="ウィンドウのみ"; ar="النوافذ فقط" }
        let sessionApps = { en="Windows and missing apps"; zh="窗口和缺少的应用"; ja="ウィンドウと不足アプリ"; ar="النوافذ والتطبيقات الناقصة" }
        let settingsFile = { en="Settings file"; zh="设置文件"; ja="設定ファイル"; ar="ملف الإعدادات" }
        let openFolder = { en="Open folder"; zh="打开文件夹"; ja="フォルダーを開く"; ar="فتح المجلد" }
        let export = { en="Export…"; zh="导出…"; ja="エクスポート…"; ar="تصدير…" }
        let import = { en="Import…"; zh="导入…"; ja="インポート…"; ar="استيراد…" }
        let settingsFileHelp = { en="If the WindowTabs.exe folder has a settings file, WindowTabs uses it (portable mode); otherwise it uses the one in AppData. Older versions named it WindowTabsSettings.txt; it is now WindowTabsSettings.json, and an old .txt file is still read until the .json one exists."
                                 zh="如果 WindowTabs.exe 所在文件夹里有设置文件，WindowTabs 就使用它（便携模式）；否则使用 AppData 中的。旧版本的设置文件名为 WindowTabsSettings.txt，现在改为 WindowTabsSettings.json；在 .json 文件生成之前，仍会读取旧的 .txt 文件。"
                                 ja="WindowTabs.exe のフォルダーに設定ファイルがあれば、WindowTabs はそれを使います（ポータブル モード）。なければ AppData のものを使います。以前のバージョンでは WindowTabsSettings.txt という名前でしたが、現在は WindowTabsSettings.json です。.json ファイルができるまでは、古い .txt ファイルも読み込まれます。"
                                 ar="إذا وُجد ملف إعدادات بجوار WindowTabs.exe فسيستخدمه البرنامج (الوضع المحمول)، وإلا فسيستخدم الملف الموجود في AppData. كان اسمه في الإصدارات القديمة WindowTabsSettings.txt، وأصبح الآن WindowTabsSettings.json، وسيظل ملف .txt القديم يُقرأ حتى إنشاء ملف .json." }
        let exported = { en="Settings exported."; zh="设置已导出。"; ja="設定をエクスポートしました。"; ar="تم تصدير الإعدادات." }
        let reset = { en="Reset…"; zh="重置…"; ja="リセット…"; ar="إعادة تعيين…" }
        let resetConfirm = { en="Reset"; zh="重置"; ja="リセット"; ar="إعادة تعيين" }
        let resetTitle = { en="Reset settings"; zh="重置设置"; ja="設定のリセット"; ar="إعادة تعيين الإعدادات" }
        let resetMessage = { en="Restore every setting to its default and restart WindowTabs? The current settings are saved first, so you can import them back."
                             zh="将所有设置恢复为默认值并重新启动 WindowTabs？当前设置会先保存下来，之后可以再导入。"
                             ja="すべての設定を既定値に戻して WindowTabs を再起動しますか？現在の設定は先に保存されるので、後でインポートして戻せます。"
                             ar="هل تريد إعادة جميع الإعدادات إلى قيمها الافتراضية وإعادة تشغيل WindowTabs؟ ستُحفظ الإعدادات الحالية أولًا لتتمكن من استيرادها لاحقًا." }
        let resetAlsoClear = { en="Also clear:"; zh="同时清除："; ja="次も消去する："; ar="مسح أيضًا:"}
        let resetRestarting path =
            { en=sprintf "Settings were reset. WindowTabs will now restart.\n\nYour previous settings were saved to:\n%s" path
              zh=sprintf "设置已重置。WindowTabs 将重新启动。\n\n原来的设置已保存到：\n%s" path
              ja=sprintf "設定をリセットしました。WindowTabs を再起動します。\n\n以前の設定の保存先:\n%s" path
              ar=sprintf "تمت إعادة تعيين الإعدادات. سيُعاد تشغيل WindowTabs الآن.\n\nتم حفظ الإعدادات السابقة في:\n%s" path }
        let importTitle = { en="Import settings"; zh="导入设置"; ja="設定のインポート"; ar="استيراد الإعدادات" }
        let notSettingsFile = { en="This file does not contain WindowTabs settings."; zh="此文件不包含 WindowTabs 设置。"; ja="このファイルには WindowTabs の設定が含まれていません。"; ar="لا يحتوي هذا الملف على إعدادات WindowTabs." }
        let importedRestarting path =
            { en=sprintf "Settings imported. WindowTabs will now restart to apply them.\n\nYour previous settings were saved to:\n%s" path
              zh=sprintf "设置已导入。WindowTabs 将重新启动以应用这些设置。\n\n原来的设置已保存到：\n%s" path
              ja=sprintf "設定をインポートしました。適用するために WindowTabs を再起動します。\n\n以前の設定の保存先:\n%s" path
              ar=sprintf "تم استيراد الإعدادات. سيُعاد تشغيل WindowTabs لتطبيقها.\n\nتم حفظ الإعدادات السابقة في:\n%s" path }
        let tabMenuHint = { en="Change it for one group from the tab menu."; zh="可在标签菜单中为单个分组修改。"; ja="タブメニューでグループごとに変更できます。"; ar="غيّره لمجموعة واحدة من قائمة علامة التبويب." }
        let startup = { en="Startup"; zh="启动"; ja="起動"; ar="بدء التشغيل" }
        let tabBehavior = { en="Tab behavior"; zh="标签行为"; ja="タブの動作"; ar="سلوك علامات التبويب" }
        let taskbar = { en="Taskbar"; zh="任务栏"; ja="タスクバー"; ar="شريط المهام" }
        let windowSwitcher = { en="Window switcher"; zh="窗口切换器"; ja="ウィンドウ切り替え"; ar="مبدّل النوافذ" }

    module Appearance =
        let system = { en="System"; zh="跟随系统"; ja="システム"; ar="النظام" }
        let light = { en="Light"; zh="浅色"; ja="ライト"; ar="فاتح" }
        let dark = { en="Dark"; zh="深色"; ja="ダーク"; ar="داكن" }
        let explorerPreview = { en="File Explorer theme preview"; zh="文件资源管理器主题预览"; ja="エクスプローラーのテーマのプレビュー"; ar="معاينة سمة مستكشف الملفات" }
        let tabColors = { en="Tab colors"; zh="标签配色"; ja="タブの色"; ar="ألوان علامات التبويب" }
        let colorPreset = { en="Color preset"; zh="配色预设"; ja="配色プリセット"; ar="مجموعة ألوان جاهزة" }
        let custom = { en="Custom"; zh="自定义"; ja="カスタム"; ar="مخصص" }
        /// Switcher styles: large icons in a row, or window titles in a column.
        let switcherHorizontal = { en="Horizontal"; zh="横向"; ja="横"; ar="أفقي" }
        let switcherVertical = { en="Vertical"; zh="纵向"; ja="縦"; ar="رأسي" }
        /// Tab styles, in TabStyle.names order.
        let tabStyles =
            [| { en="Joined"; zh="连贯"; ja="連結"; ar="متصل" }
               { en="Folder"; zh="文件夹"; ja="フォルダー型"; ar="مجلد" }
               { en="Floating"; zh="悬浮"; ja="フローティング"; ar="عائم" } |]
        /// The tab colours on which the text is shown darker or lighter than chosen.
        /// Samples of the tab text as chosen and as shown, side by side.
        let textBefore = { en="Before"; zh="调整前"; ja="調整前"; ar="قبل" }
        let textAfter = { en="After"; zh="调整后"; ja="調整後"; ar="بعد" }
        let colorsByCoding = { en="Set automatically while Tab color coding is By window or By app with Fill."
                               zh="标签着色为“按窗口”或“按应用”且选择“填充”时，这里自动配置。"
                               ja="タブの色分けが「ウィンドウ別」または「アプリ別」で「塗りつぶし」のときは、ここは自動で設定されます。"
                               ar="يُضبط تلقائيًا عندما يكون «تلوين علامات التبويب» على «حسب النافذة» أو «حسب التطبيق» مع «تعبئة»." }
        let textAdjusted = { en="Text color is adjusted on some tabs to stay readable."; zh="部分标签上的文字颜色已调整，以保持清晰。"; ja="一部のタブでは、読みやすさのため文字の色を調整しています。"; ar="يُعدّل لون النص في بعض علامات التبويب ليظل مقروءًا." }
        /// A colour preset whose colours the user has changed.
        let editedPreset name = { en=sprintf "%s*" name; zh=sprintf "%s*" name; ja=sprintf "%s*" name; ar=sprintf "%s*" name }
        let resetColors = { en="Reset colors"; zh="重置配色"; ja="配色をリセット"; ar="إعادة تعيين الألوان" }
        let swapActiveInactive = { en="Swap active and inactive"; zh="互换激活与非激活"; ja="アクティブと非アクティブを入れ替え"; ar="تبديل النشطة وغير النشطة" }
        let tabLayout = { en="Tab layout"; zh="标签布局"; ja="タブのレイアウト"; ar="تخطيط علامات التبويب" }
        let resetTabLayout = { en="Reset tab layout"; zh="重置标签布局"; ja="タブのレイアウトをリセット"; ar="إعادة تعيين تخطيط علامات التبويب" }
        /// Stable source order; ThemePresets arranges the displayed colour families.
        let presets =
            [| { en="Default"; zh="默认"; ja="既定"; ar="افتراضي" }
               Settings.tabColorNames.[1]
               Settings.tabColorNames.[4]
               { en="Slate"; zh="石板灰"; ja="スレート"; ar="رمادي أردوازي" }
               Settings.tabColorNames.[14]
               { en="Sand"; zh="沙色"; ja="サンド"; ar="رملي" }
               { en="Amber"; zh="琥珀橙"; ja="アンバー"; ar="كهرماني" }
               Settings.tabColorNames.[11]
               Settings.tabColorNames.[6] |]
        let pickerHelp = { en="Color picker: arrow keys adjust saturation and brightness; Shift + arrow keys adjust hue"
                           zh="颜色选择器：方向键调整饱和度和亮度；Shift + 方向键调整色相"
                           ja="色の選択：方向キーで彩度と明るさを調整し、Shift + 方向キーで色相を調整します"
                           ar="منتقي الألوان: تعدّل مفاتيح الأسهم التشبع والسطوع؛ وتعدّل الأسهم مع Shift درجة اللون" }
        let chooseColor = { en="Choose color"; zh="选择颜色"; ja="色を選択"; ar="اختيار اللون" }
        let decrease = { en="Decrease"; zh="减小"; ja="減らす"; ar="تقليل" }
        let increase = { en="Increase"; zh="增大"; ja="増やす"; ar="زيادة" }

    module Shortcuts =
        let keyboard = { en="Keyboard"; zh="键盘"; ja="キーボード"; ar="لوحة المفاتيح" }
        /// The card of the two keys that reach everything else: the window search and the palette.
        let startHere = { en="Start here"; zh="从这里开始"; ja="ここから始める"; ar="ابدأ من هنا" }
        let startHereNote = { en="Type a few letters to find any window, or to run any command or setting."
                              zh="输入几个字，即可找到任意窗口，或运行任意命令和设置。"
                              ja="数文字入力するだけで、任意のウィンドウを探したり、コマンドや設定を実行したりできます。"
                              ar="اكتب بضعة أحرف للعثور على أي نافذة، أو لتشغيل أي أمر أو إعداد." }
        let mouse = { en="Mouse"; zh="鼠标"; ja="マウス"; ar="الفأرة" }
        /// Cards for the commands that turn settings on or off and open pages, and the app's own.
        let keyboardNote = { en="Click a shortcut, then press the new combination. Esc cancels. The command palette sets one too: Alt+Enter on a command."
                             zh="点击快捷键后按下新的组合键。按 Esc 取消。在命令面板中选中命令后按 Alt+Enter 也可设置。"
                             ja="ショートカットをクリックして、新しいキーの組み合わせを押します。Esc でキャンセルします。コマンドパレットでコマンドを選んで Alt+Enter でも設定できます。"
                             ar="انقر اختصارًا ثم اضغط مجموعة المفاتيح الجديدة. اضغط Esc للإلغاء. وتعيّنه لوحة الأوامر أيضًا: Alt+Enter على أمر." }
        /// Hovering the reset button: what it resets and what it leaves.
        let restoreDefaultsHelp = { en="Restore default shortcuts: the ones on this page, every key given to a command or a list in the command palette, and the keys given to windows in the window search, which are cleared."
                                    zh="恢复默认快捷键：包括本页的快捷键、在命令面板中为命令或列表设置的按键，以及在窗口搜索中为窗口设置的按键（会被清除）。"
                                    ja="既定のショートカットに戻します。このページのショートカット、コマンドパレットでコマンドやリストに設定したキー、ウィンドウ検索でウィンドウに設定したキー（解除されます）が対象です。"
                                    ar="استعادة الاختصارات الافتراضية: اختصارات هذه الصفحة، وكل مفتاح عُيّن لأمر أو لقائمة في لوحة الأوامر، والمفاتيح المعيّنة للنوافذ في بحث النوافذ، التي تُزال." }
        let restoreDefaults = { en="Restore default shortcuts"; zh="恢复默认快捷键"; ja="既定のショートカットに戻す"; ar="استعادة الاختصارات الافتراضية" }
        let inUse = { en="Shortcut already in use"; zh="快捷键已被占用"; ja="ショートカットは使用中です"; ar="الاختصار مستخدم بالفعل" }
        /// A key pressed for one command that another had: it moves, and the other is left without.
        let takenFrom name = { en=sprintf "Taken from %s" name; zh=sprintf "已从“%s”移过来" name; ja=sprintf "「%s」から移しました" name; ar=sprintf "نُقل من «%s»" name }
        // Shortcut field messages are kept short to fit.
        let alreadySet = { en="Already set"; zh="已是当前快捷键"; ja="設定済み"; ar="هذا هو الاختصار الحالي" }
        /// A key pressed, then what was said of it, as "Alt+J: Used by Next tab".
        let keySays key (said:string) = { en=sprintf "%s: %s" key said; zh=sprintf "%s：%s" key said; ja=sprintf "%s: %s" key said; ar=sprintf "%s: %s" key said }
        /// Alt+Enter, which sets a shortcut in the palette.
        let usedByPalette = { en="Used by the command palette (set shortcut)"; zh="命令面板已占用（设置快捷键）"
                              ja="コマンドパレットが使用中（ショートカットの設定）"; ar="تستخدمه لوحة الأوامر (تعيين اختصار)" }
        let includeCtrlOrAlt = { en="Include Ctrl or Alt"; zh="需要包含 Ctrl 或 Alt"; ja="Ctrl か Alt が必要です"; ar="يجب أن يتضمن Ctrl أو Alt" }
        /// A key of the lists may also be Tab or a function key alone.
        let includeCtrlAltOrTab = { en="Use Tab, a function key, or Ctrl or Alt"; zh="请使用 Tab、功能键，或包含 Ctrl 或 Alt"; ja="Tab、ファンクション キー、または Ctrl か Alt を使ってください"; ar="استخدم Tab أو مفتاح وظيفة أو Ctrl أو Alt" }
        let pressShortcut = { en="Press a shortcut…"; zh="按下组合键…"; ja="キーを押してください…"; ar="اضغط اختصارًا…" }
        let notSet = { en="Not set"; zh="未设置"; ja="未設定"; ar="غير معيّن" }

    /// Commands run by name, from the palette or the command line, that no setting names already.
    module Commands =
        let private splitWords = { en="split view pane"; zh="分屏 窗格"; ja="分割 ペイン"; ar="عرض مقسّم جزء" }
        let splitSwap = { caption={ en="Swap panes"; zh="交换窗格"; ja="ペインを入れ替え"; ar="تبديل الأجزاء" }
                          description={ en="The two halves of the focused pane's split change places."
                                        zh="当前窗格所在分屏的两个窗格互换位置。"
                                        ja="フォーカスしているペインの分割の両側を入れ替えます。"
                                        ar="يتبادل نصفا التقسيم الذي يحتوي على الجزء المركّز مكانيهما." }
                          keywords=splitWords }
        let splitEqualize = { caption={ en="Equal panes"; zh="均分窗格"; ja="均等に分割"; ar="أجزاء متساوية" }
                              description={ en="Gives both halves of the focused pane's split the same room."
                                            zh="让当前窗格所在分屏的两个窗格占同样的空间。"
                                            ja="フォーカスしているペインの分割の両側を同じ広さにします。"
                                            ar="يمنح نصفي التقسيم الذي يحتوي على الجزء المركّز المساحة نفسها." }
                              keywords=splitWords }
        let splitTurn = { caption={ en="Turn the split"; zh="切换分屏方向"; ja="分割の向きを切り替え"; ar="تدوير التقسيم" }
                          description={ en="Side by side becomes top and bottom, and back."
                                        zh="左右变为上下，再按一次变回。"
                                        ja="左右を上下に、またその逆に切り替えます。"
                                        ar="يتحول العرض جنبًا إلى جنب إلى أعلى وأسفل، والعكس." }
                          keywords=splitWords }
        let splitClosePane = { caption={ en="Close the pane"; zh="关闭当前窗格"; ja="ペインを閉じる"; ar="إغلاق الجزء" }
                               description={ en="The focused pane leaves the split and its neighbour takes its room; the window stays a tab."
                                             zh="当前窗格退出分屏，空间让给相邻的窗格；窗口仍保留为标签。"
                                             ja="フォーカスしているペインを分割から外し、隣のペインがその場所を使います。ウィンドウはタブとして残ります。"
                                             ar="يغادر الجزء المركّز التقسيم ويأخذ جاره مكانه؛ وتبقى النافذة علامة تبويب." }
                               keywords=splitWords }
        let splitEnd = { caption={ en="End split view"; zh="结束分屏"; ja="分割表示を終了"; ar="إنهاء العرض المقسّم" }
                         description={ en="Every window becomes a tab again."; zh="所有窗口恢复为标签。"; ja="すべてのウィンドウをタブに戻します。"; ar="تعود كل نافذة إلى علامة تبويب." }
                         keywords=splitWords }
        let splitFocusOther = { caption={ en="Focus the next pane"; zh="焦点切到下一个窗格"; ja="次のペインにフォーカス"; ar="التركيز على الجزء التالي" }
                                description={ en="Around the panes in turn; brings parked panes back."
                                              zh="按顺序在各窗格之间轮换；已收起的窗格会重新显示。"
                                              ja="ペインを順に巡ります。しまわれたペインは元に戻します。"
                                              ar="يتنقل بين الأجزاء بالتناوب، ويعيد الأجزاء المخفية." }
                                keywords=splitWords }
        let private tabWords = { en="tab"; zh="标签"; ja="タブ"; ar="علامة تبويب" }
        let renameTab = { caption={ en="Rename tab"; zh="重命名标签"; ja="タブの名前を変更"; ar="إعادة تسمية علامة التبويب" }
                          description={ en="Edits the name on the tab; an empty name brings the window's own back."
                                        zh="在标签上编辑名称；留空则恢复窗口自己的标题。"
                                        ja="タブ上で名前を編集します。空にするとウィンドウ自身のタイトルに戻ります。"
                                        ar="يحرر الاسم على علامة التبويب؛ ويعيد الاسم الفارغ عنوان النافذة نفسها." }
                          keywords=tabWords }
        let resetTabName = { caption={ en="Reset tab name"; zh="重置标签名称"; ja="タブ名をリセット"; ar="إعادة تعيين اسم علامة التبويب" }
                             description={ en="The tab follows the window's title again."; zh="标签重新跟随窗口标题。"; ja="タブはウィンドウのタイトルに再び従います。"; ar="تتبع علامة التبويب عنوان النافذة من جديد." }
                             keywords=tabWords }
        let private pinWords = { en="pin unpin keep"; zh="固定 取消固定 置顶 钉住"; ja="固定 固定解除 ピン留め"; ar="تثبيت إلغاء" }
        let pinTab = { caption={ en="Pin tab"; zh="固定标签"; ja="タブを固定"; ar="تثبيت علامة التبويب" }
                       description={ en="A pinned tab stays first and shows a pin for its close button; closing tabs together leaves it."
                                     zh="固定的标签排在最前，关闭按钮处显示图钉；批量关闭标签时保留。"
                                     ja="固定したタブは先頭に並び、閉じるボタンの代わりにピンを表示します。まとめて閉じるときも残ります。"
                                     ar="تبقى علامة التبويب المثبتة أولًا وتعرض دبوسًا مكان زر الإغلاق؛ ولا تُغلق عند إغلاق علامات التبويب معًا." }
                       keywords=pinWords }
        /// The same command, for a tab already pinned.
        let unpinTab = { pinTab with caption={ en="Unpin tab"; zh="取消固定标签"; ja="タブの固定を解除"; ar="إلغاء تثبيت علامة التبويب" } }
        let tabColor = { caption={ en="Tab color…"; zh="标签颜色…"; ja="タブの色…"; ar="لون علامة التبويب…" }
                         description={ en="A color of the palette for the tab in front, or none."; zh="为当前标签选一种调色板颜色，或清除。"; ja="前面のタブにパレットの色を付けるか、解除します。"; ar="لون من اللوحة لعلامة التبويب الأمامية، أو لا شيء." }
                         keywords={ en="colour"; zh="颜色 着色"; ja="色 カラー"; ar="لون" } }
        let private colorWords = { en="colour color"; zh="颜色 着色"; ja="色 カラー"; ar="لون" }
        let customTabColor = { caption={ en="Custom tab color…"; zh="自定义标签颜色…"; ja="タブの色をカスタム…"; ar="لون مخصص لعلامة التبويب…" }
                               description={ en="A color of your own for the tab in front."; zh="为当前标签选择自定义颜色。"; ja="前面のタブに好きな色を付けます。"; ar="لون من اختيارك لعلامة التبويب الأمامية." }
                               keywords=colorWords }
        let rememberTabColor = { caption={ en="Remember tab color for this app"; zh="为此应用记住标签颜色"; ja="このアプリのタブの色を記憶"; ar="تذكّر لون علامة التبويب لهذا التطبيق" }
                                 description={ en="New windows of the app take the tab's color; again, and the app forgets it."
                                               zh="此应用的新窗口沿用该标签颜色；再执行一次则忘记。"
                                               ja="アプリの新しいウィンドウにもこの色を使います。もう一度実行すると忘れます。"
                                               ar="تأخذ نوافذ التطبيق الجديدة لون علامة التبويب؛ ومرة أخرى ينساه التطبيق." }
                                 keywords=colorWords }
        let clearTabColor = { caption={ en="Clear tab color"; zh="清除标签颜色"; ja="タブの色を解除"; ar="مسح لون علامة التبويب" }
                              description={ en="No color on the tab in front, nor kept for its app."; zh="清除当前标签的颜色，并取消为其应用记住的颜色。"; ja="前面のタブの色と、そのアプリに記憶した色を解除します。"; ar="لا لون لعلامة التبويب الأمامية، ولا لون محفوظ لتطبيقها." }
                              keywords=colorWords }
        let moveTabLeft = { caption={ en="Move tab left"; zh="向左移动标签"; ja="タブを左へ移動"; ar="نقل علامة التبويب إلى اليسار" }
                            description={ en="One place; up, with tabs at the side."; zh="移动一格；标签在侧边时为向上。"; ja="1 つ分。タブが横にあるときは上へ。"; ar="موضعًا واحدًا؛ إلى الأعلى عندما تكون علامات التبويب على الجانب." }
                            keywords={ en="reorder up"; zh="排序 上移"; ja="並べ替え 上"; ar="ترتيب أعلى" } }
        let moveTabRight = { caption={ en="Move tab right"; zh="向右移动标签"; ja="タブを右へ移動"; ar="نقل علامة التبويب إلى اليمين" }
                             description={ en="One place; down, with tabs at the side."; zh="移动一格；标签在侧边时为向下。"; ja="1 つ分。タブが横にあるときは下へ。"; ar="موضعًا واحدًا؛ إلى الأسفل عندما تكون علامات التبويب على الجانب." }
                             keywords={ en="reorder down"; zh="排序 下移"; ja="並べ替え 下"; ar="ترتيب أسفل" } }
        let sortTabs = { caption=TabMenu.sortTabs; description=none; keywords={ en="order"; zh="排序"; ja="並べ替え"; ar="ترتيب" } }
        let closeOtherTabs = { caption={ en="Close other tabs"; zh="关闭其他标签"; ja="他のタブを閉じる"; ar="إغلاق علامات التبويب الأخرى" }
                               description={ en="Closes every other window of the group but the pinned ones."; zh="关闭分组中的其他窗口，固定的标签除外。"; ja="グループの他のウィンドウを、固定したタブを除いてすべて閉じます。"; ar="يغلق كل النوافذ الأخرى في المجموعة عدا المثبتة." }
                               keywords=tabWords }
        let closeAppTabs = { caption={ en="Close all tabs of this app"; zh="关闭此应用的全部标签"; ja="このアプリのタブをすべて閉じる"; ar="إغلاق جميع علامات تبويب هذا التطبيق" }
                             description={ en="Every window of the group from the app of the tab in front, but the pinned ones."; zh="分组中属于当前标签所在应用的所有窗口，固定的标签除外。"; ja="グループ内で、前面のタブのアプリのウィンドウを、固定したタブを除いてすべて。"; ar="كل نوافذ المجموعة من تطبيق علامة التبويب الأمامية، عدا المثبتة." }
                             keywords=tabWords }
        let private iconNote = { en="For a group with combined taskbar icons."; zh="用于已合并任务栏图标的分组。"; ja="タスクバーアイコンをまとめたグループ用。"; ar="لمجموعة دُمجت أيقونات شريط المهام لها." }
        let changeGroupIcon = { caption=TabMenu.changeTaskbarIcon; description=iconNote; keywords={ en="picture image"; zh="图片 图像"; ja="画像 ピクチャ"; ar="صورة" } }
        let restoreGroupIcon = { caption=TabMenu.restoreTaskbarIcon; description=iconNote; keywords={ en="default picture"; zh="默认 图片"; ja="既定 画像"; ar="افتراضي صورة" } }
        let closeAllTabs = { caption={ en="Close all tabs"; zh="关闭全部标签"; ja="すべてのタブを閉じる"; ar="إغلاق جميع علامات التبويب" }
                             description={ en="Closes every window of the group but the pinned ones."; zh="关闭分组中的所有窗口，固定的标签除外。"; ja="グループのウィンドウを、固定したタブを除いてすべて閉じます。"; ar="يغلق كل نوافذ المجموعة عدا المثبتة." }
                             keywords=tabWords }
        let detachTab = { caption={ en="Move tab to a new group"; zh="拆出到新分组"; ja="タブを新しいグループへ"; ar="نقل علامة التبويب إلى مجموعة جديدة" }
                          description={ en="Takes the tab out on its own, as dragging it to the desktop does."; zh="把标签单独拆出来，与拖到桌面上相同。"; ja="タブを単独で取り出します。デスクトップへドラッグするのと同じです。"; ar="يخرج علامة التبويب وحدها، كما يفعل سحبها إلى سطح المكتب." }
                          keywords={ en="detach split ungroup"; zh="分离 取消分组"; ja="分離 グループ解除"; ar="فصل إلغاء التجميع" } }
        let ungroupTabs = { caption={ en="Ungroup this group"; zh="解除此分组"; ja="このグループを解除"; ar="إلغاء تجميع هذه المجموعة" }
                            description={ en="Takes every other tab out on its own, as dragging each to the desktop does. The tab in front stays."
                                          zh="把其他标签都各自拆出来，与逐个拖到桌面上相同。当前标签留在原处。"
                                          ja="手前のタブ以外を、それぞれ単独で取り出します。1 つずつデスクトップへドラッグするのと同じです。"
                                          ar="يُخرج كل علامة تبويب أخرى وحدها، كما يفعل سحب كل منها إلى سطح المكتب. وتبقى علامة التبويب الأمامية." }
                            keywords={ en="ungroup break apart separate detach all split"; zh="打散 取消分组 拆分 全部拆出 分离"; ja="グループ解除 分離 すべて 取り出す"; ar="فصل تفكيك الكل إلغاء التجميع" } }
        let groupApp = { caption={ en="Group this app"; zh="合并此应用的窗口"; ja="このアプリのウィンドウをまとめる"; ar="تجميع نوافذ هذا التطبيق" }
                         description={ en="Brings every window of the app in front into this group, from other groups or on their own."
                                       zh="把当前应用的所有窗口都并入这个分组，不论它们在别的分组里还是单独一个。"
                                       ja="手前のアプリのウィンドウを、ほかのグループにあるものも単独のものも、すべてこのグループに入れます。"
                                       ar="يضم كل نوافذ التطبيق الأمامي إلى هذه المجموعة، سواء كانت في مجموعات أخرى أو منفردة." }
                         keywords={ en="merge gather combine join windows"; zh="合并 归组 汇总 加入 窗口"; ja="結合 まとめる 集める ウィンドウ"; ar="دمج جمع ضم نوافذ" } }
        let moveTabToGroup = { caption={ en="Move tab to group…"; zh="移动标签到分组…"; ja="タブをグループへ移動…"; ar="نقل علامة التبويب إلى مجموعة…" }
                               description={ en="Lists the other groups by their first tab."; zh="按各分组的第一个标签列出其他分组。"; ja="他のグループを先頭のタブで一覧表示します。"; ar="يعرض المجموعات الأخرى بعلامة التبويب الأولى لكل منها." }
                               keywords={ en="join"; zh="加入 合并"; ja="参加 結合"; ar="انضمام" } }
        let private groupNote = { en="For this group; the setting is for new groups."; zh="只对当前分组有效；设置项针对新分组。"; ja="このグループのみ。設定は新しいグループに適用されます。"; ar="لهذه المجموعة؛ أما الإعداد فللمجموعات الجديدة." }
        /// A setting's own name for the group in front alone: listed and found beside the setting,
        /// and told apart from it.
        let private forThisGroup (setting:LocalizedText) (more:string) =
            { en=setting.en+" for this group"+more; zh=setting.zh+"（此分组）"+more; ja=setting.ja+"（このグループ）"+more; ar=setting.ar+" لهذه المجموعة"+more }
        let iconsOnly = { caption=forThisGroup TabMenu.showIconsOnly ""; description={ en="Again, and the titles come back."; zh="再次执行则恢复标题。"; ja="もう一度実行するとタイトルが戻ります。"; ar="مرة أخرى، وتعود العناوين." }; keywords={ en="titles compact"; zh="标题 紧凑"; ja="タイトル コンパクト"; ar="عناوين مضغوط" } }
        let groupTabPosition = { caption=forThisGroup Settings.tabPosition.caption "…"; description=groupNote; keywords={ en="top left right side"; zh="顶部 左侧 右侧"; ja="上 左 右"; ar="أعلى يسار يمين" } }
        let groupTabAlignment = { caption=forThisGroup Settings.tabAlignment.caption "…"; description=groupNote; keywords={ en="alignment center"; zh="对齐 居中"; ja="配置 中央"; ar="محاذاة وسط" } }
        let groupAutoHide = { caption=forThisGroup Settings.autoHide.caption "…"; description=groupNote; keywords={ en="collapse"; zh="收起"; ja="折りたたみ"; ar="طيّ" } }
        let combineGroupIcons = { caption=forThisGroup TabMenu.combineTaskbarIcons ""; description=groupNote; keywords={ en="merge taskbar"; zh="合并 任务栏"; ja="結合 タスクバー"; ar="دمج شريط المهام" } }
        let tabsForApp = { caption={ en="Use tabs for this app"; zh="此应用使用标签"; ja="このアプリでタブを使う"; ar="استخدام علامات التبويب لهذا التطبيق" }
                           description={ en="Turns tabs on or off for the app of the window in front."; zh="为当前窗口所属的应用开启或关闭标签。"; ja="前面のウィンドウのアプリでタブをオン／オフにします。"; ar="يشغّل علامات التبويب أو يوقفها لتطبيق النافذة الأمامية." }
                           keywords={ en="enable disable application"; zh="启用 禁用 应用"; ja="有効 無効 アプリ"; ar="تمكين تعطيل تطبيق" } }
        let autoGroupApp = { caption={ en="Auto-group this app"; zh="自动分组此应用"; ja="このアプリを自動グループ化"; ar="تجميع هذا التطبيق تلقائيًا" }
                             description={ en="On or off for the app of the window in front."; zh="为当前窗口所属的应用开启或关闭。"; ja="前面のウィンドウのアプリでオン／オフにします。"; ar="تشغيل أو إيقاف لتطبيق النافذة الأمامية." }
                             keywords={ en="application grouping"; zh="应用 分组"; ja="アプリ グループ化"; ar="تطبيق تجميع" } }
        let numberShortcutForApp = { caption={ en="Switch tabs by number in this app"; zh="在此应用中按数字切换标签"; ja="このアプリのタブを番号で切り替え"; ar="التبديل بين علامات التبويب باستخدام الأرقام في هذا التطبيق" }
                                     description={ en="On or off for the app of the window in front."; zh="为当前窗口所属的应用开启或关闭。"; ja="前面のウィンドウのアプリでオン／オフにします。"; ar="تشغيل أو إيقاف لتطبيق النافذة الأمامية." }
                                     keywords={ en="digit application"; zh="数字 应用"; ja="数字 アプリ"; ar="رقم تطبيق" } }
        let restoreWorkspaceNamed = { caption={ en="Restore workspace…"; zh="恢复工作区…"; ja="ワークスペースを復元…"; ar="استعادة مساحة عمل…" }
                                      description={ en="Lists the saved workspaces."; zh="列出已保存的工作区。"; ja="保存済みのワークスペースを一覧表示します。"; ar="يعرض مساحات العمل المحفوظة." }
                                      keywords={ en="layout"; zh="布局"; ja="配置"; ar="تخطيط" } }
        let deleteWorkspace = { caption={ en="Delete workspace…"; zh="删除工作区…"; ja="ワークスペースを削除…"; ar="حذف مساحة عمل…" }
                                description={ en="Lists the saved workspaces; the one chosen is forgotten."; zh="列出已保存的工作区；选中的会被删除。"; ja="保存済みのワークスペースを一覧表示し、選んだものを削除します。"; ar="يعرض مساحات العمل المحفوظة؛ وتُنسى المختارة." }
                                keywords={ en="remove layout"; zh="移除 布局"; ja="削除 配置"; ar="إزالة تخطيط" } }
        let openSettings = { caption={ en="Open settings"; zh="打开设置"; ja="設定を開く"; ar="فتح الإعدادات" }
                             description=none
                             keywords={ en="preferences options"; zh="偏好 选项"; ja="環境設定 オプション"; ar="التفضيلات الخيارات" } }
        /// One settings page by name, as "Settings: General".
        let settingsPage (page:LocalizedText) =
            { caption={ en="Settings: "+page.en; zh="设置："+page.zh; ja="設定: "+page.ja; ar="الإعدادات: "+page.ar }
              description=none
              keywords={ en="open page"; zh="打开 页面"; ja="開く ページ"; ar="فتح صفحة" } }
        let exit = { caption=Tray.exit
                     description={ en="Every window gets its own frame back."; zh="所有窗口恢复各自的边框。"; ja="すべてのウィンドウが元の枠に戻ります。"; ar="تستعيد كل نافذة إطارها الخاص." }
                     keywords={ en="quit close"; zh="退出 关闭"; ja="終了"; ar="إنهاء إغلاق" } }
        let refreshWindows = { caption={ en="Look for windows again"; zh="重新扫描窗口"; ja="ウィンドウを再スキャン"; ar="إعادة فحص النوافذ" }
                               description={ en="Groups any window that was missed."; zh="把漏掉的窗口加入分组。"; ja="見落とされたウィンドウをグループに入れます。"; ar="يضيف أي نافذة فاتها البرنامج إلى مجموعة." }
                               keywords={ en="rescan refresh reload"; zh="刷新 重新加载"; ja="更新 再読み込み"; ar="تحديث إعادة تحميل" } }

    /// Written to the console a command line was given from.
    /// The group editor's popup.
    module GroupEditor =
        let title = { en="Edit groups"; zh="编辑分组"; ja="グループを編集"; ar="تحرير المجموعات" }
        let windows count =
            { en=(if count=1 then "1 window" else sprintf "%d windows" count); zh=sprintf "%d 个窗口" count; ja=sprintf "%d 個のウィンドウ" count; ar=sprintf "%d نافذة" count }
        let save = { en="Save"; zh="保存"; ja="保存"; ar="حفظ" }
        let hint = { en="Drag tabs along the strip or onto a pane; the corner button splits a pane; right-click a tab to take it out; Esc goes back."
                     zh="沿标签条或往窗格里拖标签；窗格角上的按钮可拆分；右键标签可移出分组；Esc 回到全部分组。"
                     ja="タブをストリップに沿って、またはペインへドラッグ。ペイン隅のボタンで分割、タブの右クリックで外し、Esc で戻ります。"
                     ar="اسحب علامات التبويب على طول الشريط أو إلى لوحة؛ زر الزاوية يقسم اللوحة؛ انقر بزر الفأرة الأيمن على علامة تبويب لإخراجها؛ وEsc يعود." }
        let noGroup = { en="The window in front is in no group."; zh="当前窗口不在任何分组里。"; ja="手前のウィンドウはどのグループにもありません。"; ar="النافذة الأمامية ليست في أي مجموعة." }
        /// What saving did.
        let applied moved detached made =
            let parts (en:string list) = System.String.Join(", ",en |> List.filter(fun part -> part<>""))
            { en=parts [ (if moved=0 then "" elif moved=1 then "1 window moved" else sprintf "%d windows moved" moved)
                         (if detached=0 then "" elif detached=1 then "1 window taken out" else sprintf "%d windows taken out" detached)
                         (if made=0 then "" elif made=1 then "1 group made" else sprintf "%d groups made" made) ]
              zh=parts [ (if moved=0 then "" else sprintf "移动 %d 个窗口" moved); (if detached=0 then "" else sprintf "移出 %d 个窗口" detached); (if made=0 then "" else sprintf "新建 %d 个分组" made) ]
              ja=parts [ (if moved=0 then "" else sprintf "%d 個のウィンドウを移動" moved); (if detached=0 then "" else sprintf "%d 個のウィンドウを外しました" detached); (if made=0 then "" else sprintf "%d 個のグループを作成" made) ]
              ar=parts [ (if moved=0 then "" else sprintf "نُقلت %d نافذة" moved); (if detached=0 then "" else sprintf "أُخرجت %d نافذة" detached); (if made=0 then "" else sprintf "أُنشئت %d مجموعة" made) ] }
        let nothingChanged = { en="Nothing changed."; zh="没有改动。"; ja="変更はありません。"; ar="لم يتغير شيء." }
        let dropHere = { en="Drop a window here"; zh="拖窗口到这里"; ja="ここにウィンドウをドロップ"; ar="أفلت نافذة هنا" }
        /// Said in the footer while the split button's slots are still empty.
        let emptyCellsClose count =
            { en=(if count=1 then "1 empty cell will close at Save." else sprintf "%d empty cells will close at Save." count)
              zh=sprintf "%d 个空格子会在保存时合并。" count
              ja=sprintf "空のセル %d 個は保存時に閉じられます。" count
              ar=sprintf "ستُغلق %d خلية فارغة عند الحفظ." count }
        let splitButton = { en="Split this pane"; zh="拆分这个窗格"; ja="このペインを分割"; ar="تقسيم هذا الجزء" }
        /// The title's count of rows and of windows in all.
        let summary groups windows =
            { en=sprintf "%d %s, %d %s" groups (if groups=1 then "group" else "groups") windows (if windows=1 then "window" else "windows")
              zh=sprintf "%d 个分组，共 %d 个窗口" groups windows
              ja=sprintf "グループ %d、ウィンドウ %d" groups windows
              ar=sprintf "%d مجموعة، %d نافذة" groups windows }
        let looseWindows = { en="Loose windows"; zh="未分组窗口"; ja="未分類のウィンドウ"; ar="نوافذ غير مجمّعة" }
        /// In the loose row while no window is on its own: what the row is for.
        let looseEmpty = { en="Ungroup: drop a tab or a whole row here"; zh="移出分组：把标签或整行拖到这里"; ja="グループ解除：タブや行ごとここにドロップ"; ar="فك التجميع: أفلت علامة تبويب أو صفًا كاملًا هنا" }
        let newGroup = { en="New group: drop a tab here"; zh="新建分组：拖到这里"; ja="新しいグループ：ここにドロップ"; ar="مجموعة جديدة: أفلت علامة تبويب هنا" }
        let overviewHint = { en="Drag a tab to another row, Ungroup or New group; drag a row by its empty room onto another to merge; click a tab or a row to expand it."
                             zh="把标签拖到别的行、“移出分组”或“新建分组”；按住行的空白处拖到别的行即合并；点标签或行即展开。"
                             ja="タブを別の行、グループ解除、新しいグループへドラッグ。行の空きをつかんで別の行へ重ねると統合、タブや行をクリックで展開。"
                             ar="اسحب علامة تبويب إلى صف آخر أو فك التجميع أو مجموعة جديدة؛ اسحب صفًا من فراغه إلى صف آخر لدمجهما؛ انقر علامة تبويب أو صفًا لتوسيعه." }
        let removeTab = { en="Take out of the group"; zh="移出分组"; ja="グループから外す"; ar="إخراج من المجموعة" }
        /// For a window in a pane: it stays a tab of its group, out of the split.
        let leavePane = { en="Take out of the pane"; zh="移出窗格"; ja="ペインから外す"; ar="إخراج من اللوحة" }
        /// The row's menu, from a right click on its empty room.
        let mergeInto = { en="Merge into"; zh="合并到"; ja="統合先"; ar="دمج في" }
        let ungroup = { en="Ungroup"; zh="解散分组"; ja="グループ解除"; ar="فك التجميع" }

    module CommandLine =
        /// Shown for --help: the options, then examples to copy.
        let usage = { en="Usage: WindowTabs [option]\n\nOptions:\n  -c, --command <id> [choice]   Run a command in the running WindowTabs. A command that offers\n                                choices takes the one named, as the palette lists it.\n  -w, --workspace <name>        Restore a saved workspace.\n  -g, --group <program>...      Start the programs and put the windows they open into one group.\n      --commands                List the commands and their ids.\n  -h, --help                    Show this help.\n\nWith no option, WindowTabs starts, replacing a copy already running.\n\nExamples:\n  WindowTabs --commands\n  WindowTabs --command next-tab\n  WindowTabs --command tab-color Blue\n  WindowTabs --command group-tab-position Left\n  WindowTabs --command restore-workspace-named \"Project A\"\n  WindowTabs --workspace \"Project A\"\n  WindowTabs --group notepad.exe \"C:\\Program Files\\App\\app.exe\"\n\nA command acts on the window in front, as its key does: run it from a shortcut or a hotkey\ntool. From a terminal, the terminal is the window in front."
                      zh="用法：WindowTabs [选项]\n\n选项：\n  -c, --command <id> [选项名]   在运行中的 WindowTabs 里执行命令。有选项的命令按面板中\n                                列出的名称选择其中一项。\n  -w, --workspace <名称>        恢复已保存的工作区。\n  -g, --group <程序>...         启动这些程序，并把它们打开的窗口编成一个分组。\n      --commands                列出所有命令及其 id。\n  -h, --help                    显示本帮助。\n\n不带选项时启动 WindowTabs，并替换正在运行的副本。\n\n示例：\n  WindowTabs --commands\n  WindowTabs --command next-tab\n  WindowTabs --command tab-color Blue\n  WindowTabs --command group-tab-position Left\n  WindowTabs --command restore-workspace-named \"Project A\"\n  WindowTabs --workspace \"Project A\"\n  WindowTabs --group notepad.exe \"C:\\Program Files\\App\\app.exe\"\n\n命令作用于前台窗口，与它的快捷键相同：请从快捷方式或热键工具中运行。\n在终端中运行时，前台窗口就是终端本身。"
                      ja="使い方: WindowTabs [オプション]\n\nオプション:\n  -c, --command <id> [選択肢]   実行中の WindowTabs でコマンドを実行します。選択肢のある\n                                コマンドは、パレットに表示される名前で選びます。\n  -w, --workspace <名前>        保存済みのワークスペースを復元します。\n  -g, --group <プログラム>...   プログラムを起動し、開いたウィンドウを 1 つのグループにまとめます。\n      --commands                コマンドとその id を一覧表示します。\n  -h, --help                    このヘルプを表示します。\n\nオプションなしでは WindowTabs を起動し、実行中のコピーを置き換えます。\n\n例:\n  WindowTabs --commands\n  WindowTabs --command next-tab\n  WindowTabs --command tab-color Blue\n  WindowTabs --command group-tab-position Left\n  WindowTabs --command restore-workspace-named \"Project A\"\n  WindowTabs --workspace \"Project A\"\n  WindowTabs --group notepad.exe \"C:\\Program Files\\App\\app.exe\"\n\nコマンドはキーと同じく前面のウィンドウに働きます。ショートカットやホットキー\nツールから実行してください。ターミナルから実行すると、前面にあるのはターミナルです。"
                      ar="الاستخدام: WindowTabs [خيار]\n\nالخيارات:\n  -c, --command <id> [اختيار]   يشغّل أمرًا في WindowTabs العامل. الأمر الذي يعرض خيارات\n                                يأخذ المسمّى منها كما تسرده اللوحة.\n  -w, --workspace <اسم>         يستعيد مساحة عمل محفوظة.\n  -g, --group <برنامج>...       يشغّل البرامج ويضع النوافذ التي تفتحها في مجموعة واحدة.\n      --commands                يعرض الأوامر ومعرّفاتها.\n  -h, --help                    يعرض هذه المساعدة.\n\nبدون خيار، يبدأ WindowTabs ويحل محل النسخة العاملة.\n\nأمثلة:\n  WindowTabs --commands\n  WindowTabs --command next-tab\n  WindowTabs --command tab-color Blue\n  WindowTabs --command group-tab-position Left\n  WindowTabs --command restore-workspace-named \"Project A\"\n  WindowTabs --workspace \"Project A\"\n  WindowTabs --group notepad.exe \"C:\\Program Files\\App\\app.exe\"\n\nيعمل الأمر على النافذة الأمامية كما يفعل مفتاحه: شغّله من اختصار أو من أداة مفاتيح\nتشغيل سريع. من الطرفية، الطرفية نفسها هي النافذة الأمامية." }
        let unknownCommand id = { en=sprintf "No command is called %s. --commands lists them." id; zh=sprintf "没有名为 %s 的命令。--commands 可列出全部命令。" id; ja=sprintf "%s というコマンドはありません。--commands で一覧表示できます。" id; ar=sprintf "لا يوجد أمر باسم %s. يعرض --commands الأوامر." id }
        let unavailable caption = { en=sprintf "%s has nothing to act on for the window in front." caption; zh=sprintf "“%s”对当前前台窗口无可操作的内容。" caption; ja=sprintf "「%s」は前面のウィンドウに対して実行できる操作がありません。" caption; ar=sprintf "لا يجد «%s» ما يعمل عليه في النافذة الأمامية." caption }
        /// A command line that is not one: what was wrong, and where to look.
        let unknownOption word = { en=sprintf "Unknown option %s." word; zh=sprintf "未知选项 %s。" word; ja=sprintf "不明なオプション %s です。" word; ar=sprintf "خيار غير معروف %s." word }
        let didYouMean option = { en=sprintf "Did you mean %s?" option; zh=sprintf "是否想输入 %s？" option; ja=sprintf "%s のことですか?" option; ar=sprintf "هل تقصد %s؟" option }
        let needsValue option = { en=sprintf "%s needs a value, as the examples in --help show." option; zh=sprintf "%s 需要一个值，参见 --help 中的示例。" option; ja=sprintf "%s には値が必要です。--help の例を参照してください。" option; ar=sprintf "يحتاج %s إلى قيمة، كما تُظهر أمثلة --help." option }
        let seeHelp = { en="WindowTabs --help lists the options."; zh="WindowTabs --help 会列出所有选项。"; ja="WindowTabs --help でオプションを一覧表示します。"; ar="يعرض WindowTabs --help الخيارات." }
        let noSuchChoice wanted (choices:string list) =
            let listed = System.String.Join(", ",choices)
            { en=sprintf "No choice is called %s. The choices are: %s" wanted listed; zh=sprintf "没有名为 %s 的选项。可选：%s" wanted listed; ja=sprintf "%s という選択肢はありません。選択肢: %s" wanted listed; ar=sprintf "لا يوجد خيار باسم %s. الخيارات: %s" wanted listed }
        let noSuchWorkspace name = { en=sprintf "No workspace is called %s." name; zh=sprintf "没有名为 %s 的工作区。" name; ja=sprintf "%s というワークスペースはありません。" name; ar=sprintf "لا توجد مساحة عمل باسم %s." name }
        let notListening = { en="WindowTabs is running but does not take command lines; start it again first."; zh="WindowTabs 正在运行，但不接收命令行；请先重新启动它。"; ja="WindowTabs は実行中ですが、コマンド ラインを受け付けません。先に起動し直してください。"; ar="WindowTabs يعمل لكنه لا يقبل سطور الأوامر؛ أعد تشغيله أولًا." }

    /// The tab search box.
    module TabSearch =
        let prompt = { en="Type a title or app name to switch windows"; zh="输入标题或应用名，切换窗口"; ja="タイトルやアプリ名を入力してウィンドウを切り替え"; ar="اكتب عنوانًا أو اسم تطبيق للتبديل بين النوافذ" }
        let noMatches = { en="No matching windows."; zh="没有匹配的窗口。"; ja="一致するウィンドウはありません。"; ar="لا توجد نوافذ مطابقة." }
        /// Beside a window on another virtual desktop that has no name of its own.
        let desktop number = { en=sprintf "Desktop %d" number; zh=sprintf "桌面 %d" number; ja=sprintf "デスクトップ %d" number; ar=sprintf "سطح المكتب %d" number }
        let otherDesktop = { en="Another desktop"; zh="其他桌面"; ja="別のデスクトップ"; ar="سطح مكتب آخر" }
        let groupTabs count = { en=sprintf "This group · %d" count; zh=sprintf "当前分组 · %d" count; ja=sprintf "このグループ · %d" count; ar=sprintf "هذه المجموعة · %d" count }
        let allTabs count = { en=sprintf "All windows · %d" count; zh=sprintf "全局窗口 · %d" count; ja=sprintf "すべてのウィンドウ · %d" count; ar=sprintf "كل النوافذ · %d" count }
        /// The palette's other pages, beside the windows.
        let actions = { en="Actions"; zh="操作"; ja="アクション"; ar="الإجراءات" }
        let actionPrompt = { en="Type a command or setting"; zh="输入命令或设置名"; ja="コマンドや設定名を入力"; ar="اكتب أمرًا أو إعدادًا" }
        let choicePrompt = { en="Choose, or type to filter"; zh="选择一项，或输入以筛选"; ja="選択するか、入力して絞り込み"; ar="اختر، أو اكتب للتصفية" }
        let noActions = { en="No matching commands or settings."; zh="没有匹配的命令或设置。"; ja="一致するコマンドや設定はありません。"; ar="لا توجد أوامر أو إعدادات مطابقة." }
        let noChoices = { en="No matching choices."; zh="没有匹配的选项。"; ja="一致する項目はありません。"; ar="لا توجد خيارات مطابقة." }
        /// Asking for a size: the hint in the box, the line under it, and why a value is refused.
        let numberPrompt low high = { en=sprintf "A number from %d to %d" low high; zh=sprintf "输入 %d 到 %d 之间的数字" low high; ja=sprintf "%d～%d の数値" low high; ar=sprintf "رقم من %d إلى %d" low high }
        let colorRefused = { en="Type a colour as #RRGGBB."; zh="请输入 #RRGGBB 格式的颜色。"; ja="色を #RRGGBB の形式で入力してください。"; ar="اكتب اللون بالصيغة ‎#RRGGBB." }
        let numberRefused low high ={ en=sprintf "Type a whole number from %d to %d." low high; zh=sprintf "请输入 %d 到 %d 之间的整数。" low high; ja=sprintf "%d～%d の整数を入力してください。" low high; ar=sprintf "اكتب عددًا صحيحًا من %d إلى %d." low high }
        /// Beside the Alt+Enter key cap on the commands page.
        let setShortcut = { en="Set shortcut"; zh="设置快捷键"; ja="ショートカットを設定"; ar="تعيين اختصار" }
        /// Beside a workspace: how many windows it holds.
        let windows count = { en=sprintf "%d windows" count; zh=sprintf "%d 个窗口" count; ja=sprintf "%d 個のウィンドウ" count; ar=sprintf "%d نوافذ" count }
        /// Beside a setting that is on or off.
        let on = { en="On"; zh="开"; ja="オン"; ar="مفعّل" }
        let off = { en="Off"; zh="关"; ja="オフ"; ar="معطّل" }

    module AppRules =
        let addApplication = { en="Add application"; zh="添加应用"; ja="アプリを追加"; ar="إضافة تطبيق" }
        let deleteRule = { en="Delete"; zh="删除"; ja="削除"; ar="حذف" }
        let applicationRuleExists = { en="This application already has a grouping rule."; zh="这个应用已有分组规则，请编辑已有行。"; ja="このアプリには既にグループ化ルールがあります。既存の行を編集してください。"; ar="لدى هذا التطبيق قاعدة تجميع بالفعل." }
        /// Several apps sharing one named group, as opposed to each app's windows grouping by themselves.
        let groupingRules = { en="Groups across apps"; zh="跨应用分组"; ja="アプリをまたぐグループ"; ar="مجموعات تضم تطبيقات مختلفة" }
        let applications = { en="Applications"; zh="应用"; ja="アプリ"; ar="التطبيقات" }
        let addGroup = { en="Add group"; zh="添加组"; ja="グループを追加"; ar="إضافة مجموعة" }
        /// Shown while Groups across apps is empty.
        let noGroups = { en="Add a group to put several apps together, or drag tabs onto each other: the groups show here"
                         zh="点“添加组”把多个应用放在一起，或拖拽标签合并窗口，分组会显示在这里"
                         ja="「グループを追加」で複数のアプリをまとめるか、タブをドラッグして重ねると、ここに表示されます"
                         ar="أضف مجموعة لضم عدة تطبيقات معًا، أو اسحب علامات التبويب فوق بعضها؛ ستظهر المجموعات هنا" }
        /// The name a new group starts with.
        let newGroup number = { en=sprintf "Group %d" number; zh=sprintf "组 %d" number; ja=sprintf "グループ %d" number; ar=sprintf "المجموعة %d" number }
        let selectRule = { en="Select a group or rule to edit here. Changes save automatically."; zh="选中组或规则即可在此编辑，修改会自动保存。"; ja="グループかルールを選択して編集します。変更は自動保存されます。"; ar="حدد مجموعة أو قاعدة لتحريرها هنا. تُحفظ التغييرات تلقائيًا." }
        let newRule = { en="New rule"; zh="新规则"; ja="新しいルール"; ar="قاعدة جديدة" }
        let groupNameRequired = { en="Enter a group name."; zh="请填写组名。"; ja="グループ名を入力してください。"; ar="أدخل اسم المجموعة." }
        let groupNameExists = { en="A group with this name already exists."; zh="已有同名组，请使用其他名称。"; ja="同じ名前のグループが存在します。"; ar="توجد مجموعة بهذا الاسم بالفعل." }
        let groupRulesHelp = { en="Put windows of different apps into one named group. Apps added here always have tabs, leave the list above and don't use Auto-group. Windows you group by dragging tabs together show here too."
                               zh="把不同应用的窗口放进同一个命名组。加入这里的应用会始终启用标签，不再出现在上面的列表里，也不再使用“自动分组”。拖拽标签合并形成的窗口分组也会显示在这里。"
                               ja="異なるアプリのウィンドウを名前付きの一つのグループにまとめます。ここに追加したアプリは常にタブを使い、上の一覧からは外れ、「自動グループ化」も使われません。タブをドラッグしてまとめたウィンドウのグループもここに表示されます。"
                               ar="ضع نوافذ تطبيقات مختلفة في مجموعة واحدة ذات اسم. التطبيقات المضافة هنا تستخدم علامات التبويب دائمًا، وتُزال من القائمة أعلاه ولا تستخدم التجميع التلقائي. كما تظهر هنا النوافذ التي تجمعها بسحب علامات التبويب فوق بعضها." }
        let application = { en="Application"; zh="应用"; ja="アプリ"; ar="التطبيق" }
        let chooseApplication = { en="Add an app"; zh="添加应用"; ja="アプリを追加"; ar="إضافة تطبيق" }
        /// Hover tips on a group's buttons.
        let ungroup = { en="Ungroup"; zh="解除分组"; ja="グループを解除"; ar="إلغاء التجميع" }
        let removeFromGroup = { en="Remove from group"; zh="从分组中移除"; ja="グループから外す"; ar="إزالة من المجموعة" }
        let browseApplication = { en="Browse…"; zh="浏览…"; ja="参照…"; ar="استعراض…" }
        let applicationFiles = { en="Applications (*.exe)|*.exe"; zh="应用程序 (*.exe)|*.exe"; ja="アプリ (*.exe)|*.exe"; ar="التطبيقات (*.exe)|*.exe" }
        let applicationOnly = { en="Application only"; zh="仅按应用"; ja="アプリのみ"; ar="التطبيق فقط" }
        let targetGroup = { en="Target group"; zh="目标组"; ja="宛先グループ"; ar="المجموعة المستهدفة" }
        let chooseGroup = { en="Existing group"; zh="选择已有组"; ja="既存のグループ"; ar="مجموعة موجودة" }
        let enabled = { en="Enabled"; zh="启用"; ja="有効"; ar="ممكّن" }
        let addRule = { en="Add rule"; zh="添加规则"; ja="ルールを追加"; ar="إضافة قاعدة" }
        let invalidRule = { en="Invalid grouping rule"; zh="分组规则无效"; ja="無効なグループ化ルール"; ar="قاعدة التجميع غير صالحة" }
        let requiredFields = { en="Choose an application and enter a target group."; zh="请选择应用并填写目标组。"; ja="アプリを選択して宛先グループを入力してください。"; ar="اختر تطبيقًا وأدخل المجموعة المستهدفة." }
        let requiredTitle = { en="Enter a title pattern, or choose Application only."; zh="请填写标题条件，或选择“仅按应用”。"; ja="タイトル条件を入力するか「アプリのみ」を選択してください。"; ar="أدخل نمطًا للعنوان أو اختر «التطبيق فقط»." }
        let description = { en="Choose which apps get tabs. Auto-group puts the windows of an app into one group. Expand an app to see its windows."
                            zh="选择哪些应用使用标签。勾选“自动分组”后，同一应用的窗口会合在一个组里。展开应用可查看它的窗口。"
                            ja="タブを使うアプリを選びます。「自動グループ化」をオンにすると、同じアプリのウィンドウが一つのグループに入ります。展開するとウィンドウを表示します。"
                            ar="اختر التطبيقات التي تستخدم علامات التبويب. يجمع التجميع التلقائي نوافذ التطبيق الواحد في مجموعة واحدة. وسّع التطبيق لرؤية نوافذه." }
        let tabs = { en="Tabs"; zh="标签"; ja="タブ"; ar="علامات التبويب" }
        /// Each app's windows join one group of their own; named as in the tab menu.
        let autoGroup = { en="Auto-group"; zh="自动分组"; ja="自動グループ化"; ar="التجميع التلقائي" }
        /// ApplicationFrameHost.exe, which hosts every Store (UWP) app: Settings, Calculator, Photos.
        let windowsApps = { en="Windows apps"; zh="Windows 应用"; ja="Windows アプリ"; ar="تطبيقات Windows" }
        /// Said on hover over the icon of a group made by dragging tabs together, shown read-only
        /// after the named groups.
        let handMadeGroups = { en="Windows grouped by dragging"; zh="拖拽形成的窗口分组"; ja="ドラッグでまとめたウィンドウのグループ"; ar="النوافذ المجمّعة بالسحب" }
        let scanning = { en="Scanning…"; zh="正在扫描…"; ja="スキャン中…"; ar="جارٍ الفحص…" }
        let scanFailed details = { en="Scan failed: " + details; zh="扫描失败：" + details; ja="スキャンに失敗しました: " + details; ar="فشل الفحص: " + details }

    module Workspaces =
        /// The page's two parts: the groups as they are, to rearrange, over the saved workspaces.
        let groupsNow = { en="Groups now"; zh="当前分组"; ja="今のグループ"; ar="المجموعات الآن" }
        let groupsNowDescription = { en="Rearrange this desktop's tabs and panes."
                                     zh="重新安排当前桌面的标签和窗格。"
                                     ja="このデスクトップのタブとペインを並べ替えます。"
                                     ar="أعد ترتيب علامات سطح المكتب هذا وأجزائه." }
        let savedWorkspaces = { en="Saved workspaces"; zh="已保存的工作区"; ja="保存したワークスペース"; ar="مساحات العمل المحفوظة" }
        let savedDescription = { en="Save groups and positions to restore later."; zh="保存分组和位置，以后恢复。"; ja="グループと位置を保存し、あとで復元します。"; ar="احفظ المجموعات ومواضعها لاستعادتها لاحقًا." }
        let help = { en="How to use\n1  Group and position your windows, then click Save layout.\n2  Select a workspace and click Restore selected.\n3  To change an item's name or window title matching, double-click it, or point at it and click its edit button.\nA window whose title has changed since it was saved is still matched by its app.\nRestore uses open windows. If an app is not running, the result offers to launch it."
                     zh="使用说明\n1  将窗口分组并调整位置，然后点击“保存布局”。\n2  选中工作区，点击“恢复所选”。\n3  双击任意项，或将鼠标移到该项上点击编辑按钮，可修改名称或窗口标题的匹配方式。\n保存后标题发生变化的窗口，仍会按其所属应用匹配。\n恢复使用已打开的窗口；应用未运行时，可在结果中启动它。"
                     ja="使い方\n1  ウィンドウをグループ化して配置し、「配置を保存」をクリックします。\n2  ワークスペースを選び、「選択したものを復元」をクリックします。\n3  項目をダブルクリックするか、項目にポインターを合わせて編集ボタンをクリックすると、名前やタイトルの一致方法を変更できます。\n保存後にタイトルが変わったウィンドウも、アプリで照合されます。\n復元は開いているウィンドウを使います。アプリが起動していない場合は、結果から起動できます。"
                     ar="طريقة الاستخدام\n١  جمّع النوافذ واضبط مواضعها، ثم انقر «حفظ التخطيط».\n٢  حدد مساحة عمل وانقر «استعادة المحدد».\n٣  لتغيير اسم عنصر أو طريقة مطابقة عنوان نافذة، انقر عليه مرتين أو أشر إليه ثم انقر زر التحرير.\nتظل النافذة التي تغير عنوانها منذ الحفظ مطابقةً باستخدام التطبيق الخاص بها.\nتستخدم الاستعادة النوافذ المفتوحة. إذا لم يكن التطبيق قيد التشغيل، فستتيح لك النتيجة تشغيله." }
        /// Saving makes a new workspace of the windows as they are; restoring acts on the selected one.
        let saveCurrent = { en="Save workspace"; zh="保存工作区"; ja="ワークスペースを保存"; ar="حفظ مساحة العمل" }
        let restoreSelected = { en="Restore selected"; zh="恢复所选"; ja="選択したものを復元"; ar="استعادة المحدد" }
        let editTitle name = { en=sprintf "Edit %s" name; zh=sprintf "编辑 %s" name; ja=sprintf "%s を編集" name; ar=sprintf "تحرير %s" name }
        let matchMethod = { en="Match method"; zh="匹配方式"; ja="一致方法"; ar="طريقة المطابقة" }
        let exactMatch = { en="Exact match"; zh="完全匹配"; ja="完全一致"; ar="مطابقة تامة" }
        let startsWith = { en="Starts with"; zh="开头匹配"; ja="前方一致"; ar="يبدأ بـ" }
        let endsWith = { en="Ends with"; zh="结尾匹配"; ja="後方一致"; ar="ينتهي بـ" }
        let contains = { en="Contains"; zh="包含"; ja="部分一致"; ar="يحتوي على" }
        let regularExpression = { en="Regular expression"; zh="正则表达式"; ja="正規表現"; ar="تعبير نمطي" }
        let restoreTitle = { en="Workspace restore"; zh="恢复工作区"; ja="ワークスペースの復元"; ar="استعادة مساحة العمل" }
        /// The result counts windows in three states, each with its mark.
        let restoredState = { en="Restored"; zh="已恢复"; ja="復元済み"; ar="تمت الاستعادة" }
        let notFoundState = { en="Not found"; zh="未找到"; ja="見つからない"; ar="غير موجود" }
        let failedState = { en="Failed"; zh="失败"; ja="失敗"; ar="فشل" }
        /// Windows are matched by app when their title changed, so one not found is closed.
        let notFoundHint = { en="These windows are closed, or their app is not running."; zh="这些窗口已关闭，或其应用未运行。"; ja="これらのウィンドウは閉じられているか、アプリが起動していません。"; ar="هذه النوافذ مغلقة أو أن التطبيق الخاص بها لا يعمل." }
        let launchApps count =
            { en=(if count=1 then "Launch the app and retry" else sprintf "Launch %d apps and retry" count)
              zh=sprintf "启动 %d 个应用并重试" count
              ja=sprintf "%d 個のアプリを起動して再試行" count
              ar=(if count=1 then "تشغيل التطبيق وإعادة المحاولة" else sprintf "تشغيل التطبيقات (%d) وإعادة المحاولة" count) }
        let launchingApps count =
            { en=(if count=1 then "Launching the app…" else sprintf "Launching %d apps…" count)
              zh=sprintf "正在启动 %d 个应用…" count
              ja=sprintf "%d 個のアプリを起動中…" count
              ar=(if count=1 then "جارٍ تشغيل التطبيق…" else sprintf "جارٍ تشغيل التطبيقات (%d)…" count) }
        let noneSaved = { en="No workspace saved yet"; zh="尚未保存任何工作区"; ja="保存されたワークスペースはまだありません"; ar="لم يتم حفظ أي مساحة عمل بعد" }
        let invalidSetting = { en="Invalid workspace setting"; zh="工作区设置无效"; ja="ワークスペースの設定が無効です"; ar="إعداد مساحة العمل غير صالح" }
        let dataWarnings = { en="Workspace data"; zh="工作区数据"; ja="ワークスペースのデータ"; ar="بيانات مساحة العمل" }
        let titleInvalid = { en="Window title is missing or exceeds 4,096 characters."; zh="窗口标题缺失或超过 4,096 个字符。"; ja="ウィンドウのタイトルがないか、4,096 文字を超えています。"; ar="عنوان النافذة مفقود أو يتجاوز 4096 حرفًا." }
        let unknownMatchMethod = { en="Unknown title match method."; zh="未知的标题匹配方式。"; ja="タイトルの一致方法が不明です。"; ar="طريقة مطابقة العنوان غير معروفة." }
        let expectedObject = { en="Expected an object."; zh="应为对象。"; ja="オブジェクトが必要です。"; ar="كان متوقعًا وجود كائن." }
        let expectedList = { en=": expected a list."; zh="：应为列表。"; ja="：リストが必要です。"; ar=": كان متوقعًا وجود قائمة." }
        let missingPlacement = { en="Missing placement."; zh="缺少窗口位置。"; ja="ウィンドウの配置がありません。"; ar="موضع النافذة مفقود." }
        let invalidTabLayout = { en="Invalid saved tab layout; the group will use current defaults."; zh="保存的标签布局无效；该分组将使用当前默认设置。"; ja="保存されたタブ配置が無効です。グループには現在の既定値を使用します。"; ar="تخطيط علامات التبويب المحفوظ غير صالح؛ ستستخدم المجموعة الإعدادات الافتراضية الحالية." }
        let invalidSplit = { en="Invalid saved split view; the group will restore without it."; zh="保存的分屏无效；该分组恢复时不带分屏。"; ja="保存された分割表示が無効です。グループは分割なしで復元されます。"; ar="العرض المقسّم المحفوظ غير صالح؛ ستتم استعادة المجموعة بدونه." }
        let noValidWindows = { en="No valid windows in this group."; zh="此分组中没有有效窗口。"; ja="このグループに有効なウィンドウがありません。"; ar="لا توجد نوافذ صالحة في هذه المجموعة." }
        let unsupportedVersion = { en="Unsupported workspace version; saved workspaces will not be modified."
                                   zh="不支持此工作区版本；已保存的工作区不会被修改。"
                                   ja="このワークスペースのバージョンには対応していません。保存済みのワークスペースは変更されません。"
                                   ar="إصدار مساحة العمل غير مدعوم؛ لن يتم تعديل مساحات العمل المحفوظة." }

    module Diagnostics =
        let description = { en="Safe to share: no window titles, file paths or other personal information."
                            zh="可放心分享：不含窗口标题、文件路径等任何个人信息。"
                            ja="安心して共有できます：ウィンドウのタイトルやファイルのパスなどの個人情報は含まれません。"
                            ar="آمن للمشاركة: لا يحتوي على عناوين النوافذ أو مسارات الملفات أو معلومات شخصية أخرى." }
        let copyReport = { en="Copy the report, to paste into an issue"; zh="复制报告，以便粘贴到问题报告中"; ja="レポートをコピー（問題の報告に貼り付け）"; ar="نسخ التقرير للصقه في بلاغ مشكلة" }
        let includeWindowsHelp = { en="Include window details: each window's program, class and whether it gets tabs. Never titles."
                                   zh="包含窗口详情：每个窗口的程序、窗口类，以及是否会加上标签。不含标题。"
                                   ja="ウィンドウの詳細を含める：各ウィンドウのプログラム、クラス、タブが付くかどうか。タイトルは含みません。"
                                   ar="تضمين تفاصيل النوافذ: برنامج كل نافذة وفئتها وما إذا كانت تستخدم علامات تبويب. لا يتضمن العناوين أبدًا." }
        let refreshReport = { en="Refresh the report"; zh="刷新报告"; ja="レポートを更新"; ar="تحديث التقرير" }
        let reportCopied = { en="Report copied."; zh="报告已复制。"; ja="レポートをコピーしました。"; ar="تم نسخ التقرير." }
        let reportRefreshed = { en="Report refreshed."; zh="报告已刷新。"; ja="レポートを更新しました。"; ar="تم تحديث التقرير." }
        let reportTitle = { en="Troubleshooting report"; zh="故障排查报告"; ja="トラブルシューティング レポート"; ar="تقرير استكشاف الأخطاء وإصلاحها" }
        let projectPage = { en="Project page"; zh="项目主页"; ja="プロジェクトページ"; ar="صفحة المشروع" }
        let reportIssue = { en="Report an issue"; zh="报告问题"; ja="問題を報告"; ar="الإبلاغ عن مشكلة" }
        let releases = { en="Check Releases"; zh="查看发布版本"; ja="リリースを確認"; ar="التحقق من الإصدارات" }
        let releasesHint = { en="Check Releases ↗"; zh="查看发布版本 ↗"; ja="リリースを確認 ↗"; ar="التحقق من الإصدارات ↗" }
        let openCrashLog = { en="Crash log"; zh="崩溃日志"; ja="クラッシュログ"; ar="سجل الأعطال" }
