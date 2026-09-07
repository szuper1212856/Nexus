using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace NEXUS.Services
{
    public class ThemeOption
    {
        public string Name { get; set; }
        public string Bg0 { get; set; }
        public string Bg1 { get; set; }
        public string Panel { get; set; }
        public string PanelAlt { get; set; }
        public string Border { get; set; }
        public string BorderSoft { get; set; }
        public string Text { get; set; }
        public string TextDim { get; set; }
        public string TextMuted { get; set; }
        public Brush Swatch => new SolidColorBrush(Parse(Bg1));
        public static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    }

    public class AccentOption
    {
        public string Name { get; set; }
        public string Hex { get; set; }
        public Brush Swatch => new SolidColorBrush(ThemeOption.Parse(Hex));
    }

    /// <summary>
    /// Applies themes by mutating the Color of the shared SolidColorBrush resources.
    /// Because the brushes are never frozen, every StaticResource reference updates live.
    /// </summary>
    public class ThemeService
    {
        public IReadOnlyList<ThemeOption> Themes { get; } = new List<ThemeOption>
        {
            new ThemeOption
            {
                Name = "Deep Space",
                Bg0 = "#05070B", Bg1 = "#0A0E15", Panel = "#111823", PanelAlt = "#0D131C",
                Border = "#1D2836", BorderSoft = "#161F2B",
                Text = "#E8EFF9", TextDim = "#95A7BF", TextMuted = "#5C7089"
            },
            new ThemeOption
            {
                Name = "Graphite",
                Bg0 = "#0A0A0C", Bg1 = "#101013", Panel = "#17181C", PanelAlt = "#131418",
                Border = "#26282E", BorderSoft = "#1D1F24",
                Text = "#ECECEF", TextDim = "#9C9EA6", TextMuted = "#6A6D76"
            },
            new ThemeOption
            {
                Name = "Abyss",
                Bg0 = "#03060A", Bg1 = "#060B12", Panel = "#0B1220", PanelAlt = "#080D17",
                Border = "#16233A", BorderSoft = "#101A2C",
                Text = "#DFE9F7", TextDim = "#8699B5", TextMuted = "#51647F"
            },
            new ThemeOption
            {
                Name = "Slate",
                Bg0 = "#0D1117", Bg1 = "#12181F", Panel = "#1A222C", PanelAlt = "#151C25",
                Border = "#2A3541", BorderSoft = "#212932",
                Text = "#EAF0F6", TextDim = "#9AAABC", TextMuted = "#68798C"
            }
        };

        public IReadOnlyList<AccentOption> Accents { get; } = new List<AccentOption>
        {
            new AccentOption { Name = "Cyan",    Hex = "#22D3EE" },
            new AccentOption { Name = "Azure",   Hex = "#3B82F6" },
            new AccentOption { Name = "Violet",  Hex = "#8B5CF6" },
            new AccentOption { Name = "Emerald", Hex = "#10D9A0" },
            new AccentOption { Name = "Amber",   Hex = "#F5A524" },
            new AccentOption { Name = "Rose",    Hex = "#F43F5E" }
        };

        public void Apply(string themeName, string accentName)
        {
            var theme = Find(Themes, t => t.Name == themeName) ?? Themes[0];
            var accent = Find(Accents, a => a.Name == accentName) ?? Accents[0];

            SetBrush("BrushBg0", theme.Bg0);
            SetBrush("BrushBg1", theme.Bg1);
            SetBrush("BrushPanel", theme.Panel);
            SetBrush("BrushPanelAlt", theme.PanelAlt);
            SetBrush("BrushBorder", theme.Border);
            SetBrush("BrushBorderSoft", theme.BorderSoft);
            SetBrush("BrushText", theme.Text);
            SetBrush("BrushTextDim", theme.TextDim);
            SetBrush("BrushTextMuted", theme.TextMuted);

            var accentColor = ThemeOption.Parse(accent.Hex);
            SetBrush("BrushAccent", accentColor);
            SetBrush("BrushAccentSoft", WithAlpha(accentColor, 0x33));
            SetBrush("BrushAccentFaint", WithAlpha(accentColor, 0x1A));

            // Gradient resources are mutated stop-by-stop so they track the theme too.
            SetGradient("BrushWindow", new[]
            {
                WithAlpha(ThemeOption.Parse(theme.Bg0), 0xFF),
                WithAlpha(ThemeOption.Parse(theme.Bg1), 0xFF),
                Blend(ThemeOption.Parse(theme.Bg0), accentColor, 0.06)
            });

            SetGradient("BrushPanelGradient", new[]
            {
                WithAlpha(ThemeOption.Parse(theme.Panel), 0xE6),
                WithAlpha(ThemeOption.Parse(theme.PanelAlt), 0xCC)
            });

            SetGradient("BrushAccentGradient", new[]
            {
                accentColor,
                Blend(accentColor, Colors.White, 0.25)
            });

            SetGradient("BrushAccentGlow", new[]
            {
                WithAlpha(accentColor, 0x40),
                WithAlpha(accentColor, 0x00)
            });
        }

        private static T Find<T>(IReadOnlyList<T> list, Func<T, bool> predicate)
        {
            foreach (var item in list) if (predicate(item)) return item;
            return default;
        }

        private static void SetBrush(string key, string hex) => SetBrush(key, ThemeOption.Parse(hex));

        private static void SetBrush(string key, Color color)
        {
            if (Application.Current?.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
                brush.Color = color;
        }

        private static void SetGradient(string key, Color[] colors)
        {
            if (Application.Current?.Resources[key] is GradientBrush brush && !brush.IsFrozen)
                for (int i = 0; i < brush.GradientStops.Count && i < colors.Length; i++)
                    brush.GradientStops[i].Color = colors[i];
        }

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        private static Color Blend(Color a, Color b, double amount)
            => Color.FromArgb(255,
                (byte)(a.R + (b.R - a.R) * amount),
                (byte)(a.G + (b.G - a.G) * amount),
                (byte)(a.B + (b.B - a.B) * amount));
    }
}
