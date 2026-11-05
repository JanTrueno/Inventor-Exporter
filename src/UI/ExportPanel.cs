using System;
using System.Drawing;
using System.Windows.Forms;
using StepExport.Core;

namespace StepExport.UI
{
    /// <summary>
    /// The STEP Export panel, built like Inventor's own property panels (and the other export panels): section bars
    /// with chevrons, flat fields, iProperty lists for the file name and the folder name, the model tree to tick what
    /// is exported, and Export / Close at the bottom. It only shows and collects values; reading the model and
    /// exporting are done by <see cref="ExportCommand"/>.
    /// </summary>
    internal sealed class ExportPanel : UserControl
    {
        private const int LabelWidth = 90;
        private const int MaxTreeRows = 16;

        private readonly Theme _theme;
        private bool _loading;

        // file name
        public readonly PropertyList NameList;
        private readonly CheckBox _addDate = new CheckBox { Text = "Date at the end", AutoSize = true };
        private readonly CheckBox _folderDate = new CheckBox { Text = "Date at the end", AutoSize = true };
        private readonly Label _nameHint = new Label { AutoSize = false, Height = 34, Dock = DockStyle.Fill, Tag = "keep" };

        // folder
        private readonly FieldBox _folder;
        private readonly InventorButton _browse, _reset;
        public readonly PropertyList FolderList;
        private readonly Label _folderHint = new Label { AutoSize = false, Height = 48, Dock = DockStyle.Fill, Tag = "keep" };

        // models
        public readonly ModelTree Tree;
        private readonly Label _count = new Label { AutoSize = true, Tag = "keep" };

        // settings
        private readonly FieldBox _separator, _dateFormat, _folderSeparator, _folderDateFormat, _placeholder, _protocol, _extension;
        private readonly CheckBox _openFolder = new CheckBox { Text = "Open the folder after the export", AutoSize = true };

        private readonly Label _status = new Label { AutoSize = false, Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8, 2, 8, 0), Tag = "keep", Visible = false };
        private readonly InventorButton _export, _close;

        /// <summary>Something that changes the names or the folder (not saved).</summary>
        public event Action NamesChanged;
        public event Action FolderChanged, ResetFolderClicked;
        public event Action SettingsChanged;
        public event Action ExportClicked, CloseClicked;

        public ExportPanel(Theme theme)
        {
            _theme = theme;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = theme.Back;
            ForeColor = theme.Text;
            Dock = DockStyle.Fill;

            NameList = new PropertyList(theme, "file name") { Font = Font };
            FolderList = new PropertyList(theme, "folder name") { Font = Font };
            _folder = new FieldBox(theme);
            _browse = new InventorButton(theme, "Browse…", false);
            _reset = new InventorButton(theme, "Reset", false);
            Tree = new ModelTree(theme) { Font = Font };
            _separator = new FieldBox(theme);
            _dateFormat = new FieldBox(theme);
            _folderSeparator = new FieldBox(theme);
            _folderDateFormat = new FieldBox(theme);
            _placeholder = new FieldBox(theme);
            _protocol = new FieldBox(theme);
            _extension = new FieldBox(theme);
            _export = new InventorButton(theme, "Export", true);
            _close = new InventorButton(theme, "Close", false);

            BuildLayout();
            theme.Apply(this);
            _nameHint.ForeColor = _folderHint.ForeColor = _count.ForeColor = theme.Dim;
            WireEvents();
        }

        // ----- hints ----------------------------------------------------------------------------------------------

        /// <summary>Shows an example file name, or why it cannot be made.</summary>
        public void SetNameHint(string text, bool isError)
        {
            _nameHint.Text = text ?? "";
            _nameHint.ForeColor = isError ? ErrorColor : _theme.Dim;
        }

        public void SetFolderHint(string text, bool isError)
        {
            _folderHint.Text = text ?? "";
            _folderHint.ForeColor = isError ? ErrorColor : _theme.Dim;
        }

        // ----- folder ---------------------------------------------------------------------------------------------

        /// <summary>The folder the STEP folder is made in: the folder of the open document at first.</summary>
        public string Folder
        {
            get => _folder.Text.Trim();
            set { _loading = true; _folder.Text = value ?? ""; _loading = false; }
        }

        public bool FolderInvalid { set => _folder.Invalid = value; }

        // ----- models ---------------------------------------------------------------------------------------------

        /// <summary>The tree is as high as its open rows, up to <see cref="MaxTreeRows"/>; it scrolls beyond that.</summary>
        public void FitTree()
        {
            int rows = Math.Max(3, Math.Min(Tree.VisibleRows(), MaxTreeRows));
            Tree.Height = rows * ModelTree.RowHeight + 2;
        }

        public void SetCount(string text) => _count.Text = text ?? "";

        // ----- settings -------------------------------------------------------------------------------------------

        public void LoadSettings(ExportSettings s)
        {
            _loading = true;
            NameList.SetSpecs(s.NameProperties);
            FolderList.SetSpecs(s.FolderProperties);
            _addDate.Checked = s.AddDate;
            _separator.Text = s.Separator ?? "";
            _dateFormat.Text = s.DateFormat ?? "";
            _folderDate.Checked = s.FolderAddDate;
            _folderSeparator.Text = s.FolderSeparator ?? "";
            _folderDateFormat.Text = s.FolderDateFormat ?? "";
            _placeholder.Text = s.Placeholder ?? "";
            _protocol.Text = ProtocolName(s.Protocol);
            _extension.Text = s.Extension ?? ".stp";
            _openFolder.Checked = s.OpenFolder;
            _loading = false;
        }

        /// <summary>Reads the settings fields into <paramref name="s"/>; returns an error message, or null when all are fine.</summary>
        public string ReadSettings(ExportSettings s)
        {
            string error = null;
            s.NameProperties = NameList.Specs;
            s.FolderProperties = FolderList.Specs;
            s.AddDate = _addDate.Checked;
            s.FolderAddDate = _folderDate.Checked;

            if (ReadSeparator(_separator, out string sep)) s.Separator = sep; else error = "The separator has a character Windows refuses in a name.";
            if (ReadSeparator(_folderSeparator, out sep)) s.FolderSeparator = sep; else error = error ?? "The folder separator has a character Windows refuses in a name.";
            if (ReadDateFormat(_dateFormat, out string format)) s.DateFormat = format; else error = error ?? "The date format is not valid (for example yyyy-MM-dd).";
            if (ReadDateFormat(_folderDateFormat, out format)) s.FolderDateFormat = format; else error = error ?? "The folder date format is not valid (for example dd-MM-yyyy).";

            s.Placeholder = _placeholder.Text.Trim();

            int protocol = ProtocolNumber(_protocol.Text);
            _protocol.Invalid = protocol == 0;
            if (protocol != 0) s.Protocol = protocol; else error = error ?? "The protocol is AP203, AP214 or AP242.";

            string ext = _extension.Text.Trim().TrimStart('.').ToLowerInvariant();
            bool extOk = ext == "stp" || ext == "step";
            _extension.Invalid = !extOk;
            if (extOk) s.Extension = "." + ext; else error = error ?? "The extension is .stp or .step.";

            s.OpenFolder = _openFolder.Checked;
            return error;
        }

        private static bool ReadSeparator(FieldBox box, out string sep)
        {
            sep = box.Text;
            bool ok = sep.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
            box.Invalid = !ok;
            return ok;
        }

        private static bool ReadDateFormat(FieldBox box, out string format)
        {
            format = box.Text.Trim();
            bool ok = format.Length > 0;
            try { DateTime.Now.ToString(format); } catch (FormatException) { ok = false; }
            box.Invalid = !ok;
            return ok;
        }

        private static string ProtocolName(int p) => p == 2 ? "AP203" : p == 5 ? "AP242" : "AP214";

        private static int ProtocolNumber(string text)
        {
            switch ((text ?? "").Trim().ToUpperInvariant().Replace(" ", ""))
            {
                case "AP203": case "203": return 2;
                case "AP214": case "214": return 3;
                case "AP242": case "242": return 5;
                default: return 0;
            }
        }

        // ----- status and buttons ---------------------------------------------------------------------------------

        public void SetStatus(string text, bool isError)
        {
            _status.Text = text ?? "";
            _status.Visible = !string.IsNullOrEmpty(text);
            _status.ForeColor = isError ? ErrorColor : _theme.Dim;
            _status.Refresh();   // also while an export keeps Inventor busy
        }

        public void SetBusy(bool busy)
        {
            NameList.SetEnabled(!busy);
            FolderList.SetEnabled(!busy);
            foreach (Control c in new Control[] { _addDate, _folderDate, _folderSeparator, _folderDateFormat, _folder, _browse, _reset, Tree, _separator, _dateFormat, _placeholder,
                                                  _protocol, _extension, _openFolder, _export, _close })
                c.Enabled = !busy;
        }

        private static readonly Color ErrorColor = Color.FromArgb(240, 110, 100);

        // ----- layout ---------------------------------------------------------------------------------------------

        private void BuildLayout()
        {
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddSection(stack, "Folder", FolderTable(), true);
            AddSection(stack, "File name", NameTable(), false);
            AddSection(stack, "Models", ModelsTable(), true);
            AddSection(stack, "Settings", SettingsTable(), false);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scroll.Controls.Add(stack);

            // the main button on the left, as in Inventor
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(8, 7, 8, 0) };
            _export.Margin = new Padding(0, 0, 6, 0);
            _close.Margin = Padding.Empty;
            buttons.Controls.Add(_export);
            buttons.Controls.Add(_close);

            Controls.Add(scroll);
            Controls.Add(_status);
            Controls.Add(buttons);
        }

        private void AddSection(TableLayoutPanel stack, string title, Control body, bool open)
        {
            var header = new SectionHeader(_theme, title, open) { Font = Font, Dock = DockStyle.Fill, Margin = Padding.Empty };
            body.Dock = DockStyle.Fill;
            body.Margin = new Padding(8, 6, 8, 6);
            body.Visible = open;
            header.Toggled += isOpen => body.Visible = isOpen;
            int r = stack.RowCount++;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(header, 0, r);
            r = stack.RowCount++;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(body, 0, r);
        }

        private static TableLayoutPanel Table()
        {
            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelWidth));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        private Label Dim(string text) => new Label { Text = text, AutoSize = true, Tag = "keep", ForeColor = _theme.Dim };

        private TableLayoutPanel NameTable()
        {
            var t = Table();
            Wide(t, Dim("The model name, then these iProperties, in this order:"));
            NameList.Dock = DockStyle.Fill;
            Wide(t, NameList);
            Wide(t, _addDate);
            Wide(t, _nameHint);
            return t;
        }

        private TableLayoutPanel FolderTable()
        {
            var t = Table();
            Row(t, "In", _folder);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            _browse.Margin = new Padding(0, 0, 6, 0);
            _reset.Margin = Padding.Empty;
            buttons.Controls.Add(_browse);
            buttons.Controls.Add(_reset);
            Row(t, "", buttons);
            Wide(t, Dim("Then a folder named after these iProperties and the date:"));
            FolderList.Dock = DockStyle.Fill;
            Wide(t, FolderList);
            Wide(t, _folderDate);
            Wide(t, _folderHint);
            return t;
        }

        private TableLayoutPanel ModelsTable()
        {
            var t = Table();
            Tree.Dock = DockStyle.Fill;
            Wide(t, Tree);
            Wide(t, _count);
            var hint = new Label
            {
                Text = "Each ticked model gets its own STEP file; an assembly is exported whole. Shift click ticks every row from the last clicked one, Ctrl+A ticks all rows (again: unticks).",
                AutoSize = false, Height = 48, Dock = DockStyle.Fill, Tag = "keep", ForeColor = _theme.Dim
            };
            Wide(t, hint);
            return t;
        }

        private TableLayoutPanel SettingsTable()
        {
            var t = Table();
            Row(t, "Separator", _separator);
            Row(t, "Date format", _dateFormat);
            Row(t, "Folder sep.", _folderSeparator);
            Row(t, "Folder date", _folderDateFormat);
            Row(t, "Placeholder", _placeholder);
            Row(t, "Protocol", _protocol);
            Row(t, "Extension", _extension);
            Wide(t, _openFolder);
            var hint = new Label
            {
                Text = "Separator and Date format are for the file name, Folder sep. and Folder date for the folder name. Placeholder: an iProperty value that means \"not filled in yet\" (for example 160100); it is refused. Protocol: AP203, AP214 or AP242.",
                AutoSize = false, Height = 78, Dock = DockStyle.Fill, Tag = "keep", ForeColor = _theme.Dim
            };
            Wide(t, hint);
            return t;
        }

        private static void Row(TableLayoutPanel t, string label, Control c)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left };
            c.Margin = new Padding(0, 3, 0, 3);
            if (c is FieldBox) c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            t.Controls.Add(l, 0, r);
            t.Controls.Add(c, 1, r);
        }

        /// <summary>A control over both columns.</summary>
        private static void Wide(TableLayoutPanel t, Control c)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (!(c is PropertyList)) c.Margin = new Padding(0, 3, 0, 3);
            t.Controls.Add(c, 0, r);
            t.SetColumnSpan(c, 2);
        }

        // ----- behaviour ------------------------------------------------------------------------------------------

        private void WireEvents()
        {
            foreach (PropertyList list in new[] { NameList, FolderList })
            {
                list.ValuesChanged += () => { if (!_loading) NamesChanged?.Invoke(); };
                list.SpecsChanged += () => { if (!_loading) SettingsChanged?.Invoke(); };
            }
            _folder.TextChanged += (s, e) => { if (!_loading) FolderChanged?.Invoke(); };
            _browse.Click += (s, e) => Browse();
            _reset.Click += (s, e) => ResetFolderClicked?.Invoke();
            Tree.AfterExpand += (s, e) => FitTree();
            Tree.AfterCollapse += (s, e) => FitTree();
            foreach (FieldBox box in new[] { _separator, _dateFormat, _folderSeparator, _folderDateFormat, _placeholder, _protocol, _extension })
                box.TextChanged += (s, e) => { if (!_loading) SettingsChanged?.Invoke(); };
            foreach (CheckBox box in new[] { _addDate, _folderDate, _openFolder })
                box.CheckedChanged += (s, e) => { if (!_loading) SettingsChanged?.Invoke(); };
            _export.Click += (s, e) => ExportClicked?.Invoke();
            _close.Click += (s, e) => CloseClicked?.Invoke();
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Folder for the STEP files", UseDescriptionForTitle = true, ShowNewFolderButton = true })
            {
                try { if (System.IO.Directory.Exists(Folder)) dialog.SelectedPath = Folder; } catch { }
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                    _folder.Text = dialog.SelectedPath;   // raises FolderChanged
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { CloseClicked?.Invoke(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
