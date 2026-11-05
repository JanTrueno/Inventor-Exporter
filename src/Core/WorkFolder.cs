using System;
using System.Collections.Generic;
using System.IO;
using Inventor;
using Path = System.IO.Path;

namespace StepExport.Core
{
    /// <summary>
    /// The folder you are working in: the folder of the open assembly, part or drawing. When it is not saved yet, the
    /// folder of the model it shows; else the workspace of the active Inventor project.
    /// </summary>
    internal static class WorkFolder
    {
        public static string Find(Inventor.Application app, Document open, IEnumerable<Document> models)
        {
            string dir = FolderOf(open);
            if (dir.Length > 0) return dir;
            foreach (Document m in models)
            {
                dir = FolderOf(m);
                if (dir.Length > 0) return dir;
            }
            try
            {
                string ws = app.DesignProjectManager.ActiveDesignProject.WorkspacePath;
                if (!string.IsNullOrWhiteSpace(ws) && Directory.Exists(ws)) return ws;
            }
            catch (Exception ex) { Log.Write("Reading the active project failed: " + ex.Message); }
            return "";
        }

        private static string FolderOf(Document doc)
        {
            try
            {
                string file = doc?.FullFileName;
                return string.IsNullOrEmpty(file) ? "" : Path.GetDirectoryName(file) ?? "";
            }
            catch { return ""; }
        }
    }
}
