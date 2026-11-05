using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace StepExport.Core
{
    /// <summary>An iProperty in the file name or the folder name, with text to put before and after its value.</summary>
    internal sealed class PropertySpec
    {
        public string Name { get; set; } = "";
        public string Before { get; set; } = "";
        public string After { get; set; } = "";

        /// <summary>Remember: the value filled in is kept (also in the next session) and used for every model, instead of the iProperty.</summary>
        public bool Remember { get; set; }

        /// <summary>The remembered value; only used when <see cref="Remember"/> is on.</summary>
        public string Value { get; set; } = "";
    }

    /// <summary>The panel's settings, remembered between sessions in %APPDATA%\StepExport\settings.json.</summary>
    internal sealed class ExportSettings
    {
        /// <summary>The iProperties after the model name in the file name, in this order.</summary>
        public List<PropertySpec> NameProperties { get; set; } = new List<PropertySpec>();

        /// <summary>The iProperties that make the name of the folder the STEP files go in (inside the document folder), in this order.</summary>
        public List<PropertySpec> FolderProperties { get; set; } = new List<PropertySpec>
        {
            new PropertySpec { Name = "ordernummer", Before = "Tekeningenpakket-STP (", After = ")" }
        };

        /// <summary>Put today's date at the end of the folder name.</summary>
        public bool FolderAddDate { get; set; } = true;

        /// <summary>The .NET format of the date in the folder name.</summary>
        public string FolderDateFormat { get; set; } = "dd-MM-yyyy";

        /// <summary>Between the parts of the folder name.</summary>
        public string FolderSeparator { get; set; } = " ";

        /// <summary>Put today's date at the end of the file name.</summary>
        public bool AddDate { get; set; }

        /// <summary>The .NET format of the date.</summary>
        public string DateFormat { get; set; } = "yyyy-MM-dd";

        /// <summary>Between the parts of a name (model name, iProperties, date).</summary>
        public string Separator { get; set; } = "_";

        /// <summary>An iProperty value that means "not filled in yet" (for example 160100); refused. Empty: none.</summary>
        public string Placeholder { get; set; } = "160100";


        /// <summary>STEP protocol: 2 = AP203, 3 = AP214, 5 = AP242.</summary>
        public int Protocol { get; set; } = 3;

        /// <summary>The file extension, .stp or .step.</summary>
        public string Extension { get; set; } = ".stp";

        /// <summary>Opens the folder in Explorer after an export without errors.</summary>
        public bool OpenFolder { get; set; } = true;

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StepExport", "settings.json");

        public static ExportSettings Load()
        {
            ExportSettings s = null;
            try
            {
                if (File.Exists(FilePath))
                    s = JsonSerializer.Deserialize<ExportSettings>(File.ReadAllText(FilePath), Json);
            }
            catch (Exception ex) { Log.Write("Reading " + FilePath + " failed: " + ex.Message); }
            s = s ?? new ExportSettings();
            if (s.NameProperties == null) s.NameProperties = new List<PropertySpec>();
            if (s.FolderProperties == null) s.FolderProperties = new List<PropertySpec>();
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
            }
            catch (Exception ex) { Log.Write("Writing " + FilePath + " failed: " + ex.Message); }
        }
    }
}
