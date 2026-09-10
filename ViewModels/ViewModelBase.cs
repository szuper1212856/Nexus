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

    /// <summary>
    /// One entry in the sidebar. Entries with IsHeader set are group captions and are
    /// rendered as labels rather than navigation targets.
    /// </summary>
    public class NavSection : ObservableObject
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Glyph { get; set; }
        public string Group { get; set; } = "";
        public bool IsHeader { get; set; }
        public ViewModelBase ViewModel { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        private string _badge = "";
        /// <summary>Optional count shown on the right of the entry, e.g. open alerts.</summary>
        public string Badge
        {
            get => _badge;
            set { if (Set(ref _badge, value)) OnPropertyChanged(nameof(HasBadge)); }
        }

        public bool HasBadge => !string.IsNullOrEmpty(Badge);

        public static NavSection Header(string caption)
            => new NavSection { IsHeader = true, Label = caption, Key = null };
    }
}
