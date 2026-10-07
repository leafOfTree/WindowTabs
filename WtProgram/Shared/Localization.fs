namespace Bemo
open System.Globalization

/// One piece of user-visible text in every supported language. Every field is required, so a
/// missing translation is a compile error. All instances live in Strings.
type LocalizedText = { en:string; zh:string; ja:string }

/// All user-visible text is compiled into the single WindowTabs.exe. (Satellite resource
/// assemblies cannot be statically linked, so a .resx translation would not ship.)
module Localization =
    /// Supported languages: code, name shown in the language menu, compact label.
    let languages = [| "en","English","EN"; "zh","中文","中"; "ja","日本語","日" |]
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
        | _ -> text.en
    /// Every translation of a text, for search.
    let all (text:LocalizedText) = [text.en; text.zh; text.ja]

[<AutoOpen>]
module LocalizationOperators =
    /// The text in the current language.
    let tr text = Localization.tr text
