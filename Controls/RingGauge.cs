using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NEXUS.Controls
{
    /// <summary>
    /// Lightweight circular progress indicator drawn directly with a DrawingContext.
    /// Value changes are eased into DisplayValue so the arc animates without a Storyboard.
    /// </summary>
    public class RingGauge : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(RingGauge),
            new PropertyMetadata(0d, OnValueChanged));

        public static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
            nameof(DisplayValue), typeof(double), typeof(RingGauge),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
            nameof(RingThickness), typeof(double), typeof(RingGauge),
            new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            nameof(TrackBrush), typeof(Brush), typeof(RingGauge),
            new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
            nameof(ValueBrush), typeof(Brush), typeof(RingGauge),
            new FrameworkPropertyMetadata(Brushes.Cyan, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AnimateProperty = DependencyProperty.Register(
            nameof(Animate), typeof(bool), typeof(RingGauge), new PropertyMetadata(true));

        /// <summary>Target progress, 0 to 1.</summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double DisplayValue
        {
            get => (double)GetValue(DisplayValueProperty);
            set => SetValue(DisplayValueProperty, value);
        }

        public double RingThickness
        {
            get => (double)GetValue(RingThicknessProperty);
            set => SetValue(RingThicknessProperty, value);
        }

        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public Brush ValueBrush
        {
            get => (Brush)GetValue(ValueBrushProperty);
            set => SetValue(ValueBrushProperty, value);
        }

        public bool Animate
        {
            get => (bool)GetValue(AnimateProperty);
            set => SetValue(AnimateProperty, value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var gauge = (RingGauge)d;
            var target = Clamp((double)e.NewValue);

            if (!gauge.Animate)
            {
                gauge.BeginAnimation(DisplayValueProperty, null);
                gauge.DisplayValue = target;
                return;
            }

            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(520))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            gauge.BeginAnimation(DisplayValueProperty, anim);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var side = Math.Min(ActualWidth, ActualHeight);
            if (side <= 0 || RingThickness <= 0) return;

            var radius = (side - RingThickness) / 2.0;
            if (radius <= 0) return;

            var center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);

            var trackPen = new Pen(TrackBrush, RingThickness);
            dc.DrawEllipse(null, trackPen, center, radius, radius);

            var v = Clamp(DisplayValue);
            if (v <= 0.0005) return;

            var valuePen = new Pen(ValueBrush, RingThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            if (v >= 0.9995)
            {
                dc.DrawEllipse(null, valuePen, center, radius, radius);
                return;
            }

            var sweep = v * 360.0;
            var start = new Point(center.X, center.Y - radius);
            var rad = (sweep - 90.0) * Math.PI / 180.0;
            var end = new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();

            dc.DrawGeometry(null, valuePen, geometry);
        }

        private static double Clamp(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }
    }
}
