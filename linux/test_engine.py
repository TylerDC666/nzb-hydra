import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch
import engine

XML = b'''<?xml version="1.0"?><nzb xmlns="http://www.newzbin.com/DTD/2003/nzb"><file poster="test" date="1" subject="abc [1/1] (1/2)"><groups><group>alt.binaries.test</group></groups><segments><segment bytes="4" number="2">second@example</segment><segment bytes="4" number="1">first@example</segment></segments></file></nzb>'''

class Tests(unittest.TestCase):
    def setUp(self):
        self.link = engine.parse_link('nzblnk:?h=abc&t=Test%26Name&p=p%26%3C&g=a.b.test')

    def test_link(self):
        self.assertEqual(self.link.title, 'Test&Name')
        self.assertEqual(self.link.groups, ['alt.binaries.test'])
        for bad in ['https://example.com', 'nzblnk:?t=x', 'nzblnk:?h=a&h=b', 'nzblnk:?h=a\nxx']:
            with self.assertRaises(ValueError):
                engine.parse_link(bad)

    def test_xml_metadata_sorting(self):
        result = engine.validate(XML, self.link)
        self.assertIn(b'p&amp;&lt;', result)
        self.assertLess(result.index(b'number="1"'), result.index(b'number="2"'))
        self.assertIn(b'Test&amp;Name', result)
        self.assertIn(b'http://www.newzbin.com', engine.validate(XML.replace(b'http:', b'https:'), self.link))

    def test_reject_corruption(self):
        for bad in [XML.replace(b'number="2"', b'number="1"'), XML.replace(b'number="2"', b'number="3"'),
                    XML.replace(b'(1/2)', b'(1/3)'), XML.replace(b'[1/1]', b'[1/3]'),
                    XML.replace(b'abc', b'wrong'), XML.replace(b'<group>alt.binaries.test</group>', b''),
                    XML.replace(b'bytes="4"', b'bytes="0"'), b'<!DOCTYPE nzb [<!ENTITY x "boom">]>' + XML]:
            with self.assertRaises(engine.InvalidNzb):
                engine.validate(bad, self.link)

    def test_candidate_filtering(self):
        row = dict(id='11111111-2222-3333-4444-555555555555', name='abc', complete=True, groups=['alt.binaries.test'])
        self.assertEqual(len(engine.candidates({'data': {'content': [row]}}, self.link)), 1)
        row['id'] = '../path'
        self.assertFalse(engine.candidates({'data': {'content': [row]}}, self.link))

    def test_atomic_save_and_cancel(self):
        with tempfile.TemporaryDirectory() as directory:
            first = engine.save(b'first', 'a/b', directory)
            second = engine.save(b'second', 'a/b', directory)
            self.assertNotEqual(first, second)
            self.assertEqual(Path(first).read_bytes(), b'first')
            def cancel():
                raise engine.Cancelled()
            with self.assertRaises(engine.Cancelled):
                engine.save(b'cancelled', 'cancel', directory, cancel)
            self.assertEqual(len(list(Path(directory).iterdir())), 2)

    def test_cancelled_request(self):
        stop = threading.Event()
        stop.set()
        with self.assertRaises(engine.Cancelled):
            engine.run(self.link, '.', stop, lambda *a: None)

    def test_redirect_boundary(self):
        with self.assertRaises(ValueError):
            engine.SafeRedirect().redirect_request(None, None, 302, '', {}, 'https://evil.example/')

    def test_mocked_search_export(self):
        import io
        rows = {'data': {'content': [dict(id='11111111-2222-3333-4444-555555555555', name='abc', complete=True, groups=['alt.binaries.test'])], 'page': {'totalPages': 1}}}
        class Response(io.BytesIO):
            headers = {}
        class Opener:
            def open(self, request, timeout):
                return Response(json.dumps(rows).encode() if '/api/search?' in request.full_url else XML)
        with tempfile.TemporaryDirectory() as directory, patch('urllib.request.build_opener', return_value=Opener()):
            result = engine.run(self.link, directory, threading.Event(), lambda *a: None)
            self.assertIn(b'Test&amp;Name', Path(result).read_bytes())

if __name__ == '__main__':
    unittest.main()
