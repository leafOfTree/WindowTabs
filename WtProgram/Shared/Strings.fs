namespace Bemo

/// Caption, description and extra search words (synonyms not already in the caption or
/// description) of one entry in SettingsCatalog.
type SettingText = { caption:LocalizedText; description:LocalizedText; keywords:LocalizedText }

/// Every user-visible text, in every supported language. Show one with `tr`.
/// Text with values filled in is a function, so the format is written once per language.
module Strings =
    let private none = { en=""; zh=""; ja=""; ar=""; ko="" }

    module Common =
        let ok = { en="OK"; zh="确定"; ja="OK"; ar="موافق"; ko="확인" }
        let cancel = { en="Cancel"; zh="取消"; ja="キャンセル"; ar="إلغاء"; ko="취소" }
        let save = { en="Save"; zh="保存"; ja="保存"; ar="حفظ"; ko="저장" }
        let refresh = { en="Refresh"; zh="刷新"; ja="更新"; ar="تحديث"; ko="새로 고침" }
        let name = { en="Name"; zh="名称"; ja="名称"; ar="الاسم"; ko="이름" }
        let title = { en="Title"; zh="标题"; ja="タイトル"; ar="العنوان"; ko="제목" }
        let left = { en="Left"; zh="左侧"; ja="左"; ar="يسار"; ko="왼쪽" }
        let top = { en="Top"; zh="靠上"; ja="上"; ar="أعلى"; ko="위쪽" }
        let bottom = { en="Bottom"; zh="靠下"; ja="下"; ar="أسفل"; ko="아래쪽" }
        let center = { en="Center"; zh="居中"; ja="中央"; ar="الوسط"; ko="가운데" }
        let right = { en="Right"; zh="右侧"; ja="右"; ar="يمين"; ko="오른쪽" }
        let never = { en="Never"; zh="从不"; ja="しない"; ar="أبدًا"; ko="안 함" }
        let whenMaximized = { en="Maximized"; zh="最大化时"; ja="最大化時"; ar="عند التكبير"; ko="최대화 시" }
        let always = { en="Always"; zh="始终"; ja="常に"; ar="دائمًا"; ko="항상" }
        let operationFailed = { en="Operation failed"; zh="操作失败"; ja="操作に失敗しました"; ar="فشلت العملية"; ko="작업에 실패했습니다" }

    module Messages =
        let alreadyRunning = { en="WindowTabs is already running."; zh="WindowTabs 已在运行。"; ja="WindowTabs は既に実行中です。"; ar="برنامج WindowTabs قيد التشغيل بالفعل."; ko="WindowTabs가 이미 실행 중입니다." }
        let couldNotContinue = { en="WindowTabs could not continue"; zh="WindowTabs 无法继续运行"; ja="WindowTabs を続行できません"; ar="تعذّر على WindowTabs المتابعة"; ko="WindowTabs를 계속 실행할 수 없습니다" }
        let settingsSaveFailed = { en="Settings save failed"; zh="设置保存失败"; ja="設定を保存できませんでした"; ar="فشل حفظ الإعدادات"; ko="설정을 저장하지 못했습니다" }
        let unableToSaveSettings path details =
            { en=sprintf "Unable to save settings to %s.\nYour changes remain in memory and will be retried on the next edit or exit.\n\n%s" path details
              zh=sprintf "无法保存设置到 %s。\n更改仍保留在内存中，下次修改或退出时会重试。\n\n%s" path details
              ja=sprintf "設定を %s に保存できませんでした。\n変更はメモリに保持され、次の編集時または終了時に再試行されます。\n\n%s" path details
              ar=sprintf "تعذّر حفظ الإعدادات في %s.\nستظل تغييراتك في الذاكرة وستتم إعادة المحاولة عند التعديل التالي أو عند الخروج.\n\n%s" path details
              ko=sprintf "설정을 %s에 저장할 수 없습니다.\n변경 내용은 메모리에 유지되며, 다음에 편집하거나 종료할 때 다시 저장합니다.\n\n%s" path details }
        let settingsError = { en="Settings error"; zh="设置错误"; ja="設定エラー"; ar="خطأ في الإعدادات"; ko="설정 오류" }
        let errorLoadingSettings path details =
            { en=sprintf "Error loading settings.\n\nFix or remove the file %s.\n\nDetails: %s" path details
              zh=sprintf "读取设置出错。\n\n请修复或删除文件 %s。\n\n详细信息：%s" path details
              ja=sprintf "設定の読み込み中にエラーが発生しました。\n\nファイル %s を修正または削除してください。\n\n詳細: %s" path details
              ar=sprintf "حدث خطأ أثناء تحميل الإعدادات.\n\nأصلح الملف %s أو احذفه.\n\nالتفاصيل: %s" path details
              ko=sprintf "설정을 불러오는 중 오류가 발생했습니다.\n\n%s 파일을 수정하거나 삭제하세요.\n\n세부 정보: %s" path details }

    /// Tray icon menu.
    module Tray =
        let exit = { en="Exit WindowTabs"; zh="退出 WindowTabs"; ja="WindowTabs を終了"; ar="إنهاء WindowTabs"; ko="WindowTabs 종료" }
        let errorTitle = { en="WindowTabs ran into a problem"; zh="WindowTabs 遇到了问题"; ja="WindowTabs で問題が発生しました"; ar="واجه WindowTabs مشكلة"; ko="WindowTabs에 문제가 발생했습니다" }
        let errorText = { en="It kept running. The details are in the crash log; click to show it."
                          zh="程序仍在运行。详细信息已写入崩溃日志，点击查看。"
                          ja="動作は続いています。詳細はクラッシュログにあります。クリックすると表示します。"
                          ar="استمر البرنامج في العمل. التفاصيل موجودة في سجل الأعطال؛ انقر لعرضه."
                          ko="계속 실행 중입니다. 클릭하면 충돌 로그에서 자세한 내용을 볼 수 있습니다." }

    /// Tab context menu.
    module SwitchScope =
        let group = { en="In group"; zh="组内"; ja="グループ内"; ar="داخل المجموعة"; ko="그룹 내" }

    module TabMenu =
        let closeSelected = { en="Close selected tabs"; zh="关闭选中的标签"; ja="選択したタブを閉じる"; ar="إغلاق علامات التبويب المحددة"; ko="선택한 탭 닫기" }
        let windowBehavior = { en="Window behavior"; zh="窗口行为"; ja="ウィンドウの動作"; ar="سلوك النوافذ"; ko="창 동작" }
        let groupTopmost = { en="Pin group on top"; zh="分组置顶"; ja="グループを常に手前に表示"; ar="إبقاء المجموعة في المقدمة"; ko="그룹을 항상 위에 고정" }
        let keepWindowSizes = { en="Keep each window’s size and position"; zh="保留各窗口的大小和位置"; ja="各ウィンドウのサイズと位置を保持"; ar="الاحتفاظ بحجم كل نافذة وموضعها"; ko="각 창의 크기와 위치 유지" }
        let transparentMode = { en="Keep inactive windows off-screen"; zh="将同组后台窗口移出屏幕"; ja="グループ内の非アクティブなウィンドウを画面外に置く"; ar="إبقاء النوافذ غير النشطة في المجموعة خارج الشاشة"; ko="비활성 창을 화면 밖에 두기" }
        let transparentModeNote = { en="Useful for transparent windows."; zh="适合透明窗口。"; ja="半透明のウィンドウに便利です。"; ar="مفيد للنوافذ الشفافة."; ko="투명한 창에 유용합니다." }
        /// Under the alignments: the tabs can also be dragged along their edge with the right button.
        let dragToPlace = { en="Hold the right button on a tab, then drag anywhere"; zh="在标签上按住右键，拖动到任意位置"; ja="タブを右ボタンで押したまま、好きな位置へドラッグ"; ar="اضغط بالزر الأيمن على علامة تبويب، ثم اسحب إلى أي مكان"; ko="탭을 오른쪽 버튼으로 누른 채 원하는 곳에 끌어다 놓기" }
        /// In place of the hint once the row has been dragged, marked as the alignments are.
        let draggedPlace = { en="Dragged to a place of its own"; zh="已拖动到自定义位置"; ja="ドラッグした位置に配置済み"; ar="تم سحبها إلى موضع خاص"; ko="끌어다 놓은 위치" }
        let split = { en="Split view"; zh="组内分屏"; ja="分割表示"; ar="العرض المقسّم"; ko="분할 보기" }
        /// A window chosen from a list goes on that side of the tab.
        let splitPickRight = { en="Window on the right…"; zh="在右侧放入窗口…"; ja="右側にウィンドウを配置…"; ar="نافذة على اليمين…"; ko="오른쪽에 창 배치…" }
        let splitPickBelow = { en="Window below…"; zh="在下方放入窗口…"; ja="下側にウィンドウを配置…"; ar="نافذة في الأسفل…"; ko="아래쪽에 창 배치…" }
        let splitPickLeft = { en="Window on the left…"; zh="在左侧放入窗口…"; ja="左側にウィンドウを配置…"; ar="نافذة على اليسار…"; ko="왼쪽에 창 배치…" }
        let splitPickAbove = { en="Window above…"; zh="在上方放入窗口…"; ja="上側にウィンドウを配置…"; ar="نافذة في الأعلى…"; ko="위쪽에 창 배치…" }
        let splitEmpty = { en="Drop a tab or window here"; zh="将标签或窗口拖到这里"; ja="ここにタブまたはウィンドウをドロップ"; ar="أفلت علامة تبويب أو نافذة هنا"; ko="여기에 탭이나 창을 놓으세요" }
        let splitChoose = { en="Choose a window…"; zh="选择窗口…"; ja="ウィンドウを選択…"; ar="اختر نافذة…"; ko="창 선택…" }
        let splitOr = { en="or"; zh="或"; ja="または"; ar="أو"; ko="또는" }
        let addTabHint multiline =
            let gap = if multiline then "\n" else " "
            { en="Left-click to open a new tab."+gap+"Right-click to add an existing window."
              zh="左键新建标签。"+(if multiline then gap else "")+"右键添加已有窗口。"
              ja="左クリックで新しいタブを開きます。"+(if multiline then gap else "")+"右クリックで既存のウィンドウを追加します。"
              ar="انقر بزر الفأرة الأيسر لفتح علامة تبويب جديدة."+gap+"انقر بزر الفأرة الأيمن لإضافة نافذة موجودة."
              ko="왼쪽 클릭으로 새 탭을 엽니다."+gap+"오른쪽 클릭으로 기존 창을 추가합니다." }
        let click = { en="Click"; zh="单击"; ja="クリック"; ar="نقر"; ko="클릭" }
        let pickAdd = { en="Add"; zh="添加"; ja="追加"; ar="إضافة"; ko="추가" }
        let pickToggle = { en="Toggle"; zh="增减选择"; ja="選択切替"; ar="تبديل"; ko="선택 전환" }
        let pickMultiple = { en="Multi-select"; zh="多选"; ja="複数選択"; ar="تحديد متعدد"; ko="다중 선택" }
        let pickRange = { en="Range"; zh="连续选择"; ja="範囲選択"; ar="تحديد نطاق"; ko="범위 선택" }
        let confirmSelection = { en="Confirm"; zh="确认"; ja="確定"; ar="تأكيد"; ko="확인" }
        let addExisting = { en="Add an existing window"; zh="添加已有窗口"; ja="既存のウィンドウを追加"; ar="إضافة نافذة موجودة"; ko="기존 창 추가" }
        let splitSearch = { en="Type a title or app name to fill the pane"; zh="输入标题或应用名，放入这个窗格"; ja="タイトルやアプリ名を入力してペインに配置"; ar="اكتب عنوانًا أو اسم تطبيق لملء الجزء"; ko="제목이나 앱 이름을 입력해 창 영역 채우기" }
        let splitShow = { en="Show split view"; zh="显示分屏"; ja="分割表示に戻す"; ar="إظهار العرض المقسّم"; ko="분할 보기 표시" }
        let splitNoWindows = { en="No other windows"; zh="没有其他窗口"; ja="他のウィンドウはありません"; ar="لا توجد نوافذ أخرى"; ko="다른 창 없음" }
        let splitMerge = { en="End split view"; zh="结束分屏"; ja="分割表示を終了"; ar="إنهاء العرض المقسّم"; ko="분할 보기 종료" }
        let splitMakeStacked = { en="Switch to top and bottom"; zh="切换为上下"; ja="上下に切り替え"; ar="التبديل إلى أعلى وأسفل"; ko="위아래로 전환" }
        let splitMakeSideBySide = { en="Switch to side by side"; zh="切换为左右"; ja="左右に切り替え"; ar="التبديل إلى جنبًا إلى جنب"; ko="좌우로 전환" }
        let splitSwap = { en="Swap panes"; zh="交换窗格"; ja="ペインを入れ替え"; ar="تبديل الأجزاء"; ko="창 영역 바꾸기" }
        let splitEqual = { en="Equal panes"; zh="均分窗格"; ja="均等に分割"; ar="أجزاء متساوية"; ko="창 영역 균등 분할" }
        let splitRatio = { en="Split divider"; zh="分屏分隔条"; ja="分割境界"; ar="فاصل التقسيم"; ko="분할 구분선" }
        let splitFailed = { en="These windows could not fit the split layout. Split view has ended."; zh="窗口无法适应分屏尺寸，已退出分屏。"; ja="ウィンドウを分割領域に配置できないため、分割を終了しました。"; ar="تعذّر احتواء هذه النوافذ في تخطيط التقسيم. انتهى العرض المقسّم."; ko="창을 분할 레이아웃에 맞출 수 없어 분할 보기를 종료했습니다." }
        let top = { en="Top"; zh="顶部"; ja="上部"; ar="أعلى"; ko="위쪽" }
        let showIconsOnly = { en="Show icons only"; zh="仅显示图标"; ja="アイコンのみ表示"; ar="إظهار الأيقونات فقط"; ko="아이콘만 표시" }
        let newTab = { en="New tab"; zh="新建标签"; ja="新しいタブ"; ar="علامة تبويب جديدة"; ko="새 탭" }
        let renameTab = { en="Rename"; zh="重命名"; ja="名前を変更"; ar="إعادة تسمية"; ko="이름 바꾸기" }
        let restoreTabName = { en="Reset name"; zh="重置名称"; ja="名前をリセット"; ar="إعادة تعيين الاسم"; ko="이름 초기화" }
        let pinTab = { en="Pin"; zh="固定"; ja="固定"; ar="تثبيت"; ko="고정" }
        let unpinTab = { en="Unpin"; zh="取消固定"; ja="固定を解除"; ar="إلغاء التثبيت"; ko="고정 해제" }
        let sortTabs = { en="Sort by app and title"; zh="按应用和标题排序"; ja="アプリとタイトル順に並べ替え"; ar="ترتيب حسب التطبيق والعنوان"; ko="앱 및 제목순 정렬" }
        let combineTaskbarIcons = { en="Combine taskbar icons"; zh="合并任务栏图标"; ja="タスクバーアイコンをまとめる"; ar="دمج أيقونات شريط المهام"; ko="작업 표시줄 아이콘 합치기" }
        /// Shown greyed out under "Combine taskbar icons" while the group's windows keep their own buttons.
        let combineBlockedPermissions = { en="Not combined: WindowTabs and these windows run with different rights (one as administrator)"
                                          zh="未合并：WindowTabs 与这些窗口的权限不同（其中一方以管理员身份运行）"
                                          ja="まとめられていません: WindowTabs とこれらのウィンドウの権限が異なります (一方が管理者として実行中)"
                                          ar="لم يتم الدمج: يعمل WindowTabs وهذه النوافذ بصلاحيات مختلفة (أحدهما كمسؤول)"
                                          ko="합치지 않음: WindowTabs와 이 창들의 권한이 다릅니다(한쪽이 관리자로 실행 중)" }
        let combineBlockedDesktop = { en="Not combined: Windows has not told which virtual desktop these windows are on"
                                      zh="未合并：Windows 未提供这些窗口所在的虚拟桌面"
                                      ja="まとめられていません: これらのウィンドウがどの仮想デスクトップにあるか Windows から取得できません"
                                      ar="لم يتم الدمج: لم يُحدّد Windows سطح المكتب الافتراضي الذي توجد عليه هذه النوافذ"
                                      ko="합치지 않음: Windows가 이 창들의 가상 데스크톱을 알려 주지 않았습니다" }
        let combineBlockedPlacement = { en="Not combined: Windows did not let the taskbar button move to this virtual desktop"
                                        zh="未合并：Windows 未允许将任务栏按钮移到此虚拟桌面"
                                        ja="まとめられていません: Windows がタスクバー ボタンをこの仮想デスクトップへ移動できませんでした"
                                        ar="لم يتم الدمج: لم يسمح Windows بنقل زر شريط المهام إلى سطح المكتب الافتراضي هذا"
                                        ko="합치지 않음: Windows가 작업 표시줄 버튼을 이 가상 데스크톱으로 옮기지 못했습니다" }
        /// For the group's own taskbar button, shown while its icons are combined.
        let changeTaskbarIcon = { en="Change taskbar icon…"; zh="更换任务栏图标…"; ja="タスクバーアイコンを変更…"; ar="تغيير أيقونة شريط المهام…"; ko="작업 표시줄 아이콘 변경…" }
        let restoreTaskbarIcon = { en="Restore taskbar icon"; zh="恢复默认任务栏图标"; ja="タスクバーアイコンを元に戻す"; ar="استعادة أيقونة شريط المهام"; ko="작업 표시줄 아이콘 복원" }
        /// The kinds named with their extensions, so programs are seen to be taken too.
        let taskbarIconFiles = { en="Icon files (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|All files (*.*)|*.*"
                                 zh="图标文件 (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|所有文件 (*.*)|*.*"
                                 ja="アイコン ファイル (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|すべてのファイル (*.*)|*.*"
                                 ar="ملفات الأيقونات (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|كل الملفات (*.*)|*.*"
                                 ko="아이콘 파일 (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|모든 파일 (*.*)|*.*" }
        let numberShortcut = { en="Switch tabs by number"; zh="按数字切换标签"; ja="番号でタブを切り替え"; ar="التبديل بين علامات التبويب باستخدام الأرقام"; ko="번호로 탭 전환" }
        let tabShortcuts = { en="Use tab shortcuts"; zh="使用标签快捷键"; ja="タブのショートカットを使う"; ar="استخدام اختصارات علامات التبويب"; ko="탭 단축키 사용" }
        let enableTabs = { en="Use tabs"; zh="使用标签"; ja="タブを使う"; ar="استخدام علامات التبويب"; ko="탭 사용" }
        let autoGroupWindows = { en="Auto-group windows"; zh="自动分组"; ja="ウィンドウを自動グループ化"; ar="تجميع النوافذ تلقائيًا"; ko="창 자동 그룹화" }
        let close = { en="Close"; zh="关闭"; ja="閉じる"; ar="إغلاق"; ko="닫기" }
        let closeOthers = { en="Close others"; zh="关闭其他"; ja="他を閉じる"; ar="إغلاق علامات التبويب الأخرى"; ko="다른 탭 닫기" }
        let closeToRight = { en="Close tabs to the right"; zh="关闭右侧标签"; ja="右側のタブを閉じる"; ar="إغلاق علامات التبويب على اليمين"; ko="오른쪽 탭 닫기" }
        let closeBelow = { en="Close tabs below"; zh="关闭下方标签"; ja="下のタブを閉じる"; ar="إغلاق علامات التبويب أدناه"; ko="아래쪽 탭 닫기" }
        let closeAllOf exe = { en=sprintf "Close all '%s' tabs" exe; zh=sprintf "关闭所有“%s”标签" exe; ja=sprintf "「%s」のタブをすべて閉じる" exe; ar=sprintf "إغلاق جميع علامات تبويب «%s»" exe; ko=sprintf "‘%s’ 탭 모두 닫기" exe }
        let closeAll = { en="Close all"; zh="关闭全部"; ja="すべて閉じる"; ar="إغلاق الكل"; ko="모두 닫기" }
        let settings = { en="Settings…"; zh="设置…"; ja="設定…"; ar="الإعدادات…"; ko="설정…" }

    /// Menu shown when a tab is dropped with the right mouse button.
    module DropMenu =
        let copy = { en="Copy"; zh="复制"; ja="コピー"; ar="نسخ"; ko="복사" }
        let move = { en="Move"; zh="移动"; ja="移動"; ar="نقل"; ko="이동" }
        /// Under the strip while files are dragged over an Explorer tab; %1 is the folder's name.
        let moveTo = { en="Move to %1"; zh="移动到 %1"; ja="%1 へ移動"; ar="نقل إلى %1"; ko="%1(으)로 이동" }
        let copyTo = { en="Copy to %1"; zh="复制到 %1"; ja="%1 へコピー"; ar="نسخ إلى %1"; ko="%1(으)로 복사" }

    /// The title bar drop setting.
    module TitleBarDrop =
        /// The setting's choices: always, only with a key held, or never.
        /// Short enough that the dropdown keeps the width of the others in Settings.
        let holdShift = { en="Hold Shift"; zh="按住 Shift"; ja="Shift 押下中"; ar="بضغط Shift"; ko="Shift 누른 채" }
        let holdCtrl = { en="Hold Ctrl"; zh="按住 Ctrl"; ja="Ctrl 押下中"; ar="بضغط Ctrl"; ko="Ctrl 누른 채" }
        let off = { en="Off"; zh="关闭"; ja="オフ"; ar="إيقاف"; ko="끔" }

    /// Settings window chrome and page titles.
    module SettingsWindow =
        let title = { en="WindowTabs Settings"; zh="WindowTabs 设置"; ja="WindowTabs の設定"; ar="إعدادات WindowTabs"; ko="WindowTabs 설정" }
        let search = { en="Search"; zh="搜索"; ja="検索"; ar="بحث"; ko="검색" }
        let searchSettings = { en="Search settings"; zh="搜索设置"; ja="設定を検索"; ar="البحث في الإعدادات"; ko="설정 검색" }
        let searchSuggestions = { en="Search suggestions"; zh="搜索建议"; ja="検索候補"; ar="اقتراحات البحث"; ko="검색 제안" }
        /// Where a search result lives when it is on no page: the language picker.
        let sidebar = { en="Sidebar"; zh="侧边栏"; ja="サイドバー"; ar="الشريط الجانبي"; ko="사이드바" }
        let searchResults = { en="Search results"; zh="搜索结果"; ja="検索結果"; ar="نتائج البحث"; ko="검색 결과" }
        let noMatches = { en="No matching settings."; zh="没有找到匹配的设置。"; ja="一致する設定はありません。"; ar="لا توجد إعدادات مطابقة."; ko="일치하는 설정이 없습니다." }
        let followWindows = { en="Follow Windows"; zh="跟随系统"; ja="Windows に従う"; ar="حسب Windows"; ko="Windows 설정 따르기" }
        let howToUse = { en="How to use"; zh="使用说明"; ja="使い方"; ar="طريقة الاستخدام"; ko="사용 방법" }
        let pageScroll = { en="Page scroll"; zh="页面滚动条"; ja="ページのスクロールバー"; ar="شريط تمرير الصفحة"; ko="페이지 스크롤" }

    module Pages =
        let general = { en="General"; zh="常规"; ja="全般"; ar="عام"; ko="일반" }
        let appearance = { en="Appearance"; zh="外观"; ja="外観"; ar="المظهر"; ko="모양" }
        let shortcuts = { en="Shortcuts"; zh="快捷键"; ja="ショートカット"; ar="الاختصارات"; ko="단축키" }
        let appRules = { en="App rules"; zh="应用规则"; ja="アプリのルール"; ar="قواعد التطبيقات"; ko="앱 규칙" }
        let workspaces = { en="Workspaces"; zh="工作区"; ja="ワークスペース"; ar="مساحات العمل"; ko="작업 공간" }
        let diagnostics = { en="Support"; zh="支持"; ja="サポート"; ar="الدعم"; ko="지원" }
        let mouseGestures = { en="Gestures"; zh="手势"; ja="ジェスチャー"; ar="الإيماءات"; ko="제스처" }
        let title page =
            match page with
            | GeneralSettings -> general
            | AppearanceSettings -> appearance
            | HotKeySettings -> shortcuts
            | MouseGestureSettings -> mouseGestures
            | ProgramSettings -> appRules
            | LayoutSettings -> workspaces
            | DiagnosticsSettings -> diagnostics

    /// Captions, descriptions and search keywords of SettingsCatalog entries.
    module Settings =
        let private colorWords = { en="tab color colour"; zh="标签 颜色 配色"; ja="タブ 色 カラー"; ar="علامة تبويب اللون الألوان"; ko="탭 색 색상" }
        /// The tabs' text, found as such: the settings name it by what it colours.
        let private tabTextWords = { en="tab text title font color colour close button"; zh="标签文本 标签文字 标签 文本 文字 标题 字体 颜色 关闭按钮"; ja="タブの文字 タブ テキスト 文字 タイトル フォント 色 閉じるボタン"; ar="نص علامة التبويب علامة تبويب نص عنوان خط اللون زر الإغلاق"; ko="탭 텍스트 글자 제목 글꼴 색 닫기 버튼" }
        let private sizeWords = { en="size"; zh="尺寸 大小"; ja="サイズ 大きさ"; ar="الحجم"; ko="크기" }
        let private hotkeyWords = { en="hotkey"; zh="热键"; ja="ホットキー"; ar="مفتاح الاختصار"; ko="단축키 핫키" }
        let theme = { caption={ en="Theme"; zh="主题"; ja="テーマ"; ar="السمة"; ko="테마" }
                      description={ en="System follows your Windows app mode."; zh="“跟随系统”与 Windows 的应用模式保持一致。"; ja="「システム」は Windows のアプリ モードに合わせます。"; ar="يتبع خيار النظام وضع التطبيقات في Windows."; ko="‘시스템’은 Windows 앱 모드를 따릅니다." }
                      keywords={ en="dark mode"; zh="暗色 深色模式"; ja="ダークモード"; ar="الوضع الداكن"; ko="다크 모드 어두운 테마" } }
        let language = { caption={ en="Language"; zh="语言"; ja="言語"; ar="اللغة"; ko="언어" }
                         description={ en="Follow Windows uses your Windows display language."; zh="“跟随系统”使用 Windows 的显示语言。"; ja="「Windows に従う」では Windows の表示言語を使います。"; ar="يستخدم خيار «حسب Windows» لغة العرض في Windows."; ko="‘Windows 설정 따르기’는 Windows 표시 언어를 사용합니다." }
                         keywords={ en="locale"; zh="界面语言"; ja="表示言語"; ar="الإعدادات المحلية"; ko="로캘 표시 언어 한국어" } }
        let launchAtSignIn = { caption={ en="Start with Windows"; zh="随 Windows 启动"; ja="Windows と同時に起動"; ar="التشغيل مع Windows"; ko="Windows 시작 시 실행" }
                               description={ en="Runs in the system tray after you sign in."; zh="登录后在系统托盘中运行。"; ja="サインイン後にシステムトレイで実行します。"; ar="يعمل في منطقة الإعلام بعد تسجيل الدخول."; ko="로그인 후 알림 영역에서 실행됩니다." }
                               keywords={ en="startup autostart"; zh="开机启动 自启动"; ja="スタートアップ 自動起動"; ar="بدء التشغيل التشغيل التلقائي"; ko="시작 프로그램 자동 실행" } }
        let restoreSession = { caption={ en="Restore the last session after sign-in"; zh="登录后恢复上次会话"; ja="サインイン後に前回のセッションを復元"; ar="استعادة الجلسة الأخيرة بعد تسجيل الدخول"; ko="로그인 후 마지막 세션 복원" }
                               // One line beside the choice, which says whether apps are started.
                               description={ en="Puts windows back into their groups, places and splits."
                                             zh="将窗口放回原来的分组、位置和分屏。"
                                             ja="ウィンドウを元のグループ、位置、分割に戻します。"
                                             ar="يعيد النوافذ إلى مجموعاتها ومواضعها وتقسيماتها."
                                             ko="창을 원래 그룹, 위치, 분할 상태로 되돌립니다." }
                               keywords={ en="session reboot"; zh="会话 重启"; ja="セッション 再起動"; ar="جلسة إعادة التشغيل"; ko="세션 재부팅 다시 시작" } }
        let enableTabsByDefault = { caption={ en="Use tabs for new apps"; zh="新应用使用标签"; ja="新しいアプリでタブを使う"; ar="استخدام علامات التبويب للتطبيقات الجديدة"; ko="새 앱에 탭 사용" }
                                    description={ en="Only apps first discovered after this change. Existing apps keep their tab setting."; zh="只影响此后首次出现的应用。已有应用保留原来的标签设置。"; ja="変更後に初めて検出するアプリに適用します。既存のアプリのタブ設定は維持します。"; ar="ينطبق ذلك على التطبيقات التي يكتشفها البرنامج لأول مرة بعد هذا التغيير فقط. وتحتفظ التطبيقات الموجودة بإعداد علامات التبويب الحالي."; ko="이 설정을 바꾼 뒤 처음 발견된 앱에만 적용됩니다. 기존 앱은 탭 설정을 유지합니다." }
                                    keywords={ en=""; zh=""; ja=""; ar=""; ko="" } }
        let dimInactiveGroups = { caption={ en="Dim tabs in inactive groups"; zh="淡化非活动分组的标签"; ja="非アクティブなグループのタブの色を薄くする"; ar="تخفيف لون علامات التبويب في المجموعات غير النشطة"; ko="비활성 그룹의 탭 흐리게" }
                                  description={ en="Make the active group easier to spot."; zh="让当前活动的分组更容易辨认。"; ja="アクティブなグループを見分けやすくします。"; ar="يسهّل تمييز المجموعة النشطة."; ko="활성 그룹을 쉽게 구별할 수 있습니다." }
                                  keywords={ en="fade"; zh="变暗"; ja="薄く 暗く"; ar="تعتيم"; ko="흐리게 어둡게" } }
        /// Said as what it does, in the page's own word for them, as its per-app command is; a
        /// search for mouse gestures finds it by its keywords.
        let mouseGestures = { caption={ en="Use gestures"; zh="使用手势"; ja="ジェスチャーを使う"; ar="استخدام الإيماءات"; ko="제스처 사용" }
                              description={ en="Hold the right button and drag to run a command or send a shortcut."; zh="按住右键划动，执行命令或发送快捷键。"; ja="右ボタンを押したままドラッグして、コマンドを実行したりショートカットを送ったりします。"; ar="اضغط مع الاستمرار على الزر الأيمن واسحب لتشغيل أمر أو إرسال اختصار."; ko="오른쪽 버튼을 누른 채 끌어 명령을 실행하거나 단축키를 보냅니다." }
                              keywords={ en="mouse gestures stroke drag right click right-drag"; zh="鼠标手势 划动 右键 拖动"; ja="マウスジェスチャー ストローク ドラッグ 右クリック"; ar="إيماءات الماوس خط سحب نقرة يمين"; ko="마우스 제스처 끌기 오른쪽 클릭" } }
        let gestureHint = { caption={ en="Show a hint while drawing"; zh="划动时显示提示"; ja="描いている間ヒントを表示"; ar="إظهار تلميح أثناء الرسم"; ko="그리는 동안 힌트 표시" }
                            description={ en="Says by the pointer what the stroke will do."; zh="在指针旁显示这一笔会做什么。"; ja="そのストロークで何をするかをポインターの横に表示します。"; ar="يعرض بجوار المؤشر ما سيفعله الخط."; ko="획으로 실행될 동작을 포인터 옆에 표시합니다." }
                            keywords={ en="tooltip mouse gesture feedback"; zh="提示 鼠标手势 反馈"; ja="ツールチップ マウスジェスチャー"; ar="تلميح إيماءة الماوس"; ko="도구 설명 힌트 마우스 제스처" } }
        let pauseInFullScreen = { caption={ en="Pause while a full-screen app runs"; zh="全屏应用运行时暂停"; ja="全画面アプリの実行中は一時停止"; ar="الإيقاف المؤقت أثناء تشغيل تطبيق بملء الشاشة"; ko="전체 화면 앱 실행 중 일시 중지" }
                                  description={ en="Shortcuts leave their keys to the app; notices wait until it closes or leaves full screen."; zh="快捷键暂不生效，通知等退出全屏后再显示。"; ja="ショートカットはアプリにキーを譲り、通知は全画面が終わってから表示します。"; ar="تترك الاختصارات مفاتيحها للتطبيق، وتنتظر الإشعارات حتى يخرج من ملء الشاشة."; ko="단축키는 앱에 키를 양보하고, 알림은 앱이 닫히거나 전체 화면을 벗어날 때까지 기다립니다." }
                                  keywords={ en="fullscreen do not disturb"; zh="全屏 勿扰"; ja="フルスクリーン 応答不可"; ar="ملء الشاشة عدم الإزعاج"; ko="전체 화면 방해 금지 게임" } }
        let autoHide = { caption={ en="Auto-hide tabs"; zh="自动隐藏标签"; ja="タブを自動的に隠す"; ar="إخفاء علامات التبويب تلقائيًا"; ko="탭 자동 숨기기" }
                         description={ en="Show a thin strip until hover."; zh="只显示细栏，悬停展开。"; ja="細いバーだけ表示し、ホバーで展開します。"; ar="يبقى شريط رفيع فقط حتى تمرّر المؤشر فوقه."; ko="마우스를 올리기 전까지 얇은 띠만 표시합니다." }
                         keywords={ en="collapse"; zh="收起"; ja="折りたたみ"; ar="طيّ"; ko="접기 숨기기" } }
        let titleBarDrop = { caption={ en="Drag title bars to group windows"; zh="拖动标题栏分组"; ja="タイトルバーをドラッグしてグループ化"; ar="اسحب شريط العنوان لتجميع النوافذ"; ko="제목 표시줄을 끌어 창 그룹화" }
                             description={ en="Drag a window by its title bar until its tab is near another window’s tab, then release."; zh="拖动窗口的标题栏，让它的标签靠近另一个窗口的标签，再松手。"; ja="タイトルバーをドラッグし、そのウィンドウのタブを別のウィンドウのタブに近づけてから離します。"; ar="اسحب النافذة من شريط عنوانها حتى يقترب تبويبها من تبويب نافذة أخرى، ثم أفلتها."; ko="제목 표시줄을 끌어 창의 탭을 다른 창의 탭에 가까이 가져간 후 놓으세요." }
                             keywords={ en="drag drop window merge"; zh="拖放 窗口 合并"; ja="ドラッグ ドロップ ウィンドウ 結合"; ar="سحب إفلات نافذة دمج"; ko="끌기 놓기 창 합치기" } }
        let iconsOnly = { caption={ en="Show icons only"; zh="仅显示图标"; ja="アイコンのみ表示"; ar="إظهار الأيقونات فقط"; ko="아이콘만 표시" }
                          description={ en="Tabs show their icons without titles."; zh="标签只显示图标，不显示标题。"; ja="タブにタイトルを出さず、アイコンだけを表示します。"; ar="تعرض علامات التبويب أيقوناتها دون عناوين."; ko="탭에 제목 없이 아이콘만 표시합니다." }
                          keywords={ en="compact titles"; zh="紧凑 标题"; ja="コンパクト タイトル"; ar="مضغوط عناوين"; ko="컴팩트 제목 숨기기" } }
        let showTabsOnSwitch = { caption={ en="Show tabs briefly after switching"; zh="切换后短暂显示标签"; ja="切り替え後にタブを一時的に表示"; ar="إظهار علامات التبويب لفترة وجيزة بعد التبديل"; ko="전환 후 탭 잠시 표시" }
                                 description={ en="For about a second, so you can see where you are."; zh="显示约一秒，方便确认当前位置。"; ja="約 1 秒間表示して、今いる位置を確認できます。"; ar="لمدة ثانية تقريبًا لتتمكن من معرفة موضعك."; ko="약 1초 동안 표시해 현재 위치를 확인할 수 있습니다." }
                                 keywords={ en="peek"; zh="短暂显示"; ja="一時表示"; ar="معاينة"; ko="잠시 표시 엿보기" } }
        let tabPosition = { caption={ en="Position tabs"; zh="放置标签"; ja="タブを配置"; ar="موضع علامات التبويب"; ko="탭 위치" }
                            description=none
                            keywords={ en="move vertical horizontal sidebar left right"; zh="移动 垂直 横向 侧栏 左 右"; ja="移動 縦 横 サイドバー 左 右"; ar="نقل رأسي أفقي شريط جانبي يسار يمين"; ko="이동 세로 가로 사이드바 왼쪽 오른쪽" } }
        let tabColumnWidthHint = { en="For left/right tabs. Drag a column edge to resize one group, down to an icon rail."; zh="用于左／右标签。拖动侧栏边缘可单独调整分组宽度，最窄可只显示图标。"; ja="左・右のタブ用です。列の端をドラッグしてグループの幅を変更できます。最小幅ではアイコンのみを表示します。"; ar="لعلامات التبويب على اليسار أو اليمين. اسحب حافة العمود لتغيير عرض مجموعة واحدة، حتى لا يبقى منها إلا شريط أيقونات."; ko="왼쪽/오른쪽 탭에 적용됩니다. 열 가장자리를 끌면 그룹 너비가 바뀌며, 아이콘만 남을 때까지 줄일 수 있습니다." }
        let tabColumnWidth = { caption={ en="Vertical tab width"; zh="垂直标签宽度"; ja="縦タブ幅"; ar="عرض علامات التبويب الرأسية"; ko="세로 탭 너비" }
                               description=none
                               keywords={ en="vertical sidebar width size"; zh="垂直 侧栏 宽度 大小"; ja="縦 サイドバー 幅 サイズ"; ar="رأسي شريط جانبي عرض حجم"; ko="세로 사이드바 너비 크기" } }
        let tabAlignment = { caption={ en="Align tabs"; zh="对齐标签"; ja="タブを揃える"; ar="محاذاة علامات التبويب"; ko="탭 정렬" }
                             description={ en="Or hold the right button on a tab and drag the tabs where you like."; zh="也可按住右键拖动标签，放到任意位置。"; ja="タブを右ボタンで押しながらドラッグして好きな位置に置くこともできます。"; ar="أو اضغط بالزر الأيمن على علامة تبويب واسحب علامات التبويب حيث تشاء."; ko="탭을 오른쪽 버튼으로 누른 채 원하는 위치에 끌어다 놓을 수도 있습니다." }
                             keywords={ en="move alignment position"; zh="移动 对齐 位置"; ja="移動 配置 位置"; ar="نقل محاذاة موضع"; ko="이동 정렬 위치" } }
        let combineTaskbarIcons = { caption={ en="Combine taskbar icons per group"; zh="合并每个分组的任务栏图标"; ja="グループのタスクバーアイコンをまとめる"; ar="دمج أيقونات شريط المهام لكل مجموعة"; ko="그룹별 작업 표시줄 아이콘 합치기" }
                                    description=none
                                    keywords={ en="merge"; zh="合并"; ja="結合"; ar="دمج"; ko="합치기 결합" } }
        let replaceAltTab = { caption={ en="Use WindowTabs for Alt+Tab"; zh="使用 WindowTabs 窗口切换器"; ja="Alt+Tab に WindowTabs を使う"; ar="استخدام WindowTabs عند الضغط على Alt+Tab"; ko="Alt+Tab에 WindowTabs 사용" }
                              description={ en="Replaces the Windows switcher."; zh="替换 Windows 自带的窗口切换器。"; ja="Windows 標準のウィンドウ切り替えを置き換えます。"; ar="يحل محل مبدّل النوافذ في Windows."; ko="Windows 작업 전환을 대체합니다." }
                              keywords={ en="task switcher"; zh="任务切换"; ja="タスク切り替え"; ar="مبدّل المهام"; ko="작업 전환 창 전환" } }
        let switcherStyle = { caption={ en="Switcher style"; zh="切换器样式"; ja="スイッチャーの表示"; ar="نمط المبدّل"; ko="작업 전환 스타일" }
                              description={ en="Large icons in a row, or a list with full window titles."; zh="一排大图标，或显示完整窗口标题的列表。"; ja="大きなアイコンを横に並べるか、ウィンドウ タイトルを一覧で表示します。"; ar="أيقونات كبيرة في صف، أو قائمة تعرض عناوين النوافذ كاملة."; ko="큰 아이콘을 한 줄로 표시하거나, 전체 창 제목을 목록으로 표시합니다." }
                              keywords={ en="layout"; zh="布局"; ja="レイアウト"; ar="التخطيط"; ko="레이아웃 모양" } }
        let switcherShortcuts = { caption={ en="Use switcher shortcuts"; zh="使用切换器快捷键"; ja="切り替えショートカットを使う"; ar="استخدام اختصارات المبدّل"; ko="전환기 단축키 사용" }
                                  description={ en="Press Q to search, F to show window keys, and G to toggle group view."; zh="按 Q 搜索，按 F 显示窗口选择键，按 G 切换组内视图。"; ja="Q で検索し、F でウィンドウの選択キーを表示し、G でグループ表示を切り替えます。"; ar="اضغط Q للبحث، وF لعرض مفاتيح اختيار النوافذ، وG لتبديل عرض المجموعة."; ko="Q를 눌러 검색하고, F를 눌러 창 선택 키를 표시하고, G를 눌러 그룹 보기를 전환합니다." }
                                  keywords={ en="Alt+Tab"; zh="Alt+Tab"; ja="Alt+Tab"; ar="Alt+Tab"; ko="Alt+Tab" } }
        let groupWindowsInSwitcher = { caption={ en="Group windows in Alt+Tab"; zh="在 Alt+Tab 中按分组显示"; ja="Alt+Tab でグループをまとめる"; ar="تجميع النوافذ في Alt+Tab"; ko="Alt+Tab에서 창 그룹화" }
                                       description={ en="Each group appears as one item, with the icon its taskbar button has."; zh="每个分组只显示一项，图标与任务栏按钮相同。"; ja="各グループを、タスク バーのボタンと同じアイコンの 1 項目として表示します。"; ar="تظهر كل مجموعة كعنصر واحد بالأيقونة نفسها التي يحملها زر شريط المهام."; ko="각 그룹을 작업 표시줄 버튼과 같은 아이콘의 항목 하나로 표시합니다." }
                                       keywords={ en="switcher"; zh="切换器"; ja="切り替え"; ar="المبدّل"; ko="작업 전환 그룹" } }
        let nextTab = { caption={ en="Switch to next tab"; zh="切换到下一个标签"; ja="次のタブに切り替え"; ar="الانتقال إلى علامة التبويب التالية"; ko="다음 탭으로 전환" }
                        description=none
                        keywords=hotkeyWords }
        let previousTab = { caption={ en="Switch to previous tab"; zh="切换到上一个标签"; ja="前のタブに切り替え"; ar="الانتقال إلى علامة التبويب السابقة"; ko="이전 탭으로 전환" }
                            description=none
                            keywords=hotkeyWords }
        let lastUsedTab = { caption={ en="Switch to last used tab"; zh="切换到上次使用的标签"; ja="最後に使ったタブに切り替え"; ar="الانتقال إلى آخر علامة تبويب مستخدمة"; ko="마지막으로 사용한 탭으로 전환" }
                            description={ en="The tab that was in front before this one; again, and back."; zh="回到此标签之前所在的标签；再按一次则返回。"; ja="このタブの前に表示していたタブへ。もう一度押すと戻ります。"; ar="علامة التبويب التي كانت في المقدمة قبل هذه؛ مرة أخرى وتعود."; ko="바로 전에 보던 탭으로 이동합니다. 다시 누르면 돌아옵니다." }
                            keywords={ en="hotkey recent back toggle alternate"; zh="热键 最近 返回 来回"; ja="ホットキー 最近 戻る 交互"; ar="مفتاح الاختصار الأخيرة رجوع تبديل"; ko="단축키 최근 뒤로 번갈아" } }
        let searchTabs = { caption={ en="Switch windows"; zh="切换窗口"; ja="ウィンドウを切り替え"; ar="التبديل بين النوافذ"; ko="창 전환" }
                           description={ en="Switch to a window by title or app name."; zh="按标题或应用名查找并切换窗口。"; ja="タイトルやアプリ名でウィンドウを探して切り替えます。"; ar="الانتقال إلى نافذة باستخدام عنوانها أو اسم التطبيق."; ko="제목이나 앱 이름으로 창을 찾아 전환합니다." }
                           keywords={ en="find"; zh="查找"; ja="検索"; ar="بحث"; ko="찾기 검색" } }
        let switchGroups = { caption={ en="Switch groups"; zh="切换分组"; ja="グループを切り替え"; ar="التبديل بين المجموعات"; ko="그룹 전환" }
                             description={ en="Switch to a window group by name, title or app."; zh="按分组名、标题或应用名查找并切换分组。"; ja="グループ名、タイトル、アプリ名でグループを探して切り替えます。"; ar="الانتقال إلى مجموعة نوافذ باسمها أو عنوانها أو تطبيقها."; ko="그룹 이름, 제목 또는 앱 이름으로 그룹을 찾아 전환합니다." }
                             keywords={ en="find group tabs"; zh="查找 分组 组 标签"; ja="検索 グループ タブ"; ar="بحث مجموعة علامات التبويب"; ko="찾기 검색 그룹 탭" } }
        /// Keys inside the palette and the window search only, beside the arrows.
        let listNext = { caption={ en="Move down the list"; zh="在列表中下移"; ja="リストで下へ移動"; ar="النزول في القائمة"; ko="목록에서 아래로 이동" }
                         description={ en="Moves the choice down in the command palette and the window search, as ↓ does. Only there."
                                       zh="在命令面板和窗口搜索中把选中项下移，与 ↓ 相同。只在这两处生效。"
                                       ja="コマンドパレットとウィンドウ検索で選択を下へ移動します（↓ と同じ）。そこでのみ有効です。"
                                       ar="ينقل الاختيار لأسفل في لوحة الأوامر وبحث النوافذ، كما يفعل ↓. هناك فقط."
                                       ko="명령 팔레트와 창 검색에서만 ↓ 키처럼 선택 항목을 아래로 옮깁니다." }
                         keywords={ en="next down select move palette search"; zh="下一项 向下 选择 移动 面板 搜索"; ja="次 下 選択 移動 パレット 検索"; ar="التالي أسفل تحديد نقل لوحة بحث"; ko="다음 아래 선택 팔레트 검색" } }
        let listPrevious = { caption={ en="Move up the list"; zh="在列表中上移"; ja="リストで上へ移動"; ar="الصعود في القائمة"; ko="목록에서 위로 이동" }
                             description={ en="Moves the choice up in the command palette and the window search, as ↑ does. Only there."
                                           zh="在命令面板和窗口搜索中把选中项上移，与 ↑ 相同。只在这两处生效。"
                                           ja="コマンドパレットとウィンドウ検索で選択を上へ移動します（↑ と同じ）。そこでのみ有効です。"
                                           ar="ينقل الاختيار لأعلى في لوحة الأوامر وبحث النوافذ، كما يفعل ↑. هناك فقط."
                                           ko="명령 팔레트와 창 검색에서만 ↑ 키처럼 선택 항목을 위로 옮깁니다." }
                             keywords={ en="previous up select move palette search"; zh="上一项 向上 选择 移动 面板 搜索"; ja="前 上 選択 移動 パレット 検索"; ar="السابق أعلى تحديد نقل لوحة بحث"; ko="이전 위 선택 팔레트 검색" } }
        let commandPalette = { caption={ en="Command palette"; zh="命令面板"; ja="コマンドパレット"; ar="لوحة الأوامر"; ko="명령 팔레트" }
                               description={ en="Run any command or setting by name."
                                             zh="按名称运行任意命令或设置。"
                                             ja="名前でコマンドや設定を実行します。"
                                             ar="شغّل أي أمر أو إعداد باسمه."
                                             ko="이름으로 명령이나 설정을 실행합니다." }
                               keywords={ en="actions search everywhere"; zh="动作 全局搜索"; ja="アクション 検索"; ar="إجراءات بحث شامل"; ko="동작 명령 검색" } }
        let newTab = { caption={ en="New tab"; zh="新建标签"; ja="新しいタブ"; ar="علامة تبويب جديدة"; ko="새 탭" }
                       description={ en="Starts the current tab's app again."
                                     zh="再次启动当前标签的应用。"
                                     ja="現在のタブのアプリをもう一度起動します。"
                                     ar="يشغّل نسخة جديدة من تطبيق علامة التبويب الحالية."
                                     ko="현재 탭의 앱을 다시 실행합니다." }
                       keywords={ en="new window"; zh="新建窗口"; ja="新しいウィンドウ"; ar="نافذة جديدة"; ko="새 창 열기" } }
        let closeTab = { caption={ en="Close tab"; zh="关闭标签"; ja="タブを閉じる"; ar="إغلاق علامة التبويب"; ko="탭 닫기" }
                         description={ en="Closes the window of the tab in front."
                                       zh="关闭当前标签的窗口。"
                                       ja="前面のタブのウィンドウを閉じます。"
                                       ar="يغلق نافذة علامة التبويب الأمامية."
                                       ko="현재 탭의 창을 닫습니다." }
                         keywords={ en="close window"; zh="关闭窗口"; ja="ウィンドウを閉じる"; ar="إغلاق النافذة"; ko="창 닫기" } }
        let private workspaceWords = { en="workspace layout"; zh="工作区 布局"; ja="ワークスペース 配置"; ar="مساحة العمل التخطيط"; ko="작업 공간 레이아웃 배치" }
        let saveWorkspace = { caption={ en="Save workspace"; zh="保存工作区"; ja="ワークスペースを保存"; ar="حفظ مساحة العمل"; ko="작업 공간 저장" }
                              description={ en="Saves the windows as they are as a new workspace, as Save layout does."
                                            zh="将当前窗口保存为新的工作区，与“保存布局”相同。"
                                            ja="現在のウィンドウを新しいワークスペースとして保存します（「配置を保存」と同じ）。"
                                            ar="يحفظ النوافذ بحالتها الحالية كمساحة عمل جديدة، كما يفعل «حفظ التخطيط»."
                                            ko="현재 창 상태를 새 작업 공간으로 저장합니다(‘작업 공간 저장’과 같음)." }
                              keywords=workspaceWords }
        let restoreWorkspace = { caption={ en="Restore workspace"; zh="恢复工作区"; ja="ワークスペースを復元"; ar="استعادة مساحة العمل"; ko="작업 공간 복원" }
                                 description={ en="Restores the workspace last saved or restored."
                                               zh="恢复最近一次保存或恢复的工作区。"
                                               ja="最後に保存または復元したワークスペースを復元します。"
                                               ar="يستعيد آخر مساحة عمل تم حفظها أو استعادتها."
                                               ko="마지막으로 저장하거나 복원한 작업 공간을 다시 불러옵니다." }
                                 keywords=workspaceWords }
        let private splitWords = { en="split view pane"; zh="分屏 窗格"; ja="分割 ペイン"; ar="عرض مقسّم جزء"; ko="분할 보기 창 영역" }
        let editGroups = { caption={ en="Edit groups…"; zh="编辑分组…"; ja="グループを編集…"; ar="تحرير المجموعات…"; ko="그룹 편집…" }
                           description={ en="A picture of the group in front to rearrange its tabs and panes, applied when saved."
                                         zh="以图示方式编辑当前分组的标签和窗格，保存后一次性生效。"
                                         ja="手前のグループのタブとペインを図で編集し、保存時にまとめて適用します。"
                                         ar="صورة للمجموعة الأمامية لإعادة ترتيب علاماتها وأجزائها، تُطبَّق عند الحفظ."
                                         ko="현재 그룹의 탭과 창 영역을 그림에서 재배치합니다. 저장하면 적용됩니다." }
                           keywords={ en="group editor layout rearrange organize"; zh="分组编辑器 布局 整理 重排"; ja="グループ エディター レイアウト 整理"; ar="محرر المجموعات تخطيط ترتيب"; ko="그룹 편집기 레이아웃 정리 재배치" } }
        let splitPickRight = { caption={ en="Window on the right…"; zh="在右侧放入窗口…"; ja="右側にウィンドウを配置…"; ar="نافذة على اليمين…"; ko="오른쪽에 창 배치…" }
                               description={ en="Lists the windows to put beside the tab in front."; zh="列出可放到当前标签旁边的窗口。"; ja="前面のタブの隣に置くウィンドウを一覧表示します。"; ar="يعرض النوافذ التي يمكن وضعها بجانب علامة التبويب الأمامية."; ko="현재 탭 옆에 둘 창 목록을 표시합니다." }
                               keywords=splitWords }
        let splitPickBelow = { caption={ en="Window below…"; zh="在下方放入窗口…"; ja="下側にウィンドウを配置…"; ar="نافذة في الأسفل…"; ko="아래쪽에 창 배치…" }
                               description=none
                               keywords=splitWords }
        let splitPickLeft = { caption={ en="Window on the left…"; zh="在左侧放入窗口…"; ja="左側にウィンドウを配置…"; ar="نافذة على اليسار…"; ko="왼쪽에 창 배치…" }
                              description=none
                              keywords=splitWords }
        let splitPickAbove = { caption={ en="Window above…"; zh="在上方放入窗口…"; ja="上側にウィンドウを配置…"; ar="نافذة في الأعلى…"; ko="위쪽에 창 배치…" }
                               description=none
                               keywords=splitWords }
        let splitFocusLeft = { caption={ en="Focus the pane to the left"; zh="焦点切到左边的窗格"; ja="左のペインにフォーカス"; ar="التركيز على الجزء الأيسر"; ko="왼쪽 창 영역에 포커스" }
                               description=none
                               keywords=splitWords }
        let splitFocusRight = { caption={ en="Focus the pane to the right"; zh="焦点切到右边的窗格"; ja="右のペインにフォーカス"; ar="التركيز على الجزء الأيمن"; ko="오른쪽 창 영역에 포커스" }
                                description=none
                                keywords=splitWords }
        let splitFocusUp = { caption={ en="Focus the pane above"; zh="焦点切到上边的窗格"; ja="上のペインにフォーカス"; ar="التركيز على الجزء الأعلى"; ko="위쪽 창 영역에 포커스" }
                             description=none
                             keywords=splitWords }
        let splitFocusDown = { caption={ en="Focus the pane below"; zh="焦点切到下边的窗格"; ja="下のペインにフォーカス"; ar="التركيز على الجزء الأسفل"; ko="아래쪽 창 영역에 포커스" }
                               description=none
                               keywords=splitWords }
        let switchTabsByNumber = { caption={ en="Switch tabs by number"; zh="按数字切换标签"; ja="番号でタブを切り替え"; ar="التبديل بين علامات التبويب باستخدام الأرقام"; ko="번호로 탭 전환" }
                                   description={ en="Hold a modifier key and press 1–9."; zh="按住修饰键再按 1–9。"; ja="修飾キーを押しながら 1～9 を押します。"; ar="اضغط باستمرار على مفتاح تعديل ثم اضغط على رقم من 1 إلى 9."; ko="보조 키를 누른 채 1–9를 누릅니다." }
                                   keywords={ en="digit"; zh="数字"; ja="数字"; ar="رقم"; ko="숫자 번호" } }
        let numberShortcut = { caption={ en="Modifier key for 1–9"; zh="数字键搭配的修饰键"; ja="1～9 と組み合わせるキー"; ar="مفتاح التعديل للأرقام من 1 إلى 9"; ko="1–9와 함께 누를 보조 키" }
                               description=none
                               keywords={ en="number"; zh="数字"; ja="番号"; ar="رقم"; ko="번호 숫자" } }
        let enableNumberLeader = { caption={ en="Switch tabs with a leader key"; zh="用引导键切换标签"; ja="リーダーキーでタブを切り替え"; ar="التبديل بين علامات التبويب بمفتاح تمهيدي"; ko="리더 키로 탭 전환" }; description={ en="Press the leader, then a selection key. Esc cancels."; zh="先按引导键，再按选择键。Esc 取消。"; ja="リーダーキーの後、選択キーを押します。Esc で解除。"; ar="اضغط المفتاح التمهيدي ثم مفتاح الاختيار. اضغط Esc للإلغاء."; ko="리더 키를 누른 뒤 선택 키를 누릅니다. Esc를 누르면 취소됩니다." }; keywords=none }
        let leaderKeys = { caption={en="Selection keys";zh="选择键";ja="選択キー";ar="مفاتيح الاختيار";ko="선택 키"}; description={en="Use unique letters or digits, in tab order.";zh="按标签顺序，使用不重复的字母或数字。";ja="タブ順に、重複しない英字または数字を指定します。";ar="استخدم أحرفًا أو أرقامًا غير مكررة حسب ترتيب علامات التبويب.";ko="탭 순서대로 중복되지 않는 문자나 숫자를 사용하세요."}; keywords={en="leader keys letters home row";zh="引导键 字母 主键区";ja="リーダーキー 英字 ホームポジション";ar="مفتاح تمهيدي أحرف الصف الأوسط";ko="리더 키 문자 홈 행"} }
        let leaderKeyPresets = [| {en="123456789";zh="123456789";ja="123456789";ar="123456789";ko="123456789"}; {en="QWERTYUIOP";zh="QWERTYUIOP";ja="QWERTYUIOP";ar="QWERTYUIOP";ko="QWERTYUIOP"}; {en="ASDFGHJKL;";zh="ASDFGHJKL;";ja="ASDFGHJKL;";ar="ASDFGHJKL;";ko="ASDFGHJKL;"} |]
        let invalidLeaderKeys = {en="Use at least one unique letter, digit or unshifted punctuation key.";zh="请使用至少一个不重复的字母、数字或无需 Shift 的标点键。";ja="重複しない英字、数字、Shift 不要の記号を 1 つ以上指定してください。";ar="استخدم حرفًا أو رقمًا أو علامة ترقيم واحدة على الأقل، غير مكررة ولا تحتاج إلى Shift.";ko="중복되지 않는 문자, 숫자 또는 Shift 없이 입력하는 문장 부호 키를 하나 이상 사용하세요."}
        let numberLeader = { caption={ en="Leader key"; zh="引导键"; ja="リーダーキー"; ar="المفتاح التمهيدي"; ko="리더 키" }; description=none; keywords=none }
        let tabColors = { en="Tab color"; zh="标签颜色"; ja="タブの色"; ar="لون علامة التبويب"; ko="탭 색" }
        let customTabColor = { en="Custom…"; zh="自定义…"; ja="カスタム…"; ar="مخصص…"; ko="사용자 지정…" }
        let clearTabColor = { en="Clear color"; zh="清除颜色"; ja="色を解除"; ar="مسح اللون"; ko="색 지우기" }
        let rememberTabColor = { en="Remember color for this app"; zh="为此应用记住颜色"; ja="このアプリの色を記憶"; ar="تذكّر اللون لهذا التطبيق"; ko="이 앱의 색 기억" }
        let rememberGroupColor = { en="Remember color for this group"; zh="为此分组记住颜色"; ja="このグループの色を記憶"; ar="تذكّر اللون لهذه المجموعة"; ko="이 그룹의 색 기억" }
        /// Named for how they look: every colour is deep enough for white text, so the light hues
        /// show as their deep versions (mustard, not yellow).
        let tabColorNames = [|
            { en="Grey";zh="灰色";ja="グレー";ar="رمادي"; ko="회색" }; { en="Blue";zh="蓝色";ja="青";ar="أزرق"; ko="파랑" }
            { en="Red";zh="红色";ja="赤";ar="أحمر"; ko="빨강" }; { en="Mustard";zh="芥末黄";ja="マスタード";ar="خردلي"; ko="머스터드" }
            { en="Green";zh="绿色";ja="緑";ar="أخضر"; ko="초록" }; { en="Pink";zh="粉色";ja="ピンク";ar="وردي"; ko="분홍" }
            { en="Purple";zh="紫色";ja="紫";ar="أرجواني"; ko="보라" }; { en="Petrol";zh="孔雀蓝";ja="ピーコックブルー";ar="أزرق بترولي"; ko="피콕 블루" }
            { en="Orange";zh="橙色";ja="オレンジ";ar="برتقالي"; ko="주황" }; { en="Violet";zh="堇紫";ja="バイオレット";ar="بنفسجي"; ko="바이올렛" }
            { en="Olive";zh="橄榄色";ja="オリーブ";ar="زيتي"; ko="올리브" }; { en="Rose";zh="玫红色";ja="ローズ";ar="وردي داكن"; ko="로즈" }
            { en="Moss";zh="苔绿";ja="モスグリーン";ar="طحلبي"; ko="모스 그린" }; { en="Navy";zh="藏青色";ja="ネイビー";ar="كحلي"; ko="남색" }
            { en="Teal";zh="蓝绿色";ja="ティール";ar="أزرق مخضر"; ko="청록" }; { en="Rust";zh="铁锈红";ja="赤錆色";ar="صدئي"; ko="적갈색" } |]
        /// The sample text on each swatch in the tab colour menu, as a tab would show it.
        let tabColorSample = { en="Aa"; zh="Aa"; ja="Aa"; ar="Aa"; ko="Aa" }
        let tabColorMode = { caption={ en="Tab color coding"; zh="标签着色"; ja="タブの色分け"; ar="تلوين علامات التبويب"; ko="탭 색 구분" }
                             description={ en="Auto-assign colors by window or app."
                                           zh="按窗口或应用自动分配颜色。"
                                           ja="ウィンドウ・アプリ別に色を自動で割り当てます。"
                                           ar="تعيين الألوان تلقائيًا حسب النافذة أو التطبيق."
                                           ko="창이나 앱별로 색을 자동으로 지정합니다." }
                             keywords={ en="colorize rainbow"; zh="着色 彩虹"; ja="色分け"; ar="تلوين قوس قزح"; ko="색 구분 무지개" } }
        let colorsOff = { en="Off"; zh="关闭"; ja="オフ"; ar="إيقاف"; ko="끔" }
        let byWindow = { en="By window"; zh="按窗口"; ja="ウィンドウ別"; ar="حسب النافذة"; ko="창별" }
        let byApp = { en="By app"; zh="按应用"; ja="アプリ別"; ar="حسب التطبيق"; ko="앱별" }
        let tabColorMenuHint = { en="Change it for one tab from the tab menu. That color comes first, and shows even when this is off."
                                 zh="可在标签菜单中为单个标签选色。它优先于这里的设置，关闭时也有效。"
                                 ja="タブメニューでタブごとに色を選べます。こちらの設定より優先され、オフでも表示されます。"
                                 ar="غيّر لون علامة تبويب واحدة من قائمتها. تكون الأولوية لهذا اللون ويظهر حتى عند إيقاف هذا الخيار."
                                 ko="탭 메뉴에서 탭별로 색을 바꿀 수 있습니다. 그 색이 우선하며, 이 옵션을 꺼도 표시됩니다." }
        let tabColorStyle = { caption={ en="Show colors as"; zh="着色方式"; ja="色の付け方"; ar="طريقة عرض الألوان"; ko="색 표시 방식" }
                              description=none
                              keywords={ en="stripe fill"; zh="色条 填充"; ja="ライン 塗りつぶし"; ar="شريط تعبئة"; ko="줄 채우기" } }
        let tabColorPreset = { caption={ en="Tab color preset"; zh="标签配色预设"; ja="タブの配色プリセット"; ar="مجموعة ألوان علامات التبويب"; ko="탭 색 프리셋" }
                               description=none
                               keywords={ en="tab colors colour theme palette scheme"; zh="标签配色 颜色 主题 调色板 配色方案"; ja="タブの色 配色 テーマ パレット"; ar="ألوان علامات التبويب سمة لوحة"; ko="탭 색 테마 팔레트 색 구성" } }
        let stripe ={ en="Stripe"; zh="色条"; ja="色のライン"; ar="شريط"; ko="줄무늬" }
        let fill = { en="Fill"; zh="填充"; ja="塗りつぶし"; ar="تعبئة"; ko="채우기" }
        let numberShortcutMenuHint = { en="Enable or disable it for one app from the tab menu."; zh="可在标签右键菜单中为单个应用启用或禁用数字快捷键。"; ja="タブメニューでアプリごとに番号ショートカットを有効・無効にできます。"; ar="يمكن تمكينه أو تعطيله لتطبيق واحد من قائمة علامة التبويب."; ko="탭 메뉴에서 앱별로 사용 여부를 설정할 수 있습니다." }
        let tabShortcutsHint = { en="Tab keys work only on windows with tabs; turn them off for one app from the tab menu."; zh="标签快捷键只在有标签的窗口上生效，可在标签右键菜单中为单个应用关闭。"; ja="タブのキーはタブのあるウィンドウでのみ働き、タブメニューでアプリごとにオフにできます。"; ar="تعمل مفاتيح علامات التبويب فقط في النوافذ التي بها علامات تبويب، ويمكن إيقافها لتطبيق واحد من قائمة علامة التبويب."; ko="탭 단축키는 탭이 있는 창에서만 작동하며, 탭 메뉴에서 앱별로 사용을 해제할 수 있습니다." }
        let numberShortcutFor name = { en=sprintf "Switch tabs by number in %s" name; zh=sprintf "在 %s 中按数字切换标签" name; ja=sprintf "%s のタブを番号で切り替え" name; ar=sprintf "التبديل بين علامات التبويب بالأرقام في %s" name; ko=sprintf "%s에서 번호로 탭 전환" name }
        /// The choices of Switch tabs by number: off, or the key held with 1–9.
        let numberShortcutOff = { en="Off"; zh="关闭"; ja="オフ"; ar="إيقاف"; ko="끔" }
        let numberShortcutWith key = { en=sprintf "%s + 1–9" key; zh=sprintf "%s + 1–9" key; ja=sprintf "%s + 1～9" key; ar=sprintf "%s + 1–9" key; ko=sprintf "%s + 1–9" key }
        let numberShortcutCtrl = { en="Ctrl"; zh="Ctrl"; ja="Ctrl"; ar="Ctrl"; ko="Ctrl" }
        let numberShortcutAlt = { en="Alt"; zh="Alt"; ja="Alt"; ar="Alt"; ko="Alt" }
        let activateOnHover = { caption={ en="Switch tabs on hover"; zh="悬停切换标签"; ja="ホバーでタブを切り替え"; ar="التبديل بين علامات التبويب عند تمرير المؤشر"; ko="마우스 오버 시 탭 전환" }
                                description={ en="Rest the pointer on a tab instead of clicking."; zh="鼠标停在标签上即可，无需点击。"; ja="クリックしなくても、タブにポインターを置くだけで済みます。"; ar="ضع المؤشر فوق علامة تبويب بدلًا من النقر عليها."; ko="클릭하지 않고 탭에 포인터를 올려 두기만 하면 됩니다." }
                                keywords={ en="mouse over"; zh="悬停"; ja="マウスオーバー"; ar="تمرير المؤشر"; ko="마우스 오버 호버" } }
        let tabHoverPreview = { caption={ en="Preview a tab's window on hover"; zh="悬停时预览标签窗口"; ja="ホバーでタブのウィンドウをプレビュー"; ar="معاينة نافذة علامة التبويب عند تمرير المؤشر"; ko="마우스 오버 시 탭 창 미리 보기" }
                                description={ en="Rest the pointer on a tab behind to see its window, with its full title."
                                              zh="鼠标停在后面的标签上，显示它的窗口截图和完整标题。"
                                              ja="背面のタブにポインターを置くと、そのウィンドウと完全なタイトルを表示します。"
                                              ar="ضع المؤشر فوق علامة تبويب خلفية لرؤية نافذتها مع عنوانها الكامل."
                                              ko="뒤에 있는 탭에 포인터를 올리면 그 창과 전체 제목을 표시합니다." }
                                keywords={ en="thumbnail screenshot picture hover card"; zh="缩略图 截图 预览 悬停"; ja="サムネイル スクリーンショット プレビュー"; ar="صورة مصغرة لقطة شاشة معاينة"; ko="미리 보기 축소판 스크린샷" } }
        let shiftScroll = { caption={ en="Switch tabs with Shift + scroll"; zh="用 Shift + 滚轮切换标签"; ja="Shift + スクロールでタブを切り替え"; ar="التبديل بين علامات التبويب باستخدام Shift والتمرير"; ko="Shift + 스크롤로 탭 전환" }
                            description={ en="Works anywhere over a grouped window. Over the tab bar, Shift isn't needed."
                                          zh="在分组窗口的任意位置均可使用；在标签栏上滚动时无需按 Shift。"
                                          ja="グループ化されたウィンドウ上ならどこでも使えます。タブバー上では Shift は不要です。"
                                          ar="يعمل في أي مكان فوق نافذة مجمّعة. وفوق شريط علامات التبويب لا حاجة إلى Shift."
                                          ko="그룹화된 창 위 어디서나 작동합니다. 탭 표시줄 위에서는 Shift가 필요 없습니다." }
                            keywords={ en="wheel"; zh="滚轮"; ja="ホイール"; ar="عجلة التمرير"; ko="휠 스크롤" } }
        let ctrlScroll = { caption={ en="Move tabs with Ctrl + scroll"; zh="用 Ctrl + 滚轮移动标签"; ja="Ctrl + スクロールでタブを移動"; ar="تحريك علامات التبويب باستخدام Ctrl والتمرير"; ko="Ctrl + 스크롤로 탭 이동" }
                           description={ en="Over the tab bar, moves the current tab left or right."
                                         zh="在标签栏上滚动，将当前标签向左或向右移动。"
                                         ja="タブバー上で、現在のタブを左右に移動します。"
                                         ar="فوق شريط علامات التبويب، ينقل علامة التبويب الحالية إلى اليسار أو اليمين."
                                         ko="탭 표시줄 위에서 현재 탭을 왼쪽이나 오른쪽으로 옮깁니다." }
                           keywords={ en="wheel reorder"; zh="滚轮 排序"; ja="ホイール 並べ替え"; ar="عجلة التمرير لإعادة الترتيب"; ko="휠 스크롤 순서 바꾸기" } }
        let middleClickClose = { caption={ en="Middle-click closes a tab"; zh="按下滚轮关闭标签"; ja="中クリックでタブを閉じる"; ar="الإغلاق بالنقر الأوسط"; ko="가운데 클릭으로 탭 닫기" }
                                 description={ en="Press the wheel on a tab to close its window."
                                               zh="在标签上按下滚轮，关闭它的窗口。"
                                               ja="タブ上でホイールを押すと、そのウィンドウを閉じます。"
                                               ar="اضغط بعجلة الفأرة على علامة التبويب لإغلاق نافذتها."
                                               ko="탭에서 휠을 누르면 그 창을 닫습니다." }
                                 keywords={ en="wheel click"; zh="中键 滚轮"; ja="ホイール クリック"; ar="النقر بعجلة الفأرة"; ko="휠 클릭 가운데 버튼" } }
        let tabTextColor = { caption={ en="Text"; zh="文字"; ja="文字"; ar="النص"; ko="텍스트" }; description=none; keywords=tabTextWords }
        let tabOpacity = { caption={ en="Tab opacity (%)"; zh="标签不透明度（%）"; ja="タブの不透明度 (%)"; ar="عتامة علامات التبويب (%)"; ko="탭 불투명도(%)" }
                           description={ en="Below 100 the tabs are see-through, and solid under the pointer."; zh="低于 100 时标签半透明，鼠标指向时不透明。"; ja="100 未満でタブが半透明になり、ポインターを重ねると不透明になります。"; ar="أقل من 100 تصبح علامات التبويب شفافة، ومعتمة تحت المؤشر."; ko="100 미만이면 탭이 반투명해지고, 포인터를 올리면 불투명해집니다." }
                           keywords={ en="transparency transparent see-through alpha"; zh="透明度 透明 半透明"; ja="透明度 透明 半透明"; ar="شفافية شفاف"; ko="투명도 투명 반투명" } }
        let tabTextBold = { caption={ en="Bold tab text"; zh="标签文字加粗"; ja="タブの文字を太字にする"; ar="نص علامات التبويب بخط عريض"; ko="탭 텍스트 굵게" }
                            description={ en="Which tab titles show in a heavier weight."; zh="哪些标签的标题以粗体显示。"; ja="どのタブのタイトルを太字で表示するか。"; ar="عناوين علامات التبويب التي تظهر بخط أثقل."; ko="굵게 표시할 탭 제목을 선택합니다." }
                            keywords=tabTextWords }
        let tabAddButton = { caption={ en="Show new tab button"; zh="显示新建标签按钮"; ja="新しいタブボタンを表示"; ar="إظهار زر علامة تبويب جديدة"; ko="새 탭 버튼 표시" }
                             description=TabMenu.addTabHint false
                             keywords={ en="plus add existing window hover show hide"; zh="加号 添加已有窗口 悬停 隐藏"; ja="プラス 追加 ウィンドウ ホバー 表示 非表示"; ar="زائد إضافة نافذة إظهار إخفاء"; ko="더하기 추가 창 마우스 오버 숨기기" } }
        let addButtonHover = { en="On hover"; zh="悬停时"; ja="ホバー時"; ar="عند التمرير"; ko="마우스 오버 시" }
        let tabCloseButton = { caption={ en="Show close buttons"; zh="显示关闭按钮"; ja="閉じるボタンを表示"; ar="إظهار أزرار الإغلاق"; ko="닫기 버튼 표시" }
                               description={ en="Which tabs show the × that closes their window."
                                             zh="哪些标签显示用来关闭窗口的 × 按钮。"
                                             ja="ウィンドウを閉じる × をどのタブに表示するか。"
                                             ar="علامات التبويب التي يظهر عليها زر × لإغلاق نافذتها."
                                             ko="창을 닫는 × 버튼을 표시할 탭을 선택합니다." }
                               keywords={ en="close button x hide"; zh="关闭按钮 叉 隐藏"; ja="閉じるボタン × 非表示"; ar="زر الإغلاق إخفاء"; ko="닫기 버튼 × 숨기기" } }
        let closeOnActiveAndHover = { en="Active and on hover"; zh="当前标签和悬停时"; ja="アクティブとホバー時"; ar="النشطة وعند التمرير"; ko="활성 탭 및 마우스 오버 시" }
        let closeOnActive = { en="Active tab only"; zh="仅当前标签"; ja="アクティブなタブのみ"; ar="علامة التبويب النشطة فقط"; ko="활성 탭만" }
        let boldAllTabs = { en="All tabs"; zh="所有标签"; ja="すべてのタブ"; ar="كل علامات التبويب"; ko="모든 탭" }
        let tabNormalBgColor = { caption={ en="Inactive tab"; zh="非活动标签"; ja="非アクティブなタブ"; ar="علامة تبويب غير نشطة"; ko="비활성 탭" }; description=none; keywords=colorWords }
        let tabActiveBgColor = { caption={ en="Active tab"; zh="活动标签"; ja="アクティブなタブ"; ar="علامة تبويب نشطة"; ko="활성 탭" }; description=none; keywords=colorWords }
        let tabHighlightBgColor = { caption={ en="Hovered tab"; zh="悬停标签"; ja="ホバー中のタブ"; ar="علامة التبويب التي يشير إليها المؤشر"; ko="마우스를 올린 탭" }; description=none; keywords=colorWords }
        let tabFlashBgColor = { caption={ en="Tab asking for attention"; zh="请求注意的标签"; ja="注意を求めるタブ"; ar="علامة تبويب تطلب الانتباه"; ko="주의가 필요한 탭" }
                                description={ en="While its window flashes, as its taskbar button does."; zh="其窗口闪烁提醒时（如任务栏按钮闪烁）。"; ja="ウィンドウが点滅して知らせている間（タスクバーのボタンと同様）。"; ar="أثناء وميض نافذتها، كما يومض زرها في شريط المهام."; ko="작업 표시줄 버튼처럼 창이 깜빡이는 동안 적용됩니다." }
                                keywords={ en="tab flash attention alert danger notify blink color colour"; zh="标签 闪烁 提醒 警示 注意 危险 颜色"; ja="タブ 点滅 注意 警告 通知 色"; ar="علامة تبويب وميض انتباه تنبيه خطر لون"; ko="깜빡임 주의 알림 경고 색" } }
        let tabStyle = { caption={ en="Tab style"; zh="标签样式"; ja="タブのスタイル"; ar="نمط علامة التبويب"; ko="탭 스타일" }
                         description={ en="How the active tab stands out from the rest."; zh="活动标签以何种方式与其他标签区分。"; ja="アクティブなタブをほかのタブとどう見分けるかを選びます。"; ar="كيفية تمييز علامة التبويب النشطة عن البقية."; ko="활성 탭을 다른 탭과 구별하는 방식입니다." }
                         keywords={ en="shape folder floating pill"; zh="形状 文件夹 悬浮 胶囊"; ja="形 フォルダー フローティング ピル"; ar="شكل مجلد عائم كبسولة"; ko="모양 폴더 플로팅 알약" } }
        let tabHeightHint = { en="Left/right tabs have a minimum height of 32 px."; zh="左／右标签的实际高度最低为 32px。"; ja="左・右のタブの最小高さは 32px です。"; ar="يبلغ الحد الأدنى لارتفاع علامات التبويب الجانبية 32 بكسل."; ko="왼쪽/오른쪽 탭의 최소 높이는 32px입니다." }
        let tabHeight = { caption={ en="Tab height"; zh="标签高度"; ja="タブの高さ"; ar="ارتفاع علامة التبويب"; ko="탭 높이" }; description=none; keywords=sizeWords }
        let tabMaxWidth = { caption={ en="Maximum tab width"; zh="标签最大宽度"; ja="タブの最大幅"; ar="أقصى عرض لعلامة التبويب"; ko="최대 탭 너비" }; description=none; keywords=sizeWords }
        let tabOverlap = { caption={ en="Tab spacing"; zh="标签间距"; ja="タブの間隔"; ar="المسافة بين علامات التبويب"; ko="탭 간격" }
                           description=none
                           keywords={ en="gap"; zh="间隙"; ja="隙間"; ar="فجوة"; ko="간격 틈" } }
        let tabIndent = { caption={ en="Side margin"; zh="两侧边距"; ja="左右の余白"; ar="الهامش الجانبي"; ko="양쪽 여백" }
                          description={ en="Distance from the window edges."
                                        zh="与窗口两侧的距离。"
                                        ja="ウィンドウの左右端からの距離です。"
                                        ar="المسافة من حواف النافذة."
                                        ko="창 가장자리와의 거리입니다." }
                          keywords={ en="indent padding"; zh="缩进 边距"; ja="インデント 余白"; ar="مسافة بادئة حشو"; ko="들여쓰기 여백 안쪽 여백" } }
        let appRules = { caption=Pages.appRules
                         description={ en="Choose apps for tabs and automatic grouping."; zh="选择启用标签和自动分组的应用。"; ja="タブと自動グループ化を使うアプリを選択します。"; ar="اختر التطبيقات التي تستخدم علامات التبويب والتجميع التلقائي."; ko="탭과 자동 그룹화를 사용할 앱을 선택합니다." }
                         keywords={ en="process program"; zh="程序 进程"; ja="プロセス プログラム"; ar="عملية برنامج"; ko="프로세스 프로그램 앱" } }
        let workspaces = { caption=Pages.workspaces
                           description={ en="Save and restore window layouts."; zh="保存和恢复窗口布局。"; ja="ウィンドウの配置を保存して復元します。"; ar="حفظ تخطيطات النوافذ واستعادتها."; ko="창 레이아웃을 저장하고 복원합니다." }
                           keywords={ en="session"; zh="会话"; ja="セッション"; ar="جلسة"; ko="세션 레이아웃" } }
        let diagnostics = { caption=Pages.diagnostics
                            description={ en="Troubleshooting report and where to get help."; zh="故障排查报告与获取帮助的途径。"; ja="トラブルシューティング用のレポートとヘルプの入手先。"; ar="تقرير استكشاف الأخطاء وإصلاحها ومكان الحصول على المساعدة."; ko="문제 해결 보고서와 도움을 받을 수 있는 곳입니다." }
                            keywords={ en="about version log"; zh="关于 版本 日志"; ja="バージョン ログ"; ar="حول الإصدار السجل"; ko="정보 버전 로그" } }
        let recordResources = { caption={ en="Record resource charts"; zh="记录资源图表"; ja="リソースのグラフを記録"; ar="تسجيل مخططات الموارد"; ko="리소스 차트 기록" }
                                description={ en="Reads every 5 seconds in the background and keeps 24 hours, in memory only."; zh="在后台每 5 秒读取一次，仅在内存中保留 24 小时。"; ja="バックグラウンドで 5 秒ごとに読み取り、メモリ内にのみ 24 時間保持します。"; ar="يقرأ كل 5 ثوانٍ في الخلفية ويحتفظ بـ 24 ساعة في الذاكرة فقط."; ko="백그라운드에서 5초마다 읽어 24시간 분량을 메모리에만 보관합니다." }
                                keywords={ en="cpu memory history usage monitor leak"; zh="CPU 内存 历史 占用 监控 泄漏"; ja="CPU メモリ 履歴 使用量 監視 リーク"; ar="المعالج الذاكرة السجل الاستخدام مراقبة تسرب"; ko="CPU 메모리 사용량 모니터 누수" } }
        let settingsLocation = { caption={ en="Location"; zh="位置"; ja="場所"; ar="الموقع"; ko="위치" }
                                 description=none
                                 keywords={ en="folder path portable"; zh="文件夹 路径 便携"; ja="フォルダー パス ポータブル"; ar="مجلد مسار محمول"; ko="폴더 경로 포터블 휴대용" } }
        let settingsBackup = { caption={ en="Back up and restore"; zh="备份与恢复"; ja="バックアップと復元"; ar="النسخ الاحتياطي والاستعادة"; ko="백업 및 복원" }
                               description={ en="Export all settings to a file, or import them and restart."; zh="将所有设置导出为文件，或从文件导入并重新启动。"; ja="すべての設定をファイルにエクスポートするか、インポートして再起動します。"; ar="تصدير جميع الإعدادات إلى ملف، أو استيرادها ثم إعادة التشغيل."; ko="모든 설정을 파일로 내보내거나, 가져온 뒤 다시 시작합니다." }
                               keywords={ en="export import"; zh="导出 导入"; ja="エクスポート インポート"; ar="تصدير استيراد"; ko="내보내기 가져오기 백업" } }
        let settingsReset = { caption={ en="Reset to defaults"; zh="恢复默认设置"; ja="既定値に戻す"; ar="إعادة التعيين إلى الإعدادات الافتراضية"; ko="기본값으로 재설정" }
                              description={ en="Then restarts. App rules and workspaces can be kept."; zh="完成后重新启动，可保留应用规则和工作区。"; ja="その後再起動します。アプリのルールとワークスペースは残せます。"; ar="ثم يُعاد تشغيل البرنامج. يمكن الاحتفاظ بقواعد التطبيقات ومساحات العمل."; ko="그런 다음 다시 시작합니다. 앱 규칙과 작업 공간은 유지할 수 있습니다." }
                              keywords={ en="factory"; zh="恢复默认 出厂"; ja="初期化"; ar="إعدادات المصنع"; ko="초기화 공장 기본값" } }

    /// What letting go of a dragged tab here would do, on the preview.
    /// Notices: a bold title naming what is restored or saved (the last session, or a workspace
    /// by its name), and below it what is happening to it.
    module Notices =
        let lastSession = { en="Last session"; zh="上次会话"; ja="前回のセッション"; ar="الجلسة الأخيرة"; ko="지난 세션" }
        let restoring = { en="Restoring…"; zh="正在恢复…"; ja="復元しています…"; ar="جارٍ الاستعادة…"; ko="복원하는 중…" }
        let launching count =
            { en=(if count=1 then "Starting an app…" else sprintf "Starting %d apps…" count)
              zh=sprintf "正在启动 %d 个应用…" count
              ja=sprintf "アプリを %d 個起動しています…" count
              ar=sprintf "جارٍ تشغيل %d من التطبيقات…" count
              ko=sprintf "앱 %d개를 시작하는 중…" count }
        let restored restored missing =
            { en=(if restored=1 then "Restored 1 window" else sprintf "Restored %d windows" restored)+(if missing=0 then "" else sprintf ", %d not found" missing)
              zh=(sprintf "已恢复 %d 个窗口" restored)+(if missing=0 then "" else sprintf "，%d 个未找到" missing)
              ja=(sprintf "%d 個のウィンドウを復元しました" restored)+(if missing=0 then "" else sprintf "（%d 個は見つかりません）" missing)
              ar=(sprintf "تمت استعادة %d نافذة" restored)+(if missing=0 then "" else sprintf "، %d غير موجودة" missing)
              ko=(sprintf "창 %d개를 복원했습니다" restored)+(if missing=0 then "" else sprintf "(%d개는 찾지 못함)" missing) }
        /// An error notice, with the reason the system gave.
        let restoreFailed reason =
            { en=sprintf "Could not restore. %s" reason; zh=sprintf "恢复失败。%s" reason; ja=sprintf "復元できませんでした。%s" reason; ar=sprintf "تعذّرت الاستعادة. %s" reason; ko=sprintf "복원하지 못했습니다. %s" reason }
        let saveFailed reason =
            { en=sprintf "Could not save. %s" reason; zh=sprintf "保存失败。%s" reason; ja=sprintf "保存できませんでした。%s" reason; ar=sprintf "تعذّر الحفظ. %s" reason; ko=sprintf "저장하지 못했습니다. %s" reason }
        let saved windows =
            { en=(if windows=1 then "Saved with 1 window" else sprintf "Saved with %d windows" windows)
              zh=sprintf "已保存，共 %d 个窗口" windows
              ja=sprintf "保存しました（ウィンドウ %d 個）" windows
              ar=sprintf "تم الحفظ (%d نافذة)" windows
              ko=sprintf "저장했습니다(창 %d개)" windows }
        let deleted = { en="Deleted"; zh="已删除"; ja="削除しました"; ar="تم الحذف"; ko="삭제됨" }
        let groupNotOpen = { en="Group is not open"; zh="该分组未打开"; ja="このグループは開いていません"; ar="المجموعة غير مفتوحة"; ko="그룹이 열려 있지 않습니다" }
        /// Why a saved window was not found, after its app in the restore notice and log.
        let unknownApp = { en="Saved without its app"; zh="保存时未记录应用"; ja="アプリが記録されていません"; ar="حُفظت دون تطبيقها"; ko="앱 정보 없이 저장됨" }
        /// The reason, then what to do about it as a sentence of its own, so the two do not read as one.
        let storeApp = { en="Store app, cannot be started. Please open it and restore again"; zh="应用商店应用无法自动启动。请手动打开后再恢复"; ja="ストア アプリは起動できません。開いてから再度復元してください"; ar="تطبيق من المتجر لا يمكن تشغيله. من فضلك افتحه ثم استعد مجددًا"; ko="스토어 앱은 시작할 수 없습니다. 앱을 연 다음 다시 복원하세요" }
        let noWindowLeft = { en="App is open, but no window of it matches"; zh="应用已打开，但没有可对应的窗口"; ja="アプリは開いていますが、対応するウィンドウがありません"; ar="التطبيق مفتوح، لكن لا نافذة منه تطابقها"; ko="앱은 열려 있지만 일치하는 창이 없습니다" }
        let noWindowAfterStart = { en="App started, but showed no window in time"; zh="应用已启动，但未及时出现窗口"; ja="アプリは起動しましたが、時間内にウィンドウが現れません"; ar="بدأ التطبيق، لكن لم تظهر نافذته في الوقت المحدد"; ko="앱이 시작되었지만 제한 시간 내에 창이 나타나지 않았습니다" }
        let startFailed reason = { en=sprintf "App would not start: %s" reason; zh=sprintf "应用启动失败：%s" reason; ja=sprintf "アプリを起動できません: %s" reason; ar=sprintf "تعذّر تشغيل التطبيق: %s" reason; ko=sprintf "앱을 시작하지 못했습니다: %s" reason }
        let appGone = { en="App file no longer exists"; zh="应用文件已不存在"; ja="アプリのファイルがありません"; ar="ملف التطبيق لم يعد موجودًا"; ko="앱 파일이 더 이상 없습니다" }
        let notRunning = { en="App is not running"; zh="应用未运行"; ja="アプリが実行されていません"; ar="التطبيق لا يعمل"; ko="앱이 실행 중이 아닙니다" }
        let more count = { en=sprintf "…and %d more" count; zh=sprintf "…另有 %d 个" count; ja=sprintf "…ほか %d 個" count; ar=sprintf "…و%d أخرى" count; ko=sprintf "…외 %d개" count }
        /// The apps whose windows came back, each with how many, at the end of the restore notice.
        let restoredApps apps = { en="Restored: "+apps; zh="已恢复："+apps; ja="復元済み: "+apps; ar="تمت الاستعادة: "+apps; ko="복원됨: "+apps }

    module General =
        /// The choices of Restore the last session at start.
        let sessionOff = { en="Off"; zh="关闭"; ja="オフ"; ar="إيقاف"; ko="끔" }
        /// Short, so the choice leaves its row's description room for one line.
        let sessionWindows = { en="Windows only"; zh="仅窗口"; ja="ウィンドウのみ"; ar="النوافذ فقط"; ko="창만" }
        let sessionApps = { en="Windows and missing apps"; zh="窗口和缺少的应用"; ja="ウィンドウと不足アプリ"; ar="النوافذ والتطبيقات الناقصة"; ko="창 및 누락된 앱" }
        let settingsImportFiles =
            { en="Settings file (*.json;*.txt)|*.json;*.txt|All files (*.*)|*.*"
              zh="设置文件 (*.json;*.txt)|*.json;*.txt|所有文件 (*.*)|*.*"
              ja="設定ファイル (*.json;*.txt)|*.json;*.txt|すべてのファイル (*.*)|*.*"
              ar="ملف الإعدادات (*.json;*.txt)|*.json;*.txt|كل الملفات (*.*)|*.*"
              ko="설정 파일 (*.json;*.txt)|*.json;*.txt|모든 파일 (*.*)|*.*" }
        let settingsFile = { en="Settings file"; zh="设置文件"; ja="設定ファイル"; ar="ملف الإعدادات"; ko="설정 파일" }
        let openFolder = { en="Open folder"; zh="打开文件夹"; ja="フォルダーを開く"; ar="فتح المجلد"; ko="폴더 열기" }
        let export = { en="Export…"; zh="导出…"; ja="エクスポート…"; ar="تصدير…"; ko="내보내기…" }
        let import = { en="Import…"; zh="导入…"; ja="インポート…"; ar="استيراد…"; ko="가져오기…" }
        let settingsFileHelp = { en="If the WindowTabs.exe folder has a settings file, WindowTabs uses it (portable mode); otherwise it uses the one in AppData. Older versions named it WindowTabsSettings.txt; it is now WindowTabsSettings.json, and an old .txt file is still read until the .json one exists."
                                 zh="如果 WindowTabs.exe 所在文件夹里有设置文件，WindowTabs 就使用它（便携模式）；否则使用 AppData 中的。旧版本的设置文件名为 WindowTabsSettings.txt，现在改为 WindowTabsSettings.json；在 .json 文件生成之前，仍会读取旧的 .txt 文件。"
                                 ja="WindowTabs.exe のフォルダーに設定ファイルがあれば、WindowTabs はそれを使います（ポータブル モード）。なければ AppData のものを使います。以前のバージョンでは WindowTabsSettings.txt という名前でしたが、現在は WindowTabsSettings.json です。.json ファイルができるまでは、古い .txt ファイルも読み込まれます。"
                                 ar="إذا وُجد ملف إعدادات بجوار WindowTabs.exe فسيستخدمه البرنامج (الوضع المحمول)، وإلا فسيستخدم الملف الموجود في AppData. كان اسمه في الإصدارات القديمة WindowTabsSettings.txt، وأصبح الآن WindowTabsSettings.json، وسيظل ملف .txt القديم يُقرأ حتى إنشاء ملف .json."
                                 ko="WindowTabs.exe 폴더에 설정 파일이 있으면 WindowTabs는 그 파일을 사용합니다(포터블 모드). 없으면 AppData에 있는 파일을 사용합니다. 이전 버전에서는 이름이 WindowTabsSettings.txt였지만 지금은 WindowTabsSettings.json이며, .json 파일이 생기기 전까지는 이전 .txt 파일도 읽습니다." }
        let exported = { en="Settings exported."; zh="设置已导出。"; ja="設定をエクスポートしました。"; ar="تم تصدير الإعدادات."; ko="설정을 내보냈습니다." }
        let reset = { en="Reset…"; zh="重置…"; ja="リセット…"; ar="إعادة تعيين…"; ko="초기화…" }
        let resetConfirm = { en="Reset"; zh="重置"; ja="リセット"; ar="إعادة تعيين"; ko="초기화" }
        let resetTitle = { en="Reset settings"; zh="重置设置"; ja="設定のリセット"; ar="إعادة تعيين الإعدادات"; ko="설정 초기화" }
        let resetMessage = { en="Restore every setting to its default and restart WindowTabs? The current settings are saved first, so you can import them back."
                             zh="将所有设置恢复为默认值并重新启动 WindowTabs？当前设置会先保存下来，之后可以再导入。"
                             ja="すべての設定を既定値に戻して WindowTabs を再起動しますか？現在の設定は先に保存されるので、後でインポートして戻せます。"
                             ar="هل تريد إعادة جميع الإعدادات إلى قيمها الافتراضية وإعادة تشغيل WindowTabs؟ ستُحفظ الإعدادات الحالية أولًا لتتمكن من استيرادها لاحقًا."
                             ko="모든 설정을 기본값으로 되돌리고 WindowTabs를 다시 시작할까요? 현재 설정은 먼저 저장되므로 나중에 다시 가져올 수 있습니다." }
        let resetAlsoClear = { en="Also clear:"; zh="同时清除："; ja="次も消去する："; ar="مسح أيضًا:"; ko="함께 지우기:"}
        let resetRestarting path =
            { en=sprintf "Settings were reset. WindowTabs will now restart.\n\nYour previous settings were saved to:\n%s" path
              zh=sprintf "设置已重置。WindowTabs 将重新启动。\n\n原来的设置已保存到：\n%s" path
              ja=sprintf "設定をリセットしました。WindowTabs を再起動します。\n\n以前の設定の保存先:\n%s" path
              ar=sprintf "تمت إعادة تعيين الإعدادات. سيُعاد تشغيل WindowTabs الآن.\n\nتم حفظ الإعدادات السابقة في:\n%s" path
              ko=sprintf "설정을 초기화했습니다. WindowTabs를 다시 시작합니다.\n\n이전 설정 저장 위치:\n%s" path }
        let importTitle = { en="Import settings"; zh="导入设置"; ja="設定のインポート"; ar="استيراد الإعدادات"; ko="설정 가져오기" }
        let notSettingsFile = { en="This file does not contain WindowTabs settings."; zh="此文件不包含 WindowTabs 设置。"; ja="このファイルには WindowTabs の設定が含まれていません。"; ar="لا يحتوي هذا الملف على إعدادات WindowTabs."; ko="이 파일에는 WindowTabs 설정이 없습니다." }
        let importedRestarting path =
            { en=sprintf "Settings imported. WindowTabs will now restart to apply them.\n\nYour previous settings were saved to:\n%s" path
              zh=sprintf "设置已导入。WindowTabs 将重新启动以应用这些设置。\n\n原来的设置已保存到：\n%s" path
              ja=sprintf "設定をインポートしました。適用するために WindowTabs を再起動します。\n\n以前の設定の保存先:\n%s" path
              ar=sprintf "تم استيراد الإعدادات. سيُعاد تشغيل WindowTabs لتطبيقها.\n\nتم حفظ الإعدادات السابقة في:\n%s" path
              ko=sprintf "설정을 가져왔습니다. WindowTabs를 다시 시작하여 적용합니다.\n\n이전 설정 저장 위치:\n%s" path }
        let tabMenuHint = { en="Change it for one group from the tab menu."; zh="可在标签菜单中为单个分组修改。"; ja="タブメニューでグループごとに変更できます。"; ar="غيّره لمجموعة واحدة من قائمة علامة التبويب."; ko="탭 메뉴에서 그룹별로 바꿀 수 있습니다." }
        let startup = { en="Startup"; zh="启动"; ja="起動"; ar="بدء التشغيل"; ko="시작" }
        let tabBehavior = { en="Tab behavior"; zh="标签行为"; ja="タブの動作"; ar="سلوك علامات التبويب"; ko="탭 동작" }
        let taskbar = { en="Taskbar"; zh="任务栏"; ja="タスクバー"; ar="شريط المهام"; ko="작업 표시줄" }
        let windowSwitcher = { en="Window switcher"; zh="窗口切换器"; ja="ウィンドウ切り替え"; ar="مبدّل النوافذ"; ko="작업 전환" }

    module Appearance =
        let system = { en="System"; zh="跟随系统"; ja="システム"; ar="النظام"; ko="시스템" }
        let light = { en="Light"; zh="浅色"; ja="ライト"; ar="فاتح"; ko="밝게" }
        let dark = { en="Dark"; zh="深色"; ja="ダーク"; ar="داكن"; ko="어둡게" }
        let explorerPreview = { en="File Explorer theme preview"; zh="文件资源管理器主题预览"; ja="エクスプローラーのテーマのプレビュー"; ar="معاينة سمة مستكشف الملفات"; ko="파일 탐색기 테마 미리 보기" }
        let tabColors = { en="Tab colors"; zh="标签配色"; ja="タブの色"; ar="ألوان علامات التبويب"; ko="탭 색" }
        let show = { en="Show"; zh="显示"; ja="表示"; ar="عرض"; ko="표시" }
        let colorPreset = { en="Color preset"; zh="配色预设"; ja="配色プリセット"; ar="مجموعة ألوان جاهزة"; ko="색 프리셋" }
        let custom = { en="Custom"; zh="自定义"; ja="カスタム"; ar="مخصص"; ko="사용자 지정" }
        /// Switcher styles: large icons in a row, or window titles in a column.
        let switcherHorizontal = { en="Horizontal"; zh="横向"; ja="横"; ar="أفقي"; ko="가로" }
        let switcherVertical = { en="Vertical"; zh="纵向"; ja="縦"; ar="رأسي"; ko="세로" }
        /// Tab styles, in TabStyle.names order.
        let tabStyles =
            [| { en="Joined"; zh="连贯"; ja="連結"; ar="متصل"; ko="연결형" }
               { en="Folder"; zh="文件夹"; ja="フォルダー型"; ar="مجلد"; ko="폴더형" }
               { en="Floating"; zh="悬浮"; ja="フローティング"; ar="عائم"; ko="플로팅" } |]
        /// The tab colours on which the text is shown darker or lighter than chosen.
        /// Samples of the tab text as chosen and as shown, side by side.
        let textBefore = { en="Before"; zh="调整前"; ja="調整前"; ar="قبل"; ko="조정 전" }
        let textAfter = { en="After"; zh="调整后"; ja="調整後"; ar="بعد"; ko="조정 후" }
        let colorsByCoding = { en="Set automatically while Tab color coding is By window or By app with Fill."
                               zh="标签着色为“按窗口”或“按应用”且选择“填充”时，这里自动配置。"
                               ja="タブの色分けが「ウィンドウ別」または「アプリ別」で「塗りつぶし」のときは、ここは自動で設定されます。"
                               ar="يُضبط تلقائيًا عندما يكون «تلوين علامات التبويب» على «حسب النافذة» أو «حسب التطبيق» مع «تعبئة»."
                               ko="탭 색 구분이 ‘창별’ 또는 ‘앱별’이고 ‘채우기’일 때 자동으로 설정됩니다." }
        let textAdjusted = { en="Text color is adjusted on some tabs to stay readable."; zh="部分标签上的文字颜色已调整，以保持清晰。"; ja="一部のタブでは、読みやすさのため文字の色を調整しています。"; ar="يُعدّل لون النص في بعض علامات التبويب ليظل مقروءًا."; ko="일부 탭에서는 읽기 쉽도록 글자 색을 조정합니다." }
        /// A colour preset whose colours the user has changed.
        let editedPreset name = { en=sprintf "%s*" name; zh=sprintf "%s*" name; ja=sprintf "%s*" name; ar=sprintf "%s*" name; ko=sprintf "%s*" name }
        let resetColors = { en="Reset colors"; zh="重置配色"; ja="配色をリセット"; ar="إعادة تعيين الألوان"; ko="색 초기화" }
        let swapActiveInactive = { en="Swap active and inactive"; zh="互换激活与非激活"; ja="アクティブと非アクティブを入れ替え"; ar="تبديل النشطة وغير النشطة"; ko="활성/비활성 바꾸기" }
        let tabLayout = { en="Tab layout"; zh="标签布局"; ja="タブのレイアウト"; ar="تخطيط علامات التبويب"; ko="탭 레이아웃" }
        let resetTabLayout = { en="Reset tab layout"; zh="重置标签布局"; ja="タブのレイアウトをリセット"; ar="إعادة تعيين تخطيط علامات التبويب"; ko="탭 레이아웃 초기화" }
        /// Stable source order; ThemePresets arranges the displayed colour families.
        let presets =
            [| { en="Default"; zh="默认"; ja="既定"; ar="افتراضي"; ko="기본값" }
               Settings.tabColorNames.[1]
               Settings.tabColorNames.[4]
               { en="Slate"; zh="石板灰"; ja="スレート"; ar="رمادي أردوازي"; ko="슬레이트" }
               Settings.tabColorNames.[14]
               { en="Sand"; zh="沙色"; ja="サンド"; ar="رملي"; ko="샌드" }
               { en="Amber"; zh="琥珀橙"; ja="アンバー"; ar="كهرماني"; ko="앰버" }
               Settings.tabColorNames.[11]
               Settings.tabColorNames.[6]
               { en="Windows accent"; zh="Windows 强调色"; ja="Windows のアクセント"; ar="لون تمييز Windows"; ko="Windows 강조 색" } |]
        let pickerHelp = { en="Color picker: arrow keys adjust saturation and brightness; Shift + arrow keys adjust hue"
                           zh="颜色选择器：方向键调整饱和度和亮度；Shift + 方向键调整色相"
                           ja="色の選択：方向キーで彩度と明るさを調整し、Shift + 方向キーで色相を調整します"
                           ar="منتقي الألوان: تعدّل مفاتيح الأسهم التشبع والسطوع؛ وتعدّل الأسهم مع Shift درجة اللون"
                           ko="색 선택기: 화살표 키로 채도와 밝기를, Shift + 화살표 키로 색조를 조정합니다" }
        let chooseColor = { en="Choose color"; zh="选择颜色"; ja="色を選択"; ar="اختيار اللون"; ko="색 선택" }
        let decrease = { en="Decrease"; zh="减小"; ja="減らす"; ar="تقليل"; ko="줄이기" }
        let increase = { en="Increase"; zh="增大"; ja="増やす"; ar="زيادة"; ko="늘리기" }

    module Shortcuts =
        let keyboard = { en="Keyboard"; zh="键盘"; ja="キーボード"; ar="لوحة المفاتيح"; ko="키보드" }
        /// The card of the two keys that reach everything else: the window search and the palette.
        let startHere = { en="Start here"; zh="从这里开始"; ja="ここから始める"; ar="ابدأ من هنا"; ko="여기서 시작" }
        let startHereNote = { en="Type a few letters to find any window or group, or to run any command or setting."
                              zh="输入几个字，即可找到任意窗口或分组，或运行任意命令和设置。"
                              ja="数文字入力するだけで、任意のウィンドウやグループを探したり、コマンドや設定を実行したりできます。"
                              ar="اكتب بضعة أحرف للعثور على أي نافذة أو مجموعة، أو لتشغيل أي أمر أو إعداد."
                              ko="몇 글자만 입력하면 원하는 창이나 그룹을 찾거나 명령과 설정을 실행할 수 있습니다." }
        let mouse = { en="Mouse"; zh="鼠标"; ja="マウス"; ar="الفأرة"; ko="마우스" }
        /// Cards for the commands that turn settings on or off and open pages, and the app's own.
        let keyboardNote = { en="Click a shortcut, then press the new combination. Esc cancels. The command palette sets one too: Alt+Enter on a command."
                             zh="点击快捷键后按下新的组合键。按 Esc 取消。在命令面板中选中命令后按 Alt+Enter 也可设置。"
                             ja="ショートカットをクリックして、新しいキーの組み合わせを押します。Esc でキャンセルします。コマンドパレットでコマンドを選んで Alt+Enter でも設定できます。"
                             ar="انقر اختصارًا ثم اضغط مجموعة المفاتيح الجديدة. اضغط Esc للإلغاء. وتعيّنه لوحة الأوامر أيضًا: Alt+Enter على أمر."
                             ko="단축키를 클릭한 다음 새 키 조합을 누르세요. Esc를 누르면 취소됩니다. 명령 팔레트에서 명령을 선택하고 Alt+Enter를 눌러도 설정할 수 있습니다." }
        /// Hovering the reset button: what it resets and what it leaves.
        let restoreDefaultsHelp = { en="Restore default shortcuts: the ones on this page, every key given to a command or a list in the command palette, and the keys given to windows or groups, which are cleared."
                                    zh="恢复默认快捷键：包括本页的快捷键、在命令面板中为命令或列表设置的按键，以及为窗口或分组设置的按键（会被清除）。"
                                    ja="既定のショートカットに戻します。このページのショートカット、コマンドパレットでコマンドやリストに設定したキー、ウィンドウやグループに設定したキー（解除されます）が対象です。"
                                    ar="استعادة الاختصارات الافتراضية: اختصارات هذه الصفحة، وكل مفتاح عُيّن لأمر أو لقائمة في لوحة الأوامر، والمفاتيح المعيّنة للنوافذ أو المجموعات، التي تُزال."
                                    ko="단축키를 기본값으로 되돌립니다. 이 페이지의 단축키와 명령 팔레트에서 명령이나 목록에 지정한 키가 대상이며, 창이나 그룹에 지정한 키는 지워집니다." }
        let restoreDefaults = { en="Restore default shortcuts"; zh="恢复默认快捷键"; ja="既定のショートカットに戻す"; ar="استعادة الاختصارات الافتراضية"; ko="기본 단축키로 되돌리기" }
        let inUse = { en="Shortcut already in use"; zh="快捷键已被占用"; ja="ショートカットは使用中です"; ar="الاختصار مستخدم بالفعل"; ko="이미 사용 중인 단축키" }
        /// A key pressed for one command that another had: it moves, and the other is left without.
        let takenFrom name = { en=sprintf "Taken from %s" name; zh=sprintf "已从“%s”移过来" name; ja=sprintf "「%s」から移しました" name; ar=sprintf "نُقل من «%s»" name; ko=sprintf "‘%s’에서 가져왔습니다" name }
        // Shortcut field messages are kept short to fit.
        let alreadySet = { en="Already set"; zh="已是当前快捷键"; ja="設定済み"; ar="هذا هو الاختصار الحالي"; ko="이미 설정됨" }
        /// A key pressed, then what was said of it, as "Alt+J: Used by Next tab".
        let keySays key (said:string) = { en=sprintf "%s: %s" key said; zh=sprintf "%s：%s" key said; ja=sprintf "%s: %s" key said; ar=sprintf "%s: %s" key said; ko=sprintf "%s: %s" key said }
        /// Alt+Enter, which sets a shortcut in the palette.
        let usedByPalette = { en="Used by the command palette (set shortcut)"; zh="命令面板已占用（设置快捷键）"
                              ja="コマンドパレットが使用中（ショートカットの設定）"; ar="تستخدمه لوحة الأوامر (تعيين اختصار)"; ko="명령 팔레트에서 사용 중(단축키 설정)" }
        let includeCtrlOrAlt = { en="Include Ctrl or Alt"; zh="需要包含 Ctrl 或 Alt"; ja="Ctrl か Alt が必要です"; ar="يجب أن يتضمن Ctrl أو Alt"; ko="Ctrl 또는 Alt를 포함하세요" }
        /// A key of the lists may also be Tab or a function key alone.
        let includeCtrlAltOrTab = { en="Use Tab, a function key, or Ctrl or Alt"; zh="请使用 Tab、功能键，或包含 Ctrl 或 Alt"; ja="Tab、ファンクション キー、または Ctrl か Alt を使ってください"; ar="استخدم Tab أو مفتاح وظيفة أو Ctrl أو Alt"; ko="Tab, 기능 키 또는 Ctrl이나 Alt를 사용하세요" }
        let pressShortcut = { en="Press a shortcut…"; zh="按下组合键…"; ja="キーを押してください…"; ar="اضغط اختصارًا…"; ko="단축키를 누르세요…" }
        /// For keys sent one after another, as a gesture's may be.
        let pressKeys = { en="Press keys, one after another…"; zh="依次按下按键…"; ja="キーを順に押してください…"; ar="اضغط المفاتيح واحدًا تلو الآخر…"; ko="키를 하나씩 차례로 누르세요…" }
        let notSet = { en="Not set"; zh="未设置"; ja="未設定"; ar="غير معيّن"; ko="설정 안 됨" }

    /// Commands run by name, from the palette or the command line, that no setting names already.
    module Commands =
        let private splitWords = { en="split view pane"; zh="分屏 窗格"; ja="分割 ペイン"; ar="عرض مقسّم جزء"; ko="분할 보기 창 영역" }
        let splitSwap = { caption={ en="Swap panes"; zh="交换窗格"; ja="ペインを入れ替え"; ar="تبديل الأجزاء"; ko="창 영역 바꾸기" }
                          description={ en="The two halves of the focused pane's split change places."
                                        zh="当前窗格所在分屏的两个窗格互换位置。"
                                        ja="フォーカスしているペインの分割の両側を入れ替えます。"
                                        ar="يتبادل نصفا التقسيم الذي يحتوي على الجزء المركّز مكانيهما."
                                        ko="포커스가 있는 창 영역의 분할에서 양쪽 위치를 서로 바꿉니다." }
                          keywords=splitWords }
        let splitEqualize = { caption={ en="Equal panes"; zh="均分窗格"; ja="均等に分割"; ar="أجزاء متساوية"; ko="창 영역 균등 분할" }
                              description={ en="Gives both halves of the focused pane's split the same room."
                                            zh="让当前窗格所在分屏的两个窗格占同样的空间。"
                                            ja="フォーカスしているペインの分割の両側を同じ広さにします。"
                                            ar="يمنح نصفي التقسيم الذي يحتوي على الجزء المركّز المساحة نفسها."
                                            ko="포커스가 있는 창 영역의 분할 양쪽을 같은 크기로 맞춥니다." }
                              keywords=splitWords }
        let splitTurn = { caption={ en="Toggle split direction"; zh="切换分屏方向"; ja="分割の向きを切り替え"; ar="تدوير التقسيم"; ko="분할 방향 전환" }
                          description={ en="Side by side becomes top and bottom, and back."
                                        zh="左右变为上下，再按一次变回。"
                                        ja="左右を上下に、またその逆に切り替えます。"
                                        ar="يتحول العرض جنبًا إلى جنب إلى أعلى وأسفل، والعكس."
                                        ko="좌우 배치를 위아래로, 또는 그 반대로 바꿉니다." }
                          keywords=splitWords }
        let splitClosePane = { caption={ en="Close the pane"; zh="关闭当前窗格"; ja="ペインを閉じる"; ar="إغلاق الجزء"; ko="창 영역 닫기" }
                               description={ en="The focused pane leaves the split and its neighbour takes its room; the window stays a tab."
                                             zh="当前窗格退出分屏，空间让给相邻的窗格；窗口仍保留为标签。"
                                             ja="フォーカスしているペインを分割から外し、隣のペインがその場所を使います。ウィンドウはタブとして残ります。"
                                             ar="يغادر الجزء المركّز التقسيم ويأخذ جاره مكانه؛ وتبقى النافذة علامة تبويب."
                                             ko="포커스가 있는 창 영역을 분할에서 빼고 옆 창 영역이 그 자리를 채웁니다. 창은 탭으로 남습니다." }
                               keywords=splitWords }
        let splitWithNext = { caption={ en="Split with the next tab"; zh="与下一个标签分屏"; ja="次のタブと分割表示"; ar="تقسيم مع علامة التبويب التالية"; ko="다음 탭과 분할" }
                              description={ en="The tab in front and the one after it, side by side."; zh="当前标签和它后面的标签左右并排。"; ja="手前のタブとその次のタブを左右に並べます。"; ar="العلامة الأمامية والتي تليها جنبًا إلى جنب."; ko="현재 탭과 그다음 탭을 나란히 놓습니다." }
                              keywords=splitWords }
        let splitEnd = { caption={ en="End split view"; zh="结束分屏"; ja="分割表示を終了"; ar="إنهاء العرض المقسّم"; ko="분할 보기 종료" }
                         description={ en="Every window becomes a tab again."; zh="所有窗口恢复为标签。"; ja="すべてのウィンドウをタブに戻します。"; ar="تعود كل نافذة إلى علامة تبويب."; ko="모든 창이 다시 탭으로 돌아갑니다." }
                         keywords=splitWords }
        let splitFocusOther = { caption={ en="Focus the next pane"; zh="焦点切到下一个窗格"; ja="次のペインにフォーカス"; ar="التركيز على الجزء التالي"; ko="다음 창 영역으로 포커스" }
                                description={ en="Around the panes in turn; brings parked panes back."
                                              zh="按顺序在各窗格之间轮换；已收起的窗格会重新显示。"
                                              ja="ペインを順に巡ります。しまわれたペインは元に戻します。"
                                              ar="يتنقل بين الأجزاء بالتناوب، ويعيد الأجزاء المخفية."
                                              ko="창 영역을 차례로 이동합니다. 숨겨진 창 영역은 다시 표시합니다." }
                                keywords=splitWords }
        let private tabWords = { en="tab"; zh="标签"; ja="タブ"; ar="علامة تبويب"; ko="탭" }
        let renameTab = { caption={ en="Rename tab"; zh="重命名标签"; ja="タブの名前を変更"; ar="إعادة تسمية علامة التبويب"; ko="탭 이름 바꾸기" }
                          description={ en="Edits the name on the tab; an empty name brings the window's own back."
                                        zh="在标签上编辑名称；留空则恢复窗口自己的标题。"
                                        ja="タブ上で名前を編集します。空にするとウィンドウ自身のタイトルに戻ります。"
                                        ar="يحرر الاسم على علامة التبويب؛ ويعيد الاسم الفارغ عنوان النافذة نفسها."
                                        ko="탭에서 이름을 편집합니다. 비워 두면 창의 원래 제목으로 돌아갑니다." }
                          keywords=tabWords }
        let resetTabName = { caption={ en="Reset tab name"; zh="重置标签名称"; ja="タブ名をリセット"; ar="إعادة تعيين اسم علامة التبويب"; ko="탭 이름 초기화" }
                             description={ en="The tab follows the window's title again."; zh="标签重新跟随窗口标题。"; ja="タブはウィンドウのタイトルに再び従います。"; ar="تتبع علامة التبويب عنوان النافذة من جديد."; ko="탭이 다시 창 제목을 따릅니다." }
                             keywords=tabWords }
        let private pinWords = { en="pin unpin keep"; zh="固定 取消固定 置顶 钉住"; ja="固定 固定解除 ピン留め"; ar="تثبيت إلغاء"; ko="고정 고정 해제 핀" }
        let pinTab = { caption={ en="Pin tab"; zh="固定标签"; ja="タブを固定"; ar="تثبيت علامة التبويب"; ko="탭 고정" }
                       description={ en="A pinned tab stays first and shows a pin for its close button; closing tabs together leaves it."
                                     zh="固定的标签排在最前，关闭按钮处显示图钉；批量关闭标签时保留。"
                                     ja="固定したタブは先頭に並び、閉じるボタンの代わりにピンを表示します。まとめて閉じるときも残ります。"
                                     ar="تبقى علامة التبويب المثبتة أولًا وتعرض دبوسًا مكان زر الإغلاق؛ ولا تُغلق عند إغلاق علامات التبويب معًا."
                                     ko="고정한 탭은 맨 앞에 유지되며 닫기 버튼 자리에 핀이 표시됩니다. 탭을 한꺼번에 닫아도 남습니다." }
                       keywords=pinWords }
        /// The same command, for a tab already pinned.
        let unpinTab = { pinTab with caption={ en="Unpin tab"; zh="取消固定标签"; ja="タブの固定を解除"; ar="إلغاء تثبيت علامة التبويب"; ko="탭 고정 해제" } }
        let tabColor = { caption={ en="Tab color…"; zh="标签颜色…"; ja="タブの色…"; ar="لون علامة التبويب…"; ko="탭 색…" }
                         description={ en="A color of the palette for the tab in front, or none."; zh="为当前标签选一种调色板颜色，或清除。"; ja="前面のタブにパレットの色を付けるか、解除します。"; ar="لون من اللوحة لعلامة التبويب الأمامية، أو لا شيء."; ko="현재 탭에 팔레트 색을 지정하거나 해제합니다." }
                         keywords={ en="colour"; zh="颜色 着色"; ja="色 カラー"; ar="لون"; ko="색 색상 컬러" } }
        let private colorWords = { en="colour color"; zh="颜色 着色"; ja="色 カラー"; ar="لون"; ko="색 색상 컬러" }
        let swapTabColors = { caption={ en="Swap active and inactive tab colors"; zh="互换激活与非激活标签颜色"; ja="アクティブと非アクティブのタブの色を入れ替え"; ar="تبديل ألوان علامات التبويب النشطة وغير النشطة"; ko="활성 탭과 비활성 탭 색 바꾸기" }
                              description={ en="For a front tab lighter than the rest, or the other way round."; zh="让前台标签比其他标签更亮，或反过来。"; ja="前面のタブをほかより明るく、またはその逆にします。"; ar="لعلامة تبويب أمامية أفتح من البقية، أو العكس."; ko="현재 탭을 다른 탭보다 밝게, 또는 그 반대로 바꿉니다." }
                              keywords=colorWords }
        let resetTabColors = { caption={ en="Reset tab colors"; zh="重置标签配色"; ja="タブの配色をリセット"; ar="إعادة تعيين ألوان علامات التبويب"; ko="탭 색 초기화" }
                               description={ en="The chosen preset's own colors again."; zh="恢复所选预设本来的颜色。"; ja="選んだプリセット本来の色に戻します。"; ar="ألوان المجموعة المختارة الأصلية من جديد."; ko="선택한 프리셋의 원래 색으로 돌아갑니다." }
                               keywords=colorWords }
        let customTabColor = { caption={ en="Custom tab color…"; zh="自定义标签颜色…"; ja="タブの色をカスタム…"; ar="لون مخصص لعلامة التبويب…"; ko="사용자 지정 탭 색…" }
                               description={ en="A color of your own for the tab in front."; zh="为当前标签选择自定义颜色。"; ja="前面のタブに好きな色を付けます。"; ar="لون من اختيارك لعلامة التبويب الأمامية."; ko="현재 탭에 원하는 색을 지정합니다." }
                               keywords=colorWords }
        let rememberTabColor = { caption={ en="Remember tab color for this app"; zh="为此应用记住标签颜色"; ja="このアプリのタブの色を記憶"; ar="تذكّر لون علامة التبويب لهذا التطبيق"; ko="이 앱의 탭 색 기억" }
                                 description={ en="New windows of the app take the tab's color; again, and the app forgets it."
                                               zh="此应用的新窗口沿用该标签颜色；再执行一次则忘记。"
                                               ja="アプリの新しいウィンドウにもこの色を使います。もう一度実行すると忘れます。"
                                               ar="تأخذ نوافذ التطبيق الجديدة لون علامة التبويب؛ ومرة أخرى ينساه التطبيق."
                                               ko="앱의 새 창에도 이 탭 색을 씁니다. 한 번 더 실행하면 기억한 색을 지웁니다." }
                                 keywords=colorWords }
        let rememberGroupColor = { caption={ en="Remember tab color for this group"; zh="为此分组记住标签颜色"; ja="このグループのタブの色を記憶"; ar="تذكّر لون علامة التبويب لهذه المجموعة"; ko="이 그룹의 탭 색 기억" }
                                   description={ en="Every tab in the group takes the tab's color, for a group across apps or one made by dragging; again, and the group forgets it."
                                                 zh="分组中的每个标签沿用该颜色，适用于跨应用分组或拖拽形成的分组；再执行一次则忘记。"
                                                 ja="グループのすべてのタブにこの色を使います。アプリをまたぐグループやドラッグで作ったグループが対象。もう一度実行すると忘れます。"
                                                 ar="تأخذ كل علامات التبويب في المجموعة لون علامة التبويب، لمجموعة عبر التطبيقات أو مجموعة أُنشئت بالسحب؛ ومرة أخرى تنساه المجموعة."
                                                 ko="그룹의 모든 탭에 이 색을 씁니다. 여러 앱에 걸친 그룹이나 끌어다 놓아 만든 그룹에 유용합니다. 한 번 더 실행하면 기억한 색을 지웁니다." }
                                   keywords=colorWords }
        let clearTabColor = { caption={ en="Clear tab color"; zh="清除标签颜色"; ja="タブの色を解除"; ar="مسح لون علامة التبويب"; ko="탭 색 지우기" }
                              description={ en="No color on the tab in front, nor kept for its app."; zh="清除当前标签的颜色，并取消为其应用记住的颜色。"; ja="前面のタブの色と、そのアプリに記憶した色を解除します。"; ar="لا لون لعلامة التبويب الأمامية، ولا لون محفوظ لتطبيقها."; ko="현재 탭의 색과 그 앱에 기억한 색을 지웁니다." }
                              keywords=colorWords }
        let moveTabLeft = { caption={ en="Move tab left"; zh="向左移动标签"; ja="タブを左へ移動"; ar="نقل علامة التبويب إلى اليسار"; ko="탭을 왼쪽으로 이동" }
                            description={ en="One place; up, with tabs at the side."; zh="移动一格；标签在侧边时为向上。"; ja="1 つ分。タブが横にあるときは上へ。"; ar="موضعًا واحدًا؛ إلى الأعلى عندما تكون علامات التبويب على الجانب."; ko="한 칸 이동합니다. 탭이 측면에 있으면 위로 이동합니다." }
                            keywords={ en="reorder up"; zh="排序 上移"; ja="並べ替え 上"; ar="ترتيب أعلى"; ko="순서 위로" } }
        let moveTabRight = { caption={ en="Move tab right"; zh="向右移动标签"; ja="タブを右へ移動"; ar="نقل علامة التبويب إلى اليمين"; ko="탭을 오른쪽으로 이동" }
                             description={ en="One place; down, with tabs at the side."; zh="移动一格；标签在侧边时为向下。"; ja="1 つ分。タブが横にあるときは下へ。"; ar="موضعًا واحدًا؛ إلى الأسفل عندما تكون علامات التبويب على الجانب."; ko="한 칸 이동합니다. 탭이 측면에 있으면 아래로 이동합니다." }
                             keywords={ en="reorder down"; zh="排序 下移"; ja="並べ替え 下"; ar="ترتيب أسفل"; ko="순서 아래로" } }
        let sortTabs = { caption=TabMenu.sortTabs; description=none; keywords={ en="order"; zh="排序"; ja="並べ替え"; ar="ترتيب"; ko="정렬 순서" } }
        let closeOtherTabs = { caption={ en="Close other tabs"; zh="关闭其他标签"; ja="他のタブを閉じる"; ar="إغلاق علامات التبويب الأخرى"; ko="다른 탭 닫기" }
                               description={ en="Closes every other window of the group but the pinned ones."; zh="关闭分组中的其他窗口，固定的标签除外。"; ja="グループの他のウィンドウを、固定したタブを除いてすべて閉じます。"; ar="يغلق كل النوافذ الأخرى في المجموعة عدا المثبتة."; ko="고정한 탭을 제외하고 그룹의 다른 창을 모두 닫습니다." }
                               keywords=tabWords }
        let closeTabsAfter = { caption=TabMenu.closeToRight
                               description={ en="Closes every window after the tab in front, but the pinned ones; below, with tabs at the side."; zh="关闭当前标签之后的所有窗口，固定的标签除外；标签在侧边时为下方。"; ja="前面のタブより後ろのウィンドウを、固定したタブを除いてすべて閉じます。タブが横にあるときは下側。"; ar="يغلق كل النوافذ بعد علامة التبويب الأمامية عدا المثبتة؛ وأسفلها عندما تكون علامات التبويب على الجانب."; ko="고정한 탭을 제외하고 현재 탭 뒤의 창을 모두 닫습니다. 탭이 측면에 있으면 아래쪽 창을 닫습니다." }
                               keywords={ en="tab right below after"; zh="标签 右侧 下方 之后"; ja="タブ 右 下 後ろ"; ar="علامة تبويب يمين أسفل بعد"; ko="탭 오른쪽 아래 뒤" } }
        let closeAppTabs = { caption={ en="Close all tabs of this app"; zh="关闭此应用的全部标签"; ja="このアプリのタブをすべて閉じる"; ar="إغلاق جميع علامات تبويب هذا التطبيق"; ko="이 앱의 탭 모두 닫기" }
                             description={ en="Every window of the group from the app of the tab in front, but the pinned ones."; zh="分组中属于当前标签所在应用的所有窗口，固定的标签除外。"; ja="グループ内で、前面のタブのアプリのウィンドウを、固定したタブを除いてすべて。"; ar="كل نوافذ المجموعة من تطبيق علامة التبويب الأمامية، عدا المثبتة."; ko="고정한 탭을 제외하고 그룹에서 현재 탭과 같은 앱의 창을 모두 닫습니다." }
                             keywords=tabWords }
        let private iconNote = { en="For a group with combined taskbar icons."; zh="用于已合并任务栏图标的分组。"; ja="タスクバーアイコンをまとめたグループ用。"; ar="لمجموعة دُمجت أيقونات شريط المهام لها."; ko="작업 표시줄 아이콘을 합친 그룹용입니다." }
        let changeGroupIcon = { caption=TabMenu.changeTaskbarIcon; description=iconNote; keywords={ en="picture image"; zh="图片 图像"; ja="画像 ピクチャ"; ar="صورة"; ko="그림 이미지" } }
        let restoreGroupIcon = { caption=TabMenu.restoreTaskbarIcon; description=iconNote; keywords={ en="default picture"; zh="默认 图片"; ja="既定 画像"; ar="افتراضي صورة"; ko="기본값 그림" } }
        let closeAllTabs = { caption={ en="Close all tabs"; zh="关闭全部标签"; ja="すべてのタブを閉じる"; ar="إغلاق جميع علامات التبويب"; ko="모든 탭 닫기" }
                             description={ en="Closes every window of the group but the pinned ones."; zh="关闭分组中的所有窗口，固定的标签除外。"; ja="グループのウィンドウを、固定したタブを除いてすべて閉じます。"; ar="يغلق كل نوافذ المجموعة عدا المثبتة."; ko="고정한 탭을 제외하고 그룹의 창을 모두 닫습니다." }
                             keywords=tabWords }
        let detachTab = { caption={ en="Move tab to a new group"; zh="拆出到新分组"; ja="タブを新しいグループへ"; ar="نقل علامة التبويب إلى مجموعة جديدة"; ko="탭을 새 그룹으로 이동" }
                          description={ en="Takes the tab out on its own, as dragging it to the desktop does."; zh="把标签单独拆出来，与拖到桌面上相同。"; ja="タブを単独で取り出します。デスクトップへドラッグするのと同じです。"; ar="يخرج علامة التبويب وحدها، كما يفعل سحبها إلى سطح المكتب."; ko="탭을 따로 떼어 냅니다. 바탕 화면으로 끌어다 놓는 것과 같습니다." }
                          keywords={ en="detach split ungroup"; zh="分离 取消分组"; ja="分離 グループ解除"; ar="فصل إلغاء التجميع"; ko="분리 떼어내기 그룹 해제" } }
        let ungroupTabs = { caption={ en="Ungroup this group"; zh="解除此分组"; ja="このグループを解除"; ar="إلغاء تجميع هذه المجموعة"; ko="이 그룹 해제" }
                            description={ en="Takes every other tab out on its own, as dragging each to the desktop does. The tab in front stays."
                                          zh="把其他标签都各自拆出来，与逐个拖到桌面上相同。当前标签留在原处。"
                                          ja="手前のタブ以外を、それぞれ単独で取り出します。1 つずつデスクトップへドラッグするのと同じです。"
                                          ar="يُخرج كل علامة تبويب أخرى وحدها، كما يفعل سحب كل منها إلى سطح المكتب. وتبقى علامة التبويب الأمامية."
                                          ko="다른 탭을 모두 하나씩 떼어 냅니다. 각각 바탕 화면으로 끌어다 놓는 것과 같습니다. 현재 탭은 남습니다." }
                            keywords={ en="ungroup break apart separate detach all split"; zh="打散 取消分组 拆分 全部拆出 分离"; ja="グループ解除 分離 すべて 取り出す"; ar="فصل تفكيك الكل إلغاء التجميع"; ko="그룹 해제 분리 모두 떼어내기" } }
        let groupApp = { caption={ en="Group this app"; zh="合并此应用的窗口"; ja="このアプリのウィンドウをまとめる"; ar="تجميع نوافذ هذا التطبيق"; ko="이 앱의 창 모으기" }
                         description={ en="Brings every window of the app in front into this group, from other groups or on their own."
                                       zh="把当前应用的所有窗口都并入这个分组，不论它们在别的分组里还是单独一个。"
                                       ja="手前のアプリのウィンドウを、ほかのグループにあるものも単独のものも、すべてこのグループに入れます。"
                                       ar="يضم كل نوافذ التطبيق الأمامي إلى هذه المجموعة، سواء كانت في مجموعات أخرى أو منفردة."
                                       ko="현재 탭과 같은 앱의 창을 다른 그룹에 있든 따로 있든 모두 이 그룹으로 모읍니다." }
                         keywords={ en="merge gather combine join windows"; zh="合并 归组 汇总 加入 窗口"; ja="結合 まとめる 集める ウィンドウ"; ar="دمج جمع ضم نوافذ"; ko="병합 합치기 모으기 창" } }
        let addExisting = { caption=TabMenu.addExisting; description=TabMenu.addExisting; keywords={ en="merge join window"; zh="合并窗口 加入分组"; ja="ウィンドウを結合"; ar="دمج نافذة"; ko="창 합치기 그룹 추가" } }
        let moveTabToGroup = { caption={ en="Move tab to group…"; zh="移动标签到分组…"; ja="タブをグループへ移動…"; ar="نقل علامة التبويب إلى مجموعة…"; ko="탭을 그룹으로 이동…" }
                               description={ en="Lists the other groups by their first tab."; zh="按各分组的第一个标签列出其他分组。"; ja="他のグループを先頭のタブで一覧表示します。"; ar="يعرض المجموعات الأخرى بعلامة التبويب الأولى لكل منها."; ko="다른 그룹을 각 그룹의 첫 탭으로 표시합니다." }
                               keywords={ en="join"; zh="加入 合并"; ja="参加 結合"; ar="انضمام"; ko="합치기 추가" } }
        let private groupNote = { en="For this group; the setting is for new groups."; zh="只对当前分组有效；设置项针对新分组。"; ja="このグループのみ。設定は新しいグループに適用されます。"; ar="لهذه المجموعة؛ أما الإعداد فللمجموعات الجديدة."; ko="이 그룹에만 적용되며, 설정 창의 값은 새 그룹에 적용됩니다." }
        /// A setting's own name for the group in front alone: listed and found beside the setting,
        /// and told apart from it.
        let private forThisGroup (setting:LocalizedText) (more:string) =
            { en=setting.en+" for this group"+more; zh=setting.zh+"（此分组）"+more; ja=setting.ja+"（このグループ）"+more; ar=setting.ar+" لهذه المجموعة"+more; ko=setting.ko+" (이 그룹)"+more }
        let iconsOnly = { caption=forThisGroup TabMenu.showIconsOnly ""; description={ en="Again, and the titles come back."; zh="再次执行则恢复标题。"; ja="もう一度実行するとタイトルが戻ります。"; ar="مرة أخرى، وتعود العناوين."; ko="한 번 더 실행하면 제목이 돌아옵니다." }; keywords={ en="titles compact"; zh="标题 紧凑"; ja="タイトル コンパクト"; ar="عناوين مضغوط"; ko="제목 간단히" } }
        let groupTopmost = { caption={ en="Pin window or group on top"; zh="窗口或分组置顶"; ja="ウィンドウまたはグループを最前面に固定"; ar="إبقاء النافذة أو المجموعة في المقدمة"; ko="창 또는 그룹을 항상 위에 고정" }; description=TabMenu.windowBehavior; keywords={ en="always on top"; zh="始终置顶"; ja="最前面"; ar="دائما في المقدمة"; ko="항상 위" } }
        let keepWindowSizes = { caption=TabMenu.keepWindowSizes; description=TabMenu.windowBehavior
                                keywords={ en="independent windows"; zh="独立窗口"; ja="独立ウィンドウ"; ar="نوافذ مستقلة"; ko="독립 창 크기" } }
        let inactiveOffScreen = { caption=TabMenu.transparentMode; description=TabMenu.transparentModeNote; keywords=TabMenu.windowBehavior }
        let groupTabPosition = { caption=forThisGroup Settings.tabPosition.caption "…"; description=groupNote; keywords={ en="move top left right side"; zh="移动 顶部 左侧 右侧"; ja="移動 上 左 右"; ar="نقل أعلى يسار يمين"; ko="이동 위쪽 왼쪽 오른쪽" } }
        let groupTabAlignment = { caption=forThisGroup Settings.tabAlignment.caption "…"; description=groupNote; keywords={ en="move alignment center"; zh="移动 对齐 居中"; ja="移動 配置 中央"; ar="نقل محاذاة وسط"; ko="이동 정렬 가운데" } }
        let groupAutoHide = { caption=forThisGroup Settings.autoHide.caption "…"; description=groupNote; keywords={ en="collapse"; zh="收起"; ja="折りたたみ"; ar="طيّ"; ko="접기 숨기기" } }
        let combineGroupIcons = { caption=forThisGroup TabMenu.combineTaskbarIcons ""; description=groupNote; keywords={ en="merge taskbar"; zh="合并 任务栏"; ja="結合 タスクバー"; ar="دمج شريط المهام"; ko="합치기 작업 표시줄" } }
        let tabsForApp = { caption={ en="Use tabs for this app"; zh="此应用使用标签"; ja="このアプリでタブを使う"; ar="استخدام علامات التبويب لهذا التطبيق"; ko="이 앱에서 탭 사용" }
                           description={ en="Turns tabs on or off for the app of the window in front."; zh="为当前窗口所属的应用开启或关闭标签。"; ja="前面のウィンドウのアプリでタブをオン／オフにします。"; ar="يشغّل علامات التبويب أو يوقفها لتطبيق النافذة الأمامية."; ko="현재 창의 앱에서 탭을 켜거나 끕니다." }
                           keywords={ en="enable disable application"; zh="启用 禁用 应用"; ja="有効 無効 アプリ"; ar="تمكين تعطيل تطبيق"; ko="켜기 끄기 앱" } }
        let autoGroupApp = { caption={ en="Auto-group this app"; zh="自动分组此应用"; ja="このアプリを自動グループ化"; ar="تجميع هذا التطبيق تلقائيًا"; ko="이 앱 자동 그룹화" }
                             description={ en="On or off for the app of the window in front."; zh="为当前窗口所属的应用开启或关闭。"; ja="前面のウィンドウのアプリでオン／オフにします。"; ar="تشغيل أو إيقاف لتطبيق النافذة الأمامية."; ko="현재 창의 앱에서 켜거나 끕니다." }
                             keywords={ en="application grouping"; zh="应用 分组"; ja="アプリ グループ化"; ar="تطبيق تجميع"; ko="앱 그룹화" } }
        let numberShortcutForApp = { caption={ en="Switch tabs by number in this app"; zh="在此应用中按数字切换标签"; ja="このアプリのタブを番号で切り替え"; ar="التبديل بين علامات التبويب باستخدام الأرقام في هذا التطبيق"; ko="이 앱에서 번호로 탭 전환" }
                                     description={ en="On or off for the app of the window in front."; zh="为当前窗口所属的应用开启或关闭。"; ja="前面のウィンドウのアプリでオン／オフにします。"; ar="تشغيل أو إيقاف لتطبيق النافذة الأمامية."; ko="현재 창의 앱에서 켜거나 끕니다." }
                                     keywords={ en="digit application"; zh="数字 应用"; ja="数字 アプリ"; ar="رقم تطبيق"; ko="숫자 번호 앱" } }
        let tabShortcutsForApp = { caption={ en="Use tab shortcuts in this app"; zh="在此应用中使用标签快捷键"; ja="このアプリでタブのショートカットを使う"; ar="استخدام اختصارات علامات التبويب في هذا التطبيق"; ko="이 앱에서 탭 단축키 사용" }
                                   description={ en="Off, the app keeps keys such as Ctrl+Tab for itself."; zh="关闭后，Ctrl+Tab 这类按键留给应用自己。"; ja="オフにすると Ctrl+Tab などのキーはアプリに渡されます。"; ar="عند الإيقاف يحتفظ التطبيق بمفاتيح مثل Ctrl+Tab لنفسه."; ko="끄면 Ctrl+Tab 같은 키를 앱이 직접 사용합니다." }
                                   keywords={ en="exclude blacklist application keys"; zh="排除 黑名单 应用 按键"; ja="除外 ブラックリスト アプリ キー"; ar="استبعاد قائمة سوداء تطبيق مفاتيح"; ko="제외 차단 앱 키" } }
        let mouseGesturesForApp = { caption={ en="Use gestures in this app"; zh="在此应用中使用手势"; ja="このアプリでジェスチャーを使う"; ar="استخدام الإيماءات في هذا التطبيق"; ko="이 앱에서 제스처 사용" }
                                    description={ en="Off, the app keeps the right button, the gestures for all apps included."; zh="关闭后，右键留给应用自己，全局手势也不生效。"; ja="オフにすると右ボタンはアプリに渡され、全体のジェスチャーも使われません。"; ar="عند الإيقاف يحتفظ التطبيق بالزر الأيمن، بما في ذلك إيماءات كل التطبيقات."; ko="끄면 오른쪽 버튼을 앱이 직접 사용하며, 모든 앱용 제스처도 작동하지 않습니다." }
                                    keywords={ en="mouse gestures exclude right click"; zh="鼠标手势 排除 右键"; ja="マウスジェスチャー 除外 右クリック"; ar="إيماءات الماوس استبعاد نقرة يمين"; ko="마우스 제스처 제외 오른쪽 클릭" } }
        let restoreWorkspaceNamed = { caption={ en="Restore workspace…"; zh="恢复工作区…"; ja="ワークスペースを復元…"; ar="استعادة مساحة عمل…"; ko="작업 공간 복원…" }
                                      description={ en="Lists the saved workspaces."; zh="列出已保存的工作区。"; ja="保存済みのワークスペースを一覧表示します。"; ar="يعرض مساحات العمل المحفوظة."; ko="저장한 작업 공간 목록을 표시합니다." }
                                      keywords={ en="layout"; zh="布局"; ja="配置"; ar="تخطيط"; ko="레이아웃 배치" } }
        let deleteWorkspace = { caption={ en="Delete workspace…"; zh="删除工作区…"; ja="ワークスペースを削除…"; ar="حذف مساحة عمل…"; ko="작업 공간 삭제…" }
                                description={ en="Lists the saved workspaces; the one chosen is forgotten."; zh="列出已保存的工作区；选中的会被删除。"; ja="保存済みのワークスペースを一覧表示し、選んだものを削除します。"; ar="يعرض مساحات العمل المحفوظة؛ وتُنسى المختارة."; ko="저장한 작업 공간 목록을 표시하고 선택한 항목을 삭제합니다." }
                                keywords={ en="remove layout"; zh="移除 布局"; ja="削除 配置"; ar="إزالة تخطيط"; ko="제거 레이아웃" } }
        let openSettings = { caption={ en="Open settings"; zh="打开设置"; ja="設定を開く"; ar="فتح الإعدادات"; ko="설정 열기" }
                             description=none
                             keywords={ en="preferences options"; zh="偏好 选项"; ja="環境設定 オプション"; ar="التفضيلات الخيارات"; ko="환경 설정 옵션" } }
        /// One settings page by name, as "Settings: General".
        let settingsPage (page:LocalizedText) =
            { caption={ en="Settings: "+page.en; zh="设置："+page.zh; ja="設定: "+page.ja; ar="الإعدادات: "+page.ar; ko="설정: "+page.ko }
              description=none
              keywords={ en="open page"; zh="打开 页面"; ja="開く ページ"; ar="فتح صفحة"; ko="열기 페이지" } }
        let exit = { caption=Tray.exit
                     description={ en="Every window gets its own frame back."; zh="所有窗口恢复各自的边框。"; ja="すべてのウィンドウが元の枠に戻ります。"; ar="تستعيد كل نافذة إطارها الخاص."; ko="모든 창이 원래 테두리로 돌아갑니다." }
                     keywords={ en="quit close"; zh="退出 关闭"; ja="終了"; ar="إنهاء إغلاق"; ko="종료 끝내기 닫기" } }
        let refreshWindows = { caption={ en="Rescan windows"; zh="重新扫描窗口"; ja="ウィンドウを再スキャン"; ar="إعادة فحص النوافذ"; ko="창 다시 검색" }
                               description={ en="Groups any window that was missed."; zh="把漏掉的窗口加入分组。"; ja="見落とされたウィンドウをグループに入れます。"; ar="يضيف أي نافذة فاتها البرنامج إلى مجموعة."; ko="빠진 창을 그룹에 넣습니다." }
                               keywords={ en="rescan refresh reload"; zh="刷新 重新加载"; ja="更新 再読み込み"; ar="تحديث إعادة تحميل"; ko="새로 고침 다시 로드" } }

    /// Written to the console a command line was given from.
    /// The group editor's popup.
    module GroupEditor =
        let title = { en="Edit groups"; zh="编辑分组"; ja="グループを編集"; ar="تحرير المجموعات"; ko="그룹 편집" }
        let windows count =
            { en=(if count=1 then "1 window" else sprintf "%d windows" count); zh=sprintf "%d 个窗口" count; ja=sprintf "%d 個のウィンドウ" count; ar=sprintf "%d نافذة" count; ko=sprintf "창 %d개" count }
        let save = { en="Save"; zh="保存"; ja="保存"; ar="حفظ"; ko="저장" }
        let hint = { en="Drag tabs along the strip or onto a pane; the corner button splits a pane; right-click a tab for more; Esc goes back."
                     zh="沿标签条或往窗格里拖标签；窗格角上的按钮可拆分；右键标签有更多操作；Esc 回到全部分组。"
                     ja="タブをストリップに沿って、またはペインへドラッグ。ペイン隅のボタンで分割、タブの右クリックで他の操作、Esc で戻ります。"
                     ar="اسحب علامات التبويب على طول الشريط أو إلى لوحة؛ زر الزاوية يقسم اللوحة؛ انقر بزر الفأرة الأيمن على علامة تبويب للمزيد؛ وEsc يعود."
                     ko="탭을 탭 표시줄에서 옮기거나 창 영역에 끌어다 놓으세요. 모서리 버튼으로 창 영역을 분할하고, 탭을 오른쪽 클릭하면 메뉴가 열립니다. Esc를 누르면 돌아갑니다." }
        let noGroup = { en="The window in front is in no group."; zh="当前窗口不在任何分组里。"; ja="手前のウィンドウはどのグループにもありません。"; ar="النافذة الأمامية ليست في أي مجموعة."; ko="현재 창은 그룹에 속해 있지 않습니다." }
        /// What saving did.
        let applied moved detached made closed =
            let parts separator = List.filter ((<>) "") >> String.concat separator
            { en=parts ", " [ (if moved=0 then "" elif moved=1 then "1 window moved" else sprintf "%d windows moved" moved)
                              (if detached=0 then "" elif detached=1 then "1 window taken out" else sprintf "%d windows taken out" detached)
                              (if made=0 then "" elif made=1 then "1 group made" else sprintf "%d groups made" made)
                              (if closed=0 then "" elif closed=1 then "1 window closed" else sprintf "%d windows closed" closed) ]
              zh=String.concat "，" [ if moved<>0 then yield sprintf "移动了 %d 个窗口" moved
                                     if detached<>0 then yield sprintf "移出了 %d 个窗口" detached
                                     if made<>0 then yield sprintf "新建了 %d 个分组" made
                                     if closed<>0 then yield sprintf "关闭了 %d 个窗口" closed ]
              ja=parts "。" [ (if moved=0 then "" else sprintf "%d 個のウィンドウを移動しました" moved); (if detached=0 then "" else sprintf "%d 個のウィンドウを外しました" detached); (if made=0 then "" else sprintf "%d 個のグループを作成しました" made); (if closed=0 then "" else sprintf "%d 個のウィンドウを閉じました" closed) ]
              ar=parts "، " [ (if moved=0 then "" else sprintf "نُقلت %d نافذة" moved); (if detached=0 then "" else sprintf "أُخرجت %d نافذة" detached); (if made=0 then "" else sprintf "أُنشئت %d مجموعة" made); (if closed=0 then "" else sprintf "أُغلقت %d نافذة" closed) ]
              ko=parts ". " [ (if moved=0 then "" else sprintf "창 %d개를 이동했습니다" moved); (if detached=0 then "" else sprintf "창 %d개를 그룹에서 분리했습니다" detached); (if made=0 then "" else sprintf "그룹 %d개를 만들었습니다" made); (if closed=0 then "" else sprintf "창 %d개를 닫았습니다" closed) ] }
        let nothingChanged = { en="Nothing changed."; zh="没有改动。"; ja="変更はありません。"; ar="لم يتغير شيء."; ko="변경된 내용이 없습니다." }
        let dropHere = { en="Drop a window here"; zh="拖窗口到这里"; ja="ここにウィンドウをドロップ"; ar="أفلت نافذة هنا"; ko="여기에 창 놓기" }
        /// Said in the footer while the split button's slots are still empty.
        let emptyCellsClose count =
            { en=(if count=1 then "1 empty cell will close at Save." else sprintf "%d empty cells will close at Save." count)
              zh=sprintf "%d 个空格子会在保存时合并。" count
              ja=sprintf "空のセル %d 個は保存時に閉じられます。" count
              ar=sprintf "ستُغلق %d خلية فارغة عند الحفظ." count
              ko=sprintf "빈 칸 %d개는 저장할 때 닫힙니다." count }
        let splitButton = { en="Split this pane"; zh="拆分这个窗格"; ja="このペインを分割"; ar="تقسيم هذا الجزء"; ko="이 창 영역 분할" }
        /// The title's count of rows and of windows in all.
        let summary groups windows =
            { en=sprintf "%d %s, %d %s" groups (if groups=1 then "group" else "groups") windows (if windows=1 then "window" else "windows")
              zh=sprintf "%d 个分组，共 %d 个窗口" groups windows
              ja=sprintf "グループ %d、ウィンドウ %d" groups windows
              ar=sprintf "%d مجموعة، %d نافذة" groups windows
              ko=sprintf "그룹 %d개, 창 %d개" groups windows }
        /// The bin's label, and what it says while empty.
        let closeBin = { en="Close on save"; zh="保存时关闭"; ja="保存時に閉じる"; ar="تُغلق عند الحفظ"; ko="저장 시 닫기" }
        let closeBinEmpty = { en="Drop here to close on Save"; zh="拖到这里，保存时关闭"; ja="ここにドロップで保存時に閉じる"; ar="أفلت هنا للإغلاق عند الحفظ"; ko="저장 시 닫으려면 여기에 놓기" }
        /// The row menu's item that puts every window of the row in the bin.
        let closeRow = { en="Close its windows on save"; zh="保存时关闭这些窗口"; ja="保存時にこれらのウィンドウを閉じる"; ar="إغلاق نوافذه عند الحفظ"; ko="저장 시 이 창들 닫기" }
        /// The band under the rows that starts a row of one, and the tab menu's item for it.
        let newGroup = { en="New group"; zh="新建组"; ja="新しいグループ"; ar="مجموعة جديدة"; ko="새 그룹" }
        /// The row of the windows in no group, shown only while there are any, to drag from.
        let notGrouped = { en="Not grouped"; zh="未分组"; ja="未分類"; ar="غير مجمّعة"; ko="그룹 없음" }
        /// The tab's menu: into another row, listed by their first tabs.
        let moveTo = { en="Move to"; zh="移到"; ja="移動先"; ar="نقل إلى"; ko="이동할 위치" }
        let closeOnSave = { en="Close on save"; zh="保存时关闭"; ja="保存時に閉じる"; ar="إغلاق عند الحفظ"; ko="저장 시 닫기" }
        let closeSelectedOnSave = { en="Close selected windows on save"; zh="保存时关闭选中的窗口"; ja="保存時に選択したウィンドウを閉じる"; ar="إغلاق النوافذ المحددة عند الحفظ"; ko="저장 시 선택한 창 닫기" }
        /// A window in the bin, kept open after all: back among those with no group.
        let keepOpen = { en="Keep open"; zh="保留，不关闭"; ja="閉じない"; ar="إبقاؤها مفتوحة"; ko="열어 두기" }
        /// The tab menu's first item: the window itself to the front.
        let goToWindow = { en="Go to window"; zh="切换到此窗口"; ja="このウィンドウへ"; ar="الانتقال إلى النافذة"; ko="이 창으로 이동" }
        let expandRow = { en="Expand"; zh="展开"; ja="展開"; ar="توسيع"; ko="펼치기" }
        let collapseRow = { en="Collapse"; zh="折叠"; ja="折りたたむ"; ar="طي"; ko="접기" }
        /// In the loose row while no window is on its own: what the row is for.
        let overviewHint = { en="Drag tabs between rows, or to + New group for a group of their own. Drag a row by its empty room onto another row to merge them. Right-click for more."
                             zh="标签可拖到其他行，或拖到“+ 新建组”自成一组。按住行的空白处拖到另一行可合并。右键有更多操作。"
                             ja="タブは別の行へ、または「+ 新しいグループ」へドラッグして単独のグループにできます。行の空き部分をつかんで別の行に重ねると結合します。右クリックで他の操作。"
                             ar="اسحب علامات التبويب بين الصفوف، أو إلى «+ مجموعة جديدة» لتكوين مجموعة مستقلة. اسحب صفًا من فراغه فوق صف آخر لدمجهما. انقر بالزر الأيمن للمزيد."
                             ko="탭을 다른 행에 끌어다 놓거나 ‘+ 새 그룹’에 놓아 새 그룹을 만드세요. 행의 빈 곳을 잡아 다른 행에 놓으면 합쳐집니다. 오른쪽 클릭하면 다른 작업을 할 수 있습니다." }
        /// For a window in a pane: it stays a tab of its group, out of the split.
        let leavePane = { en="Take out of the pane"; zh="移出窗格"; ja="ペインから外す"; ar="إخراج من اللوحة"; ko="창 영역에서 빼기" }
        /// The row's menu, from a right click on its empty room.
        let mergeInto = { en="Merge into"; zh="合并到"; ja="統合先"; ar="دمج في"; ko="합칠 그룹" }
        let ungroup = { en="Ungroup"; zh="解散分组"; ja="グループ解除"; ar="فك التجميع"; ko="그룹 해제" }

    module CommandLine =
        /// Shown for --help: the options, then examples to copy.
        let usage = { en="Usage: WindowTabs [option]\n\nOptions:\n  -c, --command <id> [choice]   Run a command in the running WindowTabs. A command that offers\n                                choices takes the one named, as the palette lists it.\n  -w, --workspace <name>        Restore a saved workspace.\n  -g, --group <program>...      Start the programs and put the windows they open into one group.\n      --commands                List the commands and their ids.\n  -h, --help                    Show this help.\n\nWith no option, WindowTabs starts, replacing a copy already running.\n\nExamples:\n  WindowTabs --commands\n  WindowTabs --command next-tab\n  WindowTabs --command tab-color Blue\n  WindowTabs --command group-tab-position Left\n  WindowTabs --command restore-workspace-named \"Project A\"\n  WindowTabs --workspace \"Project A\"\n  WindowTabs --group notepad.exe \"C:\\Program Files\\App\\app.exe\"\n\nA command acts on the window in front, as its key does: run it from a shortcut or a hotkey\ntool. From a terminal, the terminal is the window in front."
                      zh="用法：WindowTabs [选项]\n\n选项：\n  -c, --command <id> [选项名]   在运行中的 WindowTabs 里执行命令。有选项的命令按面板中\n                                列出的名称选择其中一项。\n  -w, --workspace <名称>        恢复已保存的工作区。\n  -g, --group <程序>...         启动这些程序，并把它们打开的窗口编成一个分组。\n      --commands                列出所有命令及其 id。\n  -h, --help                    显示本帮助。\n\n不带选项时启动 WindowTabs，并替换正在运行的副本。\n\n示例：\n  WindowTabs --commands\n  WindowTabs --command next-tab\n  WindowTabs --command tab-color Blue\n  WindowTabs --command group-tab-position Left\n  WindowTabs --command restore-workspace-named \"Project A\"\n  WindowTabs --workspace \"Project A\"\n  WindowTabs --group notepad.exe \"C:\\Program Files\\App\\app.exe\"\n\n命令作用于前台窗口，与它的快捷键相同：请从快捷方式或热键工具中运行。\n在终端中运行时，前台窗口就是终端本身。"
                      ja="使い方: WindowTabs [オプション]\n\nオプション:\n  -c, --command <id> [選択肢]   実行中の WindowTabs でコマンドを実行します。選択肢のある\n                                コマンドは、パレットに表示される名前で選びます。\n  -w, --workspace <名前>        保存済みのワークスペースを復元します。\n  -g, --group <プログラム>...   プログラムを起動し、開いたウィンドウを 1 つのグループにまとめます。\n      --commands                コマンドとその id を一覧表示します。\n  -h, --help                    このヘルプを表示します。\n\nオプションなしでは WindowTabs を起動し、実行中のコピーを置き換えます。\n\n例:\n  WindowTabs --commands\n  WindowTabs --command next-tab\n  WindowTabs --command tab-color Blue\n  WindowTabs --command group-tab-position Left\n  WindowTabs --command restore-workspace-named \"Project A\"\n  WindowTabs --workspace \"Project A\"\n  WindowTabs --group notepad.exe \"C:\\Program Files\\App\\app.exe\"\n\nコマンドはキーと同じく前面のウィンドウに働きます。ショートカットやホットキー\nツールから実行してください。ターミナルから実行すると、前面にあるのはターミナルです。"
                      ar="الاستخدام: WindowTabs [خيار]\n\nالخيارات:\n  -c, --command <id> [اختيار]   يشغّل أمرًا في WindowTabs العامل. الأمر الذي يعرض خيارات\n                                يأخذ المسمّى منها كما تسرده اللوحة.\n  -w, --workspace <اسم>         يستعيد مساحة عمل محفوظة.\n  -g, --group <برنامج>...       يشغّل البرامج ويضع النوافذ التي تفتحها في مجموعة واحدة.\n      --commands                يعرض الأوامر ومعرّفاتها.\n  -h, --help                    يعرض هذه المساعدة.\n\nبدون خيار، يبدأ WindowTabs ويحل محل النسخة العاملة.\n\nأمثلة:\n  WindowTabs --commands\n  WindowTabs --command next-tab\n  WindowTabs --command tab-color Blue\n  WindowTabs --command group-tab-position Left\n  WindowTabs --command restore-workspace-named \"Project A\"\n  WindowTabs --workspace \"Project A\"\n  WindowTabs --group notepad.exe \"C:\\Program Files\\App\\app.exe\"\n\nيعمل الأمر على النافذة الأمامية كما يفعل مفتاحه: شغّله من اختصار أو من أداة مفاتيح\nتشغيل سريع. من الطرفية، الطرفية نفسها هي النافذة الأمامية."
                      ko="사용법: WindowTabs [옵션]\n\n옵션:\n  -c, --command <id> [선택]     실행 중인 WindowTabs에서 명령을 실행합니다. 선택 항목이 있는\n                                명령은 팔레트에 표시되는 이름으로 하나를 고릅니다.\n  -w, --workspace <이름>        저장된 작업 공간을 복원합니다.\n  -g, --group <프로그램>...     프로그램을 시작하고, 열린 창을 하나의 그룹으로 묶습니다.\n      --commands                명령과 해당 id를 나열합니다.\n  -h, --help                    이 도움말을 표시합니다.\n\n옵션 없이 실행하면 WindowTabs를 시작하고, 이미 실행 중인 복사본을 대체합니다.\n\n예:\n  WindowTabs --commands\n  WindowTabs --command next-tab\n  WindowTabs --command tab-color Blue\n  WindowTabs --command group-tab-position Left\n  WindowTabs --command restore-workspace-named \"Project A\"\n  WindowTabs --workspace \"Project A\"\n  WindowTabs --group notepad.exe \"C:\\Program Files\\App\\app.exe\"\n\n명령은 해당 키와 마찬가지로 현재 창에 적용됩니다. 바로 가기나 단축키\n도구에서 실행하세요. 터미널에서 실행하면 현재 창은 터미널입니다." }
        let unknownCommand id = { en=sprintf "No command is called %s. --commands lists them." id; zh=sprintf "没有名为 %s 的命令。--commands 可列出全部命令。" id; ja=sprintf "%s というコマンドはありません。--commands で一覧表示できます。" id; ar=sprintf "لا يوجد أمر باسم %s. يعرض --commands الأوامر." id; ko=sprintf "%s(이)라는 명령이 없습니다. --commands로 목록을 볼 수 있습니다." id }
        let unavailable caption = { en=sprintf "%s has nothing to act on for the window in front." caption; zh=sprintf "“%s”对当前前台窗口无可操作的内容。" caption; ja=sprintf "「%s」は前面のウィンドウに対して実行できる操作がありません。" caption; ar=sprintf "لا يجد «%s» ما يعمل عليه في النافذة الأمامية." caption; ko=sprintf "현재 창에서는 ‘%s’을(를) 실행할 수 없습니다." caption }
        /// A command line that is not one: what was wrong, and where to look.
        let unknownOption word = { en=sprintf "Unknown option %s." word; zh=sprintf "未知选项 %s。" word; ja=sprintf "不明なオプション %s です。" word; ar=sprintf "خيار غير معروف %s." word; ko=sprintf "알 수 없는 옵션 %s입니다." word }
        let didYouMean option = { en=sprintf "Did you mean %s?" option; zh=sprintf "是否想输入 %s？" option; ja=sprintf "%s のことですか?" option; ar=sprintf "هل تقصد %s؟" option; ko=sprintf "%s을(를) 입력하려고 하셨나요?" option }
        let needsValue option = { en=sprintf "%s needs a value, as the examples in --help show." option; zh=sprintf "%s 需要一个值，参见 --help 中的示例。" option; ja=sprintf "%s には値が必要です。--help の例を参照してください。" option; ar=sprintf "يحتاج %s إلى قيمة، كما تُظهر أمثلة --help." option; ko=sprintf "%s에는 값이 필요합니다. --help의 예를 참고하세요." option }
        let seeHelp = { en="WindowTabs --help lists the options."; zh="WindowTabs --help 会列出所有选项。"; ja="WindowTabs --help でオプションを一覧表示します。"; ar="يعرض WindowTabs --help الخيارات."; ko="WindowTabs --help로 옵션 목록을 볼 수 있습니다." }
        let noSuchChoice wanted (choices:string list) =
            let listed = System.String.Join(", ",choices)
            { en=sprintf "No choice is called %s. The choices are: %s" wanted listed; zh=sprintf "没有名为 %s 的选项。可选：%s" wanted listed; ja=sprintf "%s という選択肢はありません。選択肢: %s" wanted listed; ar=sprintf "لا يوجد خيار باسم %s. الخيارات: %s" wanted listed; ko=sprintf "%s(이)라는 선택 항목이 없습니다. 선택 항목: %s" wanted listed }
        let noSuchWorkspace name = { en=sprintf "No workspace is called %s." name; zh=sprintf "没有名为 %s 的工作区。" name; ja=sprintf "%s というワークスペースはありません。" name; ar=sprintf "لا توجد مساحة عمل باسم %s." name; ko=sprintf "%s(이)라는 작업 공간이 없습니다." name }
        let notListening = { en="WindowTabs is running but does not take command lines; start it again first."; zh="WindowTabs 正在运行，但不接收命令行；请先重新启动它。"; ja="WindowTabs は実行中ですが、コマンド ラインを受け付けません。先に起動し直してください。"; ar="WindowTabs يعمل لكنه لا يقبل سطور الأوامر؛ أعد تشغيله أولًا."; ko="WindowTabs가 실행 중이지만 명령줄 명령을 처리할 수 없습니다. 먼저 다시 시작하세요." }

    /// The tab search box.
    module TabSearch =
        let prompt = { en="Type a title or app name to switch windows"; zh="输入标题或应用名，切换窗口"; ja="タイトルやアプリ名を入力してウィンドウを切り替え"; ar="اكتب عنوانًا أو اسم تطبيق للتبديل بين النوافذ"; ko="제목이나 앱 이름을 입력해 창 전환" }
        let noMatches = { en="No matching windows."; zh="没有匹配的窗口。"; ja="一致するウィンドウはありません。"; ar="لا توجد نوافذ مطابقة."; ko="일치하는 창이 없습니다." }
        /// Beside a window on another virtual desktop that has no name of its own.
        let desktop number = { en=sprintf "Desktop %d" number; zh=sprintf "桌面 %d" number; ja=sprintf "デスクトップ %d" number; ar=sprintf "سطح المكتب %d" number; ko=sprintf "데스크톱 %d" number }
        let otherDesktop = { en="Another desktop"; zh="其他桌面"; ja="別のデスクトップ"; ar="سطح مكتب آخر"; ko="다른 데스크톱" }
        let groupTabs count = { en=sprintf "This group · %d" count; zh=sprintf "当前分组 · %d" count; ja=sprintf "このグループ · %d" count; ar=sprintf "هذه المجموعة · %d" count; ko=sprintf "이 그룹 · %d" count }
        let allTabs count = { en=sprintf "All windows · %d" count; zh=sprintf "全局窗口 · %d" count; ja=sprintf "すべてのウィンドウ · %d" count; ar=sprintf "كل النوافذ · %d" count; ko=sprintf "모든 창 · %d" count }
        /// The palette's other pages, beside the windows.
        let actions = { en="Actions"; zh="操作"; ja="アクション"; ar="الإجراءات"; ko="동작" }
        let groups = { en="Groups"; zh="分组"; ja="グループ"; ar="المجموعات"; ko="그룹" }
        let actionPrompt = { en="Type a command or setting"; zh="输入命令或设置名"; ja="コマンドや設定名を入力"; ar="اكتب أمرًا أو إعدادًا"; ko="명령 또는 설정 입력" }
        let groupPrompt = { en="Type a group name, title or app to switch groups"; zh="输入分组名、标题或应用名，切换分组"; ja="グループ名、タイトル、アプリ名を入力してグループを切り替え"; ar="اكتب اسم مجموعة أو عنوانًا أو تطبيقًا للتبديل بين المجموعات"; ko="그룹 이름, 제목 또는 앱 이름을 입력해 그룹 전환" }
        let choicePrompt = { en="Choose, or type to filter"; zh="选择一项，或输入以筛选"; ja="選択するか、入力して絞り込み"; ar="اختر، أو اكتب للتصفية"; ko="선택하거나 입력해 필터링" }
        let noActions = { en="No matching commands or settings."; zh="没有匹配的命令或设置。"; ja="一致するコマンドや設定はありません。"; ar="لا توجد أوامر أو إعدادات مطابقة."; ko="일치하는 명령이나 설정이 없습니다." }
        let noGroups = { en="No matching groups."; zh="没有匹配的分组。"; ja="一致するグループはありません。"; ar="لا توجد مجموعات مطابقة."; ko="일치하는 그룹이 없습니다." }
        let noOpenGroups = { en="No open groups."; zh="当前没有打开的分组。"; ja="開いているグループはありません。"; ar="لا توجد مجموعات مفتوحة."; ko="열려 있는 그룹이 없습니다." }
        let noChoices = { en="No matching choices."; zh="没有匹配的选项。"; ja="一致する項目はありません。"; ar="لا توجد خيارات مطابقة."; ko="일치하는 항목이 없습니다." }
        /// Asking for a size: the hint in the box, the line under it, and why a value is refused.
        let numberPrompt low high = { en=sprintf "A number from %d to %d" low high; zh=sprintf "输入 %d 到 %d 之间的数字" low high; ja=sprintf "%d～%d の数値" low high; ar=sprintf "رقم من %d إلى %d" low high; ko=sprintf "%d~%d 사이의 숫자" low high }
        let colorRefused = { en="Type a colour as #RRGGBB."; zh="请输入 #RRGGBB 格式的颜色。"; ja="色を #RRGGBB の形式で入力してください。"; ar="اكتب اللون بالصيغة ‎#RRGGBB."; ko="색을 #RRGGBB 형식으로 입력하세요." }
        let numberRefused low high ={ en=sprintf "Type a whole number from %d to %d." low high; zh=sprintf "请输入 %d 到 %d 之间的整数。" low high; ja=sprintf "%d～%d の整数を入力してください。" low high; ar=sprintf "اكتب عددًا صحيحًا من %d إلى %d." low high; ko=sprintf "%d~%d 사이의 정수를 입력하세요." low high }
        /// Beside the Alt+Enter key cap on the commands page.
        let setShortcut = { en="Set shortcut"; zh="设置快捷键"; ja="ショートカットを設定"; ar="تعيين اختصار"; ko="단축키 설정" }
        /// Beside a workspace or a group: how many windows it holds.
        let windows count =
            { en=(if count=1 then "1 window" else sprintf "%d windows" count); zh=sprintf "%d 个窗口" count; ja=sprintf "%d 個のウィンドウ" count
              ar=(match count with 1 -> "نافذة واحدة" | 2 -> "نافذتان" | n when n>=3 && n<=10 -> sprintf "%d نوافذ" n | n -> sprintf "%d نافذة" n); ko=sprintf "창 %d개" count }
        /// Beside a setting that is on or off.
        let on = { en="On"; zh="开"; ja="オン"; ar="مفعّل"; ko="켬" }
        let off = { en="Off"; zh="关"; ja="オフ"; ar="معطّل"; ko="끔" }

    /// The Gestures page, its add-gesture dialog and the note by the pointer.
    module MouseGestures =
        let scopes = { en="Applies to"; zh="作用于"; ja="適用先"; ar="ينطبق على"; ko="적용 대상" }
        let everywhere = { en="Everywhere"; zh="全局"; ja="全体"; ar="في كل مكان"; ko="전체" }
        let allApps = { en="All apps"; zh="所有程序"; ja="すべてのアプリ"; ar="كل التطبيقات"; ko="모든 앱" }
        let addApp = { en="Add app"; zh="添加程序"; ja="アプリを追加"; ar="إضافة تطبيق"; ko="앱 추가" }
        let addGesture = { en="Add gesture"; zh="添加手势"; ja="ジェスチャーを追加"; ar="إضافة إيماءة"; ko="제스처 추가" }
        /// The link under the tiles that puts out the box to draw a gesture in, and away again.
        let drawInstead = { en="Draw it with the mouse"; zh="用鼠标画出手势"; ja="マウスで描く"; ar="ارسمها بالفأرة"; ko="마우스로 그리기" }
        let hideDrawing = { en="Hide the drawing box"; zh="收起手绘区域"; ja="描画エリアを閉じる"; ar="إخفاء مربع الرسم"; ko="그리기 영역 숨기기" }
        /// The dialog's title when a gesture of the list is opened in it, filled in.
        let editGesture = { en="Edit gesture"; zh="编辑手势"; ja="ジェスチャーを編集"; ar="تعديل الإيماءة"; ko="제스처 편집" }
        let gesture = { en="Gesture"; zh="手势"; ja="ジェスチャー"; ar="الإيماءة"; ko="제스처" }
        let action = { en="Action"; zh="动作"; ja="動作"; ar="الإجراء"; ko="동작" }
        let does = { en="Does"; zh="执行"; ja="実行内容"; ar="ينفّذ"; ko="실행 내용" }
        let commandKind = { en="Command"; zh="命令"; ja="コマンド"; ar="أمر"; ko="명령" }
        let keysKind = { en="Shortcut"; zh="快捷键"; ja="ショートカット"; ar="اختصار"; ko="단축키" }
        let overridesEverywhere arrows (action:string) =
            { en=sprintf "Overrides %s for all apps: %s" arrows action; zh=sprintf "覆盖了全局的 %s：%s" arrows action
              ja=sprintf "全体の %s を上書きします: %s" arrows action; ar=sprintf "يتجاوز %s لكل التطبيقات: %s" arrows action; ko=sprintf "모든 앱의 %s을(를) 재정의합니다: %s" arrows action }
        let removeApp (app:string) = { en=sprintf "Remove %s and its gestures" app; zh=sprintf "移除 %s 及其手势" app; ja=sprintf "%s とそのジェスチャーを削除" app; ar=sprintf "إزالة %s وإيماءاته" app; ko=sprintf "%s 및 해당 제스처 제거" app }
        let useIn (app:string) = { en=sprintf "Use gestures in %s" app; zh=sprintf "在 %s 中使用手势" app; ja=sprintf "%s でジェスチャーを使う" app; ar=sprintf "استخدام الإيماءات في %s" app; ko=sprintf "%s에서 제스처 사용" app }
        let resetEverywhere = { en="Restore the default gestures for all apps"; zh="恢复默认的全局手势"; ja="全体のジェスチャーを既定に戻す"; ar="استعادة الإيماءات الافتراضية لكل التطبيقات"; ko="모든 앱의 제스처를 기본값으로 복원" }
        let resetApp = { en="Restore this app's default gestures"; zh="恢复这个程序的默认手势"; ja="このアプリのジェスチャーを既定に戻す"; ar="استعادة الإيماءات الافتراضية لهذا التطبيق"; ko="이 앱의 제스처를 기본값으로 복원" }
        let legend = { en="Press the right button at the circle, release at the arrow"; zh="圆圈处按下右键，到箭头处松开"; ja="円で右ボタンを押し、矢印で離します"; ar="اضغط الزر الأيمن عند الدائرة وحرّره عند السهم"; ko="원에서 오른쪽 버튼을 누르고 화살표에서 놓으세요" }
        let holdToDrag = { en="Hold still a moment, or hold Shift, Ctrl or Alt, for the app's own right button"; zh="按住不动片刻，或按住 Shift、Ctrl、Alt，右键就交给应用"; ja="少し止めるか Shift・Ctrl・Alt を押すと、右ボタンはアプリのものになります"; ar="اثبت لحظة أو اضغط Shift أو Ctrl أو Alt ليصبح الزر الأيمن للتطبيق"; ko="잠시 멈추거나 Shift, Ctrl, Alt를 누르면 오른쪽 버튼이 앱으로 전달됩니다" }
        /// By the pointer once a right press held still is given back to the app: dragging is then
        /// the app's right-drag, and letting go its menu.
        let passedToApp = { en="Right-click passed to the app"; zh="右键已交给应用"; ja="右ボタンの入力をアプリに渡しました"; ar="أُرسل ضغط الزر الأيمن إلى التطبيق"; ko="오른쪽 버튼 입력을 앱에 전달했습니다" }
        let browserPreset = { en="browser preset"; zh="浏览器预设"; ja="ブラウザーのプリセット"; ar="الإعداد المسبق للمتصفح"; ko="브라우저 프리셋" }
        let explorerPreset = { en="File Explorer preset"; zh="资源管理器预设"; ja="エクスプローラーのプリセット"; ar="الإعداد المسبق لمستكشف الملفات"; ko="파일 탐색기 프리셋" }
        let presetFilled (preset:LocalizedText) =
            { en=sprintf "Filled in the %s." preset.en; zh=sprintf "已填入%s。" preset.zh; ja=sprintf "%sを入れました。" preset.ja; ar=sprintf "تمت تعبئة %s." preset.ar; ko=sprintf "%s을(를) 적용했습니다." preset.ko }
        let startEmpty = { en="Start empty"; zh="改为空白"; ja="空にする"; ar="البدء فارغًا"; ko="빈 상태로 시작" }
        let noOwnGestures = { en="This app has no gestures of its own; the ones for all apps work here."; zh="这个程序还没有单独的手势，全局手势照常生效。"; ja="このアプリ独自のジェスチャーはありません。全体のジェスチャーが使えます。"; ar="ليس لهذا التطبيق إيماءات خاصة به؛ تعمل هنا إيماءات كل التطبيقات."; ko="이 앱에는 자체 제스처가 없습니다. 모든 앱용 제스처가 적용됩니다." }
        let offIn (app:string) =
            { en=sprintf "Gestures are off in %s, those for all apps included. Turn its switch on to use them again." app
              zh=sprintf "已在 %s 中关闭手势，包括全局手势。打开左边的开关即可恢复。" app
              ja=sprintf "%s ではジェスチャーがオフです (全体のものも含む)。左のスイッチをオンにすると戻ります。" app
              ar=sprintf "الإيماءات متوقفة في %s، بما فيها إيماءات كل التطبيقات. شغّل مفتاحه لاستخدامها مجددًا." app
              ko=sprintf "%s에서는 모든 앱용 제스처까지 모두 꺼져 있습니다. 스위치를 켜면 다시 사용할 수 있습니다." app }
        let everywhereOff = { en="The gestures for all apps are off; apps on the list use only their own. Turn its switch on to use them again."
                              zh="全局手势已关闭，列表中的程序只用自己的手势。打开左边的开关即可恢复。"
                              ja="全体のジェスチャーはオフです。一覧のアプリは自分のジェスチャーだけを使います。左のスイッチをオンにすると戻ります。"
                              ar="إيماءات كل التطبيقات متوقفة، وتستخدم التطبيقات في القائمة إيماءاتها فقط. شغّل مفتاحها لاستخدامها مجددًا."
                              ko="모든 앱용 제스처가 꺼져 있습니다. 목록의 앱은 자체 제스처만 사용합니다. 스위치를 켜면 다시 사용할 수 있습니다." }
        let useEverywhere = { en="Use the gestures for all apps"; zh="使用全局手势"; ja="全体のジェスチャーを使う"; ar="استخدام إيماءات كل التطبيقات"; ko="모든 앱용 제스처 사용" }
        /// A gesture's strokes in words, as a screen reader says it and the dialog names it.
        let drawn (gesture:string) =
            let word index (en:string[]) (zh:string[]) (ja:string[]) (ar:string[]) (ko:string[]) =
                let i = match gesture.[index] with 'U' -> 0 | 'D' -> 1 | 'L' -> 2 | _ -> 3
                en.[i],zh.[i],ja.[i],ar.[i],ko.[i]
            let words index = word index [|"up";"down";"left";"right"|] [|"向上";"向下";"向左";"向右"|] [|"上";"下";"左";"右"|] [|"لأعلى";"لأسفل";"لليسار";"لليمين"|] [|"위";"아래";"왼쪽";"오른쪽"|]
            let capital (text:string) = string (System.Char.ToUpperInvariant text.[0])+text.Substring(1)
            let en1,zh1,ja1,ar1,ko1 = words 0
            if gesture.Length=1 then { en=capital en1; zh=zh1; ja=ja1; ar=ar1; ko=ko1 }
            else
                let en2,zh2,ja2,ar2,ko2 = words 1
                { en=capital en1+", then "+en2; zh=zh1+"再"+zh2; ja=ja1+"、次に"+ja2; ar=ar1+"، ثم "+ar2; ko=ko1+", 이어서 "+ko2 }
        /// What a key sent by a preset is called, where it has no name of the user's.
        let back = { en="Back"; zh="后退"; ja="戻る"; ar="رجوع"; ko="뒤로" }
        let forward = { en="Forward"; zh="前进"; ja="進む"; ar="تقدّم"; ko="앞으로" }
        let closeWebTab = { en="Close web tab"; zh="关闭网页标签"; ja="Web タブを閉じる"; ar="إغلاق علامة تبويب الويب"; ko="웹 탭 닫기" }
        let upOneFolder = { en="Up one folder"; zh="上一级"; ja="1 つ上のフォルダー"; ar="المجلد الأعلى"; ko="상위 폴더로" }
        // The add-gesture dialog.
        let appliesTo (scope:string) = { en="For "+scope; zh="作用于 "+scope; ja=scope+" に適用"; ar="لـ "+scope; ko=scope+"에 적용" }
        let pickOne = { en="Gesture · pick one"; zh="手势 · 选一个"; ja="ジェスチャー · 1 つ選ぶ"; ar="الإيماءة · اختر واحدة"; ko="제스처 · 하나 선택" }
        let orDraw = { en="Pick one above, or draw it here with the mouse"; zh="在上面选一个，或在这里按住鼠标划出来"; ja="上で選ぶか、ここにマウスで描きます"; ar="اختر واحدة أعلاه، أو ارسمها هنا بالماوس"; ko="위에서 선택하거나 여기에 마우스로 그리세요" }
        let thisApp = { en="This app"; zh="本程序"; ja="このアプリ"; ar="هذا التطبيق"; ko="이 앱" }
        let findCommand = { en="Search commands"; zh="搜索命令"; ja="コマンドを検索"; ar="ابحث في الأوامر"; ko="명령 검색" }
        let keepGoing = { en="Keep going…"; zh="继续划…"; ja="続けて描いて…"; ar="تابع الرسم…"; ko="계속 그리세요…" }
        let tooShort = { en="Too short, try again"; zh="划得太短了，再试一次"; ja="短すぎます。もう一度描いてください"; ar="قصيرة جدًا، حاول مرة أخرى"; ko="너무 짧습니다. 다시 그리세요" }
        let tooMany = { en="More than two strokes"; zh="超过两段了"; ja="2 画を超えています"; ar="أكثر من خطين"; ko="두 획을 넘었습니다" }
        let notRecognized = { en="Not recognized: two strokes at most, up, down, left or right"; zh="认不出：最多两段，只认上下左右"; ja="認識できません: 上下左右に 2 画までです"; ar="غير معروفة: خطان على الأكثر، لأعلى أو لأسفل أو لليسار أو لليمين"; ko="인식할 수 없음: 위, 아래, 왼쪽, 오른쪽으로 최대 두 획" }
        let usedEverywhere = { en="Used for all apps"; zh="全局已用"; ja="全体で使用中"; ar="مستخدمة لكل التطبيقات"; ko="모든 앱에서 사용 중" }
        let used = { en="Used"; zh="已用"; ja="使用中"; ar="مستخدمة"; ko="사용 중" }
        let replaces (action:string) =
            { en=sprintf "replaces “%s”" action; zh=sprintf "将替换“%s”" action
              ja=sprintf "「%s」を置き換えます" action; ar=sprintf "تستبدل «%s»" action; ko=sprintf "“%s”을(를) 대체" action }
        let runCommand = { en="Run a WindowTabs command"; zh="执行 WindowTabs 命令"; ja="WindowTabs のコマンドを実行する"; ar="تشغيل أمر WindowTabs"; ko="WindowTabs 명령 실행" }
        let sendKeys = { en="Send a shortcut"; zh="发送快捷键"; ja="ショートカットを送る"; ar="إرسال اختصار"; ko="단축키 보내기" }
        let chooseCommand = { en="Choose a command…"; zh="选择命令…"; ja="コマンドを選択…"; ar="اختر أمرًا…"; ko="명령 선택…" }
        let name = { en="Name (optional)"; zh="名称（可选）"; ja="名前 (省略可)"; ar="الاسم (اختياري)"; ko="이름(선택 사항)" }
        let add = { en="Add"; zh="添加"; ja="追加"; ar="إضافة"; ko="추가" }
        let needGesture = { en="Pick a gesture above, or draw one in the box."; zh="先在上面选一个手势，或者在框里划一个。"; ja="上でジェスチャーを選ぶか、枠内に描いてください。"; ar="اختر إيماءة أعلاه، أو ارسم واحدة داخل المربع."; ko="위에서 제스처를 선택하거나 상자에 그리세요." }
        let needCommand = { en="Choose the command to run."; zh="选择要执行的命令。"; ja="実行するコマンドを選んでください。"; ar="اختر الأمر المطلوب تشغيله."; ko="실행할 명령을 선택하세요." }
        let needKeys = { en="Click the shortcut box and press the keys to send."; zh="点快捷键框，按下要发送的快捷键。"; ja="ショートカット欄をクリックして、送るキーを押してください。"; ar="انقر مربع الاختصار واضغط المفاتيح المطلوب إرسالها."; ko="단축키 상자를 클릭하고 보낼 키를 누르세요." }

    module AppRules =
        let addApplication ={ en="Add application"; zh="添加应用"; ja="アプリを追加"; ar="إضافة تطبيق"; ko="앱 추가" }
        let deleteRule = { en="Delete"; zh="删除"; ja="削除"; ar="حذف"; ko="삭제" }
        let applicationRuleExists = { en="This application already has a grouping rule."; zh="这个应用已有分组规则，请编辑已有行。"; ja="このアプリには既にグループ化ルールがあります。既存の行を編集してください。"; ar="لدى هذا التطبيق قاعدة تجميع بالفعل."; ko="이 앱에는 이미 그룹화 규칙이 있습니다." }
        /// Several apps sharing one named group, as opposed to each app's windows grouping by themselves.
        let groupingRules = { en="Groups across apps"; zh="跨应用分组"; ja="アプリをまたぐグループ"; ar="مجموعات تضم تطبيقات مختلفة"; ko="여러 앱을 묶는 그룹" }
        let applications = { en="Applications"; zh="应用"; ja="アプリ"; ar="التطبيقات"; ko="앱" }
        let addGroup = { en="Add group"; zh="添加组"; ja="グループを追加"; ar="إضافة مجموعة"; ko="그룹 추가" }
        /// Shown while Groups across apps is empty.
        let noGroups = { en="Add a group to put several apps together, or drag tabs onto each other: the groups show here"
                         zh="点“添加组”把多个应用放在一起，或拖拽标签合并窗口，分组会显示在这里"
                         ja="「グループを追加」で複数のアプリをまとめるか、タブをドラッグして重ねると、ここに表示されます"
                         ar="أضف مجموعة لضم عدة تطبيقات معًا، أو اسحب علامات التبويب فوق بعضها؛ ستظهر المجموعات هنا"
                         ko="그룹을 추가해 여러 앱을 묶거나, 탭을 서로 끌어다 놓으세요. 그룹이 여기에 표시됩니다" }
        /// The name a new group starts with.
        let newGroup number = { en=sprintf "Group %d" number; zh=sprintf "组 %d" number; ja=sprintf "グループ %d" number; ar=sprintf "المجموعة %d" number; ko=sprintf "그룹 %d" number }
        let selectRule = { en="Select a group or rule to edit here. Changes save automatically."; zh="选中组或规则即可在此编辑，修改会自动保存。"; ja="グループかルールを選択して編集します。変更は自動保存されます。"; ar="حدد مجموعة أو قاعدة لتحريرها هنا. تُحفظ التغييرات تلقائيًا."; ko="편집할 그룹이나 규칙을 선택하세요. 변경 내용은 자동으로 저장됩니다." }
        let newRule = { en="New rule"; zh="新规则"; ja="新しいルール"; ar="قاعدة جديدة"; ko="새 규칙" }
        let groupRulesHelp = { en="Put windows of different apps into one named group. Apps added here always have tabs, leave the list above and don't use Auto-group. Click an app to take only some of its windows, by title, window class or command line, or to remove it."
                               zh="把不同应用的窗口放进同一个命名组。加入这里的应用会始终启用标签，不再出现在上面的列表里，也不再使用“自动分组”。点击应用可只收纳它的部分窗口（按标题、窗口类名或命令行），或将其移除。"
                               ja="異なるアプリのウィンドウを名前付きの一つのグループにまとめます。ここに追加したアプリは常にタブを使い、上の一覧からは外れ、「自動グループ化」も使われません。アプリをクリックすると、タイトル・ウィンドウ クラス・コマンド ラインで一部のウィンドウだけを対象にしたり、外したりできます。"
                               ar="ضع نوافذ تطبيقات مختلفة في مجموعة واحدة ذات اسم. التطبيقات المضافة هنا تستخدم علامات التبويب دائمًا، وتُزال من القائمة أعلاه ولا تستخدم التجميع التلقائي. انقر تطبيقًا لضم بعض نوافذه فقط حسب العنوان أو فئة النافذة أو سطر الأوامر، أو لإزالته."
                               ko="여러 앱의 창을 이름을 붙인 그룹 하나로 묶습니다. 여기에 추가한 앱은 항상 탭을 사용하고, 위 목록에서 빠지며 자동 그룹화를 사용하지 않습니다. 앱을 클릭하면 제목, 창 클래스, 명령줄로 일부 창만 포함하거나 앱을 제거할 수 있습니다." }
        /// Manual groups include both dragged tabs and groups created with the picker.
        let handMadeHeading count = { en=sprintf "Groups created manually (%d)" count; zh=sprintf "手动分组（%d）" count; ja=sprintf "手動グループ（%d）" count; ar=sprintf "مجموعات يدوية (%d)" count; ko=sprintf "수동 그룹(%d)" count }
        let handMadeHelp = { en="Groups you created with New group or by dragging tabs together. They are restored when WindowTabs restarts while their windows remain open. Ungroup forgets the group and separates its open windows."
                             zh="通过“新建组”或拖拽标签合并创建的分组。窗口仍打开时，重启 WindowTabs 会恢复这些分组。“解除分组”会忘记该组并拆开它打开的窗口。"
                             ja="「新しいグループ」またはタブのドラッグで作成したグループです。ウィンドウが開いたままであれば、WindowTabs の再起動後に復元されます。「グループを解除」はグループを忘れ、開いているウィンドウを分けます。"
                             ar="مجموعات أنشأتها باستخدام «مجموعة جديدة» أو بسحب علامات التبويب معًا. تُستعاد عند إعادة تشغيل WindowTabs ما دامت نوافذها مفتوحة. «إلغاء التجميع» ينسى المجموعة ويفصل نوافذها المفتوحة."
                             ko="‘새 그룹’이나 탭 끌기로 만든 그룹입니다. 창이 열린 상태에서 WindowTabs를 다시 시작하면 복원됩니다. ‘그룹 해제’를 누르면 그룹이 삭제되고 열린 창이 분리됩니다." }
        let application = { en="Application"; zh="应用"; ja="アプリ"; ar="التطبيق"; ko="앱" }
        let chooseApplication = { en="Add an app"; zh="添加应用"; ja="アプリを追加"; ar="إضافة تطبيق"; ko="앱 추가" }
        /// Hover tips on a group's buttons.
        let ungroup = { en="Ungroup"; zh="解除分组"; ja="グループを解除"; ar="إلغاء التجميع"; ko="그룹 해제" }
        let removeFromGroup = { en="Remove from group"; zh="从分组中移除"; ja="グループから外す"; ar="إزالة من المجموعة"; ko="그룹에서 제거" }
        let browseApplication = { en="Browse…"; zh="浏览…"; ja="参照…"; ar="استعراض…"; ko="찾아보기…" }
        let findApplication = { en="Find an app"; zh="查找程序"; ja="アプリを検索"; ar="ابحث عن تطبيق"; ko="앱 찾기" }
        let applicationFiles = { en="Applications (*.exe)|*.exe"; zh="应用程序 (*.exe)|*.exe"; ja="アプリ (*.exe)|*.exe"; ar="التطبيقات (*.exe)|*.exe"; ko="앱 (*.exe)|*.exe" }
        let allWindows = { en="All windows"; zh="全部窗口"; ja="すべてのウィンドウ"; ar="كل النوافذ"; ko="모든 창" }
        let targetGroup = { en="Target group"; zh="目标组"; ja="宛先グループ"; ar="المجموعة المستهدفة"; ko="대상 그룹" }
        let chooseGroup = { en="Existing group"; zh="选择已有组"; ja="既存のグループ"; ar="مجموعة موجودة"; ko="기존 그룹" }
        let enabled = { en="Enabled"; zh="启用"; ja="有効"; ar="ممكّن"; ko="사용" }
        let addRule = { en="Add rule"; zh="添加规则"; ja="ルールを追加"; ar="إضافة قاعدة"; ko="규칙 추가" }
        let invalidRule = { en="Invalid grouping rule"; zh="分组规则无效"; ja="無効なグループ化ルール"; ar="قاعدة التجميع غير صالحة"; ko="잘못된 그룹화 규칙" }
        let requiredFields = { en="Choose an application and enter a target group."; zh="请选择应用并填写目标组。"; ja="アプリを選択して宛先グループを入力してください。"; ar="اختر تطبيقًا وأدخل المجموعة المستهدفة."; ko="앱을 선택하고 대상 그룹을 입력하세요." }
        let requiredTitle = { en="Enter what to match."; zh="请填写匹配内容。"; ja="一致させる内容を入力してください。"; ar="أدخل ما تريد مطابقته."; ko="일치시킬 내용을 입력하세요." }
        let invalidPattern details = { en="The pattern cannot be used: " + details; zh="无法使用这个模式：" + details; ja="このパターンは使えません: " + details; ar="تعذر استخدام هذا النمط: " + details; ko="이 패턴은 사용할 수 없습니다: " + details }
        /// What a rule can look at beside the app, as the condition's first choice.
        let windowTitle = { en="Window title"; zh="窗口标题"; ja="ウィンドウのタイトル"; ar="عنوان النافذة"; ko="창 제목" }
        let windowClass = { en="Window class"; zh="窗口类名"; ja="ウィンドウ クラス"; ar="فئة النافذة"; ko="창 클래스" }
        let commandLine = { en="Command line"; zh="命令行"; ja="コマンド ライン"; ar="سطر الأوامر"; ko="명령줄" }
        /// What each field is and when to match on it, under the condition dialog's heading.
        let tabTitle = { en="Tab title"; zh="标签标题"; ja="タブのタイトル"; ar="عنوان علامة التبويب"; ko="탭 제목" }
        let tabTitleHint = { en="The title shown on the tab, including a name you set yourself."; zh="标签上显示的标题，包括手动设置的名称。"; ja="手動で設定した名前を含む、タブに表示されるタイトル。"; ar="العنوان المعروض على علامة التبويب، بما في ذلك الاسم الذي تحدده بنفسك."; ko="직접 지정한 이름을 포함하여 탭에 표시되는 제목입니다." }
        let windowTitleHint = { en="The text in the title bar; it changes with what the window shows."
                                zh="标题栏里的文字，会随窗口内容变化。"
                                ja="タイトル バーの文字。表示内容によって変わります。"
                                ar="النص في شريط العنوان؛ يتغير مع ما تعرضه النافذة."
                                ko="제목 표시줄의 텍스트입니다. 창에 표시되는 내용에 따라 바뀝니다." }
        let windowClassHint = { en="The window's internal type name; it stays the same across restarts, so it suits windows whose titles change."
                                zh="窗口的内部类型名，重启后也不变；标题经常变化时用它。"
                                ja="ウィンドウ内部の種類名。再起動しても変わらないので、タイトルがよく変わるウィンドウに向きます。"
                                ar="اسم النوع الداخلي للنافذة؛ يبقى كما هو بعد إعادة التشغيل، فيناسب النوافذ التي يتغير عنوانها."
                                ko="창의 내부 종류 이름입니다. 다시 시작해도 바뀌지 않으므로 제목이 자주 바뀌는 창에 적합합니다." }
        let commandLineHint = { en="The arguments the window's program was started with; they tell apart apps that share one program, such as pythonw or java."
                                zh="窗口程序启动时带的参数；用来区分共用同一个程序的应用，如 pythonw、java。"
                                ja="ウィンドウのプログラムに起動時に渡された引数。pythonw や java など同じプログラムを共有するアプリを見分けます。"
                                ar="الوسائط التي بدأ بها برنامج النافذة؛ تميز التطبيقات التي تشترك في برنامج واحد مثل pythonw أو java."
                                ko="창의 프로그램이 시작될 때 받은 인수입니다. pythonw나 java처럼 하나의 프로그램을 공유하는 앱을 구분합니다." }
        /// Over the chip's menu: what its choices decide.
        let menuHeading = { en="Which windows join"; zh="收纳哪些窗口"; ja="含めるウィンドウ"; ar="أي النوافذ تنضم"; ko="포함할 창" }
        /// Over an empty list of command lines: the app's windows are open, with nothing after the program.
        let noArguments = { en="Its windows open now were started with no arguments."; zh="它当前打开的窗口启动时都没有带参数。"; ja="今開いているウィンドウは引数なしで起動されています。"; ar="نوافذه المفتوحة الآن بدأت بلا وسائط."; ko="지금 열려 있는 창은 인수 없이 시작되었습니다." }
        /// Over the values of the app's open windows, which a click puts in the pattern.
        let openWindowsNow = { en="From the app's windows open now, to click:"; zh="该应用当前打开的窗口，点击填入："; ja="このアプリで今開いているウィンドウ（クリックで入力）："; ar="من نوافذ التطبيق المفتوحة الآن، انقر للإدخال:"; ko="지금 열려 있는 이 앱의 창(클릭하여 입력):" }
        let noOpenWindows = { en="The app has no window open now."; zh="该应用现在没有打开的窗口。"; ja="このアプリのウィンドウは今開いていません。"; ar="لا توجد نافذة مفتوحة لهذا التطبيق الآن."; ko="지금 열려 있는 이 앱의 창이 없습니다." }
        /// What an app's chip menu is, for assistive technology; nothing is said on hover.
        let chipTip = { en="Which of the app's windows join: all, or those whose title, window class or command line matches; or remove the app"
                        zh="该应用的哪些窗口加入：全部，或标题、窗口类名或命令行匹配的窗口；或移除该应用"
                        ja="このアプリのどのウィンドウを含めるか：すべて、またはタイトル・ウィンドウ クラス・コマンド ラインが一致するもの。またはアプリを外す"
                        ar="أي نوافذ التطبيق تنضم: كلها، أو التي يطابق عنوانها أو فئتها أو سطر أوامرها؛ أو إزالة التطبيق"
                        ko="이 앱의 어떤 창을 포함할지 선택: 모두 또는 제목, 창 클래스, 명령줄이 일치하는 창. 또는 앱 제거" }
        /// The chip's menu: a field to match on, which asks for the condition in a dialog.
        let conditionChoice field = { en=field+"…"; zh=field+"…"; ja=field+"…"; ar=field+"…"; ko=field+"…" }
        /// The condition dialog's title: the app whose chip opened it and the field it matches on.
        let conditionFor app field = { en=sprintf "%s – %s" app field; zh=sprintf "%s · %s" app field; ja=sprintf "%s – %s" app field; ar=sprintf "%s – %s" app field; ko=sprintf "%s – %s" app field }
        /// Said on hover over a chip that takes only some of its app's windows.
        let conditionNote field (method:string) pattern =
            { en=sprintf "%s %s “%s”" field (method.ToLowerInvariant()) pattern; zh=sprintf "%s %s “%s”" field method pattern
              ja=sprintf "%s %s「%s」" field method pattern; ar=sprintf "%s %s «%s»" field method pattern; ko=sprintf "%s %s “%s”" field method pattern }
        let description = { en="Choose which apps get tabs. Auto-group puts the windows of an app into one group. Expand an app to see its windows."
                            zh="选择哪些应用使用标签。勾选“自动分组”后，同一应用的窗口会合在一个组里。展开应用可查看它的窗口。"
                            ja="タブを使うアプリを選びます。「自動グループ化」をオンにすると、同じアプリのウィンドウが一つのグループに入ります。展開するとウィンドウを表示します。"
                            ar="اختر التطبيقات التي تستخدم علامات التبويب. يجمع التجميع التلقائي نوافذ التطبيق الواحد في مجموعة واحدة. وسّع التطبيق لرؤية نوافذه."
                            ko="탭을 사용할 앱을 선택합니다. 자동 그룹화는 앱의 창을 하나의 그룹으로 묶습니다. 앱을 펼치면 창이 표시됩니다." }
        let tabs = { en="Tabs"; zh="标签"; ja="タブ"; ar="علامات التبويب"; ko="탭" }
        /// Each app's windows join one group of their own; named as in the tab menu.
        let autoGroup = { en="Auto-group"; zh="自动分组"; ja="自動グループ化"; ar="التجميع التلقائي"; ko="자동 그룹화" }
        /// ApplicationFrameHost.exe, which hosts every Store (UWP) app: Settings, Calculator, Photos.
        let windowsApps = { en="Windows apps"; zh="Windows 应用"; ja="Windows アプリ"; ar="تطبيقات Windows"; ko="Windows 앱" }
        /// Said on hover over the icon of a group made by dragging tabs together, shown read-only
        /// after the named groups.
        /// An open window of a group made by hand that will not be put back in it after a restart.
        let notKept = { en="Won't come back to this group after a restart: another window has had the same title"
                        zh="重启后不会回到这个分组：有其他窗口用过同样的标题"
                        ja="再起動後はこのグループに戻りません：同じタイトルのウィンドウが他にもありました"
                        ar="لن تعود إلى هذه المجموعة بعد إعادة التشغيل: نافذة أخرى حملت العنوان نفسه"
                        ko="다른 창과 제목이 같아 다시 시작하면 이 그룹으로 돌아오지 않습니다" }
        let handMadeGroups = { en="Manually grouped windows"; zh="手动分组的窗口"; ja="手動でまとめたウィンドウ"; ar="نوافذ مجمّعة يدويًا"; ko="수동으로 그룹화한 창" }
        let scanning = { en="Scanning…"; zh="正在扫描…"; ja="スキャン中…"; ar="جارٍ الفحص…"; ko="스캔 중…" }
        let scanFailed details = { en="Scan failed: " + details; zh="扫描失败：" + details; ja="スキャンに失敗しました: " + details; ar="فشل الفحص: " + details; ko="스캔 실패: " + details }

    module Workspaces =
        /// The page's two parts: the groups as they are, to rearrange, over the saved workspaces.
        let groupsNow = { en="Groups now"; zh="当前分组"; ja="今のグループ"; ar="المجموعات الآن"; ko="현재 그룹" }
        let groupsNowDescription = { en="Rearrange this desktop's tabs and panes."
                                     zh="重新安排当前桌面的标签和窗格。"
                                     ja="このデスクトップのタブとペインを並べ替えます。"
                                     ar="أعد ترتيب علامات سطح المكتب هذا وأجزائه."
                                     ko="이 데스크톱의 탭과 창 영역을 다시 정렬합니다." }
        let savedWorkspaces = { en="Saved workspaces"; zh="已保存的工作区"; ja="保存したワークスペース"; ar="مساحات العمل المحفوظة"; ko="저장된 작업 공간" }
        let savedDescription = { en="Save groups and positions to restore later."; zh="保存分组和位置，以后恢复。"; ja="グループと位置を保存し、あとで復元します。"; ar="احفظ المجموعات ومواضعها لاستعادتها لاحقًا."; ko="그룹과 위치를 저장해 나중에 복원합니다." }
        let help = { en="How to use\n1  Group and position your windows, then click Save layout.\n2  Select a workspace and click Restore selected.\n3  To change an item's name or window title matching, double-click it, or point at it and click its edit button.\nA window whose title has changed since it was saved is still matched by its app.\nRestore uses open windows. If an app is not running, the result offers to launch it."
                     zh="使用说明\n1  将窗口分组并调整位置，然后点击“保存布局”。\n2  选中工作区，点击“恢复所选”。\n3  双击任意项，或将鼠标移到该项上点击编辑按钮，可修改名称或窗口标题的匹配方式。\n保存后标题发生变化的窗口，仍会按其所属应用匹配。\n恢复使用已打开的窗口；应用未运行时，可在结果中启动它。"
                     ja="使い方\n1  ウィンドウをグループ化して配置し、「配置を保存」をクリックします。\n2  ワークスペースを選び、「選択したものを復元」をクリックします。\n3  項目をダブルクリックするか、項目にポインターを合わせて編集ボタンをクリックすると、名前やタイトルの一致方法を変更できます。\n保存後にタイトルが変わったウィンドウも、アプリで照合されます。\n復元は開いているウィンドウを使います。アプリが起動していない場合は、結果から起動できます。"
                     ar="طريقة الاستخدام\n١  جمّع النوافذ واضبط مواضعها، ثم انقر «حفظ التخطيط».\n٢  حدد مساحة عمل وانقر «استعادة المحدد».\n٣  لتغيير اسم عنصر أو طريقة مطابقة عنوان نافذة، انقر عليه مرتين أو أشر إليه ثم انقر زر التحرير.\nتظل النافذة التي تغير عنوانها منذ الحفظ مطابقةً باستخدام التطبيق الخاص بها.\nتستخدم الاستعادة النوافذ المفتوحة. إذا لم يكن التطبيق قيد التشغيل، فستتيح لك النتيجة تشغيله."
                     ko="사용 방법\n1  창을 그룹화하고 배치한 다음 ‘작업 공간 저장’을 클릭하세요.\n2  작업 공간을 선택하고 ‘선택 항목 복원’을 클릭하세요.\n3  항목의 이름이나 창 제목 일치 방식을 바꾸려면 항목을 두 번 클릭하거나, 항목을 가리키고 편집 버튼을 클릭하세요.\n저장 후 제목이 바뀐 창도 앱으로 찾습니다.\n복원은 열려 있는 창을 사용합니다. 앱이 실행 중이 아니면 결과에서 실행할 수 있습니다." }
        /// Saving makes a new workspace of the windows as they are; restoring acts on the selected one.
        let saveCurrent = { en="Save workspace"; zh="保存工作区"; ja="ワークスペースを保存"; ar="حفظ مساحة العمل"; ko="작업 공간 저장" }
        let restoreSelected = { en="Restore selected"; zh="恢复所选"; ja="選択したものを復元"; ar="استعادة المحدد"; ko="선택 항목 복원" }
        let editTitle name = { en=sprintf "Edit %s" name; zh=sprintf "编辑 %s" name; ja=sprintf "%s を編集" name; ar=sprintf "تحرير %s" name; ko=sprintf "%s 편집" name }
        let matchMethod = { en="Match method"; zh="匹配方式"; ja="一致方法"; ar="طريقة المطابقة"; ko="일치 방식" }
        let exactMatch = { en="Exact match"; zh="完全匹配"; ja="完全一致"; ar="مطابقة تامة"; ko="정확히 일치" }
        let startsWith = { en="Starts with"; zh="开头匹配"; ja="前方一致"; ar="يبدأ بـ"; ko="다음으로 시작" }
        let endsWith = { en="Ends with"; zh="结尾匹配"; ja="後方一致"; ar="ينتهي بـ"; ko="다음으로 끝남" }
        let contains = { en="Contains"; zh="包含"; ja="部分一致"; ar="يحتوي على"; ko="포함" }
        let regularExpression = { en="Regular expression"; zh="正则表达式"; ja="正規表現"; ar="تعبير نمطي"; ko="정규식" }
        let restoreTitle = { en="Workspace restore"; zh="恢复工作区"; ja="ワークスペースの復元"; ar="استعادة مساحة العمل"; ko="작업 공간 복원" }
        /// The result counts windows in three states, each with its mark.
        let restoredState = { en="Restored"; zh="已恢复"; ja="復元済み"; ar="تمت الاستعادة"; ko="복원됨" }
        let notFoundState = { en="Not found"; zh="未找到"; ja="見つからない"; ar="غير موجود"; ko="찾을 수 없음" }
        let failedState = { en="Failed"; zh="失败"; ja="失敗"; ar="فشل"; ko="실패" }
        /// Windows are matched by app when their title changed, so one not found is closed.
        let notFoundHint = { en="These windows are closed, or their app is not running."; zh="这些窗口已关闭，或其应用未运行。"; ja="これらのウィンドウは閉じられているか、アプリが起動していません。"; ar="هذه النوافذ مغلقة أو أن التطبيق الخاص بها لا يعمل."; ko="이 창들은 닫혔거나 해당 앱이 실행 중이 아닙니다." }
        let launchApps count =
            { en=(if count=1 then "Launch the app and retry" else sprintf "Launch %d apps and retry" count)
              zh=sprintf "启动 %d 个应用并重试" count
              ja=sprintf "%d 個のアプリを起動して再試行" count
              ar=(if count=1 then "تشغيل التطبيق وإعادة المحاولة" else sprintf "تشغيل التطبيقات (%d) وإعادة المحاولة" count)
              ko=(if count=1 then "앱을 실행하고 다시 시도" else sprintf "앱 %d개를 실행하고 다시 시도" count) }
        let launchingApps count =
            { en=(if count=1 then "Launching the app…" else sprintf "Launching %d apps…" count)
              zh=sprintf "正在启动 %d 个应用…" count
              ja=sprintf "%d 個のアプリを起動中…" count
              ar=(if count=1 then "جارٍ تشغيل التطبيق…" else sprintf "جارٍ تشغيل التطبيقات (%d)…" count)
              ko=(if count=1 then "앱 실행 중…" else sprintf "앱 %d개 실행 중…" count) }
        let noneSaved = { en="No workspace saved yet"; zh="尚未保存任何工作区"; ja="保存されたワークスペースはまだありません"; ar="لم يتم حفظ أي مساحة عمل بعد"; ko="아직 저장된 작업 공간이 없습니다" }
        let invalidSetting = { en="Invalid workspace setting"; zh="工作区设置无效"; ja="ワークスペースの設定が無効です"; ar="إعداد مساحة العمل غير صالح"; ko="잘못된 작업 공간 설정" }
        let dataWarnings = { en="Workspace data"; zh="工作区数据"; ja="ワークスペースのデータ"; ar="بيانات مساحة العمل"; ko="작업 공간 데이터" }
        let titleInvalid = { en="Window title is missing or exceeds 4,096 characters."; zh="窗口标题缺失或超过 4,096 个字符。"; ja="ウィンドウのタイトルがないか、4,096 文字を超えています。"; ar="عنوان النافذة مفقود أو يتجاوز 4096 حرفًا."; ko="창 제목이 없거나 4,096자를 넘습니다." }
        let unknownMatchMethod = { en="Unknown title match method."; zh="未知的标题匹配方式。"; ja="タイトルの一致方法が不明です。"; ar="طريقة مطابقة العنوان غير معروفة."; ko="알 수 없는 제목 일치 방식입니다." }
        let expectedObject = { en="Expected an object."; zh="应为对象。"; ja="オブジェクトが必要です。"; ar="كان متوقعًا وجود كائن."; ko="개체가 필요합니다." }
        let expectedList = { en=": expected a list."; zh="：应为列表。"; ja="：リストが必要です。"; ar=": كان متوقعًا وجود قائمة."; ko=": 목록이 필요합니다." }
        let missingPlacement = { en="Missing placement."; zh="缺少窗口位置。"; ja="ウィンドウの配置がありません。"; ar="موضع النافذة مفقود."; ko="창 배치 정보가 없습니다." }
        let invalidTabLayout = { en="Invalid saved tab layout; the group will use current defaults."; zh="保存的标签布局无效；该分组将使用当前默认设置。"; ja="保存されたタブ配置が無効です。グループには現在の既定値を使用します。"; ar="تخطيط علامات التبويب المحفوظ غير صالح؛ ستستخدم المجموعة الإعدادات الافتراضية الحالية."; ko="저장된 탭 레이아웃이 잘못되었습니다. 이 그룹은 현재 기본값을 사용합니다." }
        let invalidSplit = { en="Invalid saved split view; the group will restore without it."; zh="保存的分屏无效；该分组恢复时不带分屏。"; ja="保存された分割表示が無効です。グループは分割なしで復元されます。"; ar="العرض المقسّم المحفوظ غير صالح؛ ستتم استعادة المجموعة بدونه."; ko="저장된 분할 보기가 잘못되었습니다. 이 그룹은 분할 보기 없이 복원됩니다." }
        let noValidWindows = { en="No valid windows in this group."; zh="此分组中没有有效窗口。"; ja="このグループに有効なウィンドウがありません。"; ar="لا توجد نوافذ صالحة في هذه المجموعة."; ko="이 그룹에 유효한 창이 없습니다." }
        let unsupportedVersion = { en="Unsupported workspace version; saved workspaces will not be modified."
                                   zh="不支持此工作区版本；已保存的工作区不会被修改。"
                                   ja="このワークスペースのバージョンには対応していません。保存済みのワークスペースは変更されません。"
                                   ar="إصدار مساحة العمل غير مدعوم؛ لن يتم تعديل مساحات العمل المحفوظة."
                                   ko="지원하지 않는 작업 공간 버전입니다. 저장된 작업 공간은 수정되지 않습니다." }

    module Diagnostics =
        let rawReport = { en="View raw JSON"; zh="查看原始 JSON"; ja="元の JSON を表示"; ar="عرض JSON الخام"; ko="원본 JSON 보기" }
        let taskbarSummary = { en="Taskbar grouping"; zh="任务栏分组"; ja="タスクバーのグループ化"; ar="تجميع شريط المهام"; ko="작업 표시줄 그룹화" }
        let combinedCount a b = { en=sprintf "Combined groups: %d/%d" a b; zh=sprintf "已合并 %d/%d 个分组" a b; ja=sprintf "結合済みグループ: %d/%d" a b; ar=sprintf "المجموعات المدمجة: %d/%d" a b; ko=sprintf "합쳐진 그룹: %d/%d" a b }
        let noGroups = { en="No multi-window groups"; zh="没有多窗口分组"; ja="複数ウィンドウのグループなし"; ar="لا توجد مجموعات متعددة النوافذ"; ko="창이 여러 개인 그룹 없음" }
        let groupLine i n = { en=sprintf "Group %d (%d windows)" i n; zh=sprintf "分组 %d（%d 个窗口）" i n; ja=sprintf "グループ %d（ウィンドウ %d 個）" i n; ar=sprintf "المجموعة %d (%d نوافذ)" i n; ko=sprintf "그룹 %d (창 %d개)" i n }
        let combined = { en="Combined"; zh="已合并"; ja="結合済み"; ar="مدمجة"; ko="합쳐짐" }
        let singleWindow = { en="Single window"; zh="单个窗口"; ja="単一ウィンドウ"; ar="نافذة واحدة"; ko="단일 창" }
        let displays = { en="Displays and scaling"; zh="显示器与缩放"; ja="ディスプレイと拡大率"; ar="الشاشات والتحجيم"; ko="디스플레이 및 배율" }
        let display i = { en=sprintf "Display %d" i; zh=sprintf "显示器 %d" i; ja=sprintf "画面 %d" i; ar=sprintf "الشاشة %d" i; ko=sprintf "디스플레이 %d" i }
        let primary = { en="Primary"; zh="主显示器"; ja="メイン"; ar="الرئيسية"; ko="주 디스플레이" }
        let unknown = { en="Unknown"; zh="未知"; ja="不明"; ar="غير معروف"; ko="알 수 없음" }
        let environmentTitle = { en="Runtime environment"; zh="运行环境"; ja="実行環境"; ar="بيئة التشغيل"; ko="실행 환경" }
        let recentCrashes = { en="Recent crashes"; zh="最近崩溃"; ja="最近のクラッシュ"; ar="الأعطال الأخيرة"; ko="최근 충돌" }
        let description = { en="Safe to share: no window titles, file paths or other personal information."
                            zh="可放心分享：不含窗口标题、文件路径等任何个人信息。"
                            ja="安心して共有できます：ウィンドウのタイトルやファイルのパスなどの個人情報は含まれません。"
                            ar="آمن للمشاركة: لا يحتوي على عناوين النوافذ أو مسارات الملفات أو معلومات شخصية أخرى."
                            ko="안심하고 공유할 수 있습니다. 창 제목, 파일 경로 등 개인 정보는 포함되지 않습니다." }
        let copyReport = { en="Copy report as JSON"; zh="复制 JSON 报告"; ja="レポートを JSON としてコピー"; ar="نسخ التقرير بصيغة JSON"; ko="보고서를 JSON으로 복사" }
        let includeWindowsHelp = { en="Include window details: each window's program, class and whether it gets tabs. Never titles."
                                   zh="包含窗口详情：每个窗口的程序、窗口类，以及是否会加上标签。不含标题。"
                                   ja="ウィンドウの詳細を含める：各ウィンドウのプログラム、クラス、タブが付くかどうか。タイトルは含みません。"
                                   ar="تضمين تفاصيل النوافذ: برنامج كل نافذة وفئتها وما إذا كانت تستخدم علامات تبويب. لا يتضمن العناوين أبدًا."
                                   ko="창 세부 정보 포함: 각 창의 프로그램, 클래스, 탭 사용 여부. 제목은 포함하지 않습니다." }
        let refreshReport = { en="Refresh the report"; zh="刷新报告"; ja="レポートを更新"; ar="تحديث التقرير"; ko="보고서 새로 고침" }
        let reportCopied = { en="Report copied"; zh="报告已复制"; ja="レポートをコピーしました"; ar="تم نسخ التقرير"; ko="보고서를 복사했습니다" }
        let reportRefreshed = { en="Report refreshed"; zh="报告已刷新"; ja="レポートを更新しました"; ar="تم تحديث التقرير"; ko="보고서를 새로 고쳤습니다" }
        let reportTitle = { en="Troubleshooting report"; zh="故障排查报告"; ja="トラブルシューティング レポート"; ar="تقرير استكشاف الأخطاء وإصلاحها"; ko="문제 해결 보고서" }
        let projectPage = { en="Project page"; zh="项目主页"; ja="プロジェクトページ"; ar="صفحة المشروع"; ko="프로젝트 페이지" }
        let reportIssue = { en="Report an issue"; zh="报告问题"; ja="問題を報告"; ar="الإبلاغ عن مشكلة"; ko="문제 보고" }
        let releases = { en="Check Releases"; zh="查看发布版本"; ja="リリースを確認"; ar="التحقق من الإصدارات"; ko="릴리스 확인" }
        let releasesHint = { en="Check Releases ↗"; zh="查看发布版本 ↗"; ja="リリースを確認 ↗"; ar="التحقق من الإصدارات ↗"; ko="릴리스 확인 ↗" }
        let openCrashLog = { en="Crash log"; zh="崩溃日志"; ja="クラッシュログ"; ar="سجل الأعطال"; ko="충돌 로그" }
        let resourcesTitle = { en="Resources"; zh="资源占用"; ja="リソース"; ar="الموارد"; ko="리소스" }
        let resourcesHelp = { en="See how much CPU and memory WindowTabs is using."; zh="查看 WindowTabs 当前占用的 CPU 和内存。"; ja="WindowTabs の現在の CPU とメモリの使用量を確認できます。"; ar="اطّلع على استخدام WindowTabs الحالي للمعالج والذاكرة."; ko="WindowTabs가 사용하는 CPU와 메모리를 확인합니다." }
        let chartsHelp = { en="Turn on recording to see how usage changes over time."; zh="开启记录，查看资源占用随时间的变化。"; ja="記録をオンにすると、使用量の推移を確認できます。"; ar="فعّل التسجيل لمتابعة تغيّر الاستخدام بمرور الوقت."; ko="기록을 켜면 시간에 따른 사용량 변화를 볼 수 있습니다." }
        /// When the charts start; they always run to now.
        let chartsSince (time:string) = { en=sprintf "Since %s" time; zh=sprintf "自 %s 起" time; ja=sprintf "%s から" time; ar=sprintf "منذ %s" time; ko=sprintf "%s부터" time }
        let charts = { en="Charts"; zh="详细图表"; ja="詳細グラフ"; ar="المخططات"; ko="차트" }
        let chartsCpuMemory = { en="CPU and memory"; zh="CPU 和内存"; ja="CPU とメモリ"; ar="المعالج والذاكرة"; ko="CPU 및 메모리" }
        let chartsSystemObjects = { en="System objects"; zh="系统对象"; ja="システム オブジェクト"; ar="كائنات النظام"; ko="시스템 개체" }
        let chartsWindowEvents = { en="Window events"; zh="窗口事件"; ja="ウィンドウ イベント"; ar="أحداث النوافذ"; ko="창 이벤트" }
        let chartsGroupsTabs = { en="Groups and tabs"; zh="组和标签页"; ja="グループとタブ"; ar="المجموعات وعلامات التبويب"; ko="그룹 및 탭" }
        /// What each figure is, shown while it is pointed at.
        let pointForMeaning = { en="Point at an item to see what it is."; zh="指向一项可查看它的含义。"; ja="項目をポイントすると意味を表示します。"; ar="أشر إلى عنصر لمعرفة ما هو."; ko="항목을 가리키면 설명이 표시됩니다." }
        let privateMemoryMeaning = { en="Memory WindowTabs alone uses, in RAM or paged out. A leak shows here."
                                     zh="只属于 WindowTabs 的内存，含已换出的部分。内存泄漏会体现在这里。"
                                     ja="WindowTabs だけが使うメモリ（ページアウト分も含む）。リークはここに表れます。"
                                     ar="الذاكرة التي يستخدمها WindowTabs وحده، سواء في الذاكرة الفعلية أو في ملف الترحيل. يظهر التسرب هنا."
                                     ko="WindowTabs만 쓰는 메모리(페이지 아웃 포함). 누수는 여기에 보입니다." }
        let dotNetObjectsMeaning = { en="Objects of WindowTabs' own code in use, freed by .NET's garbage collector."
                                     zh="WindowTabs 自身代码正在使用的对象，由 .NET 垃圾回收释放。"
                                     ja="WindowTabs 自身のコードが使用中のオブジェクト。.NET のガベージ コレクターが解放します。"
                                     ar="كائنات شيفرة WindowTabs نفسها قيد الاستخدام، يحررها جامع البيانات المهملة في .NET."
                                     ko="사용 중인 WindowTabs 자체 코드의 개체로, .NET 가비지 수집기가 해제합니다." }
        let heapFreeMeaning = { en="Room the .NET heap keeps for new objects, so it need not ask Windows each time."
                                zh=".NET 堆为新对象预留的空间，这样不必每次都向 Windows 申请。"
                                ja=".NET ヒープが新しいオブジェクト用に確保している領域。毎回 Windows に要求せずに済みます。"
                                ar="مساحة تحتفظ بها كومة .NET للكائنات الجديدة، كي لا تطلبها من Windows في كل مرة."
                                ko="새 개체를 위해 .NET 힙이 확보해 둔 공간으로, 매번 Windows에 요청하지 않아도 됩니다." }
        let tabPicturesMeaning = { en="The last picture of each covered tab, for its previews; kept on the .NET heap."
                                   zh="每个被遮住的标签页的最后一张截图，用于预览；存放在 .NET 堆中。"
                                   ja="隠れている各タブの最後の画像。プレビューに使い、.NET ヒープに保持します。"
                                   ar="آخر صورة لكل علامة تبويب مغطاة، لمعايناتها؛ محفوظة في كومة .NET."
                                   ko="가려진 각 탭의 마지막 이미지로, 미리 보기에 사용되며 .NET 힙에 보관됩니다." }
        let compiledCodeMeaning = { en="WindowTabs' code as .NET compiled it for this processor while it ran."
                                    zh=".NET 在运行时为这台处理器编译出的 WindowTabs 代码。"
                                    ja=".NET が実行中にこのプロセッサ向けにコンパイルした WindowTabs のコード。"
                                    ar="شيفرة WindowTabs كما ترجمها .NET لهذا المعالج أثناء التشغيل."
                                    ko="실행 중에 .NET이 이 프로세서용으로 컴파일한 WindowTabs 코드입니다." }
        let libraryDataMeaning = { en="Writable data of the Windows and .NET libraries WindowTabs loaded."
                                   zh="WindowTabs 加载的 Windows 和 .NET 库的可写数据。"
                                   ja="WindowTabs が読み込んだ Windows と .NET のライブラリの書き込み可能なデータ。"
                                   ar="البيانات القابلة للكتابة لمكتبات Windows و.NET التي حمّلها WindowTabs."
                                   ko="WindowTabs가 로드한 Windows 및 .NET 라이브러리의 쓰기 가능한 데이터입니다." }
        let threadStacksMeaning = { en="Each thread's working memory: a group's thread has one, as has each of the app's."
                                    zh="每个线程的工作内存：每个组的线程一份，程序自己的线程也各一份。"
                                    ja="各スレッドの作業用メモリ。グループごとのスレッドとアプリのスレッドに 1 つずつ。"
                                    ar="ذاكرة عمل كل سلسلة تنفيذ: لكل سلسلة مجموعة واحدة، ولكل سلسلة من سلاسل التطبيق واحدة."
                                    ko="각 스레드의 작업 메모리입니다. 그룹의 스레드마다 하나씩, 앱의 스레드에도 하나씩 있습니다." }
        let otherMemoryMeaning = { en=".NET's type data and memory Windows libraries keep, such as drawing and shell parts."
                                   zh=".NET 类型数据，以及 Windows 各库自留的内存，如绘图和外壳组件。"
                                   ja=".NET の型データと、描画やシェルなど Windows のライブラリが確保するメモリ。"
                                   ar="بيانات الأنواع في .NET وذاكرة مكتبات Windows، مثل مكونات الرسم والواجهة."
                                   ko="그리기와 셸 구성 요소 등 Windows 라이브러리가 확보한 메모리와 .NET 형식 데이터입니다." }
        let cpuMeaning = { en="Share of all processors over the last 5 seconds, as Task Manager shows it."
                           zh="最近 5 秒占全部处理器的比例，与任务管理器的算法相同。"
                           ja="直近 5 秒間の全プロセッサに対する割合。タスク マネージャーと同じ計算です。"
                           ar="الحصة من جميع المعالجات خلال آخر 5 ثوانٍ، كما يعرضها مدير المهام."
                           ko="최근 5초 동안 전체 프로세서 중 사용한 비율로, 작업 관리자와 같은 방식입니다." }
        let physicalMemoryMeaning = { en="Private memory now in RAM, as in Task Manager's Memory column; falls when idle."
                                      zh="专用内存中当前在物理内存里的部分，同任务管理器“内存”列；闲置时会变小。"
                                      ja="物理メモリ上のプライベート メモリ。タスク マネージャーの「メモリ」列と同じで、アイドル時に減ります。"
                                      ar="الذاكرة الخاصة الموجودة في الذاكرة الفعلية الآن، كعمود «الذاكرة» في مدير المهام؛ تنخفض عند الخمول."
                                      ko="현재 RAM에 있는 개인 메모리로, 작업 관리자의 메모리 열과 같습니다. 유휴 시 줄어듭니다." }
        let threadsMeaning = { en="Each group runs on a thread of its own, and the app on a few more."
                               zh="每个组在自己的线程上运行，程序本身另有几个线程。"
                               ja="各グループは専用のスレッドで動き、アプリ自体にもいくつかあります。"
                               ar="تعمل كل مجموعة على سلسلة تنفيذ خاصة بها، وللتطبيق بضع سلاسل أخرى."
                               ko="각 그룹은 자체 스레드에서 실행되며, 앱에도 스레드가 몇 개 더 있습니다." }
        let handlesMeaning = { en="Open system objects, such as events, files and processes."
                               zh="打开的系统对象，例如事件、文件和进程。"
                               ja="開いているシステム オブジェクト（イベント、ファイル、プロセスなど）。"
                               ar="كائنات النظام المفتوحة، مثل الأحداث والملفات والعمليات."
                               ko="이벤트, 파일, 프로세스 등 열려 있는 시스템 개체입니다." }
        let gdiObjectsMeaning = { en="Drawing resources such as bitmaps, fonts and brushes. Windows allows 10,000 per app."
                                  zh="位图、字体、画刷等绘图资源。Windows 允许每个程序最多 10,000 个。"
                                  ja="ビットマップ、フォント、ブラシなどの描画リソース。上限は 1 アプリ 10,000 個。"
                                  ar="موارد الرسم مثل الصور النقطية والخطوط والفرش. يسمح Windows بـ 10,000 لكل تطبيق."
                                  ko="비트맵, 글꼴, 브러시 등 그리기 리소스입니다. Windows는 앱당 10,000개까지 허용합니다." }
        let userObjectsMeaning = { en="Windows, menus and icons. Windows allows 10,000 per app."
                                   zh="窗口、菜单和图标。Windows 允许每个程序最多 10,000 个。"
                                   ja="ウィンドウ、メニュー、アイコン。上限は 1 アプリ 10,000 個。"
                                   ar="النوافذ والقوائم والأيقونات. يسمح Windows بـ 10,000 لكل تطبيق."
                                   ko="창, 메뉴, 아이콘입니다. Windows는 앱당 10,000개까지 허용합니다." }
        let eventHooksMeaning = { en="What WindowTabs listens to in other apps' windows, about ten for each tab."
                                  zh="WindowTabs 在其他程序窗口上监听的事件，每个标签页约 10 个。"
                                  ja="WindowTabs がほかのアプリのウィンドウで受け取るイベント。タブ 1 つに約 10 個。"
                                  ar="ما يستمع إليه WindowTabs في نوافذ التطبيقات الأخرى، نحو عشرة لكل علامة تبويب."
                                  ko="WindowTabs가 다른 앱의 창에서 수신하는 이벤트로, 탭당 약 10개입니다." }
        let desktopHooksMeaning = { en="Those hearing every app, for rare events such as switching windows: three per group."
                                    zh="监听所有程序的那部分，用于切换窗口这类少见的事件：每个组 3 个。"
                                    ja="全アプリから受け取るもの。ウィンドウ切り替えなどまれなイベント用で、グループごとに 3 個。"
                                    ar="تلك التي تستمع إلى كل التطبيقات، لأحداث نادرة كتبديل النوافذ: ثلاثة لكل مجموعة."
                                    ko="모든 앱에서 수신하는 후크로, 창 전환 같은 드문 이벤트용입니다. 그룹당 3개입니다." }
        let groupsMeaning = { en="Windows with a tab strip, each window alone in one counted too."
                              zh="带标签栏的窗口组，单独一个窗口的组也算。"
                              ja="タブ バーの付いたグループ。ウィンドウ 1 つだけのグループも数えます。"
                              ar="المجموعات ذات شريط علامات التبويب، وتُحسب النافذة المنفردة في مجموعة أيضًا."
                              ko="탭 표시줄이 있는 창 그룹으로, 창이 하나뿐인 그룹도 포함합니다." }
        let tabsMeaning = { en="Windows in all the groups."; zh="所有组里的窗口数。"; ja="すべてのグループにあるウィンドウの数。"; ar="النوافذ في كل المجموعات."; ko="모든 그룹에 있는 창 수입니다." }
        let dotNetObjects = { en=".NET objects"; zh=".NET 对象"; ja=".NET オブジェクト"; ar="كائنات .NET"; ko=".NET 개체" }
        let heapFree = { en=".NET heap free space"; zh=".NET 堆空余"; ja=".NET ヒープの空き"; ar="المساحة الحرة في كومة .NET"; ko=".NET 힙 여유 공간" }
        let compiledCode = { en="Compiled code"; zh="编译后的代码"; ja="コンパイル済みコード"; ar="الشيفرة المترجمة"; ko="컴파일된 코드" }
        let libraryData = { en="Library data"; zh="系统库数据"; ja="ライブラリのデータ"; ar="بيانات المكتبات"; ko="라이브러리 데이터" }
        let threadStacks = { en="Thread stacks"; zh="线程栈"; ja="スレッドのスタック"; ar="مكدسات سلاسل التنفيذ"; ko="스레드 스택" }
        let otherMemory = { en="Other"; zh="其他"; ja="その他"; ar="أخرى"; ko="기타" }
        let collecting = { en="Collecting…"; zh="正在收集…"; ja="収集中…"; ar="جارٍ الجمع…"; ko="수집 중…" }
        let cpu = { en="CPU"; zh="CPU"; ja="CPU"; ar="المعالج"; ko="CPU" }
        let privateMemory = { en="Private memory"; zh="专用内存"; ja="プライベート メモリ"; ar="الذاكرة الخاصة"; ko="개인 메모리" }
        let physicalMemory = { en="Physical memory"; zh="物理内存"; ja="物理メモリ"; ar="الذاكرة الفعلية"; ko="실제 메모리" }
        let managedHeap = { en=".NET heap"; zh=".NET 堆"; ja=".NET ヒープ"; ar="كومة .NET"; ko=".NET 힙" }
        let tabPictures = { en="Tab pictures"; zh="标签页截图"; ja="タブの画像"; ar="صور علامات التبويب"; ko="탭 이미지" }
        let threads = { en="Threads"; zh="线程"; ja="スレッド"; ar="سلاسل التنفيذ"; ko="스레드" }
        let handles = { en="Handles"; zh="句柄"; ja="ハンドル"; ar="المقابض"; ko="핸들" }
        let gdiObjects = { en="GDI objects"; zh="GDI 对象"; ja="GDI オブジェクト"; ar="كائنات GDI"; ko="GDI 개체" }
        let userObjects = { en="USER objects"; zh="USER 对象"; ja="USER オブジェクト"; ar="كائنات USER"; ko="USER 개체" }
        let eventHooks = { en="Event hooks"; zh="事件钩子"; ja="イベント フック"; ar="خطافات الأحداث"; ko="이벤트 후크" }
        let desktopHooks = { en="Desktop-wide hooks"; zh="全桌面钩子"; ja="デスクトップ全体のフック"; ar="خطافات على مستوى سطح المكتب"; ko="데스크톱 전체 후크" }
        let groups = { en="Groups"; zh="组"; ja="グループ"; ar="المجموعات"; ko="그룹" }
        let tabs = { en="Tabs"; zh="标签页"; ja="タブ"; ar="علامات التبويب"; ko="탭" }
