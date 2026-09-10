using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace NEXUS.Controls
{
    /// <summary>
    /// WPF has no letter-spacing property. This attached property rebuilds the string with
    /// thin spaces between characters, which gives section headers their instrument-panel feel.
    /// </summary>
    public static class Tracking
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(Tracking), new PropertyMetadata(null, OnTextChanged));

        public static void SetText(DependencyObject o, string value) => o.SetValue(TextProperty, value);
        public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock block) return;
            var source = e.NewValue as string ?? "";

            var sb = new StringBuilder(source.Length * 2);
            for (int i = 0; i < source.Length; i++)
            {
                sb.Append(source[i]);
                if (i < source.Length - 1) sb.Append('\u2009');
            }
            block.Text = sb.ToString();
        }
    }
}
