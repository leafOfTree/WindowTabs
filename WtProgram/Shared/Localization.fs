namespace Bemo
open System.Globalization

/// One piece of user-visible text in every supported language. Every field is required, so a
/// missing translation is a compile error. All instances live in Strings.
type LocalizedText = { en:string; zh:string; ja:string }

/// All user-visible text is compiled into the single WindowTabs.exe. (Satellite resource
/// assemblies cannot be statically linked, so a .resx translation would not ship.)
module Localization =
    /// Supported languages: code, name shown in the language menu, compact label.
    /// Arabic translations keyed by the source English text.
    /// Untranslated dynamic messages fall back to English rather than hiding content.
    let private arabicTranslations =
        dict [
            ("", "");
            ("OK", "موافق");
            ("Cancel", "إلغاء");
            ("Save", "حفظ");
            ("Refresh", "تحديث");
            ("Name", "الاسم");
            ("Title", "العنوان");
            ("Left", "يسار");
            ("Top", "أعلى");
            ("Bottom", "أسفل");
            ("Center", "توسيط");
            ("Right", "يمين");
            ("Never", "أبدًا");
            ("When maximized", "عند التكبير");
            ("Always", "دائمًا");
            ("Operation failed", "فشلت العملية");
            ("WindowTabs is already running.", "برنامج WindowTabs قيد التشغيل بالفعل.");
            ("WindowTabs could not continue", "تعذّر على WindowTabs المتابعة");
            ("Settings save failed", "فشل حفظ الإعدادات");
            ("Settings error", "خطأ في الإعدادات");
            ("Exit WindowTabs", "إنهاء WindowTabs");
            ("WindowTabs ran into a problem", "واجه WindowTabs مشكلة");
            ("It kept running. The details are in the crash log; click to show it.", "استمر البرنامج في العمل. التفاصيل موجودة في سجل الأعطال؛ انقر لعرضه.");
            ("Tab position", "موضع علامات التبويب");
            ("Show tab titles", "إظهار عناوين علامات التبويب");
            ("Show icons only", "إظهار الأيقونات فقط");
            ("New tab", "علامة تبويب جديدة");
            ("Rename", "إعادة تسمية");
            ("Reset name", "إعادة تعيين الاسم");
            ("Sort by app and title", "ترتيب حسب التطبيق والعنوان");
            ("Combine taskbar icons", "دمج أيقونات شريط المهام");
            ("Change taskbar icon…", "تغيير أيقونة شريط المهام…");
            ("Restore taskbar icon", "استعادة أيقونة شريط المهام");
            ("Switch by number", "التبديل باستخدام الرقم");
            ("Enable tabs", "تمكين علامات التبويب");
            ("Auto-group windows", "تجميع النوافذ تلقائيًا");
            ("Close", "إغلاق");
            ("Close others", "إغلاق علامات التبويب الأخرى");
            ("Close all", "إغلاق الكل");
            ("Settings...", "الإعدادات...");
            ("Copy", "نسخ");
            ("Move", "نقل");
            ("Move to %1", "نقل إلى %1");
            ("Copy to %1", "نسخ إلى %1");
            ("WindowTabs Settings", "إعدادات WindowTabs");
            ("Search", "بحث");
            ("Search settings", "البحث في الإعدادات");
            ("Search suggestions", "اقتراحات البحث");
            ("Sidebar", "الشريط الجانبي");
            ("Search results", "نتائج البحث");
            ("No matching settings.", "لا توجد إعدادات مطابقة.");
            ("Follow Windows", "اتباع إعدادات Windows");
            ("How to use", "طريقة الاستخدام");
            ("Page scroll", "تمرير الصفحة");
            ("General", "عام");
            ("Appearance", "المظهر");
            ("Shortcuts", "اختصارات");
            ("App rules", "قواعد التطبيقات");
            ("Workspaces", "مساحات العمل");
            ("Support", "الدعم");
            ("color colour", "اللون الألوان");
            ("size", "الحجم");
            ("hotkey", "مفتاح الاختصار");
            ("Theme", "السمة");
            ("System follows your Windows app mode.", "يتبع خيار النظام وضع التطبيقات في Windows.");
            ("dark mode", "الوضع الداكن");
            ("Language", "اللغة");
            ("Follow Windows uses your Windows display language.", "يستخدم خيار اتباع Windows لغة العرض المحددة في Windows.");
            ("locale", "الإعدادات المحلية");
            ("Start with Windows", "التشغيل مع Windows");
            ("Runs in the system tray after you sign in.", "يعمل في منطقة الإعلام بعد تسجيل الدخول.");
            ("startup autostart", "بدء التشغيل التشغيل التلقائي");
            ("Use tabs for new apps", "استخدام علامات التبويب للتطبيقات الجديدة");
            ("Only apps first discovered after this change. Existing apps keep their tab setting.", "ينطبق ذلك على التطبيقات التي يكتشفها البرنامج لأول مرة بعد هذا التغيير فقط. وتحتفظ التطبيقات الموجودة بإعداد علامات التبويب الحالي.");
            ("Dim tabs in inactive groups", "تخفيف لون علامات التبويب في المجموعات غير النشطة");
            ("Make the active group easier to spot.", "لتسهيل تمييز المجموعة النشطة.");
            ("fade", "تلاشي");
            ("Auto-hide tabs", "إخفاء علامات التبويب تلقائيًا");
            ("Show a thin strip until hover.", "إظهار شريط رفيع حتى تمرير المؤشر فوقه.");
            ("collapse", "طيّ");
            ("Show tabs briefly after switching", "إظهار علامات التبويب لفترة وجيزة بعد التبديل");
            ("For about a second, so you can see where you are.", "لمدة ثانية تقريبًا لتتمكن من معرفة موضعك.");
            ("peek", "معاينة");
            ("Position tabs", "موضع علامات التبويب");
            ("vertical horizontal sidebar left right", "رأسي أفقي شريط جانبي يسار يمين");
            ("For left/right tabs. Drag a column edge to resize one group, down to an icon rail.", "للعلامات الجانبية اليمنى أو اليسرى. اسحب حافة العمود لتغيير عرض مجموعة واحدة، حتى يصبح شريطًا للأيقونات فقط.");
            ("Vertical tab width", "عرض علامات التبويب الرأسية");
            ("vertical sidebar width size", "رأسي شريط جانبي عرض حجم");
            ("Align tabs", "محاذاة علامات التبويب");
            ("alignment position", "محاذاة موضع");
            ("Combine taskbar icons per group", "دمج أيقونات شريط المهام لكل مجموعة");
            ("merge", "دمج");
            ("Use WindowTabs for Alt+Tab", "استخدام WindowTabs عند الضغط على Alt+Tab");
            ("Replaces the Windows switcher.", "يستبدل أداة التبديل بين النوافذ في Windows.");
            ("task switcher", "مبدّل المهام");
            ("Switcher style", "نمط أداة التبديل");
            ("Large icons in a row, or a list with full window titles.", "أيقونات كبيرة في صف، أو قائمة تعرض عناوين النوافذ كاملة.");
            ("layout", "التخطيط");
            ("Group windows in Alt+Tab", "تجميع النوافذ في Alt+Tab");
            ("Each group appears as one item.", "تظهر كل مجموعة كعنصر واحد.");
            ("switcher", "أداة التبديل");
            ("Switch to next tab", "الانتقال إلى علامة التبويب التالية");
            ("Switch to previous tab", "الانتقال إلى علامة التبويب السابقة");
            ("Switch windows", "تبديل النوافذ");
            ("Switch to a window by title or app name.", "الانتقال إلى نافذة باستخدام عنوانها أو اسم التطبيق.");
            ("find", "بحث");
            ("Starts the current tab's app again.", "يشغّل التطبيق الخاص بعلامة التبويب الحالية مرة أخرى.");
            ("new window", "نافذة جديدة");
            ("Close tab", "إغلاق علامة التبويب");
            ("Closes the window of the tab in front.", "يغلق النافذة الخاصة بعلامة التبويب الحالية.");
            ("close window", "إغلاق النافذة");
            ("Switch tabs by number", "التبديل بين علامات التبويب باستخدام الأرقام");
            ("Hold a modifier key and press 1–9.", "اضغط باستمرار على مفتاح تعديل ثم اضغط على رقم من 1 إلى 9.");
            ("digit", "رقم");
            ("Modifier key for 1–9", "مفتاح التعديل للأرقام من 1 إلى 9");
            ("number", "رقم");
            ("Switch tabs with a leader key", "التبديل باستخدام مفتاح تمهيدي");
            ("Press the leader, then a selection key. Esc cancels.", "اضغط المفتاح التمهيدي ثم مفتاح الاختيار. يلغي Esc العملية.");
            ("Selection keys", "مفاتيح الاختيار");
            ("Use unique letters or digits, in tab order.", "استخدم أحرفًا أو أرقامًا غير مكررة حسب ترتيب علامات التبويب.");
            ("leader keys letters home row", "مفتاح تمهيدي أحرف الصف الأوسط");
            ("Use at least one unique letter, digit or unshifted punctuation key.", "استخدم حرفًا أو رقمًا أو مفتاح ترقيم غير مكرر واحدًا على الأقل دون Shift.");
            ("Tab selection leader", "المفتاح التمهيدي لاختيار علامة التبويب");
            ("Color", "اللون");
            ("Custom…", "مخصص…");
            ("Clear color", "مسح اللون");
            ("Remember color for current app", "تذكّر لون التطبيق الحالي");
            ("Grey", "رمادي");
            ("Blue", "أزرق");
            ("Red", "أحمر");
            ("Yellow", "أصفر");
            ("Green", "أخضر");
            ("Pink", "وردي");
            ("Purple", "أرجواني");
            ("Cyan", "سماوي");
            ("Orange", "برتقالي");
            ("Lavender", "لافندر");
            ("Olive", "زيتي");
            ("Rose", "وردي داكن");
            ("Lime", "ليموني");
            ("Navy", "كحلي");
            ("Teal", "أزرق مخضر");
            ("Coral", "مرجاني");
            ("Tab color coding", "ترميز علامات التبويب بالألوان");
            ("Auto-assign colors by window or app.", "تعيين الألوان تلقائيًا حسب النافذة أو التطبيق.");
            ("colorize rainbow", "تلوين قوس قزح");
            ("Off", "إيقاف");
            ("By window", "حسب النافذة");
            ("By app", "حسب التطبيق");
            ("Change it for one tab from the tab menu. That color comes first, and shows even when this is off.", "غيّر لون علامة تبويب واحدة من قائمتها. تكون الأولوية لهذا اللون ويظهر حتى عند إيقاف هذا الخيار.");
            ("Show colors as", "عرض الألوان على شكل");
            ("stripe fill", "شريط تعبئة");
            ("Stripe", "شريط");
            ("Fill", "تعبئة");
            ("Enable or disable it for one app from the tab menu.", "يمكن تمكينه أو تعطيله لتطبيق واحد من قائمة علامة التبويب.");
            ("Ctrl", "Ctrl");
            ("Alt", "Alt");
            ("Switch tabs on hover", "التبديل بين علامات التبويب عند تمرير المؤشر");
            ("Rest the pointer on a tab instead of clicking.", "ضع المؤشر فوق علامة تبويب بدلًا من النقر عليها.");
            ("mouse over", "تمرير المؤشر");
            ("Switch tabs with Shift + scroll", "التبديل بين علامات التبويب باستخدام Shift والتمرير");
            ("Works anywhere over a grouped window. Over the tab bar, Shift isn't needed.", "يعمل في أي مكان فوق نافذة مجمّعة. لا تحتاج إلى Shift فوق شريط علامات التبويب.");
            ("wheel", "عجلة التمرير");
            ("Move tabs with Ctrl + scroll", "تحريك علامات التبويب باستخدام Ctrl والتمرير");
            ("Over the tab bar, moves the current tab left or right.", "فوق شريط علامات التبويب، ينقل علامة التبويب الحالية إلى اليسار أو اليمين.");
            ("wheel reorder", "عجلة التمرير لإعادة الترتيب");
            ("Middle-click closes a tab", "النقر الأوسط يغلق علامة تبويب");
            ("Press the wheel on a tab to close its window.", "اضغط بعجلة الفأرة على علامة التبويب لإغلاق نافذتها.");
            ("wheel click", "النقر بعجلة الفأرة");
            ("Text and close button", "النص وزر الإغلاق");
            ("Inactive tab", "علامة تبويب غير نشطة");
            ("Active tab", "علامة تبويب نشطة");
            ("Hovered tab", "علامة التبويب التي يشير إليها المؤشر");
            ("Tab style", "نمط علامة التبويب");
            ("How the active tab stands out from the rest.", "كيفية تمييز علامة التبويب النشطة عن البقية.");
            ("shape folder pill", "الشكل مجلد كبسولة");
            ("Left/right tabs have a minimum height of 32 px.", "يبلغ الحد الأدنى لارتفاع علامات التبويب الجانبية 32 بكسل.");
            ("Tab height", "ارتفاع علامة التبويب");
            ("Maximum tab width", "أقصى عرض لعلامة التبويب");
            ("Tab spacing", "المسافة بين علامات التبويب");
            ("gap", "فجوة");
            ("Side margin", "الهامش الجانبي");
            ("Distance from the window edges.", "المسافة من حواف النافذة.");
            ("indent padding", "مسافة بادئة حشو");
            ("Choose apps for tabs and automatic grouping.", "اختر التطبيقات التي تستخدم علامات التبويب والتجميع التلقائي.");
            ("process program", "عملية برنامج");
            ("Save and restore window layouts.", "حفظ تخطيطات النوافذ واستعادتها.");
            ("session", "جلسة");
            ("Troubleshooting report and where to get help.", "تقرير استكشاف الأخطاء وإصلاحها ومكان الحصول على المساعدة.");
            ("about version log", "حول الإصدار السجل");
            ("Location", "الموقع");
            ("folder path portable", "مجلد مسار محمول");
            ("Back up and restore", "النسخ الاحتياطي والاستعادة");
            ("Export all settings to a file, or import them and restart.", "تصدير جميع الإعدادات إلى ملف، أو استيرادها ثم إعادة التشغيل.");
            ("export import", "تصدير استيراد");
            ("Reset to defaults", "إعادة التعيين إلى الإعدادات الافتراضية");
            ("Then restarts. App rules and workspaces can be kept.", "ثم يعاد التشغيل. يمكن الاحتفاظ بقواعد التطبيقات ومساحات العمل.");
            ("factory", "إعدادات المصنع");
            ("Settings file", "ملف الإعدادات");
            ("Open folder", "فتح المجلد");
            ("Export…", "تصدير…");
            ("Import…", "استيراد…");
            ("Settings exported.", "تم تصدير الإعدادات.");
            ("Reset…", "إعادة تعيين…");
            ("Reset", "إعادة تعيين");
            ("Reset settings", "إعادة تعيين الإعدادات");
            ("Also clear:", "مسح أيضًا:");
            ("Import settings", "استيراد الإعدادات");
            ("This file does not contain WindowTabs settings.", "لا يحتوي هذا الملف على إعدادات WindowTabs.");
            ("Startup", "بدء التشغيل");
            ("Tab behavior", "سلوك علامات التبويب");
            ("Taskbar", "شريط المهام");
            ("Window switcher", "مبدّل النوافذ");
            ("System", "النظام");
            ("Light", "فاتح");
            ("Dark", "داكن");
            ("File Explorer theme preview", "معاينة سمة مستكشف الملفات");
            ("Tab colors", "ألوان علامات التبويب");
            ("Color preset", "مجموعة ألوان جاهزة");
            ("Custom", "مخصص");
            ("Horizontal", "أفقي");
            ("Vertical", "رأسي");
            ("Joined", "متصل");
            ("Folder", "مجلد");
            ("Floating", "عائم");
            ("Before", "قبل");
            ("After", "بعد");
            ("Set automatically while Tab color coding is By window or By app with Fill.", "يُضبط تلقائيًا عندما يكون ترميز الألوان حسب النافذة أو التطبيق مع خيار التعبئة.");
            ("Text color is adjusted on some tabs to stay readable.", "يُعدّل لون النص في بعض علامات التبويب ليظل مقروءًا.");
            ("Reset colors", "إعادة تعيين الألوان");
            ("Tab layout", "تخطيط علامات التبويب");
            ("Reset tab layout", "إعادة تعيين تخطيط علامات التبويب");
            ("Default", "افتراضي");
            ("Slate", "رمادي أردوازي");
            ("Sand", "رملي");
            ("Amber", "كهرماني");
            ("Black", "أسود");
            ("White", "أبيض");
            ("Gray", "رمادي");
            ("Color picker: arrow keys adjust saturation and brightness; Shift + arrow keys adjust hue", "منتقي الألوان: تعدّل مفاتيح الأسهم التشبع والسطوع؛ وتعدّل الأسهم مع Shift درجة اللون");
            ("Choose color", "اختيار اللون");
            ("Decrease", "تقليل");
            ("Increase", "زيادة");
            ("Keyboard", "لوحة المفاتيح");
            ("Mouse", "الفأرة");
            ("Click a shortcut, then press the new combination. Esc cancels.", "انقر فوق اختصار ثم اضغط مجموعة المفاتيح الجديدة. يلغي Esc العملية.");
            ("Restore default shortcuts", "استعادة الاختصارات الافتراضية");
            ("Shortcut already in use", "الاختصار مستخدم بالفعل");
            ("Already set", "تم تعيينه بالفعل");
            ("Include Ctrl or Alt", "يجب أن يتضمن Ctrl أو Alt");
            ("Press a shortcut…", "اضغط اختصارًا…");
            ("Not set", "غير معيّن");
            ("Type a title or app name to switch windows", "اكتب عنوانًا أو اسم تطبيق للتبديل بين النوافذ");
            ("No matching windows.", "لا توجد نوافذ مطابقة.");
            ("Another desktop", "سطح مكتب آخر");
            ("Add application", "إضافة تطبيق");
            ("Delete", "حذف");
            ("This application already has a grouping rule.", "لدى هذا التطبيق قاعدة تجميع بالفعل.");
            ("Groups across apps", "مجموعات تضم تطبيقات مختلفة");
            ("Applications", "التطبيقات");
            ("Add group", "إضافة مجموعة");
            ("Select a group or rule to edit here. Changes save automatically.", "حدد مجموعة أو قاعدة لتحريرها هنا. تُحفظ التغييرات تلقائيًا.");
            ("New rule", "قاعدة جديدة");
            ("Enter a group name.", "أدخل اسم المجموعة.");
            ("A group with this name already exists.", "توجد مجموعة بهذا الاسم بالفعل.");
            ("Application", "التطبيق");
            ("Add an app", "إضافة تطبيق");
            ("Ungroup", "إلغاء التجميع");
            ("Remove from group", "إزالة من المجموعة");
            ("Browse…", "استعراض…");
            ("Applications (*.exe)|*.exe", "التطبيقات (*.exe)|*.exe");
            ("Application only", "التطبيق فقط");
            ("Target group", "المجموعة المستهدفة");
            ("Existing group", "مجموعة موجودة");
            ("Enabled", "ممكّن");
            ("Add rule", "إضافة قاعدة");
            ("Invalid grouping rule", "قاعدة التجميع غير صالحة");
            ("Choose an application and enter a target group.", "اختر تطبيقًا وأدخل المجموعة المستهدفة.");
            ("Enter a title pattern, or choose Application only.", "أدخل نمطًا للعنوان أو اختر «التطبيق فقط».");
            ("Tabs", "علامات التبويب");
            ("Auto-group", "التجميع التلقائي");
            ("Windows apps", "تطبيقات Windows");
            ("Windows grouped by dragging", "النوافذ المجمّعة بالسحب");
            ("Scanning…", "جارٍ الفحص…");
            ("Scan failed: ", "فشل الفحص: ");
            ("Save and restore window groups and positions.", "حفظ مجموعات النوافذ ومواضعها واستعادتها.");
            ("Save layout", "حفظ التخطيط");
            ("Restore selected", "استعادة المحدد");
            ("Match method", "طريقة المطابقة");
            ("Exact match", "مطابقة تامة");
            ("Starts with", "يبدأ بـ");
            ("Ends with", "ينتهي بـ");
            ("Contains", "يحتوي على");
            ("Regular expression", "تعبير نمطي");
            ("Workspace restore", "استعادة مساحة العمل");
            ("Restored", "تمت الاستعادة");
            ("Not found", "لم يتم العثور عليه");
            ("Failed", "فشل");
            ("These windows are closed, or their app is not running.", "هذه النوافذ مغلقة أو أن التطبيق الخاص بها لا يعمل.");
            ("Invalid workspace setting", "إعداد مساحة العمل غير صالح");
            ("Workspace data", "بيانات مساحة العمل");
            ("Window title is missing or exceeds 4,096 characters.", "عنوان النافذة مفقود أو يتجاوز 4096 حرفًا.");
            ("Unknown title match method.", "طريقة مطابقة العنوان غير معروفة.");
            ("Expected an object.", "كان متوقعًا وجود كائن.");
            (": expected a list.", ": كان متوقعًا وجود قائمة.");
            ("Missing placement.", "موضع النافذة مفقود.");
            ("Invalid saved tab layout; the group will use current defaults.", "تخطيط علامات التبويب المحفوظ غير صالح؛ ستستخدم المجموعة الإعدادات الافتراضية الحالية.");
            ("No valid windows in this group.", "لا توجد نوافذ صالحة في هذه المجموعة.");
            ("Unsupported workspace version; saved workspaces will not be modified.", "إصدار مساحة العمل غير مدعوم؛ لن يتم تعديل مساحات العمل المحفوظة.");
            ("Safe to share: no window titles, file paths or other personal information.", "آمن للمشاركة: لا يحتوي على عناوين النوافذ أو مسارات الملفات أو معلومات شخصية أخرى.");
            ("Copy the report, to paste into an issue", "نسخ التقرير للصقه في بلاغ مشكلة");
            ("Include window details: each window's program, class and whether it gets tabs. Never titles.", "تضمين تفاصيل النوافذ: برنامج كل نافذة وفئتها وما إذا كانت تستخدم علامات تبويب. لا يتضمن العناوين أبدًا.");
            ("Refresh the report", "تحديث التقرير");
            ("Launch the app and retry", "تشغيل التطبيق وإعادة المحاولة");
            ("Launching the app…", "جارٍ تشغيل التطبيق…");
            ("Report copied.", "تم نسخ التقرير.");
            ("Report refreshed.", "تم تحديث التقرير.");
            ("Troubleshooting report", "تقرير استكشاف الأخطاء وإصلاحها");
            ("Project page", "صفحة المشروع");
            ("Report an issue", "الإبلاغ عن مشكلة");
            ("Check Releases", "التحقق من الإصدارات");
            ("Check Releases ↗", "التحقق من الإصدارات ↗");
            ("Crash log", "سجل الأعطال")
        ]
    /// Additional longer strings whose English versions are used by settings pages.
    let private extraArabicTranslations =
        dict [
            ("Icon files (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|All files (*.*)|*.*", "ملفات الأيقونات (*.ico, *.png, *.jpg, *.bmp, *.gif, *.exe, *.dll)|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.exe;*.dll|كل الملفات (*.*)|*.*");
            ("123456789", "123456789");
            ("QWERTYUIOP", "QWERTYUIOP");
            ("ASDFGHJKL;", "ASDFGHJKL;");
            ("If the WindowTabs.exe folder has a settings file, WindowTabs uses it (portable mode); otherwise it uses the one in AppData. Older versions named it WindowTabsSettings.txt; it is now WindowTabsSettings.json, and an old .txt file is still read until the .json one exists.", "إذا وُجد ملف إعدادات بجوار WindowTabs.exe فسيستخدمه البرنامج (الوضع المحمول)، وإلا فسيستخدم الملف الموجود في AppData. كان اسمه في الإصدارات القديمة WindowTabsSettings.txt، وأصبح الآن WindowTabsSettings.json، وسيظل ملف .txt القديم يُقرأ حتى إنشاء ملف .json.");
            ("Restore every setting to its default and restart WindowTabs? The current settings are saved first, so you can import them back.", "هل تريد استعادة جميع الإعدادات الافتراضية وإعادة تشغيل WindowTabs؟ سيتم حفظ الإعدادات الحالية أولًا، حتى تتمكن من استيرادها مرة أخرى.");
            ("Change it for one group from the tab menu.", "غيّره لمجموعة واحدة من قائمة علامة التبويب.");
            ("Add a group to put several apps together, or drag tabs onto each other: the groups show here", "أضف مجموعة لضم عدة تطبيقات معًا، أو اسحب علامات التبويب فوق بعضها؛ ستظهر المجموعات هنا");
            ("Put windows of different apps into one named group. Apps added here always have tabs, leave the list above and don't use Auto-group. Windows you group by dragging tabs together show here too.", "ضع نوافذ تطبيقات مختلفة في مجموعة واحدة ذات اسم. التطبيقات المضافة هنا تستخدم علامات التبويب دائمًا، وتُزال من القائمة أعلاه ولا تستخدم التجميع التلقائي. كما تظهر هنا النوافذ التي تجمعها بسحب علامات التبويب فوق بعضها.");
            ("Choose which apps get tabs. Auto-group puts the windows of an app into one group. Expand an app to see its windows.", "اختر التطبيقات التي تستخدم علامات التبويب. يجمع التجميع التلقائي نوافذ التطبيق الواحد في مجموعة واحدة. وسّع التطبيق لرؤية نوافذه.");
            ("How to use\\n1  Group and position your windows, then click Save layout.\\n2  Select a workspace and click Restore selected.\\n3  To change an item's name or window title matching, double-click it, or point at it and click its edit button.\\nA window whose title has changed since it was saved is still matched by its app.\\nRestore uses open windows. If an app is not running, the result offers to launch it.", "طريقة الاستخدام\\n١  جمّع النوافذ واضبط مواضعها، ثم انقر «حفظ التخطيط».\\n٢  حدد مساحة عمل وانقر «استعادة المحدد».\\n٣  لتغيير اسم عنصر أو طريقة مطابقة عنوان نافذة، انقر عليه مرتين أو أشر إليه ثم انقر زر التحرير.\\nتظل النافذة التي تغير عنوانها منذ الحفظ مطابقةً باستخدام التطبيق الخاص بها.\\nتستخدم الاستعادة النوافذ المفتوحة. إذا لم يكن التطبيق قيد التشغيل، فستتيح لك النتيجة تشغيله.");

        ]

    let private translateArabic (text:string) =
        match arabicTranslations.TryGetValue text with
        | true, translated -> translated
        | _ ->
            match extraArabicTranslations.TryGetValue text with
            | true, translated -> translated
            | _ when text.StartsWith("Close all '") && text.EndsWith("' windows") ->
                "إغلاق جميع نوافذ «" + text.Substring(11, text.Length - 20) + "»"
            | _ when text.StartsWith("Unable to save settings to ") ->
                text.Replace("Unable to save settings to ", "تعذّر حفظ الإعدادات في ")
                    .Replace("Your changes remain in memory and will be retried on the next edit or exit.", "ستظل تغييراتك في الذاكرة وستتم إعادة المحاولة عند التعديل التالي أو عند الخروج.")
            | _ when text.StartsWith("Error loading settings.") ->
                text.Replace("Error loading settings.", "حدث خطأ أثناء تحميل الإعدادات.")
                    .Replace("Fix or remove the file ", "أصلح الملف أو احذفه ")
                    .Replace("Details: ", "التفاصيل: ")
            | _ when text.StartsWith("Settings were reset. WindowTabs will now restart.") ->
                text.Replace("Settings were reset. WindowTabs will now restart.", "تمت إعادة تعيين الإعدادات. سيُعاد تشغيل WindowTabs الآن.")
                    .Replace("Your previous settings were saved to:", "تم حفظ الإعدادات السابقة في:")
            | _ when text.StartsWith("Settings imported. WindowTabs will now restart to apply them.") ->
                text.Replace("Settings imported. WindowTabs will now restart to apply them.", "تم استيراد الإعدادات. سيُعاد تشغيل WindowTabs لتطبيقها.")
                    .Replace("Your previous settings were saved to:", "تم حفظ الإعدادات السابقة في:")
            | _ when text.StartsWith("Used by ") -> "يستخدمه " + text.Substring(8)
            | _ when text.StartsWith("Edit ") -> "تحرير " + text.Substring(5)
            | _ when text.StartsWith("Switch tabs by number in ") ->
                "التبديل بين علامات التبويب بالأرقام في " + text.Substring("Switch tabs by number in ".Length)
            | _ when text.StartsWith("Scan failed: ") -> "فشل الفحص: " + text.Substring("Scan failed: ".Length)
            | _ when text.EndsWith("  ✓") ->
                let baseText = text.Substring(0, text.Length - 3)
                match arabicTranslations.TryGetValue baseText with
                | true, translated -> translated + "  ✓"
                | _ -> text
            | _ when text.EndsWith("*") ->
                let baseText = text.Substring(0, text.Length - 1)
                match arabicTranslations.TryGetValue baseText with
                | true, translated -> translated + "*"
                | _ -> text
            | _ when text.StartsWith("Desktop ") -> "سطح المكتب " + text.Substring(8)
            | _ when text.StartsWith("Group ") && text.Length > 6 -> "المجموعة " + text.Substring(6)
            | _ when text.StartsWith("This group · ") -> "هذه المجموعة · " + text.Substring(13)
            | _ when text.StartsWith("All windows · ") -> "كل النوافذ · " + text.Substring(14)
            | _ when text.StartsWith("Launch ") && text.EndsWith(" apps and retry") ->
                "تشغيل " + text.Substring(7, text.Length - 22) + " تطبيقات وإعادة المحاولة"
            | _ when text.StartsWith("Launching ") && text.EndsWith(" apps…") ->
                "جارٍ تشغيل " + text.Substring(10, text.Length - 16) + " تطبيقات…"
            | _ -> text

    let languages = [| "en","English","EN"; "zh","中文","中"; "ja","日本語","日"; "ar","العربية","ع" |]
    /// Values of the language setting: "system" follows the Windows display language.
    let preferences = "system" :: [for code,_,_ in languages -> code]
    let mutable private preference = "system"
    let setPreference (value:string) = preference <- value
    /// The language text is shown in; any unsupported language falls back to English.
    let current() =
        let code = if preference="system" then CultureInfo.CurrentUICulture.TwoLetterISOLanguageName else preference
        if languages |> Array.exists (fun (c,_,_) -> c=code) then code else "en"
    let tr (text:LocalizedText) =
        match current() with
        | "zh" -> text.zh
        | "ja" -> text.ja
        | "ar" -> translateArabic text.en
        | _ -> text.en
    /// Every translation of a text, for search.
    let all (text:LocalizedText) = [text.en; text.zh; text.ja; translateArabic text.en]

[<AutoOpen>]
module LocalizationOperators =
    /// The text in the current language.
    let tr text = Localization.tr text
