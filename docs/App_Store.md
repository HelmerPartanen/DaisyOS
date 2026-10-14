# Native App Store

The store uses the same `AppWindowTitleBar`, `SystemAppWindowChrome`, full-height
sidebar surface, typography, buttons, search control, section headers and semantic
resources as the other DaisyOS applications. Installed and Updates navigation
remain visible while the category list scrolls. Ctrl+F focuses search, Escape or
Alt+Left leaves details, F5 refreshes the current page, and F11 uses the shared
native fullscreen controller.

Cards use compact spacing and the shared subtle-button background states without
hover borders or accent-colored titles. Discover uses the bundled grid icon.
Installed apps use the shared primary Open button, including live state changes.
Scroll viewers globally reserve an edge rail outside the content viewport, even
when nested controls explicitly enable auto-hide. Content margins and maximum
widths remain inside the scroller.
Picker appearance is defined globally in `DropdownMenuStyles.axaml`; Settings
only retains picker-specific sizing. The store does not display Flathub branding
or source labels; backend repository identifiers and application licenses remain
intact.

## Catalog and discovery

`LinuxAppStoreService` reads Flathub's actual AppStream desktop application
catalog, streaming one component at a time. It first uses the DaisyOS cache or an
existing Flatpak catalog. If neither exists, it downloads the architecture's
catalog from `https://dl.flathub.org/repo/appstream/<arch>/appstream.xml.gz`.
Refresh downloads and validates a replacement before atomically replacing the
cache. The previous catalog remains usable if a refresh fails.

Search matches names, descriptions of purpose, IDs, publishers, keywords and
categories, ranks exact names first, and debounces typing. The verified filter
uses Flathub's metadata; recommendations select real catalog entries. The store
displays no invented ratings, reviews, versions, download counts or sizes.
The card collections are constrained to the scroll viewport, with outer horizontal
margins and clipping independent of long card content. Results appear in pages of 24; filtering and sorting do not fetch the catalog
again. Discovery offers starting points plus the broader catalog.

Details include descriptions, screenshot galleries, licensing, publisher,
verification, version and project links. Flathub's `/api/v2/summary/<app-id>`
provides optional download/disk sizes, shared runtime estimates and requested
access. These optional network details never prevent offline navigation or app
management. See [Flathub's metadata guidance](https://docs.flathub.org/docs/for-app-authors/metainfo-guidelines)
and the [official API implementation](https://github.com/flathub-infra/website/blob/main/backend/app/routes/apps.py).

## Application management

All application operations use Flatpak with separate process arguments. New
installs use the user installation and configure Flathub there on the first
install. Existing applications are updated/removed in their actual installation,
including named installations. Native installations are recognized through exact
AppStream desktop IDs and explicit legacy desktop-ID aliases for common apps.
They show Installed/Open and launch through `gio launch`; Flatpak installations
take precedence, and native packages are not sent to Flatpak update/removal
commands. The library prioritizes the user's installation
when the same ID exists in several scopes. Removal asks for confirmation and
keeps application data. The store does not mutate the host package manager.

Transactions run serially and show indeterminate progress instead of simulated
percentages. Cancellation stops the CLI process through `SafeCommandRunner`;
refresh reconciles the actual installed state. Completion also reconciles version
and installation scope. The shell retains the store session when its window
closes, so ongoing jobs remain tracked on reopening.

`AsyncImage` loads icons/screenshots on worker threads with four concurrent
workers, bounded response sizes, scaled decoding and a 100 MB/256-file disk
cache. Bitmap lifetime follows the visual controls. Missing images retain a
bundled category glyph. Current and legacy Flathub CDN image routes are supported.

## Verification

```sh
dotnet build src/DaisyOS.Shell/DaisyOS.Shell.csproj --no-restore
dotnet test tests/DaisyOS.Tests/DaisyOS.Tests.csproj --filter FullyQualifiedName~LinuxAppStoreServiceTests
dotnet test tests/DaisyOS.AppStore.Tests/DaisyOS.AppStore.Tests.csproj
python3 scripts/localization/verify-catalog.py
dotnet run --project scripts/app-store-ui-acceptance/AppStoreUiAcceptance.csproj -- artifacts/app-store --refresh
```

The dedicated headless workflow suite avoids unrelated file-picker compilation
failures in the existing ShellDemo suite. The acceptance runner renders the real
catalog, details, search, installed apps and updates at 800/1120 pixels in both
themes, and checks native chrome/full-height sidebars. Transaction tests use
controlled command runners; they do not install, update or remove applications
on the development machine.

Screenshot thumbnails retain their full 280 px height and open a centered overlay (up to 960 by 600 px) over the application page, without a background box behind the image. The viewer includes previous/next controls and an image counter. Left/right arrows navigate, Home/End select the first/last image, and Escape or a click outside the image viewer closes it and restores focus to the selected thumbnail. The application page retains its scroll position.
