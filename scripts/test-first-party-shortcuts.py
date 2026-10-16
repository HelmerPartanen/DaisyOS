#!/usr/bin/env python3
import importlib.util
import sys
sys.dont_write_bytecode = True
from pathlib import Path
import unittest

root = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("shortcuts", root / "scripts/configure-first-party-shortcuts.py")
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)

class Registry:
    def __init__(self):
        self.keys = {("org.kde.dolphin.desktop", "_launch"): [policy.key_code("Meta+E")],
                     ("kmix", "increase_volume"): [policy.key_code("Volume Up")],
                     ("kwin", "Window Close"): [policy.key_code("Alt+F")],
                     ("org.example.Editor", "open"): [policy.key_code("Ctrl+J")]}
    def allActionsForComponent(self, component):
        return [list(key) + ["", ""] for key in self.keys if key[0] == component[0]]
    def setForeignShortcut(self, action, keys): self.keys[tuple(action[:2])] = list(keys)
    def shortcut(self, action): return self.keys.get(tuple(action[:2]), [])
    def getGlobalShortcutsByKey(self, key):
        return [(action, "", component) for (component, action), keys in self.keys.items() if key in keys]

class ShortcutTests(unittest.TestCase):
    def test_every_registered_key_has_explicit_migration(self):
        owned = policy.owned_shortcuts((root / "packaging/kwin/daisyos-window-bridge/contents/code/main.js").read_text())
        keys = [policy.key_code(sequence) for _, sequence in owned.values()]
        self.assertEqual(len(keys), len(set(keys)))
        registry = Registry()
        policy.migrate(registry, ["org.kde.dolphin.desktop", "kmix", "kwin", "org.example.Editor"], owned)
        self.assertEqual([], registry.shortcut(["org.kde.dolphin.desktop", "_launch"]))
        self.assertEqual([], registry.shortcut(["kmix", "increase_volume"]))
        self.assertEqual([policy.key_code("Alt+F")], registry.shortcut(["kwin", "Window Close"]))
        self.assertEqual([policy.key_code("Ctrl+J")], registry.shortcut(["org.example.Editor", "open"]))
        first = dict(registry.keys)
        policy.migrate(registry, ["org.kde.dolphin.desktop", "kmix", "kwin", "org.example.Editor"], owned)
        self.assertEqual(first, registry.keys)

if __name__ == "__main__": unittest.main()
