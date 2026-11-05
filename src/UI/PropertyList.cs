using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using StepExport.Core;

namespace StepExport.UI
{
    /// <summary>
    /// A list of iProperties that make a name, as in the Engrave panel: per row the iProperty, text before and after its
    /// value, the value itself (read from the open document, editable), a Remember tick and a remove button; under it
    /// "+ iProperty" with a menu of the standard and custom iProperties.
    /// </summary>
    internal sealed class PropertyList : TableLayoutPanel
    {
        private const int NameWidth = 86;

        /// <summary>One iProperty row.</summary>
        public sealed class Row
        {
            public string Name;
            public Label Label;
            public FieldBox Before, Value, After;
            public CheckBox Remember;
            public IconButton Remove;

            /// <summary>The value was typed by hand for this document: it is used for every model instead of each model's own iProperty.</summary>
            public bool Edited;
        }

        private readonly Theme _theme;
        private readonly string _what;
        private readonly TableLayoutPanel _grid = new TableLayoutPanel { ColumnCount = 6, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Dock = DockStyle.Fill };
        private readonly List<Row> _rows = new List<Row>();
        private readonly InventorButton _add;
        private readonly Label _colBefore, _colValue, _colAfter, _colRemember;
        private readonly ToolTip _tips = new ToolTip();
        private bool _loading;

        /// <summary>Reads an iProperty of the open document; set by the command.</summary>
        public Func<string, string> ReadValue;

        /// <summary>Custom iProperties of the open document, with their values, for the pick menu; set by the command.</summary>
        public Func<IList<KeyValuePair<string, string>>> CustomProperties;

        /// <summary>A value was typed (the name changes, nothing to save).</summary>
        public event Action ValuesChanged;

        /// <summary>Rows, before / after text or a Remember tick changed: save the settings.</summary>
        public event Action SpecsChanged;

        public PropertyList(Theme theme, string what)
        {
            _theme = theme;
            _what = what;
            ColumnCount = 1;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NameWidth));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
            Label Title(string text) => new Label { Text = text, AutoSize = true, Tag = "keep", ForeColor = theme.Dim, BackColor = Color.Transparent, Margin = Padding.Empty, Font = new Font("Segoe UI", 7.5f) };
            _colBefore = Title("before");
            _colValue = Title("value");
            _colAfter = Title("after");
            _colRemember = Title("remember");
            _tips.SetToolTip(_colRemember, "Ticked: the value filled in is remembered (also next time) and used for every model");
            _grid.Visible = false;

            _add = new InventorButton(theme, "+ iProperty", false) { Width = 96, Margin = new Padding(0, 3, 0, 3) };
            _add.Click += (s, e) => Menu().Show(_add, new Point(0, _add.Height));

            RowCount = 2;
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(_grid, 0, 0);
            Controls.Add(_add, 0, 1);
        }

        public IReadOnlyList<Row> Rows => _rows;

        public List<PropertySpec> Specs => _rows.ConvertAll(r => new PropertySpec
        {
            Name = r.Name, Before = r.Before.Text, After = r.After.Text, Remember = r.Remember.Checked,
            Value = r.Remember.Checked ? r.Value.Text.Trim() : ""
        });

        /// <summary>Shows the rows of <paramref name="specs"/>, with values read from the open document (or remembered).</summary>
        public void SetSpecs(IEnumerable<PropertySpec> specs)
        {
            _loading = true;
            foreach (Row row in _rows) DisposeRow(row);
            _rows.Clear();
            foreach (PropertySpec p in specs ?? new PropertySpec[0]) AddRow(p, false);
            _loading = false;
            Layout_();
        }

        /// <summary>Another document: rows that do not remember their value show the iProperty of that document.</summary>
        public void RefreshValues()
        {
            _loading = true;
            foreach (Row row in _rows)
            {
                row.Edited = false;
                row.Value.Invalid = false;
                if (row.Remember.Checked) continue;
                row.Value.Text = Read(row.Name);
            }
            _loading = false;
        }

        public void SetEnabled(bool enabled)
        {
            _add.Enabled = enabled;
            foreach (Row r in _rows)
                foreach (Control c in new Control[] { r.Before, r.Value, r.After, r.Remember, r.Remove }) c.Enabled = enabled;
        }

        private string Read(string name)
        {
            try { return ReadValue?.Invoke(name) ?? ""; } catch { return ""; }
        }

        private void AddRow(PropertySpec spec, bool notify)
        {
            string name = (spec.Name ?? "").Trim();
            if (name.Length == 0 || _rows.Exists(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            var row = new Row
            {
                Name = name,
                Label = new Label { Text = name, AutoSize = false, AutoEllipsis = true, Width = NameWidth - 4, Height = 22, TextAlign = ContentAlignment.MiddleLeft, ForeColor = _theme.Text, BackColor = Color.Transparent, Margin = new Padding(0, 3, 4, 3), Tag = "keep" },
                Before = Affix(name, "before"),
                Value = new FieldBox(_theme) { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 3, 0, 3) },
                After = Affix(name, "after"),
                Remember = new CheckBox { AutoSize = true, Checked = spec.Remember, Margin = new Padding(14, 6, 0, 3), ForeColor = _theme.Text, BackColor = Color.Transparent },
                Remove = new IconButton(_theme, "Remove " + name + " from the " + _what, ActionIcons.Delete) { Size = new Size(26, 22), Margin = new Padding(4, 3, 0, 3) }
            };
            bool wasLoading = _loading;
            _loading = true;
            row.Value.Text = spec.Remember ? spec.Value ?? "" : Read(name);
            row.Before.Text = spec.Before ?? "";
            row.After.Text = spec.After ?? "";
            _loading = wasLoading;

            _tips.SetToolTip(row.Label, name);
            _tips.SetToolTip(row.Remember, "Remember: the value of " + name + " filled in here is kept (also next time) and used for every model");
            row.Value.TextChanged += (s, e) =>
            {
                if (_loading) return;
                row.Edited = true;
                row.Value.Invalid = false;
                ValuesChanged?.Invoke();
                if (row.Remember.Checked) SpecsChanged?.Invoke();
            };
            foreach (FieldBox box in new[] { row.Before, row.After })
                box.TextChanged += (s, e) => { if (!_loading) { ValuesChanged?.Invoke(); SpecsChanged?.Invoke(); } };
            row.Remember.CheckedChanged += (s, e) =>
            {
                if (_loading) return;
                if (!row.Remember.Checked)   // back to the iProperty of the document
                {
                    _loading = true;
                    row.Value.Text = Read(name);
                    row.Edited = false;
                    _loading = false;
                }
                ValuesChanged?.Invoke();
                SpecsChanged?.Invoke();
            };
            row.Remove.Click += (s, e) =>
            {
                _rows.Remove(row);
                DisposeRow(row);
                Layout_();
                ValuesChanged?.Invoke();
                SpecsChanged?.Invoke();
            };
            _rows.Add(row);
            if (notify)
            {
                Layout_();
                ValuesChanged?.Invoke();
                SpecsChanged?.Invoke();
            }
        }

        /// <summary>A narrow field for text before or after the value.</summary>
        private FieldBox Affix(string name, string where)
        {
            var box = new FieldBox(_theme) { Width = 36, Margin = where == "before" ? new Padding(0, 3, 4, 3) : new Padding(4, 3, 0, 3) };
            _tips.SetToolTip(box, "Text " + where + " the value of " + name);
            return box;
        }

        private void DisposeRow(Row row)
        {
            foreach (Control c in new Control[] { row.Label, row.Before, row.Value, row.After, row.Remember, row.Remove })
            {
                try { _grid.Controls.Remove(c); c.Dispose(); } catch { }
            }
        }

        private void Layout_()
        {
            _grid.SuspendLayout();
            _grid.Controls.Clear();
            _grid.RowStyles.Clear();
            _grid.RowCount = _rows.Count + 1;
            _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _grid.Controls.Add(_colBefore, 1, 0);
            _grid.Controls.Add(_colValue, 2, 0);
            _grid.Controls.Add(_colAfter, 3, 0);
            _grid.Controls.Add(_colRemember, 4, 0);
            for (int i = 0; i < _rows.Count; i++)
            {
                _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _grid.Controls.Add(_rows[i].Label, 0, i + 1);
                _grid.Controls.Add(_rows[i].Before, 1, i + 1);
                _grid.Controls.Add(_rows[i].Value, 2, i + 1);
                _grid.Controls.Add(_rows[i].After, 3, i + 1);
                _grid.Controls.Add(_rows[i].Remember, 4, i + 1);
                _grid.Controls.Add(_rows[i].Remove, 5, i + 1);
            }
            _grid.Visible = _rows.Count > 0;
            _grid.ResumeLayout();
        }

        // ----- the pick menu --------------------------------------------------------------------------------------

        /// <summary>A submenu per tab of Inventor's iProperties dialog, and Custom with the custom iProperties of the open document and a box to type any name.</summary>
        private ContextMenuStrip Menu()
        {
            var menu = new ThemedMenu(_theme);
            foreach ((string tab, string[] names) in PropertyCatalog.Tabs)
            {
                var sub = new ToolStripMenuItem(tab);
                foreach (string n in names) sub.DropDownItems.Add(PickItem(n, n));
                ThemedMenu.Style(sub, _theme);
                menu.Items.Add(sub);
            }

            var custom = new ToolStripMenuItem("Custom");
            IList<KeyValuePair<string, string>> existing = null;
            try { existing = CustomProperties?.Invoke(); } catch { }
            if (existing != null && existing.Count > 0)
            {
                foreach (KeyValuePair<string, string> kv in existing)
                    custom.DropDownItems.Add(PickItem(kv.Key, kv.Value.Length > 0 ? kv.Key + "   (" + kv.Value + ")" : kv.Key));
                custom.DropDownItems.Add(new ToolStripSeparator());
            }
            var typed = new ToolStripTextBox { AutoSize = false, Width = 180, BackColor = _theme.Back, ForeColor = _theme.Text, BorderStyle = BorderStyle.FixedSingle, ToolTipText = "Type a name and press Enter" };
            typed.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                menu.Close();
                AddRow(new PropertySpec { Name = typed.Text }, true);
            };
            custom.DropDownItems.Add(new ToolStripLabel("Other name (Enter to use):") { ForeColor = _theme.Dim });
            custom.DropDownItems.Add(typed);
            custom.DropDownOpened += (s, e) => typed.Focus();
            ThemedMenu.Style(custom, _theme);
            menu.Items.Add(custom);
            return menu;
        }

        private ToolStripMenuItem PickItem(string name, string text)
        {
            bool added = _rows.Exists(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            var item = new ToolStripMenuItem(text) { Enabled = !added, ForeColor = added ? _theme.Dim : _theme.Text };
            item.Click += (s, e) => AddRow(new PropertySpec { Name = name }, true);
            return item;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
