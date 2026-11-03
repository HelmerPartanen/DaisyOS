#!/usr/bin/env python3
"""Exercise real WebKit rendering with deterministic local assets, not live mail."""
import http.server
import json
import pathlib
import struct
import subprocess
import tempfile
import threading
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]

class MailRendererTests(unittest.TestCase):
    def test_images_scripts_links_and_full_document(self):
        requests = []
        image = (ROOT / 'src/DaisyOS.Shell/Assets/AppIcons/Mail.png').read_bytes()
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass
            def do_GET(self):
                requests.append(self.path)
                self.send_response(200)
                self.send_header('Content-Type', 'image/png' if self.path == '/image.png' else 'text/javascript')
                self.end_headers()
                self.wfile.write(image if self.path == '/image.png' else b'document.body.textContent="BAD"')
        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        try:
            with tempfile.TemporaryDirectory(prefix='daisy-mail-html-test-') as folder:
                root = pathlib.Path(folder)
                (root / 'body.html').write_text(f'''<!doctype html>
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'none'; img-src http: data:; style-src 'unsafe-inline'; form-action 'none'">
<style>body{{margin:0}}table{{background:#edf4ff;width:100%;padding:24px}}.space{{height:900px}}</style>
<table><tr><td><h1>Newsletter</h1><img width="80" src="http://127.0.0.1:{server.server_port}/image.png">
<a href="https://example.org/news">Read more</a><a href="#footer">Jump to footer</a><div class="space"></div><a id="footer" href="mailto:test@example.org">Email us</a></td></tr></table>
<script src="http://127.0.0.1:{server.server_port}/probe.js"></script><script>document.body.innerHTML='BAD'</script><img src="file:///etc/passwd">''')
                process = subprocess.run([str(ROOT / 'src/native/daisy_mail_renderer/build/daisy-mail-renderer'), str(root / 'body.html'), str(root / 'body.png'), str(root / 'map.json'), '720'], capture_output=True, timeout=25)
                self.assertEqual(process.returncode, 0, 'WebKit renderer must succeed')
                layout = json.loads((root / 'map.json').read_text())
                self.assertGreater(layout['height'], 900)
                self.assertEqual(len(layout['links']), 3)
                self.assertEqual(layout['links'][0]['uri'], 'https://example.org/news')
                self.assertGreater(layout['links'][2]['y'], 900)
                self.assertGreater(layout['links'][1]['targetY'], 900)
                self.assertIn('/image.png', requests)
                self.assertNotIn('/probe.js', requests)
                width, height = struct.unpack('>II', (root / 'body.png').read_bytes()[16:24])
                self.assertEqual(width, layout['width'])
                self.assertEqual(height, layout['height'])
                scaled = subprocess.run([str(ROOT / 'src/native/daisy_mail_renderer/build/daisy-mail-renderer'), str(root / 'body.html'), str(root / 'body-2x.png'), str(root / 'map-2x.json'), '720', '2'], capture_output=True, timeout=25)
                self.assertEqual(scaled.returncode, 0)
                scaled_layout = json.loads((root / 'map-2x.json').read_text())
                scaled_width, scaled_height = struct.unpack('>II', (root / 'body-2x.png').read_bytes()[16:24])
                self.assertEqual(scaled_width, scaled_layout['width'] * 2)
                self.assertEqual(scaled_height, scaled_layout['height'] * 2)
        finally:
            server.shutdown()
            server.server_close()

if __name__ == '__main__':
    subprocess.run([str(ROOT / 'scripts/build-mail-renderer.sh')], check=True)
    unittest.main()
