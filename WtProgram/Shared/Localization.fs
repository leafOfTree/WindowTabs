namespace Bemo
open System.Globalization

/// All user-visible text is written inline in English and Chinese, plus Japanese where a
/// translation exists, so it compiles into the single WindowTabs.exe. (Satellite resource
/// assemblies cannot be statically linked, so a .resx translation would not ship.)
module Localization =
    let mutable private preference = "system"
    /// "system" follows the Windows display language; "en", "zh" or "ja" override it.
    let setPreference (value:string) = preference <- value
    let private language() =
        if preference="system" then CultureInfo.CurrentUICulture.TwoLetterISOLanguageName else preference
    /// The language text is shown in: "zh", "ja" or "en" (any other language falls back to English).
    let current() = match language() with "zh" -> "zh" | "ja" -> "ja" | _ -> "en"
    /// Japanese comes from LocalizationJa, keyed by the English text; missing entries show English.
    let text (en:string) (zh:string) =
        match language() with
        | "zh" -> zh
        | "ja" -> (match LocalizationJa.table.TryGetValue en with | true,ja -> ja | _ -> en)
        | _ -> en
    let text3 (en:string) (zh:string) (ja:string) =
        match language() with
        | "zh" -> zh
        | "ja" -> ja
        | _ -> en
