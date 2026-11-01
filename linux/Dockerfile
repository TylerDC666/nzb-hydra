FROM python:3.12-slim-bookworm
RUN apt-get update && apt-get install -y --no-install-recommends binutils libgl1 libegl1 libglib2.0-0 libdbus-1-3 libxkbcommon0 libxkbcommon-x11-0 libxcb-cursor0 libxcb-icccm4 libxcb-keysyms1 libxcb-shape0 libxcb-xinerama0 libxcb-randr0 libxcb-render-util0 libxcb-image0 libfontconfig1 fonts-noto-core && rm -rf /var/lib/apt/lists/*
WORKDIR /build
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
ENV QT_QPA_PLATFORM=offscreen
RUN python -m unittest -v test_engine test_ui
RUN pyinstaller --noconfirm --clean --name nzb-kestrel --onedir --add-data 'translations.json:.' --add-data 'kestrel.png:.' kestrel.py
RUN python collect_licenses.py && XDG_CONFIG_HOME=/tmp/kestrel-smoke ./dist/nzb-kestrel/nzb-kestrel --smoke-test
CMD ["sh", "-c", "tar -czf /out/NZB-Kestrel-v2.1.0-Linux-x86_64.tar.gz -C dist nzb-kestrel -C /build README-Linux.md QUICKSTART.md THIRD-PARTY-NOTICES.txt"]
