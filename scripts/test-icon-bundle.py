#!/usr/bin/env python3
"""Regression checks for the HugeIcons tree-shaker."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True

SCRIPT = Path(__file__).with_name("build-icon-bundle.py")
spec = importlib.util.spec_from_file_location("icon_bundle", SCRIPT)
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)


class IconBundleTests(unittest.TestCase):
    def test_only_referenced_names_are_emitted_with_weights_deduplicated(self):
        with tempfile.TemporaryDirectory(prefix="daisy-icons-test-") as directory:
            root = Path(directory)
            library = root / "library"
            scan = root / "scan"
            library.mkdir()
            scan.mkdir()
            svg = '<svg fill="none"><path stroke="#141B34" stroke-width="1.5" d="M0 0"/></svg>'
            (library / "play-source.svg").write_text(svg)
            (library / "pause.svg").write_text(svg)
            (library / "unused.svg").write_text(svg)
            (scan / "View.axaml").write_text('<DaisyIcon Icon="Play"/><DaisyIcon Icon="play" Weight="Fill"/>')
            (scan / "obj").mkdir()
            (scan / "obj" / "Generated.axaml").write_text('<DaisyIcon Icon="unused"/>')
            manifest = root / "manifest"
            manifest.write_text("fill/pause\n")
            aliases = root / "aliases.json"
            aliases.write_text(json.dumps({"play": "play-source"}))
            output = root / "output"
            args = [sys.executable, str(SCRIPT), "--library", str(library), "--output", str(output),
                    "--scan-root", str(scan), "--manifest", str(manifest), "--aliases", str(aliases)]
            subprocess.run(args, check=True, capture_output=True)
            self.assertEqual({path.name for path in output.glob("*.svg")}, {"play.svg", "pause.svg"})
            themed = (output / "play.svg").read_text()
            self.assertIn('stroke="currentColor"', themed)
            self.assertIn('fill="none"', themed)
            self.assertIn('stroke-width="1.5"', themed)
            self.assertIn('#141B34', (library / "play-source.svg").read_text())
            # Removing a usage must delete its old generated asset.
            (scan / "View.axaml").write_text("")
            subprocess.run(args, check=True, capture_output=True)
            self.assertEqual({path.name for path in output.glob("*.svg")}, {"pause.svg"})
            manifest.write_text("")
            subprocess.run(args, check=True, capture_output=True)
            self.assertEqual(list(output.glob("*.svg")), [])
            self.assertTrue((output / ".bundle-stamp").is_file())


if __name__ == "__main__":
    unittest.main()
