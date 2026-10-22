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
        let splitSearch = { en="Type a title or app name to fill the pane"; zh="输入标题或应用名，放入这一侧"; ja="タイトルやアプリ名を入力してペインに配置"; ar="اكتب عنوانًا أو اسم تطبيق لملء الجزء" }
        let splitShow = { en="Show split view"; zh="显示分屏"; ja="分割表示に戻す"; ar="إظهار العرض المقسّم" }
        let splitNoWindows = { en="No other windows"; zh="没有其他窗口"; ja="他のウィンドウはありません"; ar="لا توجد نوافذ أخرى" }
        let splitMerge = { en="End split view"; zh="结束分屏"; ja="分割表示を終了"; ar="إنهاء العرض المقسّم" }
        let splitMakeStacked = { en="Switch to top and bottom"; zh="切换为上下"; ja="上下に切り替え"; ar="التبديل إلى أعلى وأسفل" }
        let splitMakeSideBySide = { en="Switch to side by side"; zh="切换为左右"; ja="左右に切り替え"; ar="التبديل إلى جنبًا إلى جنب" }
        let splitSwap = { en="Swap panes"; zh="交换两侧"; ja="ペインを入れ替え"; ar="تبديل الأجزاء" }
        let splitEqual = { en="Equal panes"; zh="恢复均分"; ja="均等に分割"; ar="أجزاء متساوية" }
        let splitRatio = { en="Split divider"; zh="分屏分隔条"; ja="分割境界"; ar="فاصل التقسيم" }
        let splitFailed = { en="These windows could not fit the split layout. Split view has ended."; zh="窗口无法适应分屏尺寸，已退出分屏。"; ja="ウィンドウを分割領域に配置できないため、分割を終了しました。"; ar="تعذّر احتواء هذه النوافذ في تخطيط التقسيم. انتهى العرض المقسّم." }
        let position = { en="Tab position"; zh="标签位置"; ja="タブの位置"; ar="موضع علامات التبويب" }
        let top = { en="Top"; zh="顶部"; ja="上部"; ar="أعلى" }
        let showTabTitles = { en="Show tab titles"; zh="显示标签标题"; ja="タブのタイトルを表示"; ar="إظهار عناوين علامات التبويب" }
        let showIconsOnly = { en="Show icons only"; zh="仅显示图标"; ja="アイコンのみ表示"; ar="إظهار الأيقونات فقط" }
        let newTab = { en="New tab"; zh="新建标签"; ja="新しいタブ"; ar="علامة تبويب جديدة" }
        let renameTab = { en="Rename"; zh="重命名"; ja="名前を変更"; ar="إعادة تسمية" }
        let restoreTabName = { en="Reset name"; zh="重置名称"; ja="名前をリセット"; ar="إعادة تعيين الاسم" }
        let sortTabs = { en="Sort by app and title"; zh="按应用和标题排序"; ja="アプリとタイトル順に並べ替え"; ar="ترتيب حسب التطبيق والعنوان" }
        let combineTaskbarIcons = { en="Combine taskbar icons"; zh="合并任务栏图标"; ja="タスクバーアイコンをまとめる"; ar="دمج أيقونات شريط المهام" }
        /// For the group's own taskbar button, shown while its icons are combined.
        let changeTaskbarIcon = { en="Change taskbar icon…"; zh="更换任务栏图标…"; ja="タスクバーアイコンを変更…"; ar="تغيير أيقونة شريط المهام…" }
        let restoreTaskbarIcon = { en="Restore taskbar icon"; zh="恢复默认任务栏图标"; ja="タスクバーアイコンを元に戻す"; ar="استعادة أيقونة شريط المهام" }
        /// The kinds named with their extensions, so programs are seen to be taken too.
        let taskbarIconFiles = { en="Icon files (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|All files (*.*)|*.*"
                                 zh="图标文件 (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|所有文件 (*.*)|*.*"
                                 ja="アイコン ファイル (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|すべてのファイル (*.*)|*.*"
                                 ar="ملفات الأيقونات (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|كل الملفات (*.*)|*.*" }
        let numberShortcut = { en="Switch by number"; zh="数字键切换"; ja="番号で切り替え"; ar="التبديل بالأرقام" }
        let enableTabs = { en="Enable tabs"; zh="启用标签"; ja="タブを有効にする"; ar="تمكين علامات التبويب" }
        let autoGroupWindows = { en="Auto-group windows"; zh="自动分组"; ja="ウィンドウを自動グループ化"; ar="تجميع النوافذ تلقائيًا" }
        let close = { en="Close"; zh="关闭"; ja="閉じる"; ar="إغلاق" }
        let closeOthers = { en="Close others"; zh="关闭其他"; ja="他を閉じる"; ar="إغلاق علامات التبويب الأخرى" }
        let closeAllOf exe = { en=sprintf "Close all '%s' windows" exe; zh=sprintf "关闭所有“%s”窗口" exe; ja=sprintf "「%s」のウィンドウをすべて閉じる" exe; ar=sprintf "إغلاق جميع نوافذ «%s»" exe }
        let closeAll = { en="Close all"; zh="关闭全部"; ja="すべて閉じる"; ar="إغلاق الكل" }
        let settings = { en="Settings..."; zh="设置..."; ja="設定..."; ar="الإعدادات..." }

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
        let private colorWords = { en="color colour"; zh="颜色 配色"; ja="色 カラー"; ar="اللون الألوان" }
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
                               description={ en="Puts open windows back into the groups, positions and split views they had, optionally launching missing apps first."
                                             zh="把已打开的窗口放回原来的分组、位置和分屏，可选先启动缺少的应用。"
                                             ja="開いているウィンドウを元のグループ、位置、分割表示に戻します。足りないアプリを先に起動することもできます。"
                                             ar="يعيد النوافذ المفتوحة إلى مجموعاتها ومواضعها وعروضها المقسّمة السابقة، مع تشغيل التطبيقات الناقصة أولًا اختياريًا." }
                               keywords={ en="session reboot"; zh="会话 重启"; ja="セッション 再起動"; ar="جلسة إعادة التشغيل" } }
        let enableTabsByDefault = { caption={ en="Use tabs for new apps"; zh="新应用自动启用标签"; ja="新しいアプリでタブを使う"; ar="استخدام علامات التبويب للتطبيقات الجديدة" }
                                    description={ en="Only apps first discovered after this change. Existing apps keep their tab setting."; zh="只影响此后首次出现的应用。已有应用保留原来的标签设置。"; ja="変更後に初めて検出するアプリに適用します。既存のアプリのタブ設定は維持します。"; ar="ينطبق ذلك على التطبيقات التي يكتشفها البرنامج لأول مرة بعد هذا التغيير فقط. وتحتفظ التطبيقات الموجودة بإعداد علامات التبويب الحالي." }
                                    keywords={ en=""; zh=""; ja=""; ar="" } }
        let dimInactiveGroups = { caption={ en="Dim tabs in inactive groups"; zh="淡化非活动分组的标签"; ja="非アクティブなグループのタブの色を薄くする"; ar="تخفيف لون علامات التبويب في المجموعات غير النشطة" }
                                  description={ en="Make the active group easier to spot."; zh="让当前活动的分组更容易辨认。"; ja="アクティブなグループを見分けやすくします。"; ar="يسهّل تمييز المجموعة النشطة." }
                                  keywords={ en="fade"; zh="变暗"; ja="薄く 暗く"; ar="تعتيم" } }
        let autoHide = { caption={ en="Auto-hide tabs"; zh="自动隐藏标签"; ja="タブを自動的に隠す"; ar="إخفاء علامات التبويب تلقائيًا" }
                         description={ en="Show a thin strip until hover."; zh="只显示细栏，悬停展开。"; ja="細いバーだけ表示し、ホバーで展開します。"; ar="يبقى شريط رفيع فقط حتى تمرّر المؤشر فوقه." }
                         keywords={ en="collapse"; zh="收起"; ja="折りたたみ"; ar="طيّ" } }
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
        let private splitWords = { en="split view pane"; zh="分屏 窗格"; ja="分割 ペイン"; ar="عرض مقسّم جزء" }
        let splitPickRight = { caption={ en="Window on the right…"; zh="在右侧放入窗口…"; ja="右側にウィンドウを配置…"; ar="نافذة على اليمين…" }
                               description={ en="Opens the list of windows to put beside the tab in front, with an empty pane first."; zh="打开窗口列表，选中的放到当前标签右边；第一项是空白窗格。"; ja="前面のタブの隣に置くウィンドウの一覧を開きます。先頭は空のペインです。"; ar="يفتح قائمة النوافذ لوضع إحداها بجانب علامة التبويب الأمامية، وأولها جزء فارغ." }
                               keywords=splitWords }
        let splitPickBelow = { caption={ en="Window below…"; zh="在下方放入窗口…"; ja="下側にウィンドウを配置…"; ar="نافذة في الأسفل…" }
                               description=none
                               keywords=splitWords }
        let splitFocusLeft = { caption={ en="Focus the pane to the left"; zh="焦点切到左边的格"; ja="左のペインにフォーカス"; ar="التركيز على الجزء الأيسر" }
                               description=none
                               keywords=splitWords }
        let splitFocusRight = { caption={ en="Focus the pane to the right"; zh="焦点切到右边的格"; ja="右のペインにフォーカス"; ar="التركيز على الجزء الأيمن" }
                                description=none
                                keywords=splitWords }
        let splitFocusUp = { caption={ en="Focus the pane above"; zh="焦点切到上边的格"; ja="上のペインにフォーカス"; ar="التركيز على الجزء الأعلى" }
                             description=none
                             keywords=splitWords }
        let splitFocusDown = { caption={ en="Focus the pane below"; zh="焦点切到下边的格"; ja="下のペインにフォーカス"; ar="التركيز على الجزء الأسفل" }
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
        let numberLeader = { caption={ en="Tab selection leader"; zh="标签选择引导键"; ja="タブ選択のリーダーキー"; ar="المفتاح التمهيدي لاختيار علامة التبويب" }; description=none; keywords=none }
        let tabColors = { en="Color"; zh="颜色"; ja="色"; ar="اللون" }
        let customTabColor = { en="Custom…"; zh="自定义…"; ja="カスタム…"; ar="مخصص…" }
        let clearTabColor = { en="Clear color"; zh="清除颜色"; ja="色を解除"; ar="مسح اللون" }
        let rememberTabColor = { en="Remember color for current app"; zh="为当前应用记住颜色"; ja="現在のアプリの色を記憶"; ar="تذكّر اللون للتطبيق الحالي" }
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
        let tabTextColor = { caption={ en="Text and close button"; zh="文字与关闭按钮"; ja="文字と閉じるボタン"; ar="النص وزر الإغلاق" }; description=none; keywords=colorWords }
        let tabNormalBgColor = { caption={ en="Inactive tab"; zh="非活动标签"; ja="非アクティブなタブ"; ar="علامة تبويب غير نشطة" }; description=none; keywords=colorWords }
        let tabActiveBgColor = { caption={ en="Active tab"; zh="活动标签"; ja="アクティブなタブ"; ar="علامة تبويب نشطة" }; description=none; keywords=colorWords }
        let tabHighlightBgColor = { caption={ en="Hovered tab"; zh="悬停标签"; ja="ホバー中のタブ"; ar="علامة التبويب التي يشير إليها المؤشر" }; description=none; keywords=colorWords }
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
    module Session =
        let restoring = { en="Restoring the last session…"; zh="正在恢复上次会话…"; ja="前回のセッションを復元しています…"; ar="جارٍ استعادة الجلسة الأخيرة…" }
        let launching count =
            { en=(if count=1 then "Starting an app for the last session…" else sprintf "Starting %d apps for the last session…" count)
              zh=sprintf "正在为上次会话启动 %d 个应用…" count
              ja=sprintf "前回のセッションのためにアプリを %d 個起動しています…" count
              ar=sprintf "جارٍ تشغيل %d من التطبيقات للجلسة الأخيرة…" count }
        let restored restored missing =
            { en=(sprintf "Session restored: %d windows" restored)+(if missing=0 then "" else sprintf ", %d not found" missing)
              zh=(sprintf "已恢复上次会话：%d 个窗口" restored)+(if missing=0 then "" else sprintf "，%d 个未找到" missing)
              ja=(sprintf "前回のセッションを復元しました: %d 個のウィンドウ" restored)+(if missing=0 then "" else sprintf "、%d 個が見つかりません" missing)
              ar=(sprintf "تمت استعادة الجلسة: %d نافذة" restored)+(if missing=0 then "" else sprintf "، %d غير موجودة" missing) }

    module General =
        /// The choices of Restore the last session at start.
        let sessionOff = { en="Off"; zh="关闭"; ja="オフ"; ar="إيقاف" }
        let sessionWindows = { en="Open windows only"; zh="仅已打开的窗口"; ja="開いているウィンドウのみ"; ar="النوافذ المفتوحة فقط" }
        let sessionApps = { en="Open windows, and start their apps"; zh="已打开的窗口，并启动缺少的应用"; ja="開いているウィンドウと、アプリの起動"; ar="النوافذ المفتوحة، مع تشغيل تطبيقاتها" }
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
        let quickColorNames = [|
            { en="Black"; zh="黑色"; ja="黒"; ar="أسود" }
            { en="White"; zh="白色"; ja="白"; ar="أبيض" }
            { en="Gray"; zh="灰色"; ja="グレー"; ar="رمادي" }
            { en="Red"; zh="红色"; ja="赤"; ar="أحمر" }
            { en="Orange"; zh="橙色"; ja="オレンジ"; ar="برتقالي" }
            { en="Yellow"; zh="黄色"; ja="黄"; ar="أصفر" }
            { en="Green"; zh="绿色"; ja="緑"; ar="أخضر" }
            { en="Blue"; zh="蓝色"; ja="青"; ar="أزرق" } |]
        let pickerHelp = { en="Color picker: arrow keys adjust saturation and brightness; Shift + arrow keys adjust hue"
                           zh="颜色选择器：方向键调整饱和度和亮度；Shift + 方向键调整色相"
                           ja="色の選択：方向キーで彩度と明るさを調整し、Shift + 方向キーで色相を調整します"
                           ar="منتقي الألوان: تعدّل مفاتيح الأسهم التشبع والسطوع؛ وتعدّل الأسهم مع Shift درجة اللون" }
        let chooseColor = { en="Choose color"; zh="选择颜色"; ja="色を選択"; ar="اختيار اللون" }
        let decrease = { en="Decrease"; zh="减小"; ja="減らす"; ar="تقليل" }
        let increase = { en="Increase"; zh="增大"; ja="増やす"; ar="زيادة" }

    module Shortcuts =
        let keyboard = { en="Keyboard"; zh="键盘"; ja="キーボード"; ar="لوحة المفاتيح" }
        let mouse = { en="Mouse"; zh="鼠标"; ja="マウス"; ar="الفأرة" }
        let splitView = { en="Split view"; zh="分屏"; ja="分割表示"; ar="العرض المقسّم" }
        let keyboardNote = { en="Click a shortcut, then press the new combination. Esc cancels."
                             zh="点击快捷键后按下新的组合键。按 Esc 取消。"
                             ja="ショートカットをクリックして、新しいキーの組み合わせを押します。Esc でキャンセルします。"
                             ar="انقر اختصارًا ثم اضغط مجموعة المفاتيح الجديدة. اضغط Esc للإلغاء." }
        let restoreDefaults = { en="Restore default shortcuts"; zh="恢复默认快捷键"; ja="既定のショートカットに戻す"; ar="استعادة الاختصارات الافتراضية" }
        let inUse = { en="Shortcut already in use"; zh="快捷键已被占用"; ja="ショートカットは使用中です"; ar="الاختصار مستخدم بالفعل" }
        let usedBy name = { en=sprintf "Used by %s" name; zh=sprintf "已用于“%s”" name; ja=sprintf "「%s」で使用中" name; ar=sprintf "يستخدمه «%s»" name }
        // Shortcut field messages are kept short to fit.
        let alreadySet = { en="Already set"; zh="已是当前快捷键"; ja="設定済み"; ar="هذا هو الاختصار الحالي" }
        let includeCtrlOrAlt = { en="Include Ctrl or Alt"; zh="需要包含 Ctrl 或 Alt"; ja="Ctrl か Alt が必要です"; ar="يجب أن يتضمن Ctrl أو Alt" }
        let pressShortcut = { en="Press a shortcut…"; zh="按下组合键…"; ja="キーを押してください…"; ar="اضغط اختصارًا…" }
        let notSet = { en="Not set"; zh="未设置"; ja="未設定"; ar="غير معيّن" }

    /// The tab search box.
    module TabSearch =
        let prompt = { en="Type a title or app name to switch windows"; zh="输入标题或应用名，切换窗口"; ja="タイトルやアプリ名を入力してウィンドウを切り替え"; ar="اكتب عنوانًا أو اسم تطبيق للتبديل بين النوافذ" }
        let noMatches = { en="No matching windows."; zh="没有匹配的窗口。"; ja="一致するウィンドウはありません。"; ar="لا توجد نوافذ مطابقة." }
        /// Beside a window on another virtual desktop that has no name of its own.
        let desktop number = { en=sprintf "Desktop %d" number; zh=sprintf "桌面 %d" number; ja=sprintf "デスクトップ %d" number; ar=sprintf "سطح المكتب %d" number }
        let otherDesktop = { en="Another desktop"; zh="其他桌面"; ja="別のデスクトップ"; ar="سطح مكتب آخر" }
        let groupTabs count = { en=sprintf "This group · %d" count; zh=sprintf "当前分组 · %d" count; ja=sprintf "このグループ · %d" count; ar=sprintf "هذه المجموعة · %d" count }
        let allTabs count = { en=sprintf "All windows · %d" count; zh=sprintf "全局窗口 · %d" count; ja=sprintf "すべてのウィンドウ · %d" count; ar=sprintf "كل النوافذ · %d" count }

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
        let description = { en="Save and restore window groups and positions."; zh="保存和恢复窗口分组与位置。"; ja="ウィンドウのグループと位置を保存・復元します。"; ar="حفظ مجموعات النوافذ ومواضعها واستعادتها." }
        let help = { en="How to use\n1  Group and position your windows, then click Save layout.\n2  Select a workspace and click Restore selected.\n3  To change an item's name or window title matching, double-click it, or point at it and click its edit button.\nA window whose title has changed since it was saved is still matched by its app.\nRestore uses open windows. If an app is not running, the result offers to launch it."
                     zh="使用说明\n1  将窗口分组并调整位置，然后点击“保存布局”。\n2  选中工作区，点击“恢复所选”。\n3  双击任意项，或将鼠标移到该项上点击编辑按钮，可修改名称或窗口标题的匹配方式。\n保存后标题发生变化的窗口，仍会按其所属应用匹配。\n恢复使用已打开的窗口；应用未运行时，可在结果中启动它。"
                     ja="使い方\n1  ウィンドウをグループ化して配置し、「配置を保存」をクリックします。\n2  ワークスペースを選び、「選択したものを復元」をクリックします。\n3  項目をダブルクリックするか、項目にポインターを合わせて編集ボタンをクリックすると、名前やタイトルの一致方法を変更できます。\n保存後にタイトルが変わったウィンドウも、アプリで照合されます。\n復元は開いているウィンドウを使います。アプリが起動していない場合は、結果から起動できます。"
                     ar="طريقة الاستخدام\n١  جمّع النوافذ واضبط مواضعها، ثم انقر «حفظ التخطيط».\n٢  حدد مساحة عمل وانقر «استعادة المحدد».\n٣  لتغيير اسم عنصر أو طريقة مطابقة عنوان نافذة، انقر عليه مرتين أو أشر إليه ثم انقر زر التحرير.\nتظل النافذة التي تغير عنوانها منذ الحفظ مطابقةً باستخدام التطبيق الخاص بها.\nتستخدم الاستعادة النوافذ المفتوحة. إذا لم يكن التطبيق قيد التشغيل، فستتيح لك النتيجة تشغيله." }
        /// Saving makes a new workspace of the windows as they are; restoring acts on the selected one.
        let saveCurrent = { en="Save layout"; zh="保存布局"; ja="配置を保存"; ar="حفظ التخطيط" }
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
