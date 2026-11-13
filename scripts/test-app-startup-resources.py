#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('resources_benchmark', Path(__file__).with_name('benchmark-app-startup-resources.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class StartupResourcesTests(unittest.TestCase):
    def test_variants_change_only_shell_and_keep_entry_location_isolated(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / 'source'
            source.mkdir()
            (source / 'DaisyOS.Notes.dll').write_bytes(b'entry')
            (source / 'DaisyOS.Notes.deps.json').write_text('{}')
            (source / 'DaisyOS.Shell.dll').write_bytes(b'current shell')
            (source / 'Avalonia.Base.dll').write_bytes(b'shared runtime')
            (source / 'runtimes').mkdir()
            baseline = root / 'old.dll'
            baseline.write_bytes(b'old shell')
            entry = module.variant(root / 'variant', source, baseline, 'Notes')
            self.assertFalse(entry.is_symlink())
            self.assertEqual(b'old shell', (entry.parent / 'DaisyOS.Shell.dll').read_bytes())
            self.assertEqual(source / 'Avalonia.Base.dll', (entry.parent / 'Avalonia.Base.dll').resolve())
            self.assertTrue((entry.parent / 'runtimes').is_dir())
            entry.write_bytes(b'changed copy')
            self.assertEqual(b'entry', (source / 'DaisyOS.Notes.dll').read_bytes())
            self.assertEqual(b'current shell', (source / 'DaisyOS.Shell.dll').read_bytes())

    def test_resource_capture_requires_pss_and_preserves_kib_and_thread_units(self):
        with patch.object(Path, 'read_text', side_effect=['Rss: 120 kB\nPss: 80 kB\nPrivate_Dirty: 60 kB\n', 'Threads: 7\n']):
            self.assertEqual({'Rss': 120, 'Pss': 80, 'Private_Dirty': 60, 'Threads': 7}, module.resources(123))
        with patch.object(Path, 'read_text', side_effect=['Rss: 120 kB\n', 'Threads: 7\n']):
            with self.assertRaisesRegex(RuntimeError, 'PSS coverage unavailable'):
                module.resources(123)

    def test_summary_reports_variability_from_raw_samples(self):
        result = module.summarize([{'Pss': n} for n in (50, 10, 30)])['Pss']
        self.assertEqual(dict(median=30, mad=20, minimum=10, maximum=50), result)


if __name__ == '__main__':
    unittest.main()
