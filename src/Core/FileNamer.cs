using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Inventor;
using Path = System.IO.Path;

namespace StepExport.Core
{
    /// <summary>Builds file and folder names from parts (model name, iProperty values with text before and after, date) and reads iProperties.</summary>
    internal static class FileNamer
    {
        /// <summary>The text of one iProperty part: before + value + after, or "" when the value is empty.</summary>
        public static string Part(PropertySpec spec, string value)
        {
            value = (value ?? "").Trim();
            return value.Length == 0 ? "" : (spec.Before ?? "") + value + (spec.After ?? "");
        }

        /// <summary>The non-empty parts joined with the separator, without characters Windows refuses in a name.</summary>
        public static string Join(IEnumerable<string> parts, string separator)
        {
            string name = string.Join(separator ?? "", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim().TrimEnd('.');
        }

        public static string DateText(string format, DateTime date)
        {
            try { return date.ToString(string.IsNullOrWhiteSpace(format) ? "yyyy-MM-dd" : format); }
            catch (FormatException) { return date.ToString("yyyy-MM-dd"); }
        }

        public static string Extension(ExportSettings s)
        {
            string ext = string.IsNullOrWhiteSpace(s.Extension) ? ".stp" : s.Extension.Trim();
            return ext.StartsWith(".") ? ext : "." + ext;
        }

        /// <summary>Only the digits: 160261496UR and "160261496 UR" give 160261496.</summary>
        public static string Digits(string text)
        {
            var digits = new StringBuilder();
            foreach (char c in text ?? "") if (c >= '0' && c <= '9') digits.Append(c);
            return digits.ToString();
        }

        /// <summary>True when the value is the placeholder for "not filled in yet" (also with letters after it, such as 160100UR).</summary>
        public static bool IsPlaceholder(string value, ExportSettings s)
        {
            string d = Digits(s.Placeholder);
            return d.Length > 0 && Digits(value) == d;
        }

        /// <summary>
        /// The value of an iProperty by its name as typed (custom properties first, then every other set, by internal or
        /// display name); "" when there is no such property.
        /// </summary>
        public static string ReadProperty(Document doc, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || doc == null) return "";
            try { return Text(doc.PropertySets["User Defined Properties"][name].Value); } catch { }
            try
            {
                foreach (PropertySet set in doc.PropertySets)
                    foreach (Property p in set)
                    {
                        string display = "";
                        try { display = p.DisplayName; } catch { }
                        if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(display, name, StringComparison.OrdinalIgnoreCase)) return Text(p.Value);
                    }
            }
            catch { }
            return "";
        }

        private static string Text(object value) => (Convert.ToString(value) ?? "").Trim();
    }
}
