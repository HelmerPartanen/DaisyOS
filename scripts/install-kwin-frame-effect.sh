#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"

# KDecoration 6.7 cannot attach a decoration to a client-side-decorated
# window. This compositor shader is therefore the compatibility path which
# clips third-party CSD surfaces and paints DaisyOS' neutral double frame.
source_dir="$repo_root/src/native/kwin_frame_effect"
ecm_commit="68483132b87f4d7b953aec94ef51d1234d41f937"
vulkan_headers_commit="6802bb4733b63ed5efd3adb308a6c885ef180ea1"
cache_root="${XDG_CACHE_HOME:-$HOME/.cache}/daisyos/kwin-frame-effect"
ecm_source="$cache_root/ecm-source"
vulkan_source="$cache_root/vulkan-headers"
build_dir="$cache_root/native-build"
ecm_prefix="$cache_root/ecm-prefix"
plugin_root="$HOME/.local/lib/qt6/plugins"
plugin_dir="$plugin_root/kwin/effects/plugins"

# KWin effects use private compositor APIs. File existence alone cannot tell
# whether a previous build matches this checkout or the installed compositor.
stamp_path="${XDG_DATA_HOME:-$HOME/.local/share}/daisyos/kwin-frame-effect.version"
manifest_path="${stamp_path}.sha256"
kwin_version="$(kwin_wayland --version 2>/dev/null || true)"
expected_version="$(
  { printf '%s\n' "$ecm_commit" "$vulkan_headers_commit" "$kwin_version";
    sha256sum "$repo_root/scripts/install-kwin-frame-effect.sh" "$source_dir"/{CMakeLists.txt,frameeffect.cpp,frame.frag,metadata.json,resources.qrc} | cut -d ' ' -f 1;
  } | sha256sum | cut -d ' ' -f 1
)"
if [[ "${1:-}" == --check ]]; then
  [[ $# == 1 && -n "$kwin_version" && -s "$plugin_dir/daisyframe.so" \
     && -f "$stamp_path" && "$(cat "$stamp_path")" == "$expected_version" \
     && -f "$manifest_path" ]] && sha256sum --check --status "$manifest_path"
  exit "$?"
fi
if (($#)); then
  echo "Usage: scripts/install-kwin-frame-effect.sh [--check]" >&2
  exit 2
fi

for command in cmake git kwriteconfig6 python3 flock; do
  command -v "$command" >/dev/null 2>&1 || {
    echo "$command is required to install the DaisyOS KWin frame effect." >&2
    exit 1
  }
done

checkout_pinned() {
  local repository="$1" destination="$2" commit="$3"
  if [[ ! -d "$destination/.git" ]]; then
    git clone --filter=blob:none --no-checkout "$repository" "$destination"
  fi
  if ! git -C "$destination" cat-file -e "$commit^{commit}" 2>/dev/null; then
    git -C "$destination" fetch --depth=1 origin "$commit"
  fi
  git -C "$destination" checkout --detach --force "$commit"
}

mkdir -p "$cache_root"
exec 9>"$cache_root/install.lock"
flock 9
cmake_generator=()
if command -v ninja >/dev/null 2>&1; then
  cmake_generator=(-G Ninja)
fi
checkout_pinned https://github.com/KDE/extra-cmake-modules.git "$ecm_source" "$ecm_commit"
cmake -S "$ecm_source" -B "$cache_root/ecm-build" "${cmake_generator[@]}" \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$ecm_prefix"
cmake --build "$cache_root/ecm-build"
cmake --install "$cache_root/ecm-build"

checkout_pinned https://github.com/KhronosGroup/Vulkan-Headers.git "$vulkan_source" "$vulkan_headers_commit"
cmake -S "$source_dir" -B "$build_dir" "${cmake_generator[@]}" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_PREFIX_PATH="$ecm_prefix" \
  -DVulkan_INCLUDE_DIR="$vulkan_source/include"
cmake --build "$build_dir"

install -d -m 0755 "$plugin_dir"
# Never truncate a shared object mapped by the running compositor. Publish
# complete files atomically, then reload the effect after its config is ready.
atomic_install() {
  local mode="$1" source="$2" destination="$3" temporary
  temporary="$(mktemp "${destination}.XXXXXX")"
  install -m "$mode" "$source" "$temporary"
  mv -f -- "$temporary" "$destination"
}
atomic_install 0755 "$build_dir/daisyframe.so" "$plugin_dir/daisyframe.so"

kwriteconfig6 --file kwinrc --group Plugins --key kwin4_effect_shapecornersEnabled false
kwriteconfig6 --file kwinrc --group Plugins --key daisyframeEnabled true
kwriteconfig6 --file kwinrc --group Plugins --key blurEnabled true
kwriteconfig6 --file kwinrc --group Effect-blur --key BlurStrength 4
kwriteconfig6 --file kwinrc --group Round-Corners --key Size 16
kwriteconfig6 --file kwinrc --group Round-Corners --key InactiveCornerRadius 16
kwriteconfig6 --file kwinrc --group Round-Corners --key AnimationDuration 180
kwriteconfig6 --file kwinrc --group Round-Corners --key MarkWindowsTranslucent true
kwriteconfig6 --file kwinrc --group Round-Corners --key UseNativeDecorationShadows true
kwriteconfig6 --file kwinrc --group Round-Corners --key OutlineThickness 1
kwriteconfig6 --file kwinrc --group Round-Corners --key InactiveOutlineThickness 1
# Use the current semantic palette exported by the shell, including its opacity.
read -r frame_outer_rgb frame_outer_alpha frame_inner_rgb frame_inner_alpha < <(python3 - <<'PYCOLORS'
import configparser, os, re
from pathlib import Path
config = configparser.ConfigParser(interpolation=None)
config.read(Path(os.environ.get("XDG_CONFIG_HOME", str(Path.home() / ".config"))) / "daisyos-window-decorationrc")
def color(key, fallback):
    value = config.get("Decoration", key, fallback=fallback)
    if not re.fullmatch(r"#[0-9a-fA-F]{8}", value): value = fallback
    return value[0] + value[3:], int(value[1:3], 16)
outer, outer_alpha = color("ShellFrameOuterEdge", "#99000000")
inner, inner_alpha = color("ShellFrameInnerHighlight", "#1AFFFFFF")
print(outer, outer_alpha, inner, inner_alpha)
PYCOLORS
)
kwriteconfig6 --file kwinrc --group Round-Corners --key OutlineColor --type string "$frame_outer_rgb"
kwriteconfig6 --file kwinrc --group Round-Corners --key InactiveOutlineColor --type string "$frame_outer_rgb"
kwriteconfig6 --file kwinrc --group Round-Corners --key ActiveOutlineAlpha "$frame_outer_alpha"
kwriteconfig6 --file kwinrc --group Round-Corners --key InactiveOutlineAlpha "$frame_outer_alpha"
kwriteconfig6 --file kwinrc --group Round-Corners --key SecondOutlineThickness 1
kwriteconfig6 --file kwinrc --group Round-Corners --key InactiveSecondOutlineThickness 1
kwriteconfig6 --file kwinrc --group Round-Corners --key SecondOutlineColor --type string "$frame_inner_rgb"
kwriteconfig6 --file kwinrc --group Round-Corners --key InactiveSecondOutlineColor --type string "$frame_inner_rgb"
kwriteconfig6 --file kwinrc --group Round-Corners --key ActiveSecondOutlineAlpha "$frame_inner_alpha"
kwriteconfig6 --file kwinrc --group Round-Corners --key InactiveSecondOutlineAlpha "$frame_inner_alpha"
kwriteconfig6 --file kwinrc --group Round-Corners --key OuterOutlineThickness 0
kwriteconfig6 --file kwinrc --group Round-Corners --key InactiveOuterOutlineThickness 0
kwriteconfig6 --file kwinrc --group Round-Corners --key IncludeNormalWindows true
kwriteconfig6 --file kwinrc --group Round-Corners --key IncludeDialogs true
# Layer-shell surfaces are excluded by their native window type, including
# titled panels. Normal shell application windows remain eligible.
kwriteconfig6 --file kwinrc --group Round-Corners --key Exclusions \
  'DaisyOS Lock Screen,DaisyOS Lock Wallpaper'
kwriteconfig6 --file kwinrc --group Round-Corners --key DisableRoundTile true
kwriteconfig6 --file kwinrc --group Round-Corners --key DisableOutlineTile false
kwriteconfig6 --file kwinrc --group Round-Corners --key DisableRoundMaximize true
kwriteconfig6 --file kwinrc --group Round-Corners --key DisableOutlineMaximize false
kwriteconfig6 --file kwinrc --group Round-Corners --key DisableRoundFullScreen true
kwriteconfig6 --file kwinrc --group Round-Corners --key DisableOutlineFullScreen false

if command -v qdbus6 >/dev/null 2>&1; then
  timeout 3 qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.unloadEffect kwin4_effect_shapecorners >/dev/null 2>&1 || true
  timeout 3 qdbus6 org.kde.KWin /KWin org.kde.KWin.reconfigure >/dev/null 2>&1 || true
  timeout 3 qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.unloadEffect daisyframe >/dev/null 2>&1 || true
  timeout 3 qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.loadEffect daisyframe >/dev/null 2>&1 || true
fi

mkdir -p "$(dirname "$stamp_path")"
sha256sum "$plugin_dir/daisyframe.so" > "${manifest_path}.tmp"
mv -f -- "${manifest_path}.tmp" "$manifest_path"
printf '%s\n' "$expected_version" > "${stamp_path}.tmp"
mv -f -- "${stamp_path}.tmp" "$stamp_path"

echo "Installed the DaisyOS window-frame compositor effect (embedded shader)."
if command -v qdbus6 >/dev/null 2>&1; then
  loaded="$(timeout 3 qdbus6 org.kde.KWin /Effects org.kde.kwin.Effects.isEffectLoaded daisyframe 2>/dev/null || true)"
  if [[ "$loaded" == true ]]; then
    echo "KWin confirms the effect is loaded."
  else
    echo "The files are installed, but KWin has NOT loaded the effect in this session." >&2
    echo "Log out and back into DaisyOS. If it remains missing, check the KWin journal for ShapeCorners or shader errors." >&2
  fi
else
  echo "Live KWin activation could not be checked because qdbus6 is unavailable. Restart the DaisyOS session once."
fi
