# HugeIcons development library

Vendored from https://github.com/hugeicons/hugeicons at commit
`9b00d795481de43da3c672c4ecac62ffc6d389b1` (MIT; see LICENSE.md).

The raw SVG catalog is a development input, not an embedded application
resource. `scripts/build-icon-bundle.py` scans static DaisyIcon usages and adds
runtime-selected names from `IconBundle.manifest`. `IconBundle.aliases.json`
maps the shell's semantic names to HugeIcons catalog names; unmapped names
resolve directly. Missing names fail the build.

Only that selected subset is generated into obj and embedded under
`Assets/Icons/HugeIcons`. Monochrome paints become currentColor while geometry
is preserved. The free catalog is Stroke Rounded: legacy Regular/Fill weights
share one asset rather than embedding duplicate SVGs or Pro solid icons.

For new static icons, set DaisyIcon.Icon to a catalog name or add an alias.
For code-created or runtime-selected icons, also add the semantic name to the
manifest. The eject action uses the free USB-disconnected glyph.
