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
    let text (en:string) (zh:string) = if language()="zh" then zh else en
    let text3 (en:string) (zh:string) (ja:string) =
        match language() with
        | "zh" -> zh
        | "ja" -> ja
        | _ -> en
