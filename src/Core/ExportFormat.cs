using System;
using System.Collections.Generic;
using System.Linq;

namespace StepExport.Core
{
    /// <summary>
    /// A file type the panel can write. Models (parts and assemblies) go through Inventor's own translators, except IFC,
    /// which this add-in writes itself (Inventor's IFC translator only imports). Drawings go through the 2D translators.
    /// The translator ids and the document types each one saves from come from the translator manifests of Inventor 2025.
    /// </summary>
    internal sealed class ExportFormat
    {
        public string Key { get; private set; }
        public string Label { get; private set; }

        /// <summary>The translator for parts, assemblies and drawings; null when the format cannot be written from that type.</summary>
        public string PartId { get; private set; }
        public string AssemblyId { get; private set; }
        public string DrawingId { get; private set; }

        private string _partExt, _assemblyExt, _drawingExt;

        /// <summary>From an assembly the translator writes a file per part next to it; those go in a folder of their own.</summary>
        public bool PartFiles { get; private set; }

        /// <summary>Written by this add-in, not by an Inventor translator.</summary>
        public bool Own { get; private set; }

        private const string Step = "{90AF7F40-0C01-11D5-8E83-0010B541CD80}";
        private const string Iges = "{90AF7F44-0C01-11D5-8E83-0010B541CD80}";
        private const string Sat = "{89162634-02B6-11D5-8E80-0010B541CD80}";
        private const string ParasolidText = "{8F9D3571-3CB8-42f7-8AFF-2DB2779C8465}";
        private const string ParasolidBinary = "{A8F8F8E5-BBAB-4f74-8B1B-AC011251F8AC}";
        private const string Jt = "{16625A0E-F58C-4488-A969-E7EC4F99CACD}";
        private const string Stl = "{533E9A98-FC3B-11D4-8E7E-0010B541CD80}";
        private const string Obj = "{F539FB09-FC01-4260-A429-1818B14D6BAC}";
        private const string Gltf = "{856E3C5D-A1F4-4B86-A6C0-8B849C0C8BE0}";
        private const string Usdz = "{2F08D88C-E86A-490E-9059-DD97A44020AC}";
        private const string Dwg = "{C24E3AC2-122E-11D5-8E91-0010B541CD80}";
        private const string Dxf = "{C24E3AC4-122E-11D5-8E91-0010B541CD80}";
        private const string Dwf = "{0AC6FD95-2F4D-42CE-8BE0-8AEA580399E4}";
        private const string Dwfx = "{0AC6FD97-2F4D-42CE-8BE0-8AEA580399E4}";
        private const string Pdf = "{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}";
        private const string CatiaPart = "{2FEE4AE5-36D3-4392-89C7-58A9CD14D305}";
        private const string CatiaProduct = "{8A88FC01-0C32-4B3E-BE12-DDC8DF6FFF18}";
        private const string Granite = "{66CB2667-73AD-401c-A531-64EC701825A1}";
        private const string ProeNeutral = "{8CEC09E3-D638-4e8f-A6E1-0D1E1A5FC8E3}";
        private const string Smt = "{B4ECC5EB-9507-46E5-87FB-EBB9479CE1DF}";
        private const string Qif = "{C824374A-92F0-4053-9AC0-E567A96E1BBE}";

        private static ExportFormat Model(string key, string label, string id, string ext) =>
            new ExportFormat { Key = key, Label = label, PartId = id, AssemblyId = id, _partExt = ext, _assemblyExt = ext };

        private static ExportFormat Sheet(string key, string label, string id, string ext) =>
            new ExportFormat { Key = key, Label = label, DrawingId = id, _drawingExt = ext };

        /// <summary>The formats for parts and assemblies, in the order of the panel.</summary>
        public static readonly IReadOnlyList<ExportFormat> Models = new[]
        {
            Model("step", "STEP", Step, ".stp"),
            new ExportFormat { Key = "ifc", Label = "IFC", Own = true, _partExt = ".ifc", _assemblyExt = ".ifc" },
            Model("glb", "glTF (.glb)", Gltf, ".glb"),
            Model("obj", "OBJ", Obj, ".obj"),
            Model("stl", "STL", Stl, ".stl"),
            Model("dwg", "DWG (3D)", Dwg, ".dwg"),
            Model("iges", "IGES", Iges, ".igs"),
            Model("sat", "SAT", Sat, ".sat"),
            Model("x_t", "Parasolid x_t", ParasolidText, ".x_t"),
            Model("x_b", "Parasolid x_b", ParasolidBinary, ".x_b"),
            Model("jt", "JT", Jt, ".jt"),
            Model("usdz", "USDZ", Usdz, ".usdz"),
            Model("dwf", "DWF", Dwf, ".dwf"),
            new ExportFormat { Key = "catia", Label = "CATIA V5", PartId = CatiaPart, AssemblyId = CatiaProduct, _partExt = ".CATPart", _assemblyExt = ".CATProduct" },
            Model("granite", "Granite (.g)", Granite, ".g").WithPartFiles(),
            Model("neu", "Pro/E Neutral", ProeNeutral, ".neu").WithPartFiles(),
            Model("smt", "SMT", Smt, ".smt"),
            new ExportFormat { Key = "qif", Label = "QIF (parts)", PartId = Qif, _partExt = ".qif" },
        };

        /// <summary>The formats for the open drawing itself.</summary>
        public static readonly IReadOnlyList<ExportFormat> Drawings = new[]
        {
            Sheet("pdf", "PDF", Pdf, ".pdf"),
            Sheet("dwg", "DWG", Dwg, ".dwg"),
            Sheet("dxf", "DXF", Dxf, ".dxf"),
            Sheet("dwf", "DWF", Dwf, ".dwf"),
            Sheet("dwfx", "DWFx", Dwfx, ".dwfx"),
        };

        private ExportFormat WithPartFiles() { PartFiles = true; return this; }

        public static ExportFormat Model(string key) => Models.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
        public static ExportFormat Drawing(string key) => Drawings.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>The translator for this kind of document, or null when the format cannot be written from it.</summary>
        public string TranslatorFor(Inventor.Document doc) =>
            doc is Inventor.PartDocument ? PartId : doc is Inventor.AssemblyDocument ? AssemblyId : doc is Inventor.DrawingDocument ? DrawingId : null;

        public bool CanWrite(Inventor.Document doc) => Own ? !(doc is Inventor.DrawingDocument) : TranslatorFor(doc) != null;

        /// <summary>The file extension for this kind of document; STEP uses the extension of the settings (.stp or .step).</summary>
        public string ExtensionFor(Inventor.Document doc, ExportSettings s)
        {
            if (Key == "step") return FileNamer.Extension(s);
            return doc is Inventor.AssemblyDocument ? _assemblyExt : doc is Inventor.DrawingDocument ? _drawingExt : _partExt;
        }

        /// <summary>The extension shown in the tree check (the part extension, or the drawing one).</summary>
        public string AnyExtension(ExportSettings s) => Key == "step" ? FileNamer.Extension(s) : _partExt ?? _assemblyExt ?? _drawingExt;
    }
}
