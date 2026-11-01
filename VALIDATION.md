# Validation

- Windows build: compiled with the .NET Framework compiler; offline self-test passed, including all six languages and Arabic layout.
- Linux x86-64: built in a Debian 12 container with Python 3.12 and Qt/PySide 6.8.3. Eight engine tests and three UI/integration tests passed.
- Linux engine tests cover parsing, invalid input, candidate filtering, XML/segment validation, metadata, sorted segments, collision-free atomic export, cancellation, redirect restrictions and a mocked end-to-end search/export.
- Linux UI tests cover all six languages, Arabic direction, persisted language across restart, legacy settings migration and desktop registration with a mocked system command.
- The packaged Linux executable passed an offscreen startup check for all six languages, Arabic direction and the embedded logo. French and Arabic layouts were visually inspected.
- Live NZBIndex requests and actual Chrome/downloader handoff on a Linux desktop were not exercised. These depend on the target desktop, its file associations and the external search service.
