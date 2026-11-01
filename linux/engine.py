"""Independent NZBLNK/NZB implementation; no GUI or downloader dependency."""
import json
import os
from pathlib import Path
import re
import tempfile
import time
import urllib.parse
import urllib.request
import uuid
import xml.etree.ElementTree as ET
from dataclasses import dataclass

NS = 'http://www.newzbin.com/DTD/2003/nzb'
ET.register_namespace('', NS)
LIMIT = 64 * 1024 * 1024

class Cancelled(Exception):
    pass

class InvalidNzb(ValueError):
    pass

@dataclass
class Link:
    title: str
    header: str
    password: str
    groups: list

def parse_link(value):
    value = value.strip()
    if len(value) > 16000 or any(ord(c) < 32 for c in value):
        raise ValueError('Bitte genau einen vollständigen NZBLNK-Link einfügen.')
    if not value.lower().startswith(('nzblnk:?', 'nzblnk://?')) or '#' in value:
        raise ValueError('Der Link muss mit nzblnk:? oder nzblnk://? beginnen.')
    q = urllib.parse.parse_qs(value.split('?', 1)[1], keep_blank_values=True)
    for key in ('h', 't', 'p', 'd'):
        if len(q.get(key, [])) > 1:
            raise ValueError('Der Link enthält einen Parameter mehrfach: ')
    header = q.get('h', [''])[0].strip()
    if not header:
        raise ValueError('Im Link fehlt der Suchschlüssel (h). Bitte den vollständigen Link kopieren.')
    groups = re.split(r'[,;\s]+', ','.join(q.get('g', [])))
    return Link(q.get('t', [''])[0] or header, header, q.get('p', [''])[0],
                [g.replace('a.b.', 'alt.binaries.', 1) if g.startswith('a.b.') else g for g in groups if g])

def candidates(root, link):
    if not isinstance(root, dict) or root.get('error') or not isinstance(root.get('data'), dict):
        raise ValueError('Unbekanntes Antwortformat des Suchdienstes.')
    rows = root['data'].get('content')
    if not isinstance(rows, list):
        raise ValueError('Unbekanntes Antwortformat des Suchdienstes.')
    result = []
    for row in rows:
        if not isinstance(row, dict):
            continue
        try:
            uuid.UUID(str(row.get('id', '')))
        except ValueError:
            continue
        if link.header.casefold() not in str(row.get('name', '')).casefold() or row.get('complete') is not True:
            continue
        if link.groups and not {g.casefold() for g in link.groups}.intersection(str(g).casefold() for g in row.get('groups', [])):
            continue
        result.append(row)
    return sorted(result, key=lambda r: int(r.get('size', 0)), reverse=True)

def validate(data, link, check=lambda: None, progress=lambda *a: None):
    # Reject entity declarations, while permitting the standard NZB external DOCTYPE.
    if len(data) > LIMIT or b'<!ENTITY' in data.upper():
        raise InvalidNzb('Unerwartetes XML-Format innerhalb der NZB.')
    try:
        root = ET.fromstring(data)
    except ET.ParseError as exc:
        raise InvalidNzb('Die Antwort ist keine standardkonforme NZB-Datei.') from exc
    if root.tag not in ('{' + NS + '}nzb', '{https://www.newzbin.com/DTD/2003/nzb}nzb'):
        raise InvalidNzb('Die Antwort ist keine standardkonforme NZB-Datei.')
    source = root.tag[:-3]
    for element in root.iter():
        if not element.tag.startswith(source):
            raise InvalidNzb('Unerwartetes XML-Format innerhalb der NZB.')
        element.tag = '{' + NS + '}' + element.tag[len(source):]
    tag = lambda s: '{' + NS + '}' + s
    files = root.findall(tag('file'))
    if not files:
        raise InvalidNzb('Die NZB enthält keine Dateien.')
    expected_files = 0
    for i, file in enumerate(files):
        check()
        subject = file.get('subject', '')
        if link.header.casefold() not in subject.casefold():
            raise InvalidNzb('NZB enthält Dateien, die nicht zum Suchschlüssel passen.')
        if not file.get('poster', '').strip():
            raise InvalidNzb('NZB-Datei ohne Poster.')
        if not file.get('date', '').isdigit():
            raise InvalidNzb('Ungültiges Artikeldatum.')
        if not any((g.text or '').strip() for g in file.findall(tag('groups') + '/' + tag('group'))):
            raise InvalidNzb('NZB-Datei ohne Newsgroup.')
        segments = file.find(tag('segments'))
        if segments is None:
            raise InvalidNzb('NZB-Datei ohne Segmente.')
        numbers = set()
        for seg in segments:
            check()
            try:
                n, size = int(seg.get('number', '')), int(seg.get('bytes', ''))
            except ValueError:
                raise InvalidNzb('Ungültige oder doppelte NZB-Segmente.')
            if seg.tag != tag('segment') or n < 1 or size < 1 or n in numbers or not seg.text or any(c.isspace() for c in seg.text):
                raise InvalidNzb('Ungültige oder doppelte NZB-Segmente.')
            numbers.add(n)
        if not numbers or max(numbers) != len(numbers):
            raise InvalidNzb('Mindestens eine Datei hat fehlende NZB-Segmente.')
        match = re.search(r'\((\d+)/(\d+)\)\s*$', subject)
        if match and int(match[2]) != len(numbers):
            raise InvalidNzb('Die NZB ist unvollständig: Segmente fehlen.')
        match = re.search(r'\[\d+/(\d+)\]', subject)
        if match:
            expected_files = max(expected_files, int(match[1]))
        segments[:] = sorted(segments, key=lambda s: int(s.get('number')))
        progress('Dateien prüfen · ', (i + 1) * 100 // len(files))
    if expected_files > len(files):
        raise InvalidNzb('Die NZB ist unvollständig: Dateien fehlen.')
    head = root.find(tag('head'))
    if head is None:
        head = ET.Element(tag('head'))
        root.insert(0, head)
    for meta in list(head):
        if meta.get('type') in ('title', 'password'):
            head.remove(meta)
    ET.SubElement(head, tag('meta'), {'type': 'title'}).text = link.title
    if link.password:
        ET.SubElement(head, tag('meta'), {'type': 'password'}).text = link.password
    return ET.tostring(root, encoding='utf-8', xml_declaration=True)

def save(data, title, directory, check=lambda: None):
    directory = Path(directory).expanduser().resolve()
    directory.mkdir(parents=True, exist_ok=True)
    name = re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', title).strip().rstrip('.')
    # Leave enough bytes for a numeric suffix on filesystems with 255-byte names.
    name = name.encode('utf-8')[:180].decode('utf-8', 'ignore') or 'Download'
    fd, temporary = tempfile.mkstemp(prefix='.nzb-hydra-', dir=directory)
    try:
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        for n in range(1, 10000):
            check()
            target = directory / (name + ('' if n == 1 else f' ({n})') + '.nzb')
            try:
                # Atomic publication without replacing an existing file.
                os.link(temporary, target)
                return str(target)
            except FileExistsError:
                continue
        raise ValueError('Zu viele Dateien mit demselben Namen im Zielordner.')
    finally:
        os.unlink(temporary)

class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        url = urllib.parse.urlsplit(newurl)
        if url.scheme != 'https' or not (url.hostname == 'nzbindex.com' or (url.hostname or '').endswith('.nzbindex.com')):
            raise ValueError('Unerwartete Weiterleitung des Suchdienstes.')
        return super().redirect_request(req, fp, code, msg, headers, newurl)

def run(link, directory, stop, progress):
    deadline = time.monotonic() + 180
    def check():
        if stop.is_set():
            raise Cancelled()
        if time.monotonic() > deadline:
            raise TimeoutError('Die Suche wurde nach drei Minuten beendet. Bitte erneut versuchen.')
    def fetch(url, show=False):
        check()
        req = urllib.request.Request(url, headers={'User-Agent': 'NZBHydra/2.0'})
        with urllib.request.build_opener(SafeRedirect()).open(req, timeout=min(20, max(1, deadline-time.monotonic()))) as response:
            length = int(response.headers.get('Content-Length', '0'))
            if length > LIMIT:
                raise ValueError('Die Antwort des Suchdienstes ist zu groß.')
            result = bytearray()
            while True:
                check()
                chunk = response.read(32768)
                if not chunk:
                    break
                result.extend(chunk)
                if len(result) > LIMIT:
                    raise ValueError('Die Antwort des Suchdienstes ist zu groß.')
                if show:
                    progress('NZB laden · ', min(100, len(result)*100//length) if length else -1)
            return bytes(result)
    found = []
    for page in range(5):
        progress('NZBIndex durchsuchen · Seite ', -1)
        root = json.loads(fetch('https://nzbindex.com/api/search?' + urllib.parse.urlencode({'q': link.header, 'max': 100, 'page': page})))
        found.extend(candidates(root, link))
        if found or page+1 >= int(root['data']['page']['totalPages']):
            break
    if not found:
        raise ValueError('Kein vollständiger Treffer bei NZBIndex. Die Version unterstützt derzeit diesen Suchdienst; nicht jeder Beitrag ist dort indexiert.')
    for row in found[:10]:
        progress('Passenden Treffer laden …', -1)
        data = fetch('https://nzbindex.com/download/' + str(uuid.UUID(row['id'])) + '.nzb', True)
        try:
            data = validate(data, link, check, progress)
        except InvalidNzb:
            progress('Treffer passt nicht oder ist unvollständig. Nächsten prüfen …', -1)
            continue
        progress('Geprüfte NZB speichern …', -1)
        return save(data, link.title, directory, check)
    raise ValueError('Die gefundenen NZBs haben die Vollständigkeitsprüfung nicht bestanden.')
