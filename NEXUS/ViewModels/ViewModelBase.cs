using NEXUS.Common;

namespace NEXUS.ViewModels
{
    public abstract class ViewModelBase : ObservableObject
    {
        /// <summary>Called by the shell each time the section becomes visible.</summary>
        public virtual void OnActivated() { }

        /// <summary>Called on the shell clock tick for sections that show live values.</summary>
        public virtual void OnTick() { }
    }

    public class NavSection : ObservableObject
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Glyph { get; set; }
        public ViewModelBase ViewModel { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}
