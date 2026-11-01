"""Keep dependency notices with the portable release."""
from pathlib import Path
import importlib.metadata
import shutil
import sysconfig

target = Path('dist/nzb-hydra/third-party-licenses')
target.mkdir(parents=True, exist_ok=True)
shutil.copytree('upstream-licenses', target / 'Qt-PySide', dirs_exist_ok=True)
for name in ('PySide6-Essentials', 'shiboken6', 'pyinstaller'):
    dist = importlib.metadata.distribution(name)
    for entry in dist.files or []:
        if any(word in str(entry).lower() for word in ('license', 'copying', 'copyright')):
            source = Path(dist.locate_file(entry))
            if source.is_file():
                output = target / name / str(entry).replace('..', '_')
                output.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, output)
python_license = Path(sysconfig.get_path('stdlib')) / 'LICENSE.txt'
if python_license.exists():
    shutil.copy2(python_license, target/'Python-LICENSE.txt')
else:
    raise RuntimeError('Python license file missing')
