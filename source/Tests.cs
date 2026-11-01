using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace NZBKestrel {
    public static class Tests {
        static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); }
        static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Assert(rejected, name); }
        public static void Run() {
            L.Validate();
            string initialLanguage = L.Code;
            using (var form = new MainForm(null)) {
                foreach (string language in L.Codes) {
                    L.Code = language; form.ApplyLanguage();
                    Assert(form.RightToLeftLayout == (language == "ar"), "RTL layout " + language);
                    CheckTranslatedButtons(form);
                    try { LinkInfo.Parse("invalid"); throw new Exception("Validation missing"); }
                    catch (ArgumentException ex) { Assert(ex.Message == L.T("Der Link muss mit nzblnk:? oder nzblnk://? beginnen."), "Translated input error " + language); }
                }
            }
            L.Code = initialLanguage;
            var link = LinkInfo.Parse("nzblnk:?h=abc&t=A%26B&p=p%26%3C%22&g=a.b.test");
            Assert(link.Header == "abc" && link.Title == "A&B" && link.Password == "p&<\"" && link.Groups[0] == "alt.binaries.test", "Link decoding");
            foreach (var bad in new [] { "https://example.com", "nzblnk:?t=no", "nzblnk:?h=a&h=b", "nzblnk:?h=a\n--register" }) Reject(() => LinkInfo.Parse(bad), "Input validation");
            var json = "{\"data\":{\"content\":[{\"id\":\"11111111-2222-3333-4444-555555555555\",\"name\":\"abc\",\"complete\":true,\"size\":3,\"groups\":[\"alt.binaries.test\"]}]},\"error\":false}";
            Assert(Engine.Candidates(json, link).Count == 1, "Candidate selection");
            Assert(Engine.Candidates(json.Replace("true", "false"), link).Count == 0, "Reject incomplete index result");
            Assert(Engine.Candidates(json.Replace("alt.binaries.test", "wrong.group"), link).Count == 0, "Group matching");
            string xml = "<?xml version=\"1.0\"?><!DOCTYPE nzb SYSTEM \"file:///this-must-not-be-read.dtd\"><nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\"><file poster=\"a@b\" date=\"1\" subject=\"[001/001] abc (1/2)\"><groups><group>alt.binaries.test</group></groups><segments><segment bytes=\"5\" number=\"1\">a@b</segment><segment bytes=\"6\" number=\"2\">c@d</segment></segments></file></nzb>";
            var doc = Engine.ReadNzb(Encoding.UTF8.GetBytes(xml));
            Assert(Engine.ReadNzb(Encoding.UTF8.GetBytes(xml.Replace("http://www.newzbin", "https://www.newzbin"))).Root.Name.NamespaceName == "http://www.newzbin.com/DTD/2003/nzb", "Normalize HTTPS namespace");
            Assert(Engine.CheckNzb(doc, "abc", s => {}, CancellationToken.None) == 1, "XML validation without external DTD");
            Engine.Metadata(doc, link); XNamespace ns = "http://www.newzbin.com/DTD/2003/nzb";
            Assert(doc.Root.Element(ns + "head").Elements(ns + "meta").Single(m => (string)m.Attribute("type") == "password").Value == link.Password, "Password roundtrip");
            Assert(doc.Descendants(ns + "segment").Count() == 2, "Segments preserved");
            Reject(() => Engine.CheckNzb(Engine.ReadNzb(Encoding.UTF8.GetBytes(xml.Replace("number=\"2\"", "number=\"3\""))), null, s => {}, CancellationToken.None), "Missing segment");
            Reject(() => Engine.CheckNzb(Engine.ReadNzb(Encoding.UTF8.GetBytes(xml.Replace("number=\"2\"", "number=\"1\""))), null, s => {}, CancellationToken.None), "Duplicate segment");
            Reject(() => Engine.CheckNzb(Engine.ReadNzb(Encoding.UTF8.GetBytes(xml.Replace("[001/001]", "[001/002]"))), null, s => {}, CancellationToken.None), "Missing file");
            Reject(() => Engine.CheckNzb(doc, "wrong", s => {}, CancellationToken.None), "Wrong header");
            Assert(Engine.SafeName("CON.txt") == "_CON.txt" && !Engine.SafeName("../../bad").Contains('/'), "Safe filenames");
            string folder = Path.Combine(Path.GetTempPath(), "NZBKestrel-Test-" + Guid.NewGuid().ToString("N"));
            try {
                string one = Engine.Save(doc, "Example", folder, CancellationToken.None), two = Engine.Save(doc, "Example", folder, CancellationToken.None);
                Assert(one != two && File.Exists(one) && File.Exists(two), "Do not overwrite");
                Assert(Engine.ValidateNzb(two) == 1, "Saved NZB roundtrip");
                var cancelled = new CancellationToken(true);
                Reject(() => Engine.Save(doc, "cancelled", folder, cancelled), "Cancellation");
                Assert(Directory.GetFiles(folder).Length == 2, "Cancelled save cleanup");
            } finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }
        static void CheckTranslatedButtons(System.Windows.Forms.Control parent) {
            foreach (System.Windows.Forms.Control child in parent.Controls) {
                if (child is System.Windows.Forms.Button && child.Tag is string) Assert(child.Text == L.T((string)child.Tag), "Button translation");
                CheckTranslatedButtons(child);
            }
        }
    }
}
