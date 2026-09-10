using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    /// <summary>
    /// The file library: shelves on the left, a dense file table in the middle,
    /// and an inspector with preview and metadata on the right.
    /// </summary>
    public class FilesViewModel : ViewModelBase
    {
        private readonly FileVault _vault;
        private readonly EventBus _bus;

        private NexusFolder _selectedFolder;
        private NexusFile _selected;
        private string _search = "";
        private string _scope = "ALL";
        private string _previewText = "";
        private string _renameBuffer = "";
        private string _newFolderName = "";

        public FilesViewModel(ShellViewModel shell)
        {
            Shell = shell;
            _vault = AppServices.Files;
            _bus = AppServices.Bus;
            _vault.Changed += Refresh;

            SelectFolderCommand = new RelayCommand(p => SelectedFolder = p as NexusFolder);
            SelectFileCommand = new RelayCommand(p => { if (p is NexusFile f) Selected = f; });
            SetScopeCommand = new RelayCommand(p => Scope = p?.ToString() ?? "ALL");

            UploadCommand = new RelayCommand(_ => Upload());
            NewFolderCommand = new RelayCommand(_ => NewFolder(), _ => !string.IsNullOrWhiteSpace(NewFolderName));
            NewDocumentCommand = new RelayCommand(_ => NewDocument());

            OpenCommand = new RelayCommand(p => Open((p as NexusFile) ?? Selected), _ => Selected != null);
            ExportCommand = new RelayCommand(_ => Export(), _ => Selected != null);
            RenameCommand = new RelayCommand(_ => Rename(), _ => Selected != null && !string.IsNullOrWhiteSpace(RenameBuffer));
            PinCommand = new RelayCommand(p => _vault.TogglePin((p as NexusFile) ?? Selected));
            DeleteCommand = new RelayCommand(_ => Delete(), _ => Selected != null);
            RestoreCommand = new RelayCommand(_ => _vault.Restore(Selected), _ => Selected != null && Selected.IsTrashed);
            PurgeCommand = new RelayCommand(_ => Purge(), _ => Selected != null && Selected.IsTrashed);
            EmptyTrashCommand = new RelayCommand(_ => EmptyTrash());
            MoveCommand = new RelayCommand(p => { if (p is NexusFolder folder) _vault.Move(Selected, folder.Id); });
            OpenTaskCommand = new RelayCommand(_ => Shell.Navigate("TASKS"));

            SelectedFolder = _vault.SystemFolder(FolderKind.MyDocuments);
            Refresh();
        }

        public ShellViewModel Shell { get; }

        public ObservableCollection<NexusFolder> Folders => _vault.Folders;
        public ObservableCollection<NexusFile> Visible { get; } = new ObservableCollection<NexusFile>();
        public ObservableCollection<NexusFile> RecentFiles { get; } = new ObservableCollection<NexusFile>();
        public ObservableCollection<NexusFile> FrequentFiles { get; } = new ObservableCollection<NexusFile>();
        public ObservableCollection<NexusFile> PinnedFiles { get; } = new ObservableCollection<NexusFile>();
        public ObservableCollection<SystemEvent> SelectedActivity { get; } = new ObservableCollection<SystemEvent>();

        public ICommand SelectFolderCommand { get; }
        public ICommand SelectFileCommand { get; }
        public ICommand SetScopeCommand { get; }
        public ICommand UploadCommand { get; }
        public ICommand NewFolderCommand { get; }
        public ICommand NewDocumentCommand { get; }
        public ICommand OpenCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand PinCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand PurgeCommand { get; }
        public ICommand EmptyTrashCommand { get; }
        public ICommand MoveCommand { get; }
        public ICommand OpenTaskCommand { get; }

        public IReadOnlyList<string> Scopes { get; } = new[] { "ALL", "RECENT", "FREQUENT", "PINNED", "DOCUMENTS", "TRASH" };

        public NexusFolder SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (!Set(ref _selectedFolder, value)) return;
                if (value != null && value.Kind == FolderKind.Trash) _scope = "TRASH";
                else if (_scope == "TRASH") _scope = "ALL";
                OnPropertiesChanged(nameof(Scope), nameof(LocationLabel), nameof(IsTrashView));
                Refresh();
            }
        }

        public NexusFile Selected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;

                RenameBuffer = value?.Name ?? "";
                PreviewText = value != null && value.IsTextPreviewable ? _vault.ReadText(value) : "";
                LoadActivity();

                OnPropertiesChanged(nameof(HasSelection), nameof(SelectedLocation), nameof(SelectedPath),
                    nameof(CanPreviewText), nameof(CanPreviewImage), nameof(PreviewNotice), nameof(IsTrashView));
            }
        }

        public bool HasSelection => Selected != null;
        public bool IsTrashView => Scope == "TRASH" || (SelectedFolder?.Kind == FolderKind.Trash);

        public string Scope
        {
            get => _scope;
            set { if (Set(ref _scope, value)) { OnPropertyChanged(nameof(IsTrashView)); Refresh(); } }
        }

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) Refresh(); }
        }

        public string PreviewText
        {
            get => _previewText;
            private set => Set(ref _previewText, value);
        }

        public string RenameBuffer
        {
            get => _renameBuffer;
            set => Set(ref _renameBuffer, value);
        }

        public string NewFolderName
        {
            get => _newFolderName;
            set => Set(ref _newFolderName, value);
        }

        public bool CanPreviewText => Selected != null && Selected.IsTextPreviewable;
        public bool CanPreviewImage => Selected != null && Selected.IsImagePreviewable;

        public string PreviewNotice => Selected == null ? ""
            : CanPreviewText || CanPreviewImage
                ? ""
                : "No in-app preview is available for " + Selected.TypeLabel.ToLowerInvariant()
                  + ". Use OPEN to hand it to the system's default application.";

        public string SelectedLocation => Selected == null ? "" : _vault.LocationOf(Selected);
        public string SelectedPath => Selected == null ? "" : (_vault.PathOf(Selected) ?? "Stored inside NEXUS");
        public string LocationLabel => SelectedFolder == null ? "ALL FILES" : SelectedFolder.Name.ToUpperInvariant();

        public int TotalCount => _vault.ActiveCount;
        public int DocumentCount => _vault.DocumentCount;
        public int PinnedCount => _vault.PinnedCount;
        public int TrashCount => _vault.TrashCount;
        public string TotalSizeDisplay => _vault.TotalSizeDisplay;
        public string VaultRoot => _vault.VaultRoot;

        public override void OnActivated() => Refresh();

        public override void OnTick()
        {
            foreach (var f in Visible) f.RefreshTimeDependent();
        }

        public void SelectPayload(object payload)
        {
            if (payload is not NexusFile file) return;

            Scope = file.IsTrashed ? "TRASH" : "ALL";
            SelectedFolder = _vault.FolderOf(file);
            Selected = file;
        }

        /// <summary>Entry point for drag-and-drop from the view's code-behind.</summary>
        public void ImportPaths(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;

            var target = SelectedFolder != null && SelectedFolder.Kind != FolderKind.Trash
                ? SelectedFolder.Id
                : _vault.SystemFolder(FolderKind.MyDocuments)?.Id;

            var count = _vault.Import(paths, target);
            AppServices.Toasts.Show("FILES RECEIVED",
                count == 0 ? "Nothing could be imported." : count + " file(s) added to the vault.",
                count == 0 ? ToastKind.Warning : ToastKind.Success);
            Refresh();
        }

        private void Upload()
        {
            var dialog = new OpenFileDialog { Multiselect = true, Title = "Upload files into NEXUS" };
            if (dialog.ShowDialog() != true) return;
            ImportPaths(dialog.FileNames);
        }

        private void NewFolder()
        {
            var parent = SelectedFolder != null && SelectedFolder.Kind != FolderKind.Trash
                ? SelectedFolder.Id : (Guid?)null;

            var folder = _vault.CreateFolder(NewFolderName, parent);
            NewFolderName = "";
            if (folder != null) SelectedFolder = folder;
        }

        private void NewDocument()
        {
            var target = SelectedFolder != null && SelectedFolder.Kind != FolderKind.Trash
                ? SelectedFolder.Id : null;

            var doc = _vault.CreateDocument("Untitled document", target);
            Shell.OpenDocument(doc);
        }

        private void Open(NexusFile file)
        {
            if (file == null) return;

            if (file.IsDocument) { Shell.OpenDocument(file); return; }

            if (!_vault.OpenExternally(file))
                AppServices.Toasts.Show("OPEN FAILED", file.Name, ToastKind.Warning);

            LoadActivity();
        }

        private void Export()
        {
            if (Selected == null) return;

            var dialog = new SaveFileDialog
            {
                Title = "Save a copy",
                FileName = Selected.IsDocument ? Selected.Name + ".txt" : Selected.Name
            };
            if (dialog.ShowDialog() != true) return;

            if (_vault.ExportTo(Selected, dialog.FileName))
                AppServices.Toasts.Show("COPY SAVED", dialog.FileName, ToastKind.Success);
        }

        private void Rename()
        {
            _vault.Rename(Selected, RenameBuffer);
            Refresh();
        }

        private void Delete()
        {
            var target = Selected;
            if (target == null) return;

            if (!DialogService.Confirm("MOVE TO TRASH",
                    "\"" + target.Name + "\" will be moved to the trash shelf.", "MOVE"))
                return;

            _vault.MoveToTrash(target);
            Refresh();
        }

        private void Purge()
        {
            var target = Selected;
            if (target == null) return;

            if (!DialogService.Confirm("DELETE PERMANENTLY",
                    "\"" + target.Name + "\" and its stored copy will be removed for good.", "DELETE"))
                return;

            _vault.Purge(target);
            Selected = null;
            Refresh();
        }

        private void EmptyTrash()
        {
            if (_vault.TrashCount == 0) return;

            if (!DialogService.Confirm("EMPTY TRASH",
                    _vault.TrashCount + " file(s) and their stored copies will be removed for good.", "EMPTY"))
                return;

            var n = _vault.EmptyTrash();
            AppServices.Toasts.Show("TRASH EMPTIED", n + " file(s) removed.", ToastKind.Warning);
            Selected = null;
            Refresh();
        }

        private void LoadActivity()
        {
            SelectedActivity.Clear();
            if (Selected == null) return;

            foreach (var e in _bus.Events
                         .Where(e => e.Category == EventCategory.File &&
                                     ((e.Message ?? "").IndexOf(Selected.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      (e.Detail ?? "").IndexOf(Selected.Name, StringComparison.OrdinalIgnoreCase) >= 0))
                         .Take(8))
                SelectedActivity.Add(e);
        }

        private void Refresh()
        {
            _vault.RefreshCounts();

            IEnumerable<NexusFile> source;
            switch (Scope)
            {
                case "RECENT": source = _vault.Recent(40); break;
                case "FREQUENT": source = _vault.Frequent(40); break;
                case "PINNED": source = _vault.Pinned(); break;
                case "DOCUMENTS": source = _vault.Active.Where(f => f.IsDocument); break;
                case "TRASH": source = _vault.Files.Where(f => f.IsTrashed); break;
                default:
                    source = _vault.Active;
                    if (SelectedFolder != null && SelectedFolder.Kind != FolderKind.Trash)
                        source = source.Where(f => f.FolderId == SelectedFolder.Id);
                    break;
            }

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var q = Search.Trim();
                source = source.Where(f =>
                    (f.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (f.Extension ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (f.Body ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            Visible.Clear();
            foreach (var f in source.OrderByDescending(f => f.IsPinned).ThenByDescending(f => f.ModifiedUtc))
                Visible.Add(f);

            RecentFiles.Clear();
            foreach (var f in _vault.Recent(6)) RecentFiles.Add(f);

            FrequentFiles.Clear();
            foreach (var f in _vault.Frequent(6)) FrequentFiles.Add(f);

            PinnedFiles.Clear();
            foreach (var f in _vault.Pinned().Take(6)) PinnedFiles.Add(f);

            OnPropertiesChanged(nameof(TotalCount), nameof(DocumentCount), nameof(PinnedCount),
                nameof(TrashCount), nameof(TotalSizeDisplay), nameof(LocationLabel), nameof(IsTrashView));
        }
    }

    /// <summary>
    /// The document workspace. Shelves on the left, documents in the middle, and a
    /// plain-text editor on the right — deliberately a technical writing surface
    /// rather than a rich-text page.
    /// </summary>
    public class DocumentsViewModel : ViewModelBase
    {
        private readonly FileVault _vault;

        private string _shelf = "RECENT";
        private NexusFile _selected;
        private string _editorBody = "";
        private string _titleBuffer = "";
        private bool _isDirty;

        public DocumentsViewModel(ShellViewModel shell)
        {
            Shell = shell;
            _vault = AppServices.Files;
            _vault.Changed += Refresh;

            SetShelfCommand = new RelayCommand(p => Shelf = p?.ToString() ?? "RECENT");
            SelectCommand = new RelayCommand(p => { if (p is NexusFile f) Selected = f; });
            NewCommand = new RelayCommand(_ => CreateNew());
            SaveCommand = new RelayCommand(_ => Save(), _ => Selected != null && IsDirty);
            DeleteCommand = new RelayCommand(_ => Delete(), _ => Selected != null);
            RestoreCommand = new RelayCommand(_ => { _vault.Restore(Selected); Refresh(); },
                                              _ => Selected != null && Selected.IsTrashed);
            DuplicateCommand = new RelayCommand(_ => Duplicate(), _ => Selected != null);
            FromTemplateCommand = new RelayCommand(p => FromTemplate(p as NexusFile));
            OpenFilesCommand = new RelayCommand(_ => Shell.Navigate("FILES"));

            Refresh();
        }

        public ShellViewModel Shell { get; }

        public ObservableCollection<NexusFile> Documents { get; } = new ObservableCollection<NexusFile>();
        public ObservableCollection<NexusFile> Templates { get; } = new ObservableCollection<NexusFile>();

        public ICommand SetShelfCommand { get; }
        public ICommand SelectCommand { get; }
        public ICommand NewCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand DuplicateCommand { get; }
        public ICommand FromTemplateCommand { get; }
        public ICommand OpenFilesCommand { get; }

        public IReadOnlyList<string> Shelves { get; } =
            new[] { "RECENT", "MY DOCUMENTS", "SHARED", "PROJECTS", "TEMPLATES", "TRASH" };

        public string Shelf
        {
            get => _shelf;
            set { if (Set(ref _shelf, value)) Refresh(); }
        }

        public NexusFile Selected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;

                _editorBody = value?.Body ?? "";
                _titleBuffer = value?.Name ?? "";
                IsDirty = false;

                OnPropertiesChanged(nameof(EditorBody), nameof(TitleBuffer), nameof(HasSelection),
                    nameof(StatsLine), nameof(LocationLabel));
            }
        }

        public bool HasSelection => Selected != null;

        public string EditorBody
        {
            get => _editorBody;
            set
            {
                if (!Set(ref _editorBody, value)) return;
                IsDirty = true;
                OnPropertyChanged(nameof(StatsLine));
            }
        }

        public string TitleBuffer
        {
            get => _titleBuffer;
            set { if (Set(ref _titleBuffer, value)) IsDirty = true; }
        }

        public bool IsDirty
        {
            get => _isDirty;
            private set { if (Set(ref _isDirty, value)) OnPropertyChanged(nameof(StateLabel)); }
        }

        public string StateLabel => Selected == null ? "NO DOCUMENT OPEN" : IsDirty ? "UNSAVED CHANGES" : "SAVED";
        public string StateBrushKey => IsDirty ? "BrushHigh" : "BrushCompleted";

        public string StatsLine
        {
            get
            {
                if (Selected == null) return "";
                var words = string.IsNullOrWhiteSpace(EditorBody)
                    ? 0
                    : EditorBody.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                return words + " WORDS \u00B7 " + (EditorBody ?? "").Length + " CHARACTERS";
            }
        }

        public string LocationLabel => Selected == null ? "" : _vault.LocationOf(Selected);
        public int DocumentCount => Documents.Count;

        public override void OnActivated() => Refresh();

        public void SelectPayload(object payload)
        {
            if (payload is not NexusFile file || !file.IsDocument) return;

            Shelf = file.IsTrashed ? "TRASH" : "RECENT";
            Refresh();
            Selected = file;
        }

        private void CreateNew()
        {
            var folder = _vault.SystemFolder(ShelfKind() ?? FolderKind.MyDocuments);
            var doc = _vault.CreateDocument("Untitled document", folder?.Id);
            Refresh();
            Selected = doc;
        }

        private void FromTemplate(NexusFile template)
        {
            if (template == null) return;

            var target = _vault.SystemFolder(FolderKind.MyDocuments);
            var doc = _vault.CreateDocument(template.Name + " \u2014 copy", target?.Id, template.Body);
            _vault.RecordOpen(template);
            Shelf = "MY DOCUMENTS";
            Refresh();
            Selected = doc;
        }

        private void Duplicate()
        {
            if (Selected == null) return;

            var doc = _vault.CreateDocument(Selected.Name + " \u2014 copy", Selected.FolderId, EditorBody);
            Refresh();
            Selected = doc;
        }

        private void Save()
        {
            if (Selected == null) return;

            if (!string.Equals(Selected.Name, TitleBuffer, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(TitleBuffer))
                _vault.Rename(Selected, TitleBuffer);

            _vault.SaveDocument(Selected, EditorBody);
            IsDirty = false;
            AppServices.Toasts.Show("DOCUMENT SAVED", Selected.Name, ToastKind.Success);
            Refresh();
        }

        private void Delete()
        {
            var target = Selected;
            if (target == null) return;

            if (!DialogService.Confirm("MOVE TO TRASH",
                    "\"" + target.Name + "\" will be moved to the trash shelf.", "MOVE"))
                return;

            _vault.MoveToTrash(target);
            Selected = null;
            Refresh();
        }

        private FolderKind? ShelfKind() => Shelf switch
        {
            "MY DOCUMENTS" => FolderKind.MyDocuments,
            "SHARED" => FolderKind.Shared,
            "PROJECTS" => FolderKind.Projects,
            "TEMPLATES" => FolderKind.Templates,
            "TRASH" => FolderKind.Trash,
            _ => null
        };

        private void Refresh()
        {
            IEnumerable<NexusFile> source;

            if (Shelf == "RECENT")
                source = _vault.Active.Where(f => f.IsDocument).OrderByDescending(f => f.ModifiedUtc).Take(30);
            else if (Shelf == "TRASH")
                source = _vault.Files.Where(f => f.IsDocument && f.IsTrashed);
            else
            {
                var folder = _vault.SystemFolder(ShelfKind() ?? FolderKind.MyDocuments);
                source = _vault.Active.Where(f => f.IsDocument && f.FolderId == folder?.Id);
            }

            Documents.Clear();
            foreach (var d in source.OrderByDescending(d => d.ModifiedUtc)) Documents.Add(d);

            var templateShelf = _vault.SystemFolder(FolderKind.Templates);
            Templates.Clear();
            foreach (var t in _vault.Active.Where(f => f.IsDocument && f.FolderId == templateShelf?.Id))
                Templates.Add(t);

            OnPropertiesChanged(nameof(DocumentCount));
        }
    }
}
