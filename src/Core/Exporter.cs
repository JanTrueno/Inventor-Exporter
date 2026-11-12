using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inventor;
using File = System.IO.File;
using Path = System.IO.Path;

namespace StepExport.Core
{
    /// <summary>
    /// Writes a model or drawing in one of the <see cref="ExportFormat"/>s: through Inventor's translator for that
    /// format, or through <see cref="IfcWriter"/> for IFC. An assembly is always written whole.
    /// </summary>
    internal sealed class Exporter
    {
        private readonly Inventor.Application _app;
        private readonly ExportSettings _settings;
        private readonly Dictionary<string, TranslatorAddIn> _translators = new Dictionary<string, TranslatorAddIn>(StringComparer.OrdinalIgnoreCase);

        public Exporter(Inventor.Application app, ExportSettings settings)
        {
            _app = app;
            _settings = settings;
        }

        /// <summary>The name of the Inventor file, without its extension.</summary>
        public static string ModelName(Document doc)
        {
            string name = Path.GetFileNameWithoutExtension(doc.FullFileName ?? "");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        /// <summary>Writes <paramref name="doc"/> to <paramref name="path"/>; throws when nothing was written.</summary>
        public void Export(Document doc, string path, ExportFormat format)
        {
            if (format.Own)
            {
                if (File.Exists(path)) File.Delete(path);
                new IfcWriter(_app, _settings).Write(doc, path);
                if (!File.Exists(path)) throw new IOException("No IFC file was written.");
                return;
            }

            string id = format.TranslatorFor(doc);
            if (id == null) throw new InvalidOperationException(format.Label + " cannot be written from this kind of document.");
            TranslatorAddIn translator = Translator(id, format.Label);

            TranslationContext ctx = _app.TransientObjects.CreateTranslationContext();
            ctx.Type = IOMechanismEnum.kFileBrowseIOMechanism;
            NameValueMap options = _app.TransientObjects.CreateNameValueMap();
            if (translator.HasSaveCopyAsOptions[doc, ctx, options]) SetOptions(format, doc, options);
            DataMedium medium = _app.TransientObjects.CreateDataMedium();
            medium.FileName = path;

            DateTime start = DateTime.Now.AddSeconds(-2);
            if (File.Exists(path)) File.Delete(path);
            translator.SaveCopyAs(doc, ctx, options, medium);
            if (File.Exists(path)) return;
            // a drawing with several sheets can come out as one DWG / DXF per sheet: <name>_<sheet>.dwg
            if (doc is DrawingDocument && WroteSheetFiles(path, start)) return;
            throw new IOException("The " + format.Label + " translator wrote no file.");
        }

        private TranslatorAddIn Translator(string id, string label)
        {
            if (_translators.TryGetValue(id, out TranslatorAddIn t)) return t;
            try { t = _app.ApplicationAddIns.ItemById[id.ToUpperInvariant()] as TranslatorAddIn; } catch { }
            if (t == null) { try { t = _app.ApplicationAddIns.ItemById[id] as TranslatorAddIn; } catch { } }
            if (t == null) throw new InvalidOperationException("Inventor's " + label + " translator is not installed.");
            if (!t.Activated) t.Activate();
            _translators[id] = t;
            return t;
        }

        private void SetOptions(ExportFormat format, Document doc, NameValueMap options)
        {
            void Set(string name, object value)
            {
                try { options.Value[name] = value; } catch (Exception ex) { Log.Write(format.Label + " option " + name + ": " + ex.Message); }
            }

            switch (format.Key)
            {
                case "step":
                    Set("ApplicationProtocolType", _settings.Protocol);
                    Set("IncludeSketches", false);
                    break;
            }
            if (doc is DrawingDocument)
            {
                switch (format.Key)
                {
                    case "pdf":
                        Set("All_Color_AS_Black", 0);
                        Set("Remove_Line_Weights", 1);
                        Set("Vector_Resolution", 400);
                        Set("Sheet_Range", PrintRangeEnum.kPrintAllSheets);
                        Set("Display_Published_File", false);
                        break;
                    case "dwf":
                    case "dwfx":
                        Set("Sheet_Range", PrintRangeEnum.kPrintAllSheets);
                        Set("Launch_Viewer", 0);
                        break;
                    case "dwg":
                    case "dxf":
                        // without an export .ini the DWG / DXF translator refuses a drawing (E_INVALIDARG)
                        string ini = AcadIni(format.Key);
                        if (ini != null) Set("Export_Acad_IniFile", ini);
                        break;
                }
            }
        }

        /// <summary>
        /// Inventor's own exportdwg.ini / exportdxf.ini (Design Data\DWG-DXF), copied with "USE TRANSMITTAL=No" so the
        /// drawing is not packed into a zip; null when it cannot be found.
        /// </summary>
        private string AcadIni(string key)
        {
            var dirs = new List<string>();
            try
            {
                dynamic project = _app.DesignProjectManager.ActiveDesignProject;
                string data = project.DesignDataPath;
                if (!string.IsNullOrEmpty(data)) dirs.Add(Path.Combine(data, "DWG-DXF"));
            }
            catch { }
            dirs.Add(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonDocuments), "Autodesk", "Inventor 2025", "Design Data", "DWG-DXF"));
            foreach (string dir in dirs)
            {
                string source = Path.Combine(dir, "export" + key + ".ini");
                if (!File.Exists(source)) continue;
                try
                {
                    string target = Path.Combine(Path.GetTempPath(), "StepExport", "export" + key + ".ini");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    var lines = File.ReadAllLines(source).Select(l => l.StartsWith("USE TRANSMITTAL=", StringComparison.OrdinalIgnoreCase) ? "USE TRANSMITTAL=No" : l);
                    File.WriteAllLines(target, lines);
                    return target;
                }
                catch (Exception ex) { Log.Write("Copying " + source + " failed: " + ex.Message); return source; }
            }
            Log.Write("No export" + key + ".ini found in " + string.Join("; ", dirs));
            return null;
        }

        private static bool WroteSheetFiles(string path, DateTime start)
        {
            try
            {
                string dir = Path.GetDirectoryName(path), name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
                return Directory.GetFiles(dir, name + "*" + ext).Any(f => File.GetLastWriteTime(f) >= start);
            }
            catch { return false; }
        }
    }
}
