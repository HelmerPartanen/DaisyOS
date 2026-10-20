# Desktop languages and typography

DaisyOS uses standard .NET `.resx` resources with Avalonia dynamic resource bindings. English is the neutral fallback; Finnish is the first satellite catalog. Users choose **Settings → Language & input → System language**. Shell labels update immediately. Other applications receive the per-user locale when started; signing out applies it throughout the desktop session.

## Research and decisions

[Avalonia's localization guidance](https://docs.avaloniaui.net/docs/app-development/localizing) recommends `.resx` resources, culture-aware formatting, and a notification mechanism for runtime switching. A plain `x:Static` resource lookup does not update existing controls. DaisyOS publishes the selected catalog into application resources and binds XAML with `DynamicResource`; C# controls use `Loc.Bind`. No extra localization framework or runtime translation network service is needed.

[.NET resource fallback](https://learn.microsoft.com/en-us/dotnet/core/extensions/create-satellite-assemblies) supplies language satellites and neutral resources. `Messages.fi.resx` compiles into `fi/DaisyOS.Shell.resources.dll`, which normal project-reference builds and publishing copy into the shell and standalone applications. A missing translation falls back to English rather than an empty label. The shipped catalogs are complete, and the catalog verifier prevents missing keys and changed format arguments.

The language setting is **per user**. Machine-wide [systemd locale changes](https://www.man7.org/linux/man-pages/man5/org.freedesktop.locale1.5.html) require authorization and do not update existing processes. They are inappropriate for a desktop preference. DaisyOS writes its managed `environment.d` file, mirrors KDE's user locale settings, updates the user D-Bus/systemd activation environment, and changes the shell's child-process environment. Session launch scripts read only whitelisted assignments, never execute the configuration file. If the requested POSIX locale is not generated, UTF-8 formatting falls back to `C.UTF-8` while gettext retains the chosen language. The ISO generates English and Finnish UTF-8 locales.

UI language and keyboard layout remain separate. Changing language does not rename user directories, translate user documents, alter application IDs, or change physical keyboard mappings. System-command parsing explicitly uses `LC_ALL=C.UTF-8` and `LANGUAGE=C`; graphical applications inherit the selected user locale.

[Avalonia text options](https://docs.avaloniaui.net/docs/graphics-animation/text-options) guide the rendering policy, but the repository's pinned Avalonia API is authoritative: hinting is `Strong`, baseline alignment is `Aligned`, and these options must be set through `TextOptions` rather than ordinary style setters. Grayscale antialiasing is suitable for the shell's translucent surfaces; LCD subpixel assumptions would introduce colored fringes on those surfaces. Skia and HarfBuzz remain the rendering and shaping engines.

[Avalonia font fallback](https://docs.avaloniaui.net/docs/styling/custom-fonts) allows the chosen family to be supplemented without replacing it. DaisyOS retains the selected primary font, supplies Noto Sans and DejaVu Sans fallbacks, and includes Noto CJK and emoji fonts in the ISO. Missing glyphs still depend on the installed fonts; damaged or deliberately unusual fonts cannot be made visually identical to the default typeface.

## Shared layout contract

`TypographyPolicy` updates both semantic type resources and legacy aliases. Sizes are whole pixels. Natural shaped line metrics, including fallback scripts, establish the minimum line height. Buttons, dropdowns, single-line inputs, and search fields use one height, rounded to the 4 px geometry grid: at normal Atkinson Hyperlegible size it remains 32 px; large text or taller fonts grow all of them together. Shell chrome follows that height. C# labels and controls bind to the same resources as XAML.

Settings stacks field rows and Appearance controls when available width is insufficient for the selected font size. Labels wrap within their column. Confirmation actions wrap when translated labels cannot fit on one line. Existing center-based press scaling remains intact. Document text, source-code content, and user-entered names stay under their application's content semantics rather than being replaced by catalog matching.

Changing text options is not proof of perfect sharpness at every monitor scale. Headless Skia screenshots test layout and shaping; physical fractional-DPI output still requires acceptance on the user's displays.

## Translation workflow

1. Add an English literal through `Loc.Get`, `Loc.Format`, or `Loc.Bind`. Use explicit placeholders for values; never concatenate translated fragments to construct a sentence.
2. Run `python3 scripts/localization/update-catalog.py`. It adds sources without deleting existing keys or modifying translated catalogs. New XAML labels need a neutral resource entry and `DynamicResource` binding.
3. Translate matching entries in each `Messages.<language>.resx` file. The comments retain English source and usage context. Keys are deterministic SHA-256 prefixes of the English source; changing a source sentence creates a new key. Distinct meanings need distinct source text or a future explicit contextual key.
4. Run `python3 scripts/localization/verify-catalog.py`. It checks duplicates, complete satellites, format arguments and XAML references. Build and render the affected views in the new language.
5. Add a supported language to `SystemLanguages`, its generated ISO locale and the session import whitelist. Test locale persistence, font fallback, keyboard independence, and restart behavior.

English and Finnish singular/plural count messages are explicit. Do not extend English suffix concatenation to other languages. Future languages with additional plural forms must use [CLDR plural rules](https://cldr.unicode.org/index/cldr-spec/plural-rules), with separate translated forms and dedicated tests. RTL flow direction is provided by culture; a future RTL catalog also requires mirrored navigation, icon and mixed-direction acceptance tests before being offered in Settings.

The shell polling path reloads stored language/font preferences into running standalone applications. Resource-bound labels update in place; generated Settings pages and computed calendar/editor status labels refresh when the language changes. Third-party applications already running require restart or a new login session.
