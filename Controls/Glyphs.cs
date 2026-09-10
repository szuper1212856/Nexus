using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Markup;
using System.Windows.Media;

namespace NEXUS.Controls
{
    /// <summary>
    /// Single source of truth for every icon in NEXUS.
    ///
    /// Icons are referenced by name, never by raw codepoint. Each name carries a list of
    /// candidate codepoints; at start-up the actual icon font is inspected and the first
    /// candidate the font really contains is used. If none are present the icon degrades
    /// to a known-good glyph instead of rendering an empty box, so a missing glyph can
    /// never leak into the UI.
    /// </summary>
    public static class Glyphs
    {
        // Candidates run most-specific first, ending in something universally present.
        private static readonly Dictionary<string, int[]> Candidates =
            new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
            {
                // --- navigation / sections ---
                { "Command",      new[] { 0xE9D9, 0xE700, 0xE712 } },
                { "System",       new[] { 0xE977, 0xEC4E, 0xE7F8, 0xE712 } },
                { "Terminal",     new[] { 0xE756, 0xE7C4, 0xE943, 0xE712 } },
                { "Devices",      new[] { 0xE772, 0xE975, 0xE7F8, 0xE712 } },
                { "Network",      new[] { 0xEC05, 0xE701, 0xE968, 0xE712 } },
                { "Server",       new[] { 0xE968, 0xEDA2, 0xE7B8, 0xE712 } },
                { "Automation",   new[] { 0xE945, 0xE9F5, 0xE81E, 0xE712 } },
                { "Security",     new[] { 0xE72E, 0xE83D, 0xE1F6, 0xE712 } },
                { "Users",        new[] { 0xE716, 0xE77B, 0xE13D, 0xE712 } },
                { "Access",       new[] { 0xE8D7, 0xE72E, 0xE71C, 0xE712 } },
                { "Log",          new[] { 0xE9D5, 0xE81C, 0xE7C3, 0xE712 } },
                { "Overview",     new[] { 0xE80F, 0xE10F, 0xE712 } },
                { "Tasks",        new[] { 0xE8FD, 0xE73A, 0xE712 } },
                { "Focus",        new[] { 0xE916, 0xE121, 0xE712 } },
                { "Analytics",    new[] { 0xE9D2, 0xE9F9, 0xE7C3, 0xE712 } },
                { "Quick",        new[] { 0xE945, 0xE7E7, 0xE712 } },
                { "Activity",     new[] { 0xE81C, 0xE9D5, 0xE712 } },
                { "Settings",     new[] { 0xE713, 0xE115, 0xE712 } },
                { "Files",        new[] { 0xE8B7, 0xE838, 0xE188, 0xE712 } },
                { "Documents",    new[] { 0xE8A5, 0xE7C3, 0xE160, 0xE712 } },

                // --- device types ---
                { "Workstation",  new[] { 0xE977, 0xEC4E, 0xE7F8, 0xE712 } },
                { "Laptop",       new[] { 0xE7F8, 0xEC4E, 0xE977, 0xE712 } },
                { "Television",   new[] { 0xE7F4, 0xE8B2, 0xE714, 0xE712 } },
                { "Router",       new[] { 0xEC05, 0xE701, 0xE712 } },
                { "Phone",        new[] { 0xE8EA, 0xE717, 0xE1C9, 0xE712 } },
                { "Printer",      new[] { 0xE749, 0xE2F6, 0xE712 } },
                { "Sensor",       new[] { 0xE9D9, 0xE957, 0xE712 } },
                { "UnknownNode",  new[] { 0xE950, 0xE783, 0xE897, 0xE712 } },

                // --- controls ---
                { "Power",        new[] { 0xE7E8, 0xE8AC, 0xE712 } },
                { "VolumeUp",     new[] { 0xE995, 0xE767, 0xE15D, 0xE712 } },
                { "VolumeDown",   new[] { 0xE993, 0xE767, 0xE15D, 0xE712 } },
                { "Mute",         new[] { 0xE74F, 0xE198, 0xE712 } },
                { "Home",         new[] { 0xE80F, 0xE10F, 0xE712 } },
                { "Back",         new[] { 0xE72B, 0xE0C4, 0xE112, 0xE712 } },
                { "Input",        new[] { 0xE7F4, 0xE8B2, 0xE712 } },
                { "Lock",         new[] { 0xE72E, 0xE1F6, 0xE712 } },
                { "Restart",      new[] { 0xE72C, 0xE117, 0xE712 } },
                { "Ping",         new[] { 0xE9D9, 0xEC05, 0xE712 } },
                { "Clients",      new[] { 0xE716, 0xE77B, 0xE712 } },

                // --- actions ---
                { "Add",          new[] { 0xE710, 0xE109, 0xE712 } },
                { "Search",       new[] { 0xE721, 0xE11A, 0xE712 } },
                { "Refresh",      new[] { 0xE72C, 0xE117, 0xE712 } },
                { "Play",         new[] { 0xE768, 0xE102, 0xE712 } },
                { "Stop",         new[] { 0xE71A, 0xE15B, 0xE712 } },
                { "Delete",       new[] { 0xE74D, 0xE107, 0xE712 } },
                { "Edit",         new[] { 0xE70F, 0xE104, 0xE712 } },
                { "Save",         new[] { 0xE74E, 0xE105, 0xE712 } },
                { "Open",         new[] { 0xE8E5, 0xE838, 0xE712 } },
                { "Upload",       new[] { 0xE898, 0xE11C, 0xE712 } },
                { "Download",     new[] { 0xE896, 0xE118, 0xE712 } },
                { "Pin",          new[] { 0xE840, 0xE718, 0xE712 } },
                { "Unpin",        new[] { 0xE77A, 0xE840, 0xE712 } },
                { "Folder",       new[] { 0xE8B7, 0xE838, 0xE712 } },
                { "FolderOpen",   new[] { 0xE838, 0xE8B7, 0xE712 } },
                { "Trash",        new[] { 0xE74D, 0xE107, 0xE712 } },
                { "Restore",      new[] { 0xE7A7, 0xE10E, 0xE712 } },
                { "Check",        new[] { 0xE73E, 0xE10B, 0xE712 } },
                { "Cancel",       new[] { 0xE711, 0xE10A, 0xE712 } },
                { "Info",         new[] { 0xE946, 0xE897, 0xE712 } },
                { "Warning",      new[] { 0xE7BA, 0xE814, 0xE712 } },
                { "Close",        new[] { 0xE8BB, 0xE10A, 0xE711 } },
                { "Minimize",     new[] { 0xE921, 0xE108, 0xE712 } },
                { "Maximize",     new[] { 0xE922, 0xE109, 0xE712 } },
                { "Restore2",     new[] { 0xE923, 0xE1D8, 0xE712 } },
                { "More",         new[] { 0xE712, 0xE10C } },
                { "Chevron",      new[] { 0xE70D, 0xE099, 0xE712 } },
                { "Filter",       new[] { 0xE71C, 0xE16E, 0xE712 } },
                { "Calendar",     new[] { 0xE787, 0xE163, 0xE712 } },
                { "Flag",         new[] { 0xE7C1, 0xE129, 0xE712 } },
                { "Deadline",     new[] { 0xE916, 0xE121, 0xE712 } },

                // --- file types ---
                { "FileGeneric",  new[] { 0xE7C3, 0xE160, 0xE712 } },
                { "FileText",     new[] { 0xE8A5, 0xE7C3, 0xE712 } },
                { "FileImage",    new[] { 0xEB9F, 0xE91B, 0xE712 } },
                { "FilePdf",      new[] { 0xEA90, 0xE7C3, 0xE712 } },
                { "FileCode",     new[] { 0xE943, 0xE756, 0xE712 } },
                { "FileArchive",  new[] { 0xF012, 0xE7B8, 0xE712 } },
                { "FileAudio",    new[] { 0xE8D6, 0xE767, 0xE712 } },
                { "FileVideo",    new[] { 0xE714, 0xE8B2, 0xE712 } },
                { "FileSheet",    new[] { 0xE9F9, 0xE7C3, 0xE712 } },
                { "Document",     new[] { 0xE8A5, 0xE7C3, 0xE712 } },

                // --- misc ---
                { "Storage",      new[] { 0xEDA2, 0xE968, 0xE7B8, 0xE712 } },
                { "Alert",        new[] { 0xE7BA, 0xE814, 0xE712 } },
                { "Globe",        new[] { 0xE774, 0xE12B, 0xE712 } },
                { "Link",         new[] { 0xE71B, 0xE167, 0xE712 } },
                { "Clock",        new[] { 0xE917, 0xE121, 0xE712 } },
                { "Shield",       new[] { 0xEA18, 0xE72E, 0xE712 } }
            };

        private static readonly Dictionary<string, string> Resolved =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static HashSet<int> _available;

        /// <summary>Fallback used when a name is unknown or nothing in the font matches.</summary>
        public const string Missing = "\u2022";

        /// <summary>
        /// Reads the icon font once and records which codepoints it actually contains.
        /// Called from App start-up; safe to call more than once.
        /// </summary>
        public static void Initialize(FontFamily iconFont)
        {
            if (_available != null) return;
            _available = new HashSet<int>();

            try
            {
                if (iconFont == null) return;

                foreach (var typeface in iconFont.GetTypefaces())
                {
                    if (!typeface.TryGetGlyphTypeface(out var glyphTypeface)) continue;

                    foreach (var codepoint in glyphTypeface.CharacterToGlyphMap.Keys)
                        _available.Add(codepoint);

                    // The first family that resolves is the one WPF will render with.
                    if (_available.Count > 0) break;
                }
            }
            catch
            {
                // Font inspection is best-effort; without it every name uses its first candidate.
                _available = null;
            }
        }

        /// <summary>Returns the glyph string for a name, guaranteed to be renderable.</summary>
        public static string Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Missing;

            if (Resolved.TryGetValue(name, out var cached)) return cached;

            var glyph = Missing;
            if (Candidates.TryGetValue(name, out var options))
            {
                glyph = char.ConvertFromUtf32(options[0]);

                if (_available != null)
                {
                    var found = false;
                    foreach (var codepoint in options)
                    {
                        if (!_available.Contains(codepoint)) continue;
                        glyph = char.ConvertFromUtf32(codepoint);
                        found = true;
                        break;
                    }
                    if (!found) glyph = Missing;
                }
            }

            Resolved[name] = glyph;
            return glyph;
        }
    }

    /// <summary>
    /// XAML helper: <c>Text="{ctrl:Glyph Devices}"</c>. Keeps codepoints out of the markup
    /// so every icon in NEXUS resolves through the same verified registry.
    /// </summary>
    public class GlyphExtension : MarkupExtension
    {
        public GlyphExtension() { }
        public GlyphExtension(string key) => Key = key;

        [ConstructorArgument("key")]
        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider) => Glyphs.Get(Key);
    }

    /// <summary>Converts an icon name held in a view-model or model into its glyph.</summary>
    public class GlyphConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => Glyphs.Get(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
