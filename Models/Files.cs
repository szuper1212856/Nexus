using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;
using NEXUS.Common;

namespace NEXUS.Models
{
    /// <summary>The fixed shelves of the document library, plus operator-created folders.</summary>
    public enum FolderKind
    {
        Custom = 0,
        MyDocuments = 1,
        Shared = 2,
        Projects = 3,
        Templates = 4,
        Trash = 5
    }

    public class NexusFolder : ObservableObject
    {
        private string _name = "";
        private int _itemCount;

        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? ParentId { get; set; }
        public FolderKind Kind { get; set; } = FolderKind.Custom;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Built-in shelves cannot be renamed or deleted.</summary>
        public bool IsSystem { get; set; }

        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        [JsonIgnore]
        public int ItemCount
        {
            get => _itemCount;
            set => Set(ref _itemCount, value);
        }

        [JsonIgnore]
        public string Glyph => Kind switch
        {
            FolderKind.Trash => "Trash",
            FolderKind.Templates => "Document",
            FolderKind.Projects => "Server",
            FolderKind.Shared => "Users",
            _ => "Folder"
        };

        [JsonIgnore] public string CountDisplay => ItemCount + (ItemCount == 1 ? " ITEM" : " ITEMS");
    }

    /// <summary>
    /// A file held in the NEXUS vault, or a document authored inside NEXUS.
    /// Uploaded files are copied into %APPDATA%\NEXUS\vault so the record and the bytes
    /// stay together; documents keep their text in <see cref="Body"/> instead.
    /// </summary>
    public class NexusFile : ObservableObject
    {
        private string _name = "";
        private Guid? _folderId;
        private bool _isPinned;
        private bool _isTrashed;
        private DateTime _modifiedUtc = DateTime.UtcNow;
        private int _openCount;
        private string _body = "";

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Extension { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastOpenedUtc { get; set; }
        public string OriginalPath { get; set; } = "";

        /// <summary>File name inside the vault directory. Empty for NEXUS documents.</summary>
        public string VaultName { get; set; } = "";

        /// <summary>True for documents authored in NEXUS, which are edited in-app.</summary>
        public bool IsDocument { get; set; }

        /// <summary>Optional link to a task, so files and work reference each other.</summary>
        public Guid? LinkedTaskId { get; set; }

        public string Name
        {
            get => _name;
            set { if (Set(ref _name, value)) OnPropertiesChanged(nameof(DisplayName), nameof(TypeLabel), nameof(Glyph)); }
        }

        public Guid? FolderId
        {
            get => _folderId;
            set => Set(ref _folderId, value);
        }

        public bool IsPinned
        {
            get => _isPinned;
            set { if (Set(ref _isPinned, value)) OnPropertyChanged(nameof(PinGlyph)); }
        }

        public bool IsTrashed
        {
            get => _isTrashed;
            set => Set(ref _isTrashed, value);
        }

        public DateTime ModifiedUtc
        {
            get => _modifiedUtc;
            set { if (Set(ref _modifiedUtc, value)) OnPropertyChanged(nameof(ModifiedDisplay)); }
        }

        public int OpenCount
        {
            get => _openCount;
            set { if (Set(ref _openCount, value)) OnPropertyChanged(nameof(OpenCountDisplay)); }
        }

        /// <summary>Text content, for documents authored inside NEXUS.</summary>
        public string Body
        {
            get => _body;
            set { if (Set(ref _body, value)) OnPropertiesChanged(nameof(WordCount), nameof(SizeDisplay)); }
        }

        // ---- display ----

        [JsonIgnore] public string DisplayName => Name;

        [JsonIgnore]
        public string TypeLabel
        {
            get
            {
                if (IsDocument) return "NEXUS DOCUMENT";
                var ext = (Extension ?? "").TrimStart('.').ToUpperInvariant();
                return string.IsNullOrEmpty(ext) ? "FILE" : ext + " FILE";
            }
        }

        [JsonIgnore]
        public string Glyph
        {
            get
            {
                if (IsDocument) return "Document";
                switch ((Extension ?? "").ToLowerInvariant())
                {
                    case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp": case ".webp":
                        return "FileImage";
                    case ".pdf": return "FilePdf";
                    case ".txt": case ".md": case ".log": case ".rtf": return "FileText";
                    case ".cs": case ".js": case ".ts": case ".py": case ".json": case ".xml":
                    case ".html": case ".css": case ".xaml": case ".sql":
                        return "FileCode";
                    case ".zip": case ".rar": case ".7z": case ".tar": case ".gz":
                        return "FileArchive";
                    case ".mp3": case ".wav": case ".flac": case ".ogg":
                        return "FileAudio";
                    case ".mp4": case ".mkv": case ".mov": case ".avi":
                        return "FileVideo";
                    case ".csv": case ".xlsx": case ".xls":
                        return "FileSheet";
                    default: return "FileGeneric";
                }
            }
        }

        [JsonIgnore] public string PinGlyph => IsPinned ? "Unpin" : "Pin";

        /// <summary>Text and image files can be shown inside NEXUS; everything else opens externally.</summary>
        [JsonIgnore]
        public bool IsTextPreviewable
        {
            get
            {
                if (IsDocument) return true;
                switch ((Extension ?? "").ToLowerInvariant())
                {
                    case ".txt": case ".md": case ".log": case ".json": case ".xml":
                    case ".csv": case ".cs": case ".js": case ".ts": case ".py":
                    case ".html": case ".css": case ".xaml": case ".yml": case ".yaml":
                    case ".ini": case ".config": case ".sql":
                        return true;
                    default: return false;
                }
            }
        }

        [JsonIgnore]
        public bool IsImagePreviewable
        {
            get
            {
                switch ((Extension ?? "").ToLowerInvariant())
                {
                    case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp":
                        return true;
                    default: return false;
                }
            }
        }

        [JsonIgnore]
        public string SizeDisplay
        {
            get
            {
                var bytes = IsDocument
                    ? System.Text.Encoding.UTF8.GetByteCount(Body ?? "")
                    : SizeBytes;

                if (bytes >= 1L << 30) return Math.Round(bytes / (double)(1L << 30), 1) + " GB";
                if (bytes >= 1L << 20) return Math.Round(bytes / (double)(1L << 20), 1) + " MB";
                if (bytes >= 1L << 10) return Math.Round(bytes / (double)(1L << 10), 1) + " KB";
                return bytes + " B";
            }
        }

        [JsonIgnore]
        public int WordCount
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Body)) return 0;
                return Body.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }

        [JsonIgnore]
        public string ModifiedDisplay
        {
            get
            {
                var delta = DateTime.UtcNow - ModifiedUtc;
                if (delta.TotalMinutes < 1) return "JUST NOW";
                if (delta.TotalMinutes < 60) return (int)delta.TotalMinutes + " MIN AGO";
                if (delta.TotalHours < 24) return (int)delta.TotalHours + " HR AGO";
                if (delta.TotalDays < 7) return (int)delta.TotalDays + " D AGO";
                return ModifiedUtc.ToLocalTime().ToString("dd MMM yyyy").ToUpperInvariant();
            }
        }

        [JsonIgnore]
        public string OpenCountDisplay => OpenCount == 0 ? "NEVER OPENED"
            : OpenCount + (OpenCount == 1 ? " OPEN" : " OPENS");

        public void RefreshTimeDependent()
            => OnPropertiesChanged(nameof(ModifiedDisplay), nameof(SizeDisplay));

        public static string NormalizeExtension(string fileName)
        {
            var ext = Path.GetExtension(fileName ?? "");
            return string.IsNullOrEmpty(ext) ? "" : ext.ToLowerInvariant();
        }
    }
}
