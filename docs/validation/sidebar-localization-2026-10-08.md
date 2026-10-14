# Files sidebar localization — 2026-10-08

Standard sidebar labels now have semantic translation sources. Finnish displays Koti, Työpöytä, Asiakirjat, Lataukset, Kuvat, Videot, Musiikki and Roskakori. Language changes replace displayed standard labels while retaining their path, icon and color roles. Pinned folders and drive labels retain their actual names.

A shared, lazily loaded user-directory map reads `$XDG_CONFIG_HOME/user-dirs.dirs` (or `~/.config/user-dirs.dirs`). It accepts configured absolute paths and HOME-relative paths, including Unicode and spaces, without executing configuration as shell code. Missing/invalid entries retain conventional home-directory fallbacks. It is read once per app process; external user-directory configuration edits require reopening the app to reload the mapping. Changing DaisyOS's display language does not rename directories.

Files standard sidebar navigation, sidebar drop targets/active aliases, folder icon classification, and file-picker locations use this mapping. This avoids translating a label and then constructing a filesystem path from that label.

The localization refresh helper now uses Opened/Closed for window roots, whose lifecycle differs from attached child controls. This fixes live updates and removes the event subscription on close.

13 path/icon tests passed, including Finnish/custom absolute destinations and non-evaluation of shell expressions. 13 GUI tests passed, including live Finnish/English label changes with stable paths/pinned names, sidebar chrome, hover behavior, and picker workflows. All 976 catalog messages and Finnish coverage validated; whitespace checks passed. Interactive physical-display acceptance remains separate.

Files, Notes and Settings rebuilt with zero warnings/errors. The shell was reloaded, and all nine strict live compositor checks passed.
