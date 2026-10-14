# Translated action labels — 2026-10-08

Files Sort used a fixed 58px width inside a fixed 220px toolbar, clipping Finnish text. Sort now uses a minimum width and measures its icon and translated label. The toolbar retains its minimum width and grows to fit all actions; its height also measures content above the shared input minimum.

An XML audit found other translated action buttons with fixed widths in Wi-Fi password, Desktop Properties and App Store views. These now use minimum widths, including App Store actions supplied by the view model. Files table column widths remain tied to their corresponding data columns.

The regression changes English → Finnish → English on an already-open Files window at its 740px minimum width, using Inter at 100% and Noto Sans at 150% font sizing. It checks full text width and toolbar/button containment. Existing multilingual Settings, confirmation and control sizing checks are included.

Seventeen focused GUI checks passed, including App Store layout cases. Whitespace checks passed. The language audit covered current translated XAML action buttons; user file names and column widths remain content/data concerns.

Files, Notes and Settings rebuilt with zero warnings/errors. Native shell reload preserved KWin, and strict compositor preflight returned zero failures/warnings. Reopen existing system-app windows to load the new layouts. Physical translated-label acceptance remains with the user.
