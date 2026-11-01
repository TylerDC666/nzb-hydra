using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace NZBHydra {
    public class LinkInfo {
        public string Link, Title, Header, Password;
        public string[] Groups;
        public static LinkInfo Parse(string input) {
            string value = input.Trim();
            if (value.Length > 16000 || value.Any(c => char.IsControl(c))) throw new ArgumentException(L.T("Bitte genau einen vollständigen NZBLNK-Link einfügen."));
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || !string.Equals(uri.Scheme, "nzblnk", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(L.T("Der Link muss mit nzblnk:? oder nzblnk://? beginnen."));
            int queryStart = value.IndexOf('?');
            if (queryStart < 0 || value.Contains("#")) throw new ArgumentException(L.T("Der Link enthält keine gültigen Suchparameter."));
            var query = HttpUtility.ParseQueryString(value.Substring(queryStart + 1));
            if (string.IsNullOrWhiteSpace(query["h"])) throw new ArgumentException(L.T("Im Link fehlt der Suchschlüssel (h). Bitte den vollständigen Link kopieren."));
            foreach (string key in new [] { "h", "t", "p", "d" })
                if (query.GetValues(key) != null && query.GetValues(key).Length != 1) throw new ArgumentException(L.T("Der Link enthält einen Parameter mehrfach: ") + key);
            return new LinkInfo { Link = "nzblnk://?" + value.Substring(queryStart + 1), Title = string.IsNullOrWhiteSpace(query["t"]) ? query["h"] : query["t"], Header = query["h"].Trim(), Password = query["p"] ?? "", Groups = (query["g"] ?? "").Split(new [] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(g => g.StartsWith("a.b.") ? "alt.binaries." + g.Substring(4) : g).ToArray() };
        }
    }

    public class MainForm : Form {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            try { int dark = 1, black = 0, light = 0x00eeeeee; DwmSetWindowAttribute(Handle,20,ref dark,4); DwmSetWindowAttribute(Handle,35,ref black,4); DwmSetWindowAttribute(Handle,36,ref light,4); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
        TextBox input, folder, log;
        Label status, elapsed, title;
        HydraProgress progress;
        Button start, cancel, open, browse, paste, associate;
        CheckBox autoOpen;
        ComboBox languagePicker;
        ComboBox downloader; Button chooseDownloader; string downloaderPath = "";
        readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NZBHydra", "settings.xml");
        CancellationTokenSource cancellation;
        readonly Stopwatch watch = new Stopwatch();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 };
        string lastFile;
        readonly Color ink = Color.FromArgb(242, 238, 239), muted = Color.FromArgb(169, 155, 158), panel = Color.FromArgb(24, 21, 23), accent = Color.FromArgb(214, 42, 62);
        public MainForm(string initial) {
            SuspendLayout();
            Text = "NZB Hydra"; Size = new Size(940, 900); MinimumSize = new Size(870, 840);
            StartPosition = FormStartPosition.CenterScreen; BackColor = Color.Black; ForeColor = ink;
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 13 };
            int[] heights = { 96, 12, 28, 92, 38, 26, 38, 82, 50, 34, 22, 0, 30 };
            foreach (int h in heights) grid.RowStyles.Add(h == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, h));
            Controls.Add(grid);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            var logo = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 2, 18, 2), BackColor = Color.Black };
            using (var asset = typeof(MainForm).Assembly.GetManifestResourceStream("hydra.png")) using (var original = Image.FromStream(asset)) logo.Image = new Bitmap(original);
            using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("hydra.ico")) Icon = new Icon(iconStream);
            header.Controls.Add(logo, 0, 0); var name = Label("NZB HYDRA", 29, ink); name.Font = new Font("Segoe UI Semibold", 29, FontStyle.Bold); header.Controls.Add(name, 1, 0);
            var languages = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(8,18,0,0), RightToLeft = RightToLeft.No };
            languages.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); languages.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            languages.Controls.Add(Label("Sprache", 9, muted), 0, 0);
            languagePicker = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = panel, ForeColor = ink, RightToLeft = RightToLeft.No };
            languagePicker.Items.AddRange(L.Names); languages.Controls.Add(languagePicker, 0, 1); header.Controls.Add(languages, 2, 0);
            grid.Controls.Add(header, 0, 0);
            grid.Controls.Add(Label("NZBLNK-LINK", 9, accent), 0, 2);
            input = Box(true); input.ScrollBars = ScrollBars.Vertical; input.Text = initial ?? ""; grid.Controls.Add(input, 0, 3);
            var linkActions = Flow(); paste = Button("Aus Zwischenablage", 215, false);
            paste.Click += (s,e) => { try { if (Clipboard.ContainsText()) input.Text = Clipboard.GetText().Trim(); } catch (Exception ex) { ShowError(ex.Message); } };
            linkActions.Controls.Add(paste); title = Label("Bereit für deinen Link", 9, muted); title.AutoSize = true; title.MaximumSize = new Size(520, 30); title.Margin = new Padding(10, 8, 0, 0); linkActions.Controls.Add(title);
            grid.Controls.Add(linkActions, 0, 4);
            grid.Controls.Add(Label("SPEICHERORT", 9, accent), 0, 5);
            var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            folder = Box(false); folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "nzb");
            browse = Button("Auswählen", 105, false); browse.Click += (s,e) => { using (var dialog = new FolderBrowserDialog { SelectedPath = folder.Text, Description = L.T("Ordner für NZB-Dateien") }) if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
            folderRow.Controls.Add(folder, 0, 0); folderRow.Controls.Add(browse, 1, 0); grid.Controls.Add(folderRow, 0, 6);
            var delivery = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            delivery.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); delivery.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            autoOpen = new CheckBox { FlatStyle = FlatStyle.Flat, ForeColor = ink, Tag = "Nach dem Speichern an den Downloader übergeben", Text = L.T("Nach dem Speichern an den Downloader übergeben"), Dock = DockStyle.Fill, Checked = true, AutoSize = true };
            delivery.Controls.Add(autoOpen, 0, 0); var targetRow = Flow();
            downloader = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = panel, ForeColor = ink, Width = 360, Margin = new Padding(0, 5, 12, 0) };
            downloader.Items.AddRange(new object[] { L.T("Windows-Standardprogramm für .nzb"), L.T("Ausgewähltes Programm") }); downloader.SelectedIndex = 0;
            chooseDownloader = Button("Downloader auswählen …", 235, false);
            chooseDownloader.Click += (s,e) => { using (var dialog = new OpenFileDialog { Filter = L.T("Programme") + " (*.exe)|*.exe", Title = L.T("NZB-Downloader auswählen") }) if (dialog.ShowDialog(this) == DialogResult.OK) { downloaderPath = dialog.FileName; downloader.Items[1] = Path.GetFileNameWithoutExtension(downloaderPath); downloader.SelectedIndex = 1; SaveSettings(); } };
            targetRow.Controls.Add(downloader); targetRow.Controls.Add(chooseDownloader); delivery.Controls.Add(targetRow, 0, 1); grid.Controls.Add(delivery, 0, 7);
            var actions = Flow(); start = Button("NZB erstellen", 170, true); cancel = Button("Abbrechen", 115, false); cancel.Enabled = false;
            open = Button("Im Downloader öffnen", 225, false); open.Enabled = false;
            var showFolder = Button("Ordner öffnen", 145, false); showFolder.Click += (s,e) => { try { if (!Directory.Exists(folder.Text)) throw new IOException(L.T("Der Ordner existiert noch nicht.")); Shell(folder.Text); } catch (Exception ex) { ShowError(ex.Message); } };
            actions.Controls.AddRange(new Control[] { start, cancel, open, showFolder }); grid.Controls.Add(actions, 0, 8);
            start.Click += async (s,e) => await StartJob(); cancel.Click += (s,e) => { if (cancellation != null) { cancellation.Cancel(); cancel.Enabled = false; status.Text = L.T("Wird abgebrochen …"); } };
            open.Click += (s,e) => OpenResult();
            var statusRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            status = Label("Bereit", 11, ink); elapsed = Label("00:00", 10, muted); elapsed.TextAlign = ContentAlignment.MiddleRight;
            statusRow.Controls.Add(status, 0, 0); statusRow.Controls.Add(elapsed, 1, 0); grid.Controls.Add(statusRow, 0, 9);
            progress = new HydraProgress { Dock = DockStyle.Fill, Height = 10, Margin = new Padding(0, 4, 0, 5), Style = ProgressBarStyle.Continuous }; grid.Controls.Add(progress, 0, 10);
            log = Box(true); log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.Font = new Font("Segoe UI", 10); log.Text = L.T("Hier erscheinen Suchstatus und Prüfergebnisse."); grid.Controls.Add(log, 0, 11);
            var footer = Flow(); associate = Button("Browser-Links zuordnen", 255, false); associate.Height = 26; associate.Font = new Font("Segoe UI", 8); associate.Click += (s,e) => Associate(); footer.Controls.Add(associate);
            grid.Controls.Add(footer, 0, 12);
            input.TextChanged += (s,e) => { try { title.Text = LinkInfo.Parse(input.Text).Title; } catch { title.Text = L.T("Vollständigen NZBLNK-Link einfügen"); } };
            timer.Tick += (s,e) => elapsed.Text = watch.Elapsed.ToString(@"mm\:ss");
            FormClosing += (s,e) => { if (cancellation != null) { e.Cancel = true; cancellation.Cancel(); status.Text = L.T("Suche wird beendet. Danach kannst du das Fenster schließen."); } };
            LoadSettings();
            ApplyLanguage();
            languagePicker.SelectedIndex = Array.IndexOf(L.Codes, L.Code);
            languagePicker.SelectedIndexChanged += (s,e) => { if (languagePicker.SelectedIndex < 0) return; L.Code = L.Codes[languagePicker.SelectedIndex]; ApplyLanguage(); SaveSettings(); };
            FormClosed += (s,e) => { SaveSettings(); timer.Dispose(); logo.Image.Dispose(); };
            AutoScaleDimensions = new SizeF(96F, 96F);
            ResumeLayout(true);
        }
        Label Label(string text, float size, Color color) { return new Label { Text = L.T(text), Tag = text, Font = new Font("Segoe UI", size), ForeColor = color, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty }; }
        TextBox Box(bool multiline) { return new TextBox { Multiline = multiline, Dock = DockStyle.Fill, BackColor = panel, ForeColor = ink, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 0, 5) }; }
        FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty }; }
        Button Button(string text, int width, bool primary) { var b = new HydraButton { Text = L.T(text), Tag = text, Width = width, Height = 34, BackColor = primary ? accent : panel, ForeColor = primary ? Color.White : ink, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 8, 2), Cursor = Cursors.Hand, UseVisualStyleBackColor = false }; b.FlatAppearance.BorderColor = primary ? accent : Color.FromArgb(79, 43, 49); b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(240, 54, 77) : Color.FromArgb(49, 23, 30); return b; }
        void TranslateControls(Control parent) {
            foreach (Control child in parent.Controls) {
                if (child.Tag is string) child.Text = L.T((string)child.Tag);
                var label = child as Label;
                if (label != null && label != elapsed) label.TextAlign = L.IsRtl ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
                TranslateControls(child);
            }
        }
        internal void ApplyLanguage() {
            SuspendLayout();
            RightToLeft = L.IsRtl ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = L.IsRtl;
            TranslateControls(this);
            input.RightToLeft = RightToLeft.No; folder.RightToLeft = RightToLeft.No;
            languagePicker.RightToLeft = RightToLeft.No; elapsed.RightToLeft = RightToLeft.No;
            int selection = downloader.SelectedIndex;
            downloader.Items[0] = L.T("Windows-Standardprogramm für .nzb");
            downloader.Items[1] = File.Exists(downloaderPath) ? Path.GetFileNameWithoutExtension(downloaderPath) : L.T("Ausgewähltes Programm");
            downloader.SelectedIndex = selection < 0 ? 0 : selection;
            try { title.Text = LinkInfo.Parse(input.Text).Title; title.RightToLeft = RightToLeft.No; }
            catch { title.Text = L.T("Bereit für deinen Link"); title.RightToLeft = RightToLeft.Inherit; }
            if (lastFile == null && cancellation == null) { status.Text = L.T("Bereit"); log.Text = L.T("Hier erscheinen Suchstatus und Prüfergebnisse."); }
            else if (lastFile != null) status.Text = L.T("Fertig · NZB gespeichert");
            ResumeLayout(true);
        }
        void ShowError(string message) { MessageBox.Show(this, message, "NZB Hydra", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        void Shell(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        void LoadSettings() {
            try { if (!File.Exists(settingsPath)) return; var doc = XDocument.Load(settingsPath); var root = doc.Root; L.Code = (string)root.Element("language") ?? "de";
                string path = (string)root.Element("folder"); if (!string.IsNullOrEmpty(path)) folder.Text = path;
                downloaderPath = (string)root.Element("downloader") ?? "";
                if (File.Exists(downloaderPath)) { downloader.Items[1] = Path.GetFileNameWithoutExtension(downloaderPath); downloader.SelectedIndex = (string)root.Element("mode") == "custom" ? 1 : 0; }
                autoOpen.Checked = (string)root.Element("autoOpen") != "false";
            } catch { status.Text = L.T("Einstellungen konnten nicht geladen werden. Standardwerte aktiv."); }
        }
        void SaveSettings() {
            try { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                new XDocument(new XElement("settings", new XElement("language", L.Code), new XElement("folder", folder.Text), new XElement("downloader", downloaderPath), new XElement("mode", downloader.SelectedIndex == 1 ? "custom" : "default"), new XElement("autoOpen", autoOpen.Checked ? "true" : "false"))).Save(settingsPath);
            } catch { }
        }
        void OpenResult() {
            try {
                if (lastFile == null || !File.Exists(lastFile)) throw new IOException(L.T("Die NZB-Datei ist nicht mehr vorhanden."));
                if (downloader.SelectedIndex == 1) {
                    if (!File.Exists(downloaderPath)) throw new IOException(L.T("Bitte zuerst eine gültige Downloader-Programmdatei auswählen."));
                    Process.Start(new ProcessStartInfo(downloaderPath, Engine.Quote(lastFile)) { UseShellExecute = true });
                } else Shell(lastFile);
                status.Text = L.T("NZB an den ausgewählten Downloader übergeben");
            } catch (Exception ex) { ShowError(L.T("NZB gespeichert, aber Öffnen fehlgeschlagen: ") + ex.Message); }
        }
        void Associate() {
            try {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nzblnk")) { key.SetValue("", "NZB Hydra"); key.SetValue("URL Protocol", ""); }
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nzblnk\shell\open\command")) key.SetValue("", "\"" + Application.ExecutablePath + "\" \"%1\"");
                ShowError(L.T("NZBLNK-Links öffnen jetzt NZB Hydra. Bitte den Programmordner nicht verschieben. In Chrome gegebenenfalls das Öffnen bestätigen."));
            } catch (Exception ex) { ShowError(ex.Message); }
        }
        async Task StartJob() {
            LinkInfo link; string destination;
            try { link = LinkInfo.Parse(input.Text); destination = Path.GetFullPath(folder.Text.Trim()); if (!Path.IsPathRooted(folder.Text.Trim())) throw new IOException(L.T("Bitte einen absoluten Speicherpfad auswählen.")); }
            catch (Exception ex) { ShowError(ex.Message); return; }
            SaveSettings(); cancellation = new CancellationTokenSource(); var token = cancellation.Token; bool shouldOpen = autoOpen.Checked;
            foreach (var control in new Control[] { input, folder, start, browse, paste, associate, autoOpen, downloader, chooseDownloader, languagePicker, open }) control.Enabled = false;
            cancel.Enabled = true; lastFile = null; log.Clear(); watch.Restart(); timer.Start(); progress.Value = 0; progress.Style = ProgressBarStyle.Marquee; progress.MarqueeAnimationSpeed = 25; status.Text = L.T("Suche wird gestartet …");
            var updates = new Progress<string>(s => { status.Text = s; var percentage = Regex.Match(s, @"· (\d+) %$"); if (percentage.Success) { progress.Style = ProgressBarStyle.Continuous; progress.Value = Math.Min(100, int.Parse(percentage.Groups[1].Value)); } else { progress.Style = ProgressBarStyle.Marquee; } log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine); });
            try {
                lastFile = await Task.Run(() => Engine.Run(link, destination, s => ((IProgress<string>)updates).Report(s), token));
                progress.Style = ProgressBarStyle.Continuous; progress.Value = 100; status.Text = L.T("Fertig · NZB gespeichert");
                log.AppendText(L.T("Gespeichert: ") + lastFile + Environment.NewLine);
                if (shouldOpen) OpenResult();
            } catch (OperationCanceledException) { status.Text = L.T("Abgebrochen"); log.AppendText(L.T("Die Suche wurde abgebrochen.") + Environment.NewLine); }
            catch (Exception ex) { status.Text = L.T("NZB konnte nicht erstellt werden"); log.AppendText(ex.Message + Environment.NewLine); }
            finally {
                timer.Stop(); watch.Stop(); progress.Style = ProgressBarStyle.Continuous;
                cancellation.Dispose(); cancellation = null; cancel.Enabled = false;
                foreach (var control in new Control[] { input, folder, start, browse, paste, associate, autoOpen, downloader, chooseDownloader, languagePicker }) control.Enabled = true;
                open.Enabled = lastFile != null;
            }
        }
    }
    public static class Program {
        public static void TraceStart(string stage) {
            try {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NZBHydra");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "start-diagnose.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | PID " + Process.GetCurrentProcess().Id + " | " + stage + Environment.NewLine);
            } catch { }
        }
        [STAThread] public static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            TraceStart("Start; Argumente=" + args.Length);
            Application.ThreadException += (sender, eventArgs) => { TraceStart("UI-Fehler: " + eventArgs.Exception.GetType().FullName); MessageBox.Show(L.T("Hydra konnte die Aktion nicht abschließen. Bitte die Startdiagnose prüfen."), "NZB Hydra"); };
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) => TraceStart("Unbehandelter Fehler: " + eventArgs.ExceptionObject.GetType().FullName);
            if (args.Length > 0 && args[0] == "--self-test") {
                string report = args.Length > 1 ? args[1] : Path.Combine(Engine.Base, "test-result.txt");
                try { Tests.Run(); File.WriteAllText(report, "PASS: independent engine tests passed."); return 0; }
                catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); return 1; }
            }

            if (args.Length == 4 && args[0] == "--integration-test") {
                try { var result = Engine.Run(LinkInfo.Parse(File.ReadAllText(args[1])), args[2], s => {}, CancellationToken.None); File.WriteAllText(args[3], "PASS: " + Engine.ValidateNzb(result) + " files in valid NZB. No downloader launched."); return 0; }
                catch (Exception ex) { File.WriteAllText(args[3], "FAIL: " + ex.Message); return 1; }
            }
            var window = new MainForm(args.Length > 0 ? args[0] : null);
            window.Shown += (sender, eventArgs) => { TraceStart("Fenster angezeigt"); window.WindowState = FormWindowState.Normal; window.Activate(); };
            Application.Run(window); TraceStart("Beendet"); return 0;
        }
    }
}
