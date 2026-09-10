using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using NEXUS.Models;

namespace NEXUS.Controls
{
    /// <summary>
    /// Plays a one-shot flash when a task actually transitions into Completed.
    /// A plain DataTrigger would replay on every list refresh; this only fires on the change.
    /// </summary>
    public static class CompletionFlash
    {
        public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
            "Target", typeof(MissionTask), typeof(CompletionFlash), new PropertyMetadata(null, OnTargetChanged));

        private static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
            "Handler", typeof(PropertyChangedEventHandler), typeof(CompletionFlash), new PropertyMetadata(null));

        public static void SetTarget(DependencyObject o, MissionTask value) => o.SetValue(TargetProperty, value);
        public static MissionTask GetTarget(DependencyObject o) => (MissionTask)o.GetValue(TargetProperty);

        private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element) return;

            if (e.OldValue is MissionTask oldTask &&
                element.GetValue(HandlerProperty) is PropertyChangedEventHandler oldHandler)
            {
                oldTask.PropertyChanged -= oldHandler;
                element.SetValue(HandlerProperty, null);
            }

            if (e.NewValue is not MissionTask task) return;

            var wasCompleted = task.IsCompleted;

            PropertyChangedEventHandler handler = (s, args) =>
            {
                if (args.PropertyName != nameof(MissionTask.State)) return;

                var isCompleted = task.IsCompleted;
                if (isCompleted && !wasCompleted) Play(element);
                wasCompleted = isCompleted;
            };

            task.PropertyChanged += handler;
            element.SetValue(HandlerProperty, handler);

            element.Unloaded += (s, args) =>
            {
                task.PropertyChanged -= handler;
                element.SetValue(HandlerProperty, null);
            };
        }

        private static void Play(FrameworkElement element)
        {
            var flash = new DoubleAnimationUsingKeyFrames();
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(System.TimeSpan.Zero)));
            flash.KeyFrames.Add(new EasingDoubleKeyFrame(1,
                KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(120)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            flash.KeyFrames.Add(new EasingDoubleKeyFrame(0,
                KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(760)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));

            element.BeginAnimation(UIElement.OpacityProperty, flash);
        }
    }
}
