using System;
using System.IO;
using Inventor;
using File = System.IO.File;
using Path = System.IO.Path;

namespace StepExport.Core
{
    /// <summary>Writes a part or assembly to a STEP file through Inventor's STEP translator; an assembly is written whole.</summary>
    internal sealed class Exporter
    {
        private const string StepTranslatorId = "{90AF7F40-0C01-11D5-8E83-0010B541CD80}";

        private readonly Inventor.Application _app;
        private readonly TranslatorAddIn _step;

        private Exporter(Inventor.Application app, TranslatorAddIn step)
        {
            _app = app;
            _step = step;
        }

        /// <summary>The exporter, or null when Inventor's STEP translator is not there.</summary>
        public static Exporter Create(Inventor.Application app)
        {
            TranslatorAddIn step = null;
            try { step = app.ApplicationAddIns.ItemById[StepTranslatorId] as TranslatorAddIn; } catch { }
            if (step == null)
            {
                foreach (ApplicationAddIn addIn in app.ApplicationAddIns)
                {
                    try
                    {
                        if (addIn.AddInType == ApplicationAddInTypeEnum.kTranslationApplicationAddIn &&
                            (addIn.DisplayName ?? "").IndexOf("step", StringComparison.OrdinalIgnoreCase) >= 0)
                        { step = (TranslatorAddIn)addIn; break; }
                    }
                    catch { /* an add-in that cannot be read */ }
                }
            }
            if (step == null) return null;
            if (!step.Activated) step.Activate();
            return new Exporter(app, step);
        }

        /// <summary>The name of the Inventor file, without its extension.</summary>
        public static string ModelName(Document doc)
        {
            string name = Path.GetFileNameWithoutExtension(doc.FullFileName ?? "");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        public void Export(Document doc, string path, int protocol)
        {
            TranslationContext ctx = _app.TransientObjects.CreateTranslationContext();
            ctx.Type = IOMechanismEnum.kFileBrowseIOMechanism;
            NameValueMap options = _app.TransientObjects.CreateNameValueMap();
            if (_step.HasSaveCopyAsOptions[doc, ctx, options])
            {
                options.Value["ApplicationProtocolType"] = protocol;
                try { options.Value["IncludeSketches"] = false; } catch { }
            }
            DataMedium medium = _app.TransientObjects.CreateDataMedium();
            medium.FileName = path;
            if (File.Exists(path)) File.Delete(path);
            _step.SaveCopyAs(doc, ctx, options, medium);
            if (!File.Exists(path)) throw new IOException("The STEP translator wrote no file.");
        }
    }
}
