# DaisyOS Brand System

## Identity

The final product name is **DaisyOS**. Use that exact spelling in user-facing
text. Lowercase `daisyos` is reserved for Linux users, hostnames, commands,
packages, configuration directories, and repository identifiers. Uppercase
`DAISYOS` is reserved for environment variables and ISO volume labels.

- Product: DaisyOS
- Edition: Desktop Preview
- Shell: DaisyOS Shell
- Tagline: “A focused, fluid Linux desktop.”
- Default installed hostname: `daisyos`
- Live-media hostname: `daisyos-live`
- Package prefix: `daisyos-`
- Configuration directory: `$XDG_CONFIG_HOME/DaisyOS`

## Logo

`src/DaisyOS.Shell/Assets/Brand/favicon.svg` is the canonical DaisyOS product
mark. Use this scalable source for launcher, About, boot, and other branded
surfaces. Raster copies may be generated where the platform requires them, but
must preserve the favicon's silhouette, proportions, and clear space.

Keep clear space around the mark equal to at least one eighth of its rendered
width. Do not stretch, rotate, recolor, add text inside, or place it directly on
visually noisy imagery without a containing surface.

The Plymouth asset under `os/branding/boot` is a 256-pixel raster rendering of
the favicon in the DaisyOS accent on the dark neutral boot background. It is not
enabled in the default ISO until Plymouth boot behavior is VM-tested.

## Color

The source of truth is `Themes/OSColors.axaml`. Brand-facing defaults are:

| Role | Value | Use |
|---|---|---|
| Primary accent | `#59C7E8` | Primary actions and selected states |
| Accent hover | `#20B5E6` | Pointer-over primary actions |
| Accent pressed | `#1599C7` | Pressed primary actions |
| Dark neutral | `#1B1E24` | Dark surfaces and boot background |
| Light neutral | `#F5F6F8` | Light window background |
| Danger | `#D13A3A` | Destructive and error states only |

Theme resources, not literal brand colors, should be consumed by application
controls so light, dark, contrast, and future wallpaper-derived palettes remain
coherent.

## Typography

Inter is the DaisyOS interface family and is bundled as a variable font. The
fallback stack is Segoe UI, SF Pro Display, Noto Sans, and Arial. Material
Symbols Rounded is the system glyph family. `OSTypography.axaml` defines the
approved display, title, subtitle, body, caption, and link hierarchy.

Do not introduce a second UI typeface for individual applications. Monospace is
appropriate only for code, terminal content, identifiers, and diagnostics.

## Icons

System glyphs use rounded, simple silhouettes with consistent optical weight.
Full-color raster icons are reserved for applications and file types. Status
icons remain monochrome and inherit semantic foreground resources. Third-party
application icons must not be redrawn or recolored by DaisyOS.

The existing built-in icon set covers Settings, Files, Trash, launcher folders,
power actions, Settings categories, and status indicators. New icons should use
a 24-unit vector view box where practical and remain readable at 16–20 logical
pixels.

## Sound

The current startup sound is registered in `Assets/Sounds/manifest.json`.
Notification, warning, error, and device event names are reserved placeholders;
they intentionally contain no borrowed or arbitrary audio. Future sounds should
be short, calm, peak-normalized consistently, and reviewed together as one
theme. Every non-critical sound needs a visible setting and must respect Do Not
Disturb.

## Distribution naming

- ISO label: `DAISYOS_YYYYMM`
- ISO publisher: `DaisyOS`
- Release archive: `DaisyOS-Shell-VERSION-CHANNEL-linux-x64.tar.gz`
- Arch shell package: `daisyos-shell`
- Pacman repository: `[daisyos]`
- systemd user service: `daisyos-shell.service`

Do not publish artifacts under the earlier project name. The checkout directory
may retain an old local path, but repository content and produced artifacts must
use DaisyOS.

## Third-party assets

Do not use Apple, Microsoft, Google, distribution, game, or application artwork
as DaisyOS branding. Third-party marks may appear only to identify their actual
products and must follow the owner’s usage terms. A redistribution license for
DaisyOS itself remains a release gate and must not be inferred from this design
document.
