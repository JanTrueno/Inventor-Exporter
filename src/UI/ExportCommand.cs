using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Inventor;
using StepExport.Core;
using File = System.IO.File;
using Path = System.IO.Path;
using Timer = System.Windows.Forms.Timer;

namespace StepExport.UI
{
    /// <summary>
    /// The "STEP" command: a docked panel with the file name options, the folder (the folder of the open document at first) and the
    /// model tree of the open assembly, part or drawing. Every ticked model is exported to
    /// &lt;folder&gt;\&lt;name&gt;_&lt;order&gt;_&lt;date&gt;.stp. In an assembly or part the open document is ticked at
    /// first; in a drawing the assembly of sheet 1. The panel stays open for more exports and follows the active document.
    /// </summary>
    internal sealed class ExportCommand
    {
        private const string Title = "STEP Export";
        private static ExportCommand _active;

        private readonly Inventor.Application _app;
        private readonly InventorWindow _owner;
        private readonly ExportSettings _settings = ExportSettings.Load();
        private readonly Timer _refreshTimer = new Timer { Interval = 400 };

        private Document _top;
        private string _topName = "";
        private string _docFolder = "";   // the folder of the open document
        private bool _folderChosen;   // the folder was typed or browsed, so it no longer follows the document
        private ExportPanel _panel;
        private ApplicationEvents _appEvents;
        private HighlightSet _highlight;
        private bool _busy, _finished, _cancelled;

        private readonly Dictionary<string, Document> _docs = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _checked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (string Text, bool Strong)> _results = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _inFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // file names in the folder

        private ExportCommand(Inventor.Application app)
        {
            _app = app;
            _owner = new InventorWindow(app.MainFrameHWND);
        }

        /// <summary>Ends the running command, if any (used when the add-in unloads).</summary>
        public static void CloseActive() => _active?.Finish();

        /// <summary>Starts the command on the active document; returns an error message, or null.</summary>
        public static string Start(Inventor.Application app)
        {
            Document doc = app.ActiveDocument;
            if (!IsSupported(doc)) return "Open an assembly, a part or a drawing first.";
            if (doc is DrawingDocument && ModelsOf(doc).Count == 0) return "This drawing shows no part or assembly.";
            if (_active != null)
            {
                if (!SameDocument(doc, _active._top)) _active.Load(doc);
                DockHost.Reshow();
                return null;
            }

            var cmd = new ExportCommand(app);
            _active = cmd;
            try { cmd.Run(doc); }
            catch (Exception ex)
            {
                Log.Write("Starting STEP Export failed: " + ex);
                cmd.Finish();
                return "STEP Export could not start: " + ex.Message;
            }
            return null;
        }

        private void Run(Document doc)
        {
            _panel = new ExportPanel(Theme.Detect(_app));
            _panel.LoadSettings(_settings);
            _panel.Tree.IsChecked = n => _checked.Contains(n.Key);
            _panel.Tree.StateOf = StateOf;
            _panel.Tree.CheckToggled += OnCheckToggled;
            _panel.Tree.RangeChecked += OnRangeChecked;
            _panel.Tree.LoadChildren += AddChildren;
            _panel.Tree.AfterSelect += (s, e) => Highlight(e.Node?.Tag as ModelNode);
            foreach (PropertyList list in new[] { _panel.NameList, _panel.FolderList })
            {
                list.ReadValue = TopValue;
                list.CustomProperties = CustomProperties;
            }
            _panel.NamesChanged += Later;
            _panel.FolderChanged += () => { _folderChosen = true; Later(); };
            _panel.ResetFolderClicked += () => { _folderChosen = false; _panel.Folder = _docFolder; Refresh(); };
            _refreshTimer.Tick += (s, e) => { _refreshTimer.Stop(); Refresh(); };
            _panel.SettingsChanged += OnSettingsChanged;
            _panel.ExportClicked += Export;
            _panel.CloseClicked += Finish;

            _appEvents = _app.ApplicationEvents;
            _appEvents.OnCloseDocument += OnCloseDocument;
            _appEvents.OnActivateDocument += OnActivateDocument;
            DockHost.Closed += Finish;

            Load(doc);
            DockHost.Show(_panel, Title);
            DockHost.BringToFront();   // Inventor has just brought its Model browser tab forward
        }

        private void Later()
        {
            _refreshTimer.Stop();
            _refreshTimer.Start();
        }

        // ----- the open document ----------------------------------------------------------------------------------

        /// <summary>Shows the model tree, order number and folder of <paramref name="doc"/>; ticks what is selected in it, else the main model.</summary>
        private void Load(Document doc)
        {
            ClearHighlight();
            _top = doc;
            try { _topName = doc.FullDocumentName ?? ""; } catch { _topName = ""; }
            _docs.Clear();
            _checked.Clear();
            _results.Clear();

            ModelTree tree = _panel.Tree;
            tree.BeginUpdate();
            tree.Nodes.Clear();
            // a drawing: one row per model shown in its views; otherwise the document itself
            List<Document> models = ModelsOf(doc);
            var roots = new List<ModelNode>();
            foreach (Document m in models)
            {
                ModelNode root = NewModel(m, null);
                roots.Add(root);
                tree.Nodes.Add(ModelTree.NewNode(root));
            }
            tree.EndUpdate();
            _panel.FitTree();

            try { _highlight = doc is AssemblyDocument a ? a.CreateHighlightSet() : null; } catch { _highlight = null; }
            if (_highlight != null) _highlight.Color = _app.TransientObjects.CreateColor(255, 140, 0);

            if (TickSelection() == 0)
            {
                if (doc is DrawingDocument drawing)
                    foreach (Document m in FirstSheetModels(drawing)) _checked.Add(m.FullDocumentName ?? "");
                else
                    foreach (ModelNode root in roots) _checked.Add(root.Key);
            }

            // the iProperty values of the drawing, else of the model it shows
            _panel.NameList.RefreshValues();
            _panel.FolderList.RefreshValues();

            _docFolder = WorkFolder.Find(_app, doc, models);
            _folderChosen = false;   // a new document: its own folder again
            _panel.Folder = _docFolder;

            bool unsaved = roots.Any(r => string.IsNullOrEmpty(r.Doc.FullFileName));
            _panel.SetStatus(unsaved ? "Save the model first." : "", unsaved);
            Refresh();
        }


        private static bool IsSupported(Document doc) => doc is PartDocument || doc is AssemblyDocument || doc is DrawingDocument;

        /// <summary>The models of a document: for a drawing the parts and assemblies in its views, else the document itself.</summary>
        private static List<Document> ModelsOf(Document doc)
        {
            var list = new List<Document>();
            if (!(doc is DrawingDocument drawing)) { list.Add(doc); return list; }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Sheet sheet in drawing.Sheets)
                {
                    foreach (DrawingView view in sheet.DrawingViews)
                    {
                        try
                        {
                            Document m = view.ReferencedDocumentDescriptor?.ReferencedDocument as Document;
                            if (m == null || (!(m is PartDocument) && !(m is AssemblyDocument))) continue;
                            if (seen.Add(m.FullDocumentName)) list.Add(m);
                        }
                        catch { /* a view without a model, such as a sketch view */ }
                    }
                }
            }
            catch (Exception ex) { Log.Write("Reading the views of the drawing failed: " + ex.Message); }
            return list;
        }

        /// <summary>What a drawing ticks at first: the assembly in the views of sheet 1 (or the part, when it shows no assembly).</summary>
        private static List<Document> FirstSheetModels(DrawingDocument drawing)
        {
            var models = new List<Document>();
            try
            {
                if (drawing.Sheets.Count == 0) return models;
                foreach (DrawingView view in drawing.Sheets[1].DrawingViews)
                {
                    try
                    {
                        Document m = view.ReferencedDocumentDescriptor?.ReferencedDocument as Document;
                        if ((m is PartDocument || m is AssemblyDocument) && !models.Any(x => x.FullDocumentName == m.FullDocumentName)) models.Add(m);
                    }
                    catch { /* a view without a model */ }
                }
            }
            catch (Exception ex) { Log.Write("Reading the first sheet failed: " + ex.Message); }
            var assemblies = models.Where(m => m is AssemblyDocument).ToList();
            return assemblies.Count > 0 ? assemblies.Take(1).ToList() : models.Take(1).ToList();
        }

        private ModelNode NewModel(Document doc, ComponentOccurrence occ)
        {
            string key = doc.FullDocumentName ?? "";
            _docs[key] = doc;
            bool hidden = false;
            try { hidden = occ != null && !occ.Visible; } catch { }
            string name;
            try { name = occ != null ? occ.Name : doc.DisplayName; } catch { name = Exporter.ModelName(doc); }
            return new ModelNode { Doc = doc, Occ = occ, Key = key, Name = name, Assembly = doc is AssemblyDocument, Hidden = hidden };
        }

        /// <summary>The parts and subassemblies directly in an assembly row; suppressed and virtual components are left out.</summary>
        private void AddChildren(TreeNode node)
        {
            var data = (ModelNode)node.Tag;
            ComponentOccurrences occs = null;
            ComponentOccurrencesEnumerator subs = null;
            try
            {
                if (data.Occ == null) occs = ((AssemblyDocument)data.Doc).ComponentDefinition.Occurrences;
                else subs = data.Occ.SubOccurrences;
            }
            catch (Exception ex) { Log.Write("Reading the components of " + data.Name + " failed: " + ex.Message); return; }

            IEnumerable<ComponentOccurrence> list = occs != null ? occs.Cast<ComponentOccurrence>() : subs.Cast<ComponentOccurrence>();
            foreach (ComponentOccurrence occ in list)
            {
                try
                {
                    if (occ.Suppressed) continue;
                    if (occ.Definition is VirtualComponentDefinition) continue;
                    if (!(occ.Definition.Document is Document doc)) continue;
                    if (!(doc is PartDocument) && !(doc is AssemblyDocument)) continue;
                    node.Nodes.Add(ModelTree.NewNode(NewModel(doc, occ)));
                }
                catch { /* a component without a readable document */ }
            }
        }

        /// <summary>Ticks the parts and subassemblies selected in Inventor (the model browser or the window); returns how many.</summary>
        private int TickSelection()
        {
            int count = 0;
            try
            {
                foreach (object o in _top.SelectSet)
                {
                    try
                    {
                        Document doc;
                        if (o is DrawingView view) doc = view.ReferencedDocumentDescriptor?.ReferencedDocument as Document;
                        else
                        {
                            ComponentOccurrence occ = o as ComponentOccurrence ?? (o as FaceProxy)?.ContainingOccurrence ?? (o as EdgeProxy)?.ContainingOccurrence;
                            doc = occ?.Definition.Document as Document;
                        }
                        if (doc == null) continue;
                        if (!(doc is PartDocument) && !(doc is AssemblyDocument)) continue;
                        string key = doc.FullDocumentName;
                        _docs[key] = doc;
                        if (_checked.Add(key)) count++;
                    }
                    catch { /* something that is not a component */ }
                }
            }
            catch (Exception ex) { Log.Write("Reading the selection failed: " + ex.Message); }
            return count;
        }

        private void OnCheckToggled(TreeNode node)
        {
            if (!(node.Tag is ModelNode data)) return;
            if (!_checked.Remove(data.Key)) _checked.Add(data.Key);
            Changed();
        }

        private void OnRangeChecked(IList<TreeNode> nodes, bool on)
        {
            foreach (TreeNode node in nodes)
            {
                if (!(node.Tag is ModelNode data)) continue;
                if (on) _checked.Add(data.Key); else _checked.Remove(data.Key);
            }
            Changed();
        }

        /// <summary>After a tick changed: redraw (the same file elsewhere in the tree too), count and show an example name.</summary>
        private void Changed()
        {
            _panel.Tree.Invalidate();
            _panel.FitTree();
            int n = _checked.Count;
            _panel.SetCount(n == 0 ? "Nothing ticked." : n + (n == 1 ? " model" : " models") + " to export.");
            UpdateNameHint();
        }

        private void Highlight(ModelNode data)
        {
            if (_highlight == null) return;
            try
            {
                _highlight.Clear();
                if (data?.Occ != null) _highlight.AddItem(data.Occ);
                _app.ActiveView.Update();
            }
            catch { }
        }

        private void ClearHighlight()
        {
            try { _highlight?.Clear(); _highlight?.Delete(); } catch { }
            _highlight = null;
        }

        // ----- iProperties, names and folder ----------------------------------------------------------------------

        /// <summary>An iProperty of the open document; for a drawing without it, of the first model it shows.</summary>
        private string TopValue(string name)
        {
            if (_top == null) return "";
            string value = FileNamer.ReadProperty(_top, name);
            if (value.Length > 0 || !(_top is DrawingDocument)) return value;
            foreach (Document m in ModelsOf(_top))
            {
                value = FileNamer.ReadProperty(m, name);
                if (value.Length > 0) return value;
            }
            return "";
        }

        private IList<KeyValuePair<string, string>> CustomProperties()
        {
            var list = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var docs = new List<Document> { _top };
            if (_top is DrawingDocument) docs.AddRange(ModelsOf(_top).Take(1));
            foreach (Document d in docs)
            {
                try
                {
                    foreach (Property p in d.PropertySets["User Defined Properties"])
                    {
                        if (!seen.Add(p.Name)) continue;
                        string value = "";
                        try { value = (Convert.ToString(p.Value) ?? "").Trim(); } catch { }
                        list.Add(new KeyValuePair<string, string>(p.Name, value));
                    }
                }
                catch { }
            }
            list.Sort((x, y) => string.Compare(x.Key, y.Key, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        /// <summary>
        /// The value of a file name iProperty for one model: the panel's value when it is remembered or was typed,
        /// else the model's own iProperty, else the value of the open document (as shown in the panel).
        /// </summary>
        private static string ValueFor(PropertyList.Row row, Document model)
        {
            string shown = row.Value.Text.Trim();
            if (row.Remember.Checked || row.Edited) return shown;
            string own = FileNamer.ReadProperty(model, row.Name);
            return own.Length > 0 ? own : shown;
        }

        private static PropertySpec Spec(PropertyList.Row row) => new PropertySpec { Name = row.Name, Before = row.Before.Text, After = row.After.Text };

        private string StepName(Document doc)
        {
            var parts = new List<string> { Exporter.ModelName(doc) };
            foreach (PropertyList.Row row in _panel.NameList.Rows) parts.Add(FileNamer.Part(Spec(row), ValueFor(row, doc)));
            if (_settings.AddDate) parts.Add(FileNamer.DateText(_settings.DateFormat, DateTime.Today));
            return FileNamer.Join(parts, _settings.Separator) + FileNamer.Extension(_settings);
        }

        /// <summary>The name of the folder made from the folder iProperties and the date; "" when there is nothing to name it.</summary>
        private string FolderName()
        {
            var parts = _panel.FolderList.Rows.Select(r => FileNamer.Part(Spec(r), r.Value.Text)).ToList();
            if (_settings.FolderAddDate) parts.Add(FileNamer.DateText(_settings.FolderDateFormat, DateTime.Today));
            return FileNamer.Join(parts, _settings.FolderSeparator);
        }

        /// <summary>Where the STEP files go: the chosen folder \ subfolder \ iProperty folder.</summary>
        private string TargetFolder()
        {
            string folder = _panel.Folder;
            if (folder.Length == 0) return "";
            try
            {
                string name = FolderName();
                if (name.Length > 0) folder = Path.Combine(folder, name);
                return Path.GetFullPath(folder);
            }
            catch { return folder; }
        }

        /// <summary>Why no name can be made, or null: a value in the panel is the placeholder for "not filled in yet".</summary>
        private string NameProblem()
        {
            string problem = null;
            foreach (PropertyList list in new[] { _panel.NameList, _panel.FolderList })
                foreach (PropertyList.Row row in list.Rows)
                {
                    bool bad = FileNamer.IsPlaceholder(row.Value.Text, _settings);
                    row.Value.Invalid = bad;
                    if (bad && problem == null) problem = row.Name + " is not filled in (" + row.Value.Text.Trim() + ").";
                }
            return problem;
        }

        /// <summary>The empty iProperties, which drop out of the names.</summary>
        private string EmptyNote()
        {
            var empty = _panel.NameList.Rows.Concat(_panel.FolderList.Rows).Where(r => r.Value.Text.Trim().Length == 0).Select(r => r.Name).Distinct().ToList();
            return empty.Count == 0 ? "" : "  (" + string.Join(", ", empty) + (empty.Count == 1 ? " is" : " are") + " empty)";
        }

        /// <summary>The example name: the first ticked model, else the open document.</summary>
        private void UpdateNameHint()
        {
            string problem = NameProblem();
            if (problem != null) { _panel.SetNameHint(problem, true); return; }
            Document sample = _checked.Select(k => _docs.TryGetValue(k, out Document d) ? d : null).FirstOrDefault(d => d != null)
                              ?? ModelsOf(_top).FirstOrDefault();
            string file = "";
            try { file = sample?.FullFileName; } catch { }
            _panel.SetNameHint(string.IsNullOrEmpty(file) ? "" : StepName(sample) + EmptyNote(), false);
        }

        /// <summary>Shows the example name and where the export goes; marks the models whose STEP file is there already.</summary>
        private void Refresh()
        {
            string folder = TargetFolder();
            _inFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _panel.FolderInvalid = folder.Length == 0;
            if (folder.Length == 0) _panel.SetFolderHint("The document is not saved: choose a folder.", true);
            else
            {
                bool exists = false;
                try
                {
                    exists = Directory.Exists(folder);
                    if (exists) foreach (string f in Directory.GetFiles(folder)) _inFolder.Add(Path.GetFileName(f));
                }
                catch (Exception ex) { Log.Write("Reading " + folder + " failed: " + ex.Message); }
                _panel.SetFolderHint(folder + (exists ? "" : "  (new)"), false);
            }
            Changed();
        }

        private (string Text, bool Strong) StateOf(ModelNode data)
        {
            if (_results.TryGetValue(data.Key, out var result)) return result;
            string file = "";
            try { file = data.Doc.FullFileName; } catch { }
            if (string.IsNullOrEmpty(file)) return ("Not saved", false);
            return _inFolder.Contains(StepName(data.Doc)) ? ("In folder", false) : ("", false);
        }

        private void OnSettingsChanged()
        {
            if (_panel.ReadSettings(_settings) != null) return;
            _settings.Save();
            Later();
        }

        // ----- export ---------------------------------------------------------------------------------------------

        private void Export()
        {
            if (_busy) return;
            string error = _panel.ReadSettings(_settings) ?? NameProblem();
            if (error != null) { _panel.SetStatus(error, true); return; }
            _settings.Save();

            string folder = TargetFolder();
            if (folder.Length == 0) { _panel.SetStatus("Choose a folder for the STEP files.", true); return; }

            // what goes out: every ticked file once, saved, with a name not used by another ticked file
            var jobs = new List<(string Key, Document Doc, string Name)>();
            var skipped = new List<string>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in _checked)
            {
                if (!_docs.TryGetValue(key, out Document doc)) continue;
                string file = "";
                try { file = doc.FullFileName; } catch { }
                if (string.IsNullOrEmpty(file)) { skipped.Add(SafeName(doc) + ": not saved yet, so it has no file name."); continue; }
                string name = StepName(doc);
                PropertyList.Row unfilled = _panel.NameList.Rows.FirstOrDefault(r => FileNamer.IsPlaceholder(ValueFor(r, doc), _settings));
                if (unfilled != null) { skipped.Add(SafeName(doc) + ": " + unfilled.Name + " is not filled in."); continue; }
                if (!names.Add(name)) { skipped.Add(name + ": another ticked model has the same name; it is exported once."); continue; }
                jobs.Add((key, doc, name));
            }
            if (jobs.Count == 0)
            {
                _panel.SetStatus(skipped.Count > 0 ? string.Join(" ", skipped) : "Tick an assembly or part to export.", true);
                return;
            }

            var existing = jobs.Where(j => File.Exists(Path.Combine(folder, j.Name))).ToList();
            if (existing.Count > 0)
            {
                string list = string.Join("\n", existing.Take(12).Select(j => "  " + j.Name)) + (existing.Count > 12 ? "\n  …" : "");
                DialogResult answer = MessageBox.Show(_owner,
                    "Already in " + folder + ":\n\n" + list + "\n\nOverwrite? (No skips these.)",
                    Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.No)
                {
                    foreach (var j in existing) skipped.Add(j.Name + ": kept the one in the folder.");
                    jobs = jobs.Except(existing).ToList();
                    if (jobs.Count == 0) { _panel.SetStatus("Nothing exported: every STEP file is in the folder already.", false); return; }
                }
            }

            Exporter exporter = Exporter.Create(_app);
            if (exporter == null)
            {
                _panel.SetStatus("The STEP translator is not loaded (Tools > Add-Ins).", true);
                return;
            }

            try { Directory.CreateDirectory(folder); }
            catch (Exception ex) { _panel.SetStatus("Could not make the folder " + folder + ": " + ex.Message, true); return; }

            var failures = new List<string>();
            int done = 0;
            _busy = true;
            _cancelled = false;
            _panel.SetBusy(true);
            try { _highlight?.Clear(); } catch { }
            Inventor.ProgressBar progress = null;
            try
            {
                try
                {
                    progress = _app.CreateProgressBar(false, jobs.Count, Title, true, _app.MainFrameHWND);
                    progress.OnCancel += OnProgressCancel;
                }
                catch (Exception ex) { Log.Write("No progress bar: " + ex.Message); progress = null; }

                for (int i = 0; i < jobs.Count && !_cancelled; i++)
                {
                    var job = jobs[i];
                    string path = Path.Combine(folder, job.Name);
                    Report(progress, (i + 1) + " / " + jobs.Count + "  " + job.Name);
                    try
                    {
                        exporter.Export(job.Doc, path, _settings.Protocol);
                        done++;
                        _results[job.Key] = ("Exported", true);
                    }
                    catch (Exception ex)
                    {
                        failures.Add(job.Name + ": " + ex.Message);
                        _results[job.Key] = ("Failed", false);
                        Log.Write("Exporting " + path + " failed: " + ex);
                    }
                    try { progress?.UpdateProgress(); } catch { }
                    _panel.Tree.Invalidate();
                }
            }
            finally
            {
                try { if (progress != null) { progress.OnCancel -= OnProgressCancel; progress.Close(); } } catch { }
                try { _app.StatusBarText = ""; } catch { }
                _busy = false;
                _panel.SetBusy(false);
                DockHost.BringToFront();
            }

            Refresh();
            var summary = new List<string>();
            summary.Add(done + (done == 1 ? " STEP file" : " STEP files") + " written to " + folder + ".");
            if (_cancelled) summary.Add("The export was cancelled.");
            if (failures.Count > 0) summary.Add(failures.Count + " failed.");
            if (skipped.Count > 0) summary.Add(skipped.Count + " skipped.");
            _panel.SetStatus(string.Join(" ", summary), failures.Count > 0);

            if (failures.Count > 0 || skipped.Count > 0)
            {
                string text = string.Join("\n", summary);
                if (failures.Count > 0) text += "\n\nProblems:\n" + string.Join("\n", failures);
                if (skipped.Count > 0) text += "\n\nSkipped:\n" + string.Join("\n", skipped);
                if (failures.Count > 0) text += "\n\nDetails are in " + Log.Path;
                MessageBox.Show(_owner, text, Title, MessageBoxButtons.OK, failures.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            if (failures.Count == 0 && done > 0 && _settings.OpenFolder)
            {
                try { Process.Start("explorer.exe", "\"" + folder + "\""); } catch (Exception ex) { Log.Write("Opening the folder failed: " + ex.Message); }
            }
        }

        private void OnProgressCancel() => _cancelled = true;

        private void Report(Inventor.ProgressBar progress, string text)
        {
            _panel.SetStatus(text, false);
            try { if (progress != null) progress.Message = text; else _app.StatusBarText = text; } catch { }
        }

        private static string SafeName(Document doc)
        {
            try { return doc.DisplayName; } catch { return "A model"; }
        }

        private static bool SameDocument(Document a, Document b)
        {
            try { return a != null && b != null && string.Equals(a.FullDocumentName, b.FullDocumentName, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // ----- Inventor events and closing ------------------------------------------------------------------------

        /// <summary>Another assembly, part or drawing became the active document: the panel shows that one.</summary>
        private void OnActivateDocument(_Document doc, EventTimingEnum timing, NameValueMap context, out HandlingCodeEnum handling)
        {
            handling = HandlingCodeEnum.kEventNotHandled;
            if (timing != EventTimingEnum.kAfter || _busy || _finished) return;
            var d = doc as Document;
            if (!IsSupported(d) || (d is DrawingDocument && ModelsOf(d).Count == 0)) return;
            if (SameDocument(d, _top)) return;
            _panel.BeginInvoke(new Action(() =>
            {
                if (_finished || _busy) return;
                try { Load(d); }   // the panel follows, but the Model browser stays in front
                catch (Exception ex) { Log.Write("Showing the new document failed: " + ex.Message); }
            }));
        }

        private void OnCloseDocument(_Document doc, string fullName, EventTimingEnum timing, NameValueMap context, out HandlingCodeEnum handling)
        {
            handling = HandlingCodeEnum.kEventNotHandled;
            if (timing != EventTimingEnum.kBefore || _busy) return;
            bool ours = ReferenceEquals(doc, _top) || (_topName.Length > 0 && string.Equals(fullName, _topName, StringComparison.OrdinalIgnoreCase));
            if (ours) Finish();
        }

        private void Finish()
        {
            if (_finished || _busy) return;
            _finished = true;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            try { _appEvents.OnCloseDocument -= OnCloseDocument; } catch { }
            try { _appEvents.OnActivateDocument -= OnActivateDocument; } catch { }
            DockHost.Closed -= Finish;
            ClearHighlight();
            try { _app.ActiveView?.Update(); } catch { }
            DockHost.Hide(_panel);
            _panel = null;
            _appEvents = null;
            _top = null;
            _active = null;
        }
    }
}
