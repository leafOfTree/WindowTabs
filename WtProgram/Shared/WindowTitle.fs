namespace Bemo
open System
open System.Text.RegularExpressions

module WindowTitle =
    /// A path in the title: a drive, a share, or a slash that starts a word, as in
    /// `C:\Projects`, `MINGW64:/c/src` or `user@host: ~/src`; not `owner/repo` or a web address.
    let private path = Regex(@"(^|[\s:(\[])([A-Za-z]:[\\/]|\\\\|~?/(?!/))",RegexOptions.Compiled)

    /// The tab's name from a window title: the last part of a path in it, else the whole title.
    let tabName (text:string) =
        if isNull text || not (path.IsMatch text) then text
        else
            let parts = text.Split([|'\\';'/'|],StringSplitOptions.RemoveEmptyEntries)
            if parts.Length=0 then text else parts.[parts.Length-1]
