using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace NZBKestrel {
    // Independent implementation against NZBIndex's public web endpoints and NZB XML.
    public static class Engine {
        public static readonly string Base = AppDomain.CurrentDomain.BaseDirectory;
        static readonly XNamespace NzbNs = "http://www.newzbin.com/DTD/2003/nzb";
        public static string Quote(string value) {
            var b = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                b.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c); slashes = 0;
            }
            return b.Append('\\', slashes * 2).Append('"').ToString();
        }
        static byte[] Fetch(string url, Action<string> report, CancellationToken token, bool showProgress) {
            token.ThrowIfCancellationRequested();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET"; request.UserAgent = "NZBKestrel/2.1";
            request.Timeout = 30000; request.ReadWriteTimeout = 30000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            const int limit = 64 * 1024 * 1024;
            using (token.Register(() => request.Abort())) {
                try {
                    using (var response = (HttpWebResponse)request.GetResponse()) {
                        if (response.ResponseUri.Scheme != "https" || !(response.ResponseUri.Host.Equals("nzbindex.com", StringComparison.OrdinalIgnoreCase) || response.ResponseUri.Host.EndsWith(".nzbindex.com", StringComparison.OrdinalIgnoreCase))) throw new IOException(L.T("Unerwartete Weiterleitung des Suchdienstes."));
                        if (response.ContentLength > limit) throw new IOException(L.T("Die Antwort des Suchdienstes ist zu groß."));
                        using (var input = response.GetResponseStream()) using (var output = new MemoryStream()) {
                            byte[] buffer = new byte[32768]; int read, lastPercent = -1; long lastBytes = 0;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
                                token.ThrowIfCancellationRequested();
                                if (output.Length + read > limit) throw new IOException(L.T("Die Antwort des Suchdienstes ist zu groß."));
                                output.Write(buffer, 0, read);
                                if (showProgress && response.ContentLength > 0 && response.ContentEncoding.Length == 0) {
                                    int percent = (int)Math.Min(100, output.Length * 100 / response.ContentLength);
                                    if (percent >= lastPercent + 5) { report(L.T("NZB laden · ") + percent + " %"); lastPercent = percent; }
                                } else if (showProgress && output.Length >= lastBytes + 262144) { report(L.T("NZB laden · ") + (output.Length / 1024) + L.T(" KB empfangen")); lastBytes = output.Length; }
                            }
                            return output.ToArray();
                        }
                    }
                } catch (WebException ex) {
                    token.ThrowIfCancellationRequested();
                    if (ex.Status == WebExceptionStatus.Timeout) throw new IOException(L.T("Zeitüberschreitung beim Suchdienst. Bitte erneut versuchen."));
                    var response = ex.Response as HttpWebResponse;
                    if (response != null) { int code = (int)response.StatusCode; response.Dispose(); throw new IOException(L.T("Suchdienst antwortet mit HTTP ") + code + L.T(". Bitte später erneut versuchen.")); }
                    throw new IOException(L.T("Suchdienst nicht erreichbar. Bitte Internetverbindung prüfen."));
                } catch (IOException) { token.ThrowIfCancellationRequested(); throw; }
            }
        }
        public static List<Dictionary<string, object>> Candidates(string json, LinkInfo link) {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            var root = serializer.Deserialize<Dictionary<string, object>>(json);
            if (root == null || (root.ContainsKey("error") && Convert.ToBoolean(root["error"]))) throw new IOException(L.T("Der Suchdienst konnte die Anfrage nicht verarbeiten."));
            var data = root["data"] as Dictionary<string, object>;
            if (data == null || !data.ContainsKey("content")) throw new IOException(L.T("Unbekanntes Antwortformat des Suchdienstes."));
            var output = new List<Dictionary<string, object>>();
            foreach (var item in (IEnumerable)data["content"]) {
                var result = item as Dictionary<string, object>;
                if (result == null || !result.ContainsKey("name") || !result.ContainsKey("id")) continue;
                Guid id;
                if (!Guid.TryParse(Convert.ToString(result["id"]), out id)) continue;
                if (Convert.ToString(result["name"]).IndexOf(link.Header, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!result.ContainsKey("complete") || !Convert.ToBoolean(result["complete"])) continue;
                if (link.Groups.Length > 0) {
                    if (!result.ContainsKey("groups")) continue;
                    var groups = ((IEnumerable)result["groups"]).Cast<object>().Select(Convert.ToString);
                    if (!link.Groups.Intersect(groups, StringComparer.OrdinalIgnoreCase).Any()) continue;
                }
                output.Add(result);
            }
            return output.OrderByDescending(r => r.ContainsKey("size") ? Convert.ToInt64(r["size"]) : 0).ToList();
        }
        public static XDocument ReadNzb(byte[] bytes) {
            using (var stream = new MemoryStream(bytes)) using (var reader = XmlReader.Create(stream, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024
            })) {
                var doc = XDocument.Load(reader);
                if (doc.Root == null || doc.Root.Name.LocalName != "nzb" || !(doc.Root.Name.Namespace == NzbNs || doc.Root.Name.NamespaceName == "https://www.newzbin.com/DTD/2003/nzb")) throw new IOException(L.T("Die Antwort ist keine standardkonforme NZB-Datei."));
                XNamespace source = doc.Root.Name.Namespace;
                foreach (var element in doc.Root.DescendantsAndSelf()) {
                    if (element.Name.Namespace != source) throw new IOException(L.T("Unerwartetes XML-Format innerhalb der NZB."));
                    element.Name = NzbNs + element.Name.LocalName;
                    element.Attributes().Where(a => a.IsNamespaceDeclaration).Remove();
                }
                doc.Root.SetAttributeValue("xmlns", NzbNs.NamespaceName);
                if (doc.DocumentType != null) doc.DocumentType.Remove();
                return doc;
            }
        }
        public static int CheckNzb(XDocument doc, string header, Action<string> report, CancellationToken token) {
            var files = doc.Root.Elements(NzbNs + "file").ToList();
            if (files.Count == 0) throw new IOException(L.T("Die NZB enthält keine Dateien."));
            int expectedFiles = 0, previousPercent = -1;
            for (int i = 0; i < files.Count; i++) {
                token.ThrowIfCancellationRequested(); var file = files[i];
                string subject = (string)file.Attribute("subject") ?? "";
                if (header != null && subject.IndexOf(header, StringComparison.OrdinalIgnoreCase) < 0) throw new IOException(L.T("NZB enthält Dateien, die nicht zum Suchschlüssel passen."));
                if (string.IsNullOrWhiteSpace((string)file.Attribute("poster"))) throw new IOException(L.T("NZB-Datei ohne Poster."));
                long date; if (!long.TryParse((string)file.Attribute("date"), out date) || date < 0) throw new IOException(L.T("Ungültiges Artikeldatum."));
                var groups = file.Element(NzbNs + "groups");
                if (groups == null || !groups.Elements(NzbNs + "group").Any(g => !string.IsNullOrWhiteSpace(g.Value))) throw new IOException(L.T("NZB-Datei ohne Newsgroup."));
                var list = file.Element(NzbNs + "segments");
                if (list == null) throw new IOException(L.T("NZB-Datei ohne Segmente."));
                var numbers = new HashSet<int>();
                foreach (var segment in list.Elements(NzbNs + "segment")) {
                    token.ThrowIfCancellationRequested(); int number; long size;
                    if (!int.TryParse((string)segment.Attribute("number"), out number) || number < 1 ||
                        !long.TryParse((string)segment.Attribute("bytes"), out size) || size < 1 ||
                        string.IsNullOrWhiteSpace(segment.Value) || segment.Value.Any(char.IsWhiteSpace) || !numbers.Add(number))
                        throw new IOException(L.T("Ungültige oder doppelte NZB-Segmente."));
                }
                if (numbers.Count == 0 || numbers.Max() != numbers.Count) throw new IOException(L.T("Mindestens eine Datei hat fehlende NZB-Segmente."));
                var parts = Regex.Match(subject, @"\((\d+)/(\d+)\)\s*$");
                int expected;
                if (parts.Success && int.TryParse(parts.Groups[2].Value, out expected) && expected != numbers.Count) throw new IOException(L.T("Die NZB ist unvollständig: Segmente fehlen."));
                var fileTotal = Regex.Match(subject, @"\[\d+/(\d+)\]");
                if (fileTotal.Success && int.TryParse(fileTotal.Groups[1].Value, out expected)) expectedFiles = Math.Max(expectedFiles, expected);
                int percent = (i + 1) * 100 / files.Count;
                if (percent >= previousPercent + 10 || percent == 100) { report(L.T("Dateien prüfen · ") + percent + " %"); previousPercent = percent; }
            }
            if (expectedFiles > files.Count) throw new IOException(L.T("Die NZB ist unvollständig: Dateien fehlen."));
            return files.Count;
        }
        public static int ValidateNzb(string file) { return CheckNzb(ReadNzb(File.ReadAllBytes(file)), null, s => {}, CancellationToken.None); }
        public static void Metadata(XDocument doc, LinkInfo link) {
            var head = doc.Root.Element(NzbNs + "head");
            if (head == null) { head = new XElement(NzbNs + "head"); doc.Root.AddFirst(head); }
            head.Elements(NzbNs + "meta").Where(m => (string)m.Attribute("type") == "title" || (string)m.Attribute("type") == "password").Remove();
            head.Add(new XElement(NzbNs + "meta", new XAttribute("type", "title"), link.Title));
            if (!string.IsNullOrEmpty(link.Password)) head.Add(new XElement(NzbNs + "meta", new XAttribute("type", "password"), link.Password));
            foreach (var segments in doc.Descendants(NzbNs + "segments")) segments.ReplaceNodes(segments.Elements(NzbNs + "segment").OrderBy(s => (int)s.Attribute("number")).ToArray());
        }
        public static string SafeName(string title) {
            string result = new string(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (result.Length > 140) result = result.Substring(0,140).TrimEnd('.',' ');
            if (result.Length == 0) result = "Download";
            if (Regex.IsMatch(result, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) result = "_" + result;
            return result;
        }
        public static string Save(XDocument doc, string title, string folder, CancellationToken token) {
            Directory.CreateDirectory(folder);
            string temporary = Path.Combine(folder, ".nzbstudio-" + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true })) doc.Save(writer);
                token.ThrowIfCancellationRequested(); string name = SafeName(title);
                for (int n = 1; n < 10000; n++) {
                    token.ThrowIfCancellationRequested();
                    string target = Path.Combine(folder, name + (n == 1 ? "" : " (" + n + ")") + ".nzb");
                    try { File.Move(temporary, target); return target; } catch (IOException) { if (!File.Exists(target)) throw; }
                }
                throw new IOException(L.T("Zu viele Dateien mit demselben Namen im Zielordner."));
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static string Run(LinkInfo link, string destination, Action<string> report, CancellationToken token) {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                try {
                    var candidates = new List<Dictionary<string, object>>();
                    for (int page = 0; page < 5; page++) {
                        report(L.T("NZBIndex durchsuchen · Seite ") + (page + 1));
                        string json = Encoding.UTF8.GetString(Fetch("https://nzbindex.com/api/search?q=" + Uri.EscapeDataString(link.Header) + "&max=100&page=" + page, report, timeout.Token, false));
                        candidates.AddRange(Candidates(json, link));
                        var root = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
                        var data = (Dictionary<string,object>)root["data"];
                        var paging = (Dictionary<string,object>)data["page"];
                        if (page + 1 >= Convert.ToInt32(paging["totalPages"]) || candidates.Count > 0) break;
                    }
                    if (candidates.Count == 0) throw new IOException(L.T("Kein vollständiger Treffer bei NZBIndex. Die Version unterstützt derzeit diesen Suchdienst; nicht jeder Beitrag ist dort indexiert."));
                    foreach (var candidate in candidates.Take(10)) {
                        report(L.T("Passenden Treffer laden …"));
                        var bytes = Fetch("https://nzbindex.com/download/" + Convert.ToString(candidate["id"]) + ".nzb", report, timeout.Token, true);
                        XDocument doc = ReadNzb(bytes); int count;
                        try { count = CheckNzb(doc, link.Header, report, timeout.Token); }
                        catch (IOException) { report(L.T("Treffer passt nicht oder ist unvollständig. Nächsten prüfen …")); continue; }
                        Metadata(doc, link); report(L.T("Geprüfte NZB speichern …"));
                        string result = Save(doc, link.Title, destination, timeout.Token);
                        report(L.T("Gespeichert · ") + count + L.T(" Dateien · Standard-NZB")); return result;
                    }
                    throw new IOException(L.T("Die gefundenen NZBs haben die Vollständigkeitsprüfung nicht bestanden."));
                } catch (OperationCanceledException) { if (token.IsCancellationRequested) throw; throw new IOException(L.T("Die Suche wurde nach drei Minuten beendet. Bitte erneut versuchen.")); }
            }
        }
    }
}
