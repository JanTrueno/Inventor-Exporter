using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Inventor;
using Color = System.Drawing.Color;

namespace StepExport.UI
{
    /// <summary>One row of the model tree: the open document, or a part or subassembly in it.</summary>
    internal sealed class ModelNode
    {
        public Document Doc;
        public ComponentOccurrence Occ;   // null for the open document itself
        public string Key;                // the full document name: the same file gives the same key
        public string Name;
        public bool Assembly;
        public bool Hidden;
        public bool ChildrenLoaded;
    }

    /// <summary>
    /// The parts and subassemblies of the open document, like Inventor's model browser, with a tick box per row. Drawn in
    /// the panel's theme: caret, tick box, name, and the export state on the right. A file used several times is
    /// ticked everywhere at once (it is exported once). Children are read when a row is opened.
    /// </summary>
    internal sealed class ModelTree : TreeView
    {
        public const int RowHeight = 22;
        private const int Step = 16;
        private const int StatusWidth = 92;

        private readonly Theme _theme;

        /// <summary>Set by the command: is the file of this row ticked?</summary>
        public Func<ModelNode, bool> IsChecked = n => false;

        /// <summary>Set by the command: the state shown on the right (for example "Exported"), and whether it is good news.</summary>
        public Func<ModelNode, (string Text, bool Strong)> StateOf = n => ("", false);

        /// <summary>The tick box of a row was clicked (or Space pressed).</summary>
        public event Action<TreeNode> CheckToggled;

        /// <summary>A row is opened for the first time: add its children.</summary>
        public event Action<TreeNode> LoadChildren;

        public ModelTree(Theme theme)
        {
            _theme = theme;
            DrawMode = TreeViewDrawMode.OwnerDrawAll;
            ShowLines = false;
            ShowPlusMinus = false;
            ShowRootLines = false;
            FullRowSelect = true;
            HideSelection = false;
            ItemHeight = RowHeight;
            Indent = Step;
            BorderStyle = BorderStyle.None;
            BackColor = theme.Field;
            ForeColor = theme.Text;
            HotTracking = false;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            const int TVM_SETEXTENDEDSTYLE = 0x1100 + 44, TVS_EX_DOUBLEBUFFER = 0x0004;
            SendMessage(Handle, TVM_SETEXTENDEDSTYLE, (IntPtr)TVS_EX_DOUBLEBUFFER, (IntPtr)TVS_EX_DOUBLEBUFFER);
        }

        /// <summary>A row that may have children gets a placeholder child, so it can be opened.</summary>
        public static TreeNode NewNode(ModelNode data)
        {
            var node = new TreeNode(data.Name) { Tag = data };
            if (data.Assembly && !data.ChildrenLoaded) node.Nodes.Add(new TreeNode("…"));
            return node;
        }

        protected override void OnBeforeExpand(TreeViewCancelEventArgs e)
        {
            if (e.Node.Tag is ModelNode data && !data.ChildrenLoaded)
            {
                data.ChildrenLoaded = true;
                BeginUpdate();
                e.Node.Nodes.Clear();
                try { LoadChildren?.Invoke(e.Node); }
                finally { EndUpdate(); }
            }
            base.OnBeforeExpand(e);
        }

        private static int IndentOf(TreeNode n) => 4 + n.Level * Step;
        private static Rectangle CaretRect(TreeNode n, Rectangle row) => new Rectangle(IndentOf(n), row.Y + (row.Height - 12) / 2, 12, 12);
        private static Rectangle BoxRect(TreeNode n, Rectangle row) => new Rectangle(IndentOf(n) + 16, row.Y + (row.Height - 13) / 2, 13, 13);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            TreeNode n = GetNodeAt(e.Location);
            if (n != null && e.Button == MouseButtons.Left)
            {
                Rectangle row = new Rectangle(0, n.Bounds.Y, ClientSize.Width, n.Bounds.Height);
                Rectangle caret = CaretRect(n, row), box = BoxRect(n, row);
                caret.Inflate(3, 4);
                box.Inflate(3, 4);
                if (n.Nodes.Count > 0 && caret.Contains(e.Location))
                {
                    if (n.IsExpanded) n.Collapse(); else n.Expand();
                    return;
                }
                // Shift click: every row from the last clicked one (else the top row) to this one is ticked;
                // when they were all ticked already, they are all unticked
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    TreeNode from = _anchor != null && _anchor.TreeView == this ? _anchor : (Nodes.Count > 0 ? Nodes[0] : n);
                    SelectedNode = n;
                    List<TreeNode> range = Range(from, n);
                    RangeChecked?.Invoke(range, !AllChecked(range));
                    return;
                }
                // Ctrl click, or a click on the box: that row only
                if ((ModifierKeys & Keys.Control) != 0 || box.Contains(e.Location))
                {
                    SelectedNode = n;
                    _anchor = n;
                    CheckToggled?.Invoke(n);
                    return;
                }
                _anchor = n;
            }
            base.OnMouseDown(e);
        }

        private TreeNode _anchor;   // the row clicked last, where a Shift click range starts

        /// <summary>Shift click: these rows all get ticked (true) or unticked (false).</summary>
        public event Action<IList<TreeNode>, bool> RangeChecked;

        /// <summary>The rows from <paramref name="a"/> to <paramref name="b"/> as they are shown, top to bottom.</summary>
        private List<TreeNode> Range(TreeNode a, TreeNode b)
        {
            List<TreeNode> shown = Shown();
            int i = shown.IndexOf(a), j = shown.IndexOf(b);
            if (i < 0 || j < 0) return new List<TreeNode> { b };
            if (i > j) (i, j) = (j, i);
            return shown.GetRange(i, j - i + 1);
        }

        /// <summary>The rows as they are shown, top to bottom (children of opened rows only).</summary>
        private List<TreeNode> Shown()
        {
            var shown = new List<TreeNode>();
            void Walk(TreeNodeCollection nodes)
            {
                foreach (TreeNode n in nodes)
                {
                    shown.Add(n);
                    if (n.IsExpanded) Walk(n.Nodes);
                }
            }
            Walk(Nodes);
            return shown;
        }

        private bool AllChecked(IEnumerable<TreeNode> nodes)
        {
            foreach (TreeNode n in nodes)
                if (n.Tag is ModelNode d && !IsChecked(d)) return false;
            return true;
        }

        /// <summary>Ticks every row that is shown (or unticks them all when they were all ticked), as Ctrl+A does.</summary>
        public void CheckAllShown()
        {
            if (Nodes.Count == 0) return;
            List<TreeNode> all = Shown();
            RangeChecked?.Invoke(all, !AllChecked(all));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.A && e.Control)
            {
                CheckAllShown();
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Space && SelectedNode != null)
            {
                CheckToggled?.Invoke(SelectedNode);
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            if (e.Bounds.Height <= 0) return;
            Graphics g = e.Graphics;
            var row = new Rectangle(0, e.Bounds.Y, ClientSize.Width, e.Bounds.Height);
            bool selected = (e.State & TreeNodeStates.Selected) != 0;
            using (var fill = new SolidBrush(selected ? _theme.AccentFill : _theme.Field)) g.FillRectangle(fill, row);

            TreeNode n = e.Node;
            var data = n.Tag as ModelNode;
            if (data == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (n.Nodes.Count > 0 && !WeaveIcons.Draw(g, CaretRect(n, row), n.IsExpanded ? "caret-down-xs" : "caret-right-xs"))
            {
                Rectangle c = CaretRect(n, row);
                using (var tri = new SolidBrush(_theme.Dim))
                    g.FillPolygon(tri, n.IsExpanded
                        ? new[] { new PointF(c.X + 2, c.Y + 4), new PointF(c.Right - 2, c.Y + 4), new PointF(c.X + 6, c.Bottom - 3) }
                        : new[] { new PointF(c.X + 4, c.Y + 2), new PointF(c.X + 4, c.Bottom - 2), new PointF(c.Right - 3, c.Y + 6) });
            }

            bool on = IsChecked(data);
            Rectangle box = BoxRect(n, row);
            DrawBox(g, box, on);

            (string state, bool strong) = StateOf(data);
            int textX = box.Right + 6;
            int stateW = string.IsNullOrEmpty(state) ? 0 : StatusWidth;
            Color ink = data.Hidden ? _theme.Dim : _theme.Text;
            string text = data.Name + (data.Hidden ? " (hidden)" : "");
            TextRenderer.DrawText(g, text, Font, new Rectangle(textX, row.Y, Math.Max(10, row.Right - textX - stateW - 6), row.Height), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (stateW > 0)
                TextRenderer.DrawText(g, state, Font, new Rectangle(row.Right - stateW - 6, row.Y, stateW, row.Height), strong ? _theme.Link : _theme.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        /// <summary>A flat tick box: an outline when off, filled with the accent colour and a white tick when on.</summary>
        private void DrawBox(Graphics g, Rectangle r, bool on)
        {
            if (on)
            {
                using (var fill = new SolidBrush(_theme.Accent)) g.FillRectangle(fill, r);
                using (var pen = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(r.X + 3, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.42f, r.Bottom - 3.5f), new PointF(r.Right - 3, r.Y + 3.5f) });
            }
            else
                using (var pen = new Pen(_theme.Dim)) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        /// <summary>The number of rows that can be seen when scrolled (opened rows and their children).</summary>
        public int VisibleRows()
        {
            int count = 0;
            void Walk(TreeNodeCollection nodes)
            {
                foreach (TreeNode n in nodes)
                {
                    count++;
                    if (n.IsExpanded) Walk(n.Nodes);
                }
            }
            Walk(Nodes);
            return count;
        }
    }
}
