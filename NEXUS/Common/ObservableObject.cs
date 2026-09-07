using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NEXUS.Common
{
    /// <summary>Minimal INotifyPropertyChanged base used by every model and view-model.</summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>Raises change notifications for several dependent (computed) properties at once.</summary>
        protected void OnPropertiesChanged(params string[] names)
        {
            foreach (var n in names) OnPropertyChanged(n);
        }

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
