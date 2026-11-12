using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Inventor;
using File = System.IO.File;
using Path = System.IO.Path;

namespace StepExport.Core
{
    /// <summary>
    /// Writes an IFC4 file (ISO 10303-21 text, in millimetres) of a part or an assembly:
    /// IfcProject &gt; IfcSite &gt; IfcBuilding &gt; IfcBuildingStorey, holding the assembly tree as IfcElementAssembly
    /// and every part as IfcMember (profiles), IfcPlate (sheet metal and plates) or IfcBuildingElementProxy (the rest).
    /// The mesh of a part is written once and placed per instance (IfcRepresentationMap + IfcMappedItem). Every part
    /// carries a property set (part number, description, profile, material, length, thickness, volume, weight), base
    /// quantities and its material; one set per part, shared by all its instances.
    /// Suppressed, hidden and reference components are left out, and so are components whose name holds one of the
    /// "leave out" words of the settings.
    /// </summary>
    internal sealed class IfcWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly string[] PlateWords = { "plate", "plaat", "sheet" };
        private static readonly Regex ProfileWords = new Regex(
            @"tube|pipe|buis|koker|beam|balk|angle|hoek|channel|strip|\bhe[abm]\b|\bipe\b|\bunp\b|\bupe\b|\binp\b|l-profiel|t-profiel|l-profile|t-profile|round bar|flat bar|square bar|\brod\b|\bbar\b|stair step",
            RegexOptions.IgnoreCase);
        private static readonly Regex LengthInText = new Regex(@"\bL\s*=\s*([0-9]+(?:[.,][0-9]+)?)", RegexOptions.IgnoreCase);

        private readonly Inventor.Application _app;
        private readonly ExportSettings _settings;
        private readonly string[] _exclude;
        private readonly double _toleranceCm;

        private StreamWriter _w;
        private int _next = 1;

        // shared entities
        private int _origin, _world, _body, _identityOperator, _storeyPlacement;

        private sealed class PartInfo
        {
            public string Class = "IfcBuildingElementProxy";
            public string PartNumber = "", Description = "", Profile = "", Material = "", ObjectType = "";
            public double? Length, Thickness, Volume, Mass;   // mm, mm, m3, kg
            public int Map;                                   // IfcRepresentationMap, 0: no geometry
            public readonly List<int> Elements = new List<int>();
        }

        private readonly Dictionary<string, PartInfo> _parts = new Dictionary<string, PartInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<int>> _byMaterial = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _styles = new Dictionary<string, int>();

        public IfcWriter(Inventor.Application app, ExportSettings settings)
        {
            _app = app;
            _settings = settings;
            _exclude = (settings.IfcLeaveOut ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            double mm = settings.IfcMeshMm > 0 ? settings.IfcMeshMm : 0.1;
            _toleranceCm = mm / 10.0;
        }

        // ----- the file -------------------------------------------------------------------------------------------

        public void Write(Document doc, string path)
        {
            string temp = path + ".tmp";
            using (_w = new StreamWriter(temp, false, new UTF8Encoding(false)))
            {
                string name = Exporter.ModelName(doc);
                _w.WriteLine("ISO-10303-21;");
                _w.WriteLine("HEADER;");
                _w.WriteLine("FILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');");
                _w.WriteLine("FILE_NAME(" + S(Path.GetFileName(path)) + "," + S(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv)) +
                             ",(''),(''),'Exporter for Inventor','Autodesk Inventor 2025','');");
                _w.WriteLine("FILE_SCHEMA(('IFC4'));");
                _w.WriteLine("ENDSEC;");
                _w.WriteLine("DATA;");
                WriteModel(doc, name);
                _w.WriteLine("ENDSEC;");
                _w.WriteLine("END-ISO-10303-21;");
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private void WriteModel(Document doc, string name)
        {
            // units: millimetre, square and cubic metre, kilogram, radian
            int length = Add("IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.)");
            int area = Add("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)");
            int volume = Add("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)");
            int mass = Add("IFCSIUNIT(*,.MASSUNIT.,.KILO.,.GRAM.)");
            int angle = Add("IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.)");
            int units = Add("IFCUNITASSIGNMENT(" + Refs(length, area, volume, mass, angle) + ")");

            _origin = Add("IFCCARTESIANPOINT((0.,0.,0.))");
            _world = Add("IFCAXIS2PLACEMENT3D(" + R(_origin) + ",$,$)");
            int context = Add("IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.E-05," + R(_world) + ",$)");
            _body = Add("IFCGEOMETRICREPRESENTATIONSUBCONTEXT('Body','Model',*,*,*,*," + R(context) + ",$,.MODEL_VIEW.,$)");
            _identityOperator = Add("IFCCARTESIANTRANSFORMATIONOPERATOR3D($,$," + R(_origin) + ",$,$)");

            int project = Add("IFCPROJECT(" + Guid() + ",$," + S(name) + ",$,$,$,$,(" + R(context) + ")," + R(units) + ")");
            int sitePlacement = Add("IFCLOCALPLACEMENT($," + R(_world) + ")");
            int site = Add("IFCSITE(" + Guid() + ",$,'Site',$,$," + R(sitePlacement) + ",$,$,.ELEMENT.,$,$,$,$,$)");
            int buildingPlacement = Add("IFCLOCALPLACEMENT(" + R(sitePlacement) + "," + R(_world) + ")");
            int building = Add("IFCBUILDING(" + Guid() + ",$," + S(name) + ",$,$," + R(buildingPlacement) + ",$,$,.ELEMENT.,$,$,$)");
            _storeyPlacement = Add("IFCLOCALPLACEMENT(" + R(buildingPlacement) + "," + R(_world) + ")");
            int storey = Add("IFCBUILDINGSTOREY(" + Guid() + ",$,'Storey',$,$," + R(_storeyPlacement) + ",$,$,.ELEMENT.,0.)");
            Aggregate(project, new List<int> { site });
            Aggregate(site, new List<int> { building });
            Aggregate(building, new List<int> { storey });

            int root;
            if (doc is AssemblyDocument asm)
            {
                root = AssemblyElement(asm, null, name);
                var children = new List<int>();
                Walk(asm.ComponentDefinition.Occurrences.Cast<ComponentOccurrence>(), children);
                Aggregate(root, children);
            }
            else if (doc is PartDocument part)
            {
                root = PartElement(part, null, name);
            }
            else throw new InvalidOperationException("IFC is written from parts and assemblies only.");

            Add("IFCRELCONTAINEDINSPATIALSTRUCTURE(" + Guid() + ",$,$,$,(" + R(root) + ")," + R(storey) + ")");
            WriteProperties();
        }

        // ----- the assembly tree ----------------------------------------------------------------------------------

        private void Walk(IEnumerable<ComponentOccurrence> occurrences, List<int> into)
        {
            foreach (ComponentOccurrence occ in occurrences)
            {
                try
                {
                    if (Skip(occ)) continue;
                    if (occ.Definition is VirtualComponentDefinition) continue;
                    Document doc = occ.Definition.Document as Document;
                    if (doc is AssemblyDocument asm)
                    {
                        int element = AssemblyElement(asm, occ, occ.Name);
                        var children = new List<int>();
                        Walk(occ.SubOccurrences.Cast<ComponentOccurrence>(), children);
                        Aggregate(element, children);
                        into.Add(element);
                    }
                    else if (doc is PartDocument part)
                    {
                        int element = PartElement(part, occ, occ.Name);
                        if (element != 0) into.Add(element);
                    }
                }
                catch (Exception ex) { Log.Write("IFC: leaving out a component: " + ex.Message); }
            }
        }

        private bool Skip(ComponentOccurrence occ)
        {
            try { if (occ.Suppressed) return true; } catch { }
            try { if (!occ.Visible) return true; } catch { }
            try { if (occ.BOMStructure == BOMStructureEnum.kReferenceBOMStructure) return true; } catch { }
            if (_exclude.Length > 0)
            {
                string name = "";
                try { name = occ.Name ?? ""; } catch { }
                string file = "";
                try { file = ((Document)occ.Definition.Document).DisplayName ?? ""; } catch { }
                if (_exclude.Any(x => name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0 || file.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            }
            return false;
        }

        private int AssemblyElement(AssemblyDocument asm, ComponentOccurrence occ, string name)
        {
            string number = FileNamer.ReadProperty((Document)asm, "Part Number");
            string description = FileNamer.ReadProperty((Document)asm, "Description");
            string type = description.Length > 0 ? description : number;
            int element = Add("IFCELEMENTASSEMBLY(" + Guid() + ",$," + S(name) + "," + SOpt(description) + "," + SOpt(type) + "," +
                              R(Placement(occ)) + ",$," + SOpt(number) + ",$,$)");
            string key = "asm|" + (((Document)asm).FullDocumentName ?? name);
            if (!_parts.TryGetValue(key, out PartInfo info))
            {
                info = new PartInfo { Class = "IfcElementAssembly", PartNumber = number, Description = description };
                _parts[key] = info;
            }
            info.Elements.Add(element);
            return element;
        }

        /// <summary>The element of a part, or 0 for a part without a visible body (such as a Frame Generator skeleton).</summary>
        private int PartElement(PartDocument part, ComponentOccurrence occ, string name)
        {
            PartInfo info = Info(part);
            if (info.Map == 0 && occ != null) return 0;
            int shape = 0;
            if (info.Map != 0)
            {
                int item = Add("IFCMAPPEDITEM(" + R(info.Map) + "," + R(_identityOperator) + ")");
                int rep = Add("IFCSHAPEREPRESENTATION(" + R(_body) + ",'Body','MappedRepresentation',(" + R(item) + "))");
                shape = Add("IFCPRODUCTDEFINITIONSHAPE($,$,(" + R(rep) + "))");
            }
            // IfcMember, IfcPlate and IfcBuildingElementProxy share their attributes; the last one is PredefinedType
            int element = Add(info.Class.ToUpperInvariant() + "(" + Guid() + ",$," + S(name) + "," + SOpt(info.Description) + "," + SOpt(info.ObjectType) + "," +
                              R(Placement(occ)) + "," + (shape != 0 ? R(shape) : "$") + "," + SOpt(info.PartNumber) + ",$)");
            info.Elements.Add(element);
            if (info.Material.Length > 0)
            {
                if (!_byMaterial.TryGetValue(info.Material, out List<int> list)) _byMaterial[info.Material] = list = new List<int>();
                list.Add(element);
            }
            return element;
        }

        /// <summary>The place of a component in the top assembly (its transformation is in top-level coordinates), relative to the storey.</summary>
        private int Placement(ComponentOccurrence occ)
        {
            if (occ == null) return Add("IFCLOCALPLACEMENT(" + R(_storeyPlacement) + "," + R(_world) + ")");
            Matrix m = occ.Transformation;
            int point = Add("IFCCARTESIANPOINT((" + F(m.Cell[1, 4] * 10) + "," + F(m.Cell[2, 4] * 10) + "," + F(m.Cell[3, 4] * 10) + "))");
            int z = Add("IFCDIRECTION((" + F(m.Cell[1, 3]) + "," + F(m.Cell[2, 3]) + "," + F(m.Cell[3, 3]) + "))");
            int x = Add("IFCDIRECTION((" + F(m.Cell[1, 1]) + "," + F(m.Cell[2, 1]) + "," + F(m.Cell[3, 1]) + "))");
            int axes = Add("IFCAXIS2PLACEMENT3D(" + R(point) + "," + R(z) + "," + R(x) + ")");
            return Add("IFCLOCALPLACEMENT(" + R(_storeyPlacement) + "," + R(axes) + ")");
        }

        // ----- one part: what it is, and its mesh ------------------------------------------------------------------

        private PartInfo Info(PartDocument part)
        {
            Document doc = (Document)part;
            string key = doc.FullDocumentName ?? doc.DisplayName;
            if (_parts.TryGetValue(key, out PartInfo info)) return info;
            info = new PartInfo();
            _parts[key] = info;

            info.PartNumber = FileNamer.ReadProperty(doc, "Part Number");
            info.Description = FileNamer.ReadProperty(doc, "Description");
            try { info.Material = part.ActiveMaterial?.DisplayName ?? ""; } catch { }

            PartComponentDefinition def = part.ComponentDefinition;
            try
            {
                MassProperties mp = def.MassProperties;
                info.Mass = mp.Mass;                 // kg
                info.Volume = mp.Volume * 1e-6;      // cm3 to m3
            }
            catch (Exception ex) { Log.Write("IFC: no mass for " + doc.DisplayName + ": " + ex.Message); }

            bool sheetMetal = false;
            try
            {
                if (part.SubType == "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}" && def is SheetMetalComponentDefinition smd)
                {
                    sheetMetal = true;
                    info.Thickness = Math.Round(Convert.ToDouble(smd.Thickness.Value) * 10, 2);
                }
            }
            catch { }

            // the length: Frame Generator's G_L parameter, else "L=..." in the description
            bool frameMember = false;
            try
            {
                Parameter gl = def.Parameters["G_L"];
                info.Length = Math.Round(Convert.ToDouble(gl.Value) * 10, 1);
                frameMember = true;
            }
            catch { }
            Match lm = LengthInText.Match(info.Description);
            if (lm.Success)
            {
                if (info.Length == null && double.TryParse(lm.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, Inv, out double l)) info.Length = l;
                info.Profile = Regex.Replace(info.Description.Substring(0, lm.Index), @"[\s\-]+$", "").Trim();
            }

            string low = info.Description.ToLowerInvariant();
            if (sheetMetal || PlateWords.Any(w => low.Contains(w)))
            {
                info.Class = "IfcPlate";
                info.ObjectType = info.Thickness != null ? "PL" + info.Thickness.Value.ToString("0.##", Inv) : (info.Profile.Length > 0 ? info.Profile : "PL");
            }
            else if (frameMember || lm.Success || ProfileWords.IsMatch(info.Description))
            {
                info.Class = "IfcMember";
                // Frame Generator and Content Center: the family and size, such as "Rechthoekige Koker 100 x 50 x 3"
                string size = FileNamer.ReadProperty(doc, "Size Designation");
                if (size.Length == 0) size = FileNamer.ReadProperty(doc, "Stock Number");
                string family = FileNamer.ReadProperty(doc, "Family");
                if (size.Length > 0)
                {
                    string name = family.Length > 0 ? family : (info.Profile.Length > 0 ? info.Profile : info.Description);
                    info.Profile = name.IndexOf(size, StringComparison.OrdinalIgnoreCase) >= 0 ? name : (name + " " + size).Trim();
                }
                if (info.Profile.Length == 0) info.Profile = info.Description;
                info.ObjectType = info.Profile;
            }

            info.Map = Mesh(part, def);
            return info;
        }

        /// <summary>The mesh of the part's visible bodies, in part coordinates, as a representation map; 0 when there is none.</summary>
        private int Mesh(PartDocument part, PartComponentDefinition def)
        {
            int style = Style(part);
            var sets = new List<int>();
            foreach (SurfaceBody body in def.SurfaceBodies)
            {
                try
                {
                    if (!body.Visible) continue;
                    if (!Facets(body, out int vertexCount, out int facetCount, out double[] coords, out int[] indices)) continue;
                    if (vertexCount == 0 || facetCount == 0) continue;

                    var points = new StringBuilder("IFCCARTESIANPOINTLIST3D((");
                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (i > 0) points.Append(',');
                        points.Append('(').Append(F(coords[3 * i] * 10)).Append(',').Append(F(coords[3 * i + 1] * 10)).Append(',').Append(F(coords[3 * i + 2] * 10)).Append(')');
                    }
                    points.Append("))");
                    int list = Add(points.ToString());

                    // the vertex indices count from 1, as IFC wants them
                    var faces = new StringBuilder("IFCTRIANGULATEDFACESET(").Append(R(list)).Append(",$,$,(");
                    for (int f = 0; f < facetCount; f++)
                    {
                        if (f > 0) faces.Append(',');
                        faces.Append('(').Append(indices[3 * f]).Append(',').Append(indices[3 * f + 1]).Append(',').Append(indices[3 * f + 2]).Append(')');
                    }
                    faces.Append("),$)");
                    int set = Add(faces.ToString());
                    if (style != 0) Add("IFCSTYLEDITEM(" + R(set) + ",(" + R(style) + "),$)");
                    sets.Add(set);
                }
                catch (Exception ex) { Log.Write("IFC: no mesh for a body of " + ((Document)part).DisplayName + ": " + ex.Message); }
            }
            if (sets.Count == 0) return 0;
            int rep = Add("IFCSHAPEREPRESENTATION(" + R(_body) + ",'Body','Tessellation',(" + string.Join(",", sets.Select(R)) + "))");
            return Add("IFCREPRESENTATIONMAP(" + R(_world) + "," + R(rep) + ")");
        }

        /// <summary>
        /// The triangles of a body. CalculateFacets hands back its arrays through COM SAFEARRAYs, which need typed arrays
        /// going in: with C# "out" (null) Inventor answers DISP_E_TYPEMISMATCH, so it is called late bound with "ref".
        /// </summary>
        private bool Facets(SurfaceBody body, out int vertexCount, out int facetCount, out double[] coords, out int[] indices)
        {
            int vc = 0, fc = 0;
            double[] v = new double[0], n = new double[0];
            int[] ix = new int[0];
            dynamic b = body;
            b.CalculateFacets(_toleranceCm, ref vc, ref fc, ref v, ref n, ref ix);
            vertexCount = vc;
            facetCount = fc;
            coords = v;
            indices = ix;
            return v != null && ix != null && v.Length >= 3 * vc && ix.Length >= 3 * fc;
        }

        /// <summary>The colour of the part's appearance as a surface style (one per colour); 0 when it cannot be read.</summary>
        private int Style(PartDocument part)
        {
            double r = 0.7, g = 0.7, b = 0.7;
            try
            {
                Asset look = part.ActiveAppearance;
                if (look != null && look["generic_diffuse"] is ColorAssetValue colour)
                {
                    Inventor.Color c = colour.Value;
                    r = c.Red / 255.0; g = c.Green / 255.0; b = c.Blue / 255.0;
                }
            }
            catch { }
            string key = F(r) + "," + F(g) + "," + F(b);
            if (_styles.TryGetValue(key, out int style)) return style;
            int rgb = Add("IFCCOLOURRGB($," + key + ")");
            int shading = Add("IFCSURFACESTYLESHADING(" + R(rgb) + ",0.)");
            style = Add("IFCSURFACESTYLE($,.BOTH.,(" + R(shading) + "))");
            _styles[key] = style;
            return style;
        }

        // ----- properties, quantities and materials ------------------------------------------------------------------

        private void WriteProperties()
        {
            string setName = string.IsNullOrWhiteSpace(_settings.IfcPropertySet) ? "Onderdeel" : _settings.IfcPropertySet.Trim();
            foreach (PartInfo info in _parts.Values)
            {
                if (info.Elements.Count == 0) continue;
                string objects = "(" + string.Join(",", info.Elements.Select(R)) + ")";

                var props = new List<int>();
                void Label(string name, string value)
                {
                    if (!string.IsNullOrWhiteSpace(value)) props.Add(Add("IFCPROPERTYSINGLEVALUE(" + S(name) + ",$,IFCLABEL(" + S(value) + "),$)"));
                }
                void Measure(string name, string type, double? value)
                {
                    if (value != null && value.Value > 0) props.Add(Add("IFCPROPERTYSINGLEVALUE(" + S(name) + ",$," + type + "(" + F(value.Value) + "),$)"));
                }
                Label("Artikelnummer", info.PartNumber);
                Label("Omschrijving", info.Description);
                Label("Profiel", info.Profile);
                Label("Materiaal", info.Material);
                Measure("Lengte", "IFCPOSITIVELENGTHMEASURE", info.Length);
                Measure("Dikte", "IFCPOSITIVELENGTHMEASURE", info.Thickness);
                Measure("Volume", "IFCVOLUMEMEASURE", info.Volume);
                Measure("Gewicht", "IFCMASSMEASURE", info.Mass);
                if (props.Count > 0)
                {
                    int set = Add("IFCPROPERTYSET(" + Guid() + ",$," + S(setName) + ",$,(" + string.Join(",", props.Select(R)) + "))");
                    Add("IFCRELDEFINESBYPROPERTIES(" + Guid() + ",$,$,$," + objects + "," + R(set) + ")");
                }

                if (info.Class == "IfcElementAssembly") continue;
                var quantities = new List<int>();
                if (info.Volume > 0) quantities.Add(Add("IFCQUANTITYVOLUME('NetVolume',$,$," + F(info.Volume.Value) + ",$)"));
                if (info.Mass > 0) quantities.Add(Add("IFCQUANTITYWEIGHT('NetWeight',$,$," + F(info.Mass.Value) + ",$)"));
                if (info.Length > 0) quantities.Add(Add("IFCQUANTITYLENGTH('Length',$,$," + F(info.Length.Value) + ",$)"));
                if (quantities.Count > 0)
                {
                    string qto = info.Class == "IfcPlate" ? "Qto_PlateBaseQuantities" : info.Class == "IfcMember" ? "Qto_MemberBaseQuantities" : "Qto_BuildingElementProxyBaseQuantities";
                    int q = Add("IFCELEMENTQUANTITY(" + Guid() + ",$," + S(qto) + ",$,$,(" + string.Join(",", quantities.Select(R)) + "))");
                    Add("IFCRELDEFINESBYPROPERTIES(" + Guid() + ",$,$,$," + objects + "," + R(q) + ")");
                }
            }

            foreach (KeyValuePair<string, List<int>> kv in _byMaterial)
            {
                int material = Add("IFCMATERIAL(" + S(kv.Key) + ",$," + SOpt(Category(kv.Key)) + ")");
                Add("IFCRELASSOCIATESMATERIAL(" + Guid() + ",$,$,$,(" + string.Join(",", kv.Value.Select(R)) + ")," + R(material) + ")");
            }
        }

        /// <summary>A material category from the name: steel for 1.0xxx and S235/S355 grades, stainless for 1.4xxx, aluminium.</summary>
        private static string Category(string material)
        {
            string m = material ?? "";
            if (Regex.IsMatch(m, @"\b1\.0\d") || Regex.IsMatch(m, @"\bS\d{3}", RegexOptions.IgnoreCase) || m.IndexOf("staal", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("steel", StringComparison.OrdinalIgnoreCase) >= 0 && m.IndexOf("stainless", StringComparison.OrdinalIgnoreCase) < 0)
                return "Staal";
            if (Regex.IsMatch(m, @"\b1\.4\d") || m.IndexOf("rvs", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("stainless", StringComparison.OrdinalIgnoreCase) >= 0)
                return "RVS";
            if (m.IndexOf("alumin", StringComparison.OrdinalIgnoreCase) >= 0 || Regex.IsMatch(m, @"\bAW-|\bAlMg", RegexOptions.IgnoreCase))
                return "Aluminium";
            return "";
        }

        private void Aggregate(int whole, List<int> parts)
        {
            if (parts.Count == 0) return;
            Add("IFCRELAGGREGATES(" + Guid() + ",$,$,$," + R(whole) + ",(" + string.Join(",", parts.Select(R)) + "))");
        }

        // ----- STEP text ------------------------------------------------------------------------------------------

        private int Add(string entity)
        {
            int id = _next++;
            _w.Write('#');
            _w.Write(id.ToString(Inv));
            _w.Write('=');
            _w.Write(entity);
            _w.WriteLine(';');
            return id;
        }

        private static string R(int id) => "#" + id.ToString(Inv);

        private static string Refs(params int[] ids) => "(" + string.Join(",", ids.Select(R)) + ")";

        /// <summary>A real as STEP wants it: always with a decimal point.</summary>
        private static string F(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            if (Math.Abs(v) < 1e-9) return "0.";
            string s = v.ToString("0.0#####", Inv);
            return s;
        }

        /// <summary>A STEP string: quotes and backslashes doubled, everything outside printable ASCII as \X2\hhhh\X0\.</summary>
        private static string S(string text)
        {
            var b = new StringBuilder("'");
            string t = text ?? "";
            int i = 0;
            while (i < t.Length)
            {
                char c = t[i];
                if (c == '\'') { b.Append("''"); i++; }
                else if (c == '\\') { b.Append("\\\\"); i++; }
                else if (c >= 32 && c <= 126) { b.Append(c); i++; }
                else
                {
                    b.Append("\\X2\\");
                    while (i < t.Length && (t[i] < 32 || t[i] > 126))
                    {
                        b.Append(((int)t[i]).ToString("X4", Inv));
                        i++;
                    }
                    b.Append("\\X0\\");
                }
            }
            return b.Append('\'').ToString();
        }

        private static string SOpt(string text) => string.IsNullOrWhiteSpace(text) ? "$" : S(text.Trim());

        private const string Base64 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

        /// <summary>A new IFC GlobalId: 128 random bits as 22 characters of IFC's base 64.</summary>
        private static string Guid()
        {
            byte[] b = System.Guid.NewGuid().ToByteArray();
            var s = new StringBuilder(22);
            void Put(long value, int digits)
            {
                var chars = new char[digits];
                for (int k = digits - 1; k >= 0; k--) { chars[k] = Base64[(int)(value % 64)]; value /= 64; }
                s.Append(chars);
            }
            Put(b[0], 2);
            for (int k = 1; k < 16; k += 3) Put((b[k] << 16) | (b[k + 1] << 8) | b[k + 2], 4);
            return "'" + s + "'";
        }
    }
}
