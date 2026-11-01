"""NZB Hydra Linux desktop application."""
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import urllib.error

from PySide6.QtCore import Qt, QThread, Signal, QUrl
from PySide6.QtGui import QDesktopServices, QIcon, QPixmap
from PySide6.QtWidgets import (QApplication, QWidget, QVBoxLayout, QHBoxLayout,
    QLabel, QComboBox, QPlainTextEdit, QLineEdit, QPushButton, QFileDialog,
    QCheckBox, QProgressBar, QMessageBox)
import engine

BASE = Path(__file__).resolve().parent
CODES = ['de', 'en', 'es', 'fr', 'ru', 'ar']
NAMES = ['Deutsch', 'English', 'Español', 'Français', 'Русский', 'العربية']
TEXT = json.loads((BASE / 'translations.json').read_text(encoding='utf-8'))

def tr(key, code):
    return TEXT.get(key, [key]*6)[CODES.index(code)]

def desktop_quote(value):
    # Desktop Entry Exec quoting, not shell quoting. Percent is a field-code escape.
    return '"' + str(value).replace('\\', '\\\\').replace('"', '\\"').replace('`', '\\`').replace('$', '\\$').replace('%', '%%') + '"'

def register():
    if getattr(sys, 'frozen', False):
        command = desktop_quote(sys.executable)
    else:
        command = desktop_quote(sys.executable) + ' ' + desktop_quote(Path(__file__).resolve())
    appdir = Path(os.environ.get('XDG_DATA_HOME', str(Path.home()/'.local/share'))) / 'applications'
    appdir.mkdir(parents=True, exist_ok=True)
    icon = BASE / 'hydra.png'
    content = ('[Desktop Entry]\nType=Application\nName=NZB Hydra\n'
               'Comment=Create NZB files from NZBLNK links\nTerminal=false\n'
               f'Exec={command} %u\nIcon={icon}\n'
               'MimeType=x-scheme-handler/nzblnk;\nCategories=Network;\n')
    (appdir/'nzb-hydra.desktop').write_text(content, encoding='utf-8')
    subprocess.run(['xdg-mime', 'default', 'nzb-hydra.desktop', 'x-scheme-handler/nzblnk'], check=True, timeout=15)

class Worker(QThread):
    progress = Signal(str, int)
    result = Signal(str, str)
    def __init__(self, link, folder):
        super().__init__()
        self.link, self.folder = link, folder
        self.stop = threading.Event()
    def run(self):
        try:
            path = engine.run(self.link, self.folder, self.stop, self.progress.emit)
            self.result.emit('ok', path)
        except engine.Cancelled:
            self.result.emit('cancel', 'Die Suche wurde abgebrochen.')
        except urllib.error.HTTPError as exc:
            self.result.emit('error', 'HTTP ' + str(exc.code))
        except (urllib.error.URLError, ConnectionError):
            self.result.emit('error', 'Suchdienst nicht erreichbar. Bitte Internetverbindung prüfen.')
        except Exception as exc:
            # Never include URL-bearing network exceptions (links may contain search keys).
            self.result.emit('error', str(exc) if isinstance(exc, (ValueError, TimeoutError)) else type(exc).__name__)

class Window(QWidget):
    def __init__(self, initial=''):
        super().__init__()
        self.settings_path = Path(os.environ.get('XDG_CONFIG_HOME', str(Path.home()/'.config'))) / 'nzb-hydra/settings.json'
        try:
            self.settings = json.loads(self.settings_path.read_text(encoding='utf-8'))
            if not isinstance(self.settings, dict):
                self.settings = {}
        except (OSError, ValueError):
            self.settings = {}
        self.code = self.settings.get('language', 'de')
        if self.code not in CODES:
            self.code = 'de'
        self.worker = None
        self.last = ''
        self.downloader = self.settings.get('downloader', '')
        self.bound = []
        self.setWindowTitle('NZB Hydra')
        self.setWindowIcon(QIcon(str(BASE/'hydra.png')))
        self.resize(960, 800)
        self.setMinimumSize(760, 680)
        self.setStyleSheet('''
            QWidget { background:#080708; color:#f2edef; font-family:"Noto Sans"; font-size:14px; }
            QPlainTextEdit,QLineEdit,QComboBox { background:#201a1d; border:1px solid #53313b; border-radius:5px; padding:8px; selection-background-color:#ad263d; }
            QPushButton { background:#34151d; border:1px solid #7e2336; border-radius:6px; padding:10px 13px; }
            QPushButton:hover { background:#631b2c; }
            QPushButton:disabled { color:#887d80; background:#1c1719; border-color:#30262a; }
            QPushButton#primary { background:#bc203b; font-weight:600; }
            QProgressBar { border:1px solid #53313b; border-radius:4px; text-align:center; min-height:20px; }
            QProgressBar::chunk { background:#cc2743; }
            QCheckBox { spacing:10px; }
        ''')
        layout = QVBoxLayout(self)
        layout.setContentsMargins(26, 20, 26, 24)
        layout.setSpacing(12)
        header = QHBoxLayout()
        logo = QLabel()
        logo.setPixmap(QPixmap(str(BASE/'hydra.png')).scaled(90,90,Qt.KeepAspectRatio,Qt.SmoothTransformation))
        header.addWidget(logo)
        brand = QLabel('NZB HYDRA')
        brand.setStyleSheet('font-size:32px;font-weight:700;')
        header.addWidget(brand, 1)
        header.addWidget(self.label('Sprache'))
        self.language = QComboBox()
        self.language.addItems(NAMES)
        self.language.setCurrentIndex(CODES.index(self.code))
        self.language.setMinimumWidth(160)
        header.addWidget(self.language)
        layout.addLayout(header)
        layout.addWidget(self.label('NZBLNK-LINK'))
        self.link = QPlainTextEdit(initial)
        self.link.setMaximumHeight(110)
        layout.addWidget(self.link)
        row = QHBoxLayout()
        row.addWidget(self.button('Aus Zwischenablage', lambda: self.link.setPlainText(QApplication.clipboard().text().strip())))
        row.addStretch()
        layout.addLayout(row)
        layout.addWidget(self.label('SPEICHERORT'))
        row = QHBoxLayout()
        self.folder = QLineEdit(self.settings.get('folder', str(Path.home()/'Downloads/nzb')))
        row.addWidget(self.folder, 1)
        row.addWidget(self.button('Auswählen', self.choose_folder))
        layout.addLayout(row)
        self.auto = QCheckBox()
        self.bound.append((self.auto, 'Nach dem Speichern an den Downloader übergeben'))
        self.auto.setChecked(self.settings.get('auto_open', True))
        layout.addWidget(self.auto)
        row = QHBoxLayout()
        self.mode = QComboBox()
        self.mode.addItems(['', ''])
        self.mode.setCurrentIndex(1 if self.downloader else 0)
        row.addWidget(self.mode, 1)
        row.addWidget(self.button('Downloader auswählen …', self.choose_downloader))
        layout.addLayout(row)
        row = QHBoxLayout()
        self.start = self.button('NZB erstellen', self.search)
        self.start.setObjectName('primary')
        self.cancel = self.button('Abbrechen', self.cancel_search)
        self.cancel.setEnabled(False)
        self.open = self.button('Im Downloader öffnen', self.open_nzb)
        self.open.setEnabled(False)
        for b in (self.start, self.cancel, self.open):
            row.addWidget(b)
        layout.addLayout(row)
        self.status = QLabel()
        self.status.setWordWrap(True)
        layout.addWidget(self.status)
        self.progress = QProgressBar()
        self.progress.setValue(0)
        layout.addWidget(self.progress)
        self.log = QPlainTextEdit()
        self.log.setReadOnly(True)
        layout.addWidget(self.log, 1)
        footer = QHBoxLayout()
        footer.addWidget(self.button('Browser-Links zuordnen', self.associate))
        footer.addStretch()
        footer.addWidget(self.button('Ordner öffnen', self.open_folder))
        layout.addLayout(footer)
        self.language.currentIndexChanged.connect(self.change_language)
        self.apply_language()

    def label(self, key):
        item = QLabel()
        self.bound.append((item, key))
        return item

    def button(self, key, callback):
        item = QPushButton()
        item.clicked.connect(callback)
        self.bound.append((item, key))
        return item

    def t(self, key):
        return tr(key, self.code)

    def change_language(self, index):
        self.code = CODES[index]
        self.apply_language()
        self.save_settings()

    def apply_language(self):
        self.setLayoutDirection(Qt.RightToLeft if self.code == 'ar' else Qt.LeftToRight)
        for field in (self.link, self.folder, self.language):
            field.setLayoutDirection(Qt.LeftToRight)
        for item, key in self.bound:
            item.setText(self.t(key))
        self.mode.setItemText(0, self.t('Standardprogramm für .nzb'))
        self.mode.setItemText(1, Path(self.downloader).name if self.downloader else self.t('Ausgewähltes Programm'))
        self.status.setText(self.t('Fertig' if self.last else 'Bereit'))
        if not self.log.toPlainText():
            self.log.setPlaceholderText(self.t('Hier erscheinen Suchstatus und Prüfergebnisse.'))

    def save_settings(self):
        data = dict(language=self.code, folder=self.folder.text(), downloader=self.downloader if self.mode.currentIndex() else '', auto_open=self.auto.isChecked())
        self.settings_path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.settings_path.with_suffix('.tmp')
        temporary.write_text(json.dumps(data, ensure_ascii=False), encoding='utf-8')
        temporary.replace(self.settings_path)

    def choose_folder(self):
        chosen = QFileDialog.getExistingDirectory(self, self.t('Ordner für NZB-Dateien'), self.folder.text())
        if chosen:
            self.folder.setText(chosen)

    def choose_downloader(self):
        chosen, _ = QFileDialog.getOpenFileName(self, self.t('NZB-Downloader auswählen'), '/usr/bin')
        if chosen:
            self.downloader = chosen
            self.mode.setCurrentIndex(1)
            self.apply_language()

    def error(self, text):
        QMessageBox.warning(self, 'NZB Hydra', self.t(text))

    def search(self):
        try:
            link = engine.parse_link(self.link.toPlainText())
            if not Path(self.folder.text()).is_absolute():
                raise ValueError('Bitte einen absoluten Speicherpfad auswählen.')
            self.save_settings()
        except (ValueError, OSError) as exc:
            self.error(str(exc))
            return
        self.last = ''
        self.log.clear()
        self.start.setEnabled(False)
        self.open.setEnabled(False)
        self.language.setEnabled(False)
        self.cancel.setEnabled(True)
        self.worker = Worker(link, self.folder.text())
        self.worker.progress.connect(self.report)
        self.worker.result.connect(self.done)
        self.worker.finished.connect(self.worker_finished)
        self.worker.start()

    def report(self, key, percent):
        message = self.t(key) + (str(percent) + ' %' if percent >= 0 else '')
        self.status.setText(message)
        self.log.appendPlainText(message)
        self.progress.setRange(0, 0 if percent < 0 else 100)
        if percent >= 0:
            self.progress.setValue(percent)

    def cancel_search(self):
        if self.worker:
            self.worker.stop.set()
            self.cancel.setEnabled(False)
            self.status.setText(self.t('Wird abgebrochen …'))

    def done(self, kind, value):
        self.progress.setRange(0,100)
        self.progress.setValue(100 if kind == 'ok' else 0)
        if kind == 'ok':
            self.last = value
            self.status.setText(self.t('Fertig'))
            self.log.appendPlainText(self.t('Gespeichert: ') + value)
            self.open.setEnabled(True)
            if self.auto.isChecked():
                self.open_nzb()
        else:
            self.status.setText(self.t('Abgebrochen' if kind == 'cancel' else 'NZB konnte nicht erstellt werden'))
            self.log.appendPlainText(self.t(value))

    def worker_finished(self):
        self.worker.deleteLater()
        self.worker = None
        self.start.setEnabled(True)
        self.language.setEnabled(True)
        self.cancel.setEnabled(False)

    def open_nzb(self):
        try:
            if not self.last or not Path(self.last).is_file():
                raise ValueError('Die NZB-Datei ist nicht mehr vorhanden.')
            if self.mode.currentIndex() == 1:
                if not self.downloader or not os.access(self.downloader, os.X_OK):
                    raise ValueError('Bitte zuerst eine gültige Downloader-Programmdatei auswählen.')
                subprocess.Popen([self.downloader, self.last], start_new_session=True)
            elif not QDesktopServices.openUrl(QUrl.fromLocalFile(self.last)):
                raise ValueError('NZB gespeichert, aber Öffnen fehlgeschlagen: ')
        except (OSError, ValueError) as exc:
            self.error(str(exc))

    def open_folder(self):
        path = Path(self.folder.text())
        if path.is_dir():
            QDesktopServices.openUrl(QUrl.fromLocalFile(str(path.resolve())))
        else:
            self.error('Der Ordner existiert noch nicht.')

    def associate(self):
        try:
            register()
            QMessageBox.information(self, 'NZB Hydra', self.t('NZBLNK-Links öffnen jetzt NZB Hydra. Bitte den Programmordner nicht verschieben. In Chrome gegebenenfalls das Öffnen bestätigen.'))
        except (OSError, subprocess.SubprocessError) as exc:
            self.error(type(exc).__name__)

    def closeEvent(self, event):
        if self.worker is not None:
            self.cancel_search()
            event.ignore()
            self.status.setText(self.t('Suche wird beendet. Danach kannst du das Fenster schließen.'))
        else:
            try:
                self.save_settings()
            except OSError as exc:
                self.error(str(exc))
            event.accept()

if __name__ == '__main__':
    app = QApplication(sys.argv)
    window = Window(next((s for s in sys.argv[1:] if s.lower().startswith('nzblnk:')), ''))
    window.show()
    if '--smoke-test' in sys.argv:
        for i, code in enumerate(CODES):
            window.language.setCurrentIndex(i)
            app.processEvents()
            assert window.start.text() == tr('NZB erstellen', code)
            assert (window.layoutDirection() == Qt.RightToLeft) == (code == 'ar')
            assert not QPixmap(str(BASE/'hydra.png')).isNull()
        print('PASS: packaged Linux executable, six languages, RTL, logo')
        window.close()
    else:
        sys.exit(app.exec())
