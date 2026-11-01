import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
from PySide6.QtWidgets import QApplication
from PySide6.QtCore import Qt
import hydra

APP = QApplication.instance() or QApplication([])

class Tests(unittest.TestCase):
    def test_languages_and_restart(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {'XDG_CONFIG_HOME': directory}):
            window = hydra.Window()
            window.show()
            for i, code in enumerate(hydra.CODES):
                window.language.setCurrentIndex(i)
                APP.processEvents()
                self.assertEqual(window.start.text(), hydra.tr('NZB erstellen', code))
                self.assertEqual(window.layoutDirection() == Qt.RightToLeft, code == 'ar')
                if os.environ.get('HYDRA_SCREENSHOTS'):
                    window.grab().save('/out/hydra-' + code + '.png')
            window.close()
            reopened = hydra.Window()
            self.assertEqual(reopened.code, 'ar')
            reopened.close()

    def test_desktop_registration(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {'XDG_DATA_HOME': directory}), patch('subprocess.run') as run:
            hydra.register()
            text = (Path(directory)/'applications/nzb-hydra.desktop').read_text()
            self.assertIn('x-scheme-handler/nzblnk', text)
            self.assertIn(' %u', text)
            run.assert_called_once()
            self.assertEqual(run.call_args.args[0], ['xdg-mime','default','nzb-hydra.desktop','x-scheme-handler/nzblnk'])

if __name__ == '__main__':
    unittest.main()
