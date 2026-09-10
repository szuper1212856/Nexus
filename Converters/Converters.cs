using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NEXUS.Models;

namespace NEXUS.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public bool UseHidden { get; set; }

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var b = value is bool v && v;
            if (Invert) b = !b;
            return b ? Visibility.Visible : (UseHidden ? Visibility.Hidden : Visibility.Collapsed);
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is Visibility vis && vis == Visibility.Visible ? !Invert : Invert;
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c) => !(value is bool b && b);
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => !(value is bool b && b);
    }

    /// <summary>Collapses when the bound string is null or whitespace.</summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var has = !string.IsNullOrWhiteSpace(value as string);
            if (Invert) has = !has;
            return has ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var count = System.Convert.ToInt32(value ?? 0);
            var show = count > 0;
            if (Invert) show = !show;
            return show ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Maps a task priority onto its signal colour.</summary>
    public class PriorityBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var key = value is TaskPriority pr ? pr switch
            {
                TaskPriority.Critical => "BrushCritical",
                TaskPriority.High => "BrushHigh",
                TaskPriority.Normal => "BrushNormal",
                _ => "BrushLow"
            } : "BrushLow";
            return Application.Current.Resources[key];
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public class StateBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var key = value is TaskState s ? s switch
            {
                TaskState.Planned => "BrushPlanned",
                TaskState.InProgress => "BrushAccent",
                TaskState.Blocked => "BrushCritical",
                _ => "BrushCompleted"
            } : "BrushPlanned";
            return Application.Current.Resources[key];
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public class ActivityBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var key = value is ActivityKind k ? k switch
            {
                ActivityKind.Completed => "BrushCompleted",
                ActivityKind.Created => "BrushAccent",
                ActivityKind.Deleted => "BrushCritical",
                ActivityKind.Priority => "BrushHigh",
                ActivityKind.Status => "BrushNormal",
                ActivityKind.Focus => "BrushLow",
                ActivityKind.Reopened => "BrushHigh",
                ActivityKind.Data => "BrushAccent",
                _ => "BrushTextMuted"
            } : "BrushTextMuted";
            return Application.Current.Resources[key];
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public class ToastBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var key = value is Services.ToastKind k ? k switch
            {
                Services.ToastKind.Success => "BrushCompleted",
                Services.ToastKind.Warning => "BrushHigh",
                Services.ToastKind.Critical => "BrushCritical",
                _ => "BrushAccent"
            } : "BrushAccent";
            return Application.Current.Resources[key];
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Resolves a resource key held in a view-model into the live brush instance.</summary>
    public class ResourceKeyToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var key = value as string;
            if (string.IsNullOrEmpty(key)) key = "BrushAccent";
            return Application.Current.Resources[key] ?? Application.Current.Resources["BrushAccent"];
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Turns a 0-1 fraction into a star GridLength, giving resolution-independent bars.</summary>
    public class FractionToStarConverter : IValueConverter
    {
        public bool Remainder { get; set; }

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var f = value == null ? 0 : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(f) || double.IsInfinity(f)) f = 0;
            f = Math.Max(0, Math.Min(1, f));
            var used = Remainder ? 1 - f : f;
            return new GridLength(Math.Max(0.0001, used), GridUnitType.Star);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Scales a 0-1 fraction to a pixel height. ConverterParameter is the max height.</summary>
    public class FractionToHeightConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var f = value == null ? 0 : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(f) || double.IsInfinity(f)) f = 0;
            var max = 100.0;
            if (p != null) double.TryParse(p.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out max);
            return Math.Max(2, Math.Min(1, Math.Max(0, f)) * max);
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Compares the bound value to the ConverterParameter. Used for radio-style toggles.</summary>
    public class EqualityToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value != null && p != null && string.Equals(value.ToString(), p.ToString(), StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is bool b && b ? p : Binding.DoNothing;
    }

    public class EqualityToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var match = value != null && p != null && string.Equals(value.ToString(), p.ToString(), StringComparison.OrdinalIgnoreCase);
            if (Invert) match = !match;
            return match ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Multiplies a value by the ConverterParameter. Handy for opacity and sizing.</summary>
    public class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var v = value == null ? 0 : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var m = 1.0;
            if (p != null) double.TryParse(p.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out m);
            return v * m;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Formats a 0-1 fraction as a whole percentage string.</summary>
    public class FractionToPercentTextConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var f = value == null ? 0 : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return Math.Round(f * 100) + "%";
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }


    /// <summary>Compares two bound values. Used where ConverterParameter cannot be bound.</summary>
    public class MultiEqualityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object p, CultureInfo c)
        {
            if (values == null || values.Length < 2) return false;
            if (values[0] == null || values[1] == null) return false;
            return string.Equals(values[0].ToString(), values[1].ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c)
            => new object[] { Binding.DoNothing, Binding.DoNothing };
    }


    /// <summary>True when both bound values are the same instance. Drives master/detail row selection.</summary>
    public class SameObjectConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object p, CultureInfo c)
            => values != null && values.Length >= 2 && values[0] != null &&
               ReferenceEquals(values[0], values[1]);

        public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c)
            => new object[] { Binding.DoNothing, Binding.DoNothing };
    }


    /// <summary>Scales a 0-1 fraction into a pixel height, for the small history charts.</summary>
    public class FractionToHeightConverter : IValueConverter
    {
        public double Scale { get; set; } = 40;

        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var fraction = value is double d ? d : 0;
            if (fraction < 0) fraction = 0;
            if (fraction > 1) fraction = 1;
            return Math.Max(1.0, fraction * Scale);
        }

        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Collapses when the bound reference is null.</summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var has = value != null;
            if (Invert) has = !has;
            return has ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }


    /// <summary>Renders a Permission enum value as its human-readable label.</summary>
    public class PermissionLabelConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is NEXUS.Models.Permission perm ? NEXUS.Models.Permissions.Label(perm) : "";
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Green when true, muted when false. Used for allow/deny marks.</summary>
    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            var on = value is bool b && b;
            var key = on ? "BrushCompleted" : "BrushTextMuted";
            return Application.Current?.TryFindResource(key) as Brush;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Completed tasks are rendered dimmed rather than hidden.</summary>
    public class BoolToOpacityConverter : IValueConverter
    {
        public double TrueValue { get; set; } = 0.55;
        public double FalseValue { get; set; } = 1.0;
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b && b ? TrueValue : FalseValue;
        public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}
