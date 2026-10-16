#!/usr/bin/env python3
"""Own shell UI shortcuts in DaisyOS; leave KWin window operations and hardware backends intact."""
import argparse
import configparser
import os
from pathlib import Path
import re
import shutil

MODIFIERS = {"Meta": 0x10000000, "Ctrl": 0x04000000, "Alt": 0x08000000, "Shift": 0x02000000}
KEYS = {"Meta": 0x01000022, "Print": 0x01000009, "Esc": 0x01000000, "Tab": 0x01000001,
        "Space": 0x20, "Del": 0x01000007, "Volume Down": 0x01000070, "Volume Mute": 0x01000071,
        "Volume Up": 0x01000072, "Microphone Mute": 0x01000113, "Media Play": 0x01000080,
        "Media Next": 0x01000083, "Media Previous": 0x01000082}
BLOCKED = {"plasmashell", "ksmserver", "kmix", "mediacontrol", "krunner", "ActivityManager"}
BLOCKED_KWIN = {"Edit Tiles", "Window Operations Menu", "Setup Window Shortcut", "Cube"}


def key_code(sequence):
    if sequence == "Meta":
        return KEYS[sequence]
    parts = sequence.split("+")
    key = parts.pop()
    value = KEYS[key] if key in KEYS else ord(key.upper()) if len(key) == 1 else None
    if value is None:
        raise ValueError(f"Unsupported shortcut: {sequence}")
    for modifier in parts:
        value |= MODIFIERS[modifier]
    return value


def owned_shortcuts(script):
    result = {name: (description, sequence) for name, description, sequence in re.findall(
        r'registerShortcut\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)"', script)}
    result["DaisyOS Task Overview"] = ("Toggle DaisyOS Task Overview", "Meta+Tab")
    return result


def blocked_component(name):
    return name in BLOCKED or name.startswith("org.kde.")


def migrate(accel, components, owned):
    """Disarm foreign UI actions before assigning keys, including live-only service entries."""
    for component in components:
        for action in accel.allActionsForComponent([component, "", "", ""]):
            if blocked_component(component) or (component == "kwin" and action[1] in BLOCKED_KWIN):
                accel.setForeignShortcut(action, [])
    for name, (_, sequence) in owned.items():
        action = ["kwin", name, "", ""]
        key = key_code(sequence)
        # Remove any stale owner of this key (including shortcuts from older DaisyOS releases).
        for conflict in accel.getGlobalShortcutsByKey(key):
            # KGlobalShortcutInfo: uniqueName, friendlyName, componentUniqueName, ...
            foreign = [str(conflict[2]), str(conflict[0]), "", ""]
            if foreign[:2] != action[:2]:
                remaining = [int(k) for k in accel.shortcut(foreign) if int(k) != key]
                accel.setForeignShortcut(foreign, remaining)
        accel.setForeignShortcut(action, [key])
        if list(map(int, accel.shortcut(action))) != [key]:
            raise RuntimeError(f"Shortcut ownership failed: {name}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config")) / "kglobalshortcutsrc")
    parser.add_argument("--offline", action="store_true")
    options = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    owned = owned_shortcuts((root / "packaging/kwin/daisyos-window-bridge/contents/code/main.js").read_text())
    config = configparser.RawConfigParser(strict=False, delimiters=("=",))
    config.optionxform = str
    if options.config.exists():
        config.read(options.config)
        backup = options.config.with_suffix(".before-daisyos-shortcuts")
        if not backup.exists():
            shutil.copy2(options.config, backup)
    for section in config.sections():
        component = section.removeprefix("services][").replace(r"\x5d\x5b", "][")
        if blocked_component(component) or "org.kde." in component:
            for key in config[section]:
                if not key.startswith("_"):
                    config[section][key] = "none,none," + key
    if not config.has_section("kwin"):
        config.add_section("kwin")
    for name in BLOCKED_KWIN:
        config["kwin"][name] = "none,none," + name
    for name, (description, sequence) in owned.items():
        config["kwin"][name] = f"{sequence},{sequence},{description}"
    options.config.parent.mkdir(parents=True, exist_ok=True)
    temporary = options.config.with_suffix(".daisyos-tmp")
    with temporary.open("w") as stream:
        config.write(stream, space_around_delimiters=False)
    temporary.replace(options.config)
    if not options.offline:
        import dbus
        bus = dbus.SessionBus()
        accel = dbus.Interface(bus.get_object("org.kde.kglobalaccel", "/kglobalaccel"), "org.kde.KGlobalAccel")
        components = []
        for path in accel.allComponents():
            properties = dbus.Interface(bus.get_object("org.kde.kglobalaccel", path), "org.freedesktop.DBus.Properties")
            components.append(str(properties.Get("org.kde.kglobalaccel.Component", "uniqueName")))
        # Typed arrays matter when clearing the last key of an action.
        class TypedAccel:
            def __getattr__(self, name):
                return getattr(accel, name)
            def setForeignShortcut(self, action, keys):
                accel.setForeignShortcut(action, dbus.Array(keys, signature="i"))
        migrate(TypedAccel(), components, owned)
    print(f"Configured {len(owned)} DaisyOS shortcuts; foreign desktop UI shortcuts disabled.")


if __name__ == "__main__":
    main()
