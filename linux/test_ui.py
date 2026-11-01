import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
from PySide6.QtWidgets import QApplication
from PySide6.QtCore import Qt
import kestrel

APP = QApplication.instance() or QApplication([])

class Tests(unittest.TestCase):
    def test_legacy_settings_migration(self):
        import json
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {'XDG_CONFIG_HOME': directory}):
            legacy = Path(directory) / 'nzb-hydra/settings.json'
            legacy.parent.mkdir()
            legacy.write_text(json.dumps({'language': 'fr', 'folder': '/tmp/nzb-import', 'auto_open': False}))
            window = kestrel.Window()
            self.assertEqual(window.code, 'fr')
            self.assertEqual(window.folder.text(), '/tmp/nzb-import')
            self.assertFalse(window.auto.isChecked())
            window.language.setCurrentIndex(kestrel.CODES.index('en'))
            window.close()
            reopened = kestrel.Window()
            self.assertEqual(reopened.code, 'en')
            reopened.close()

    def test_languages_and_restart(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {'XDG_CONFIG_HOME': directory}):
            window = kestrel.Window()
            window.show()
            for i, code in enumerate(kestrel.CODES):
                window.language.setCurrentIndex(i)
                APP.processEvents()
                self.assertEqual(window.start.text(), kestrel.tr('NZB erstellen', code))
                self.assertEqual(window.layoutDirection() == Qt.RightToLeft, code == 'ar')
                if os.environ.get('KESTREL_SCREENSHOTS'):
                    window.grab().save('/out/kestrel-' + code + '.png')
            window.close()
            reopened = kestrel.Window()
            self.assertEqual(reopened.code, 'ar')
            reopened.close()

    def test_desktop_registration(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {'XDG_DATA_HOME': directory}), patch('subprocess.run') as run:
            kestrel.register()
            text = (Path(directory)/'applications/nzb-kestrel.desktop').read_text()
            self.assertIn('x-scheme-handler/nzblnk', text)
            self.assertIn(' %u', text)
            run.assert_called_once()
            self.assertEqual(run.call_args.args[0], ['xdg-mime','default','nzb-kestrel.desktop','x-scheme-handler/nzblnk'])

if __name__ == '__main__':
    unittest.main()
