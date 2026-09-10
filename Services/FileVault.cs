using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using NEXUS.Models;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace NEXUS.Services
{
    /// <summary>
    /// Owns the NEXUS file library. Uploaded files are copied into a vault directory
    /// beside the data file, so the catalogue never points at paths that move or vanish.
    /// Documents authored inside NEXUS store their text in the record itself.
    /// </summary>
    public class FileVault
    {
        private readonly DataStore _store;
        private readonly EventBus _bus;

        public FileVault(DataStore store, EventBus bus)
        {
            _store = store;
            _bus = bus;
            VaultRoot = Path.Combine(_store.RootFolder, "vault");
        }

        public string VaultRoot { get; }

        public ObservableCollection<NexusFile> Files => _store.Data.Files;
        public ObservableCollection<NexusFolder> Folders => _store.Data.Folders;

        public event Action Changed;

        // ---------- queries ----------

        public IEnumerable<NexusFile> Active => Files.Where(f => !f.IsTrashed);
        public int ActiveCount => Active.Count();
        public int DocumentCount => Active.Count(f => f.IsDocument);
        public int TrashCount => Files.Count(f => f.IsTrashed);
        public int PinnedCount => Active.Count(f => f.IsPinned);

        public long TotalBytes => Active.Sum(f => f.IsDocument
            ? System.Text.Encoding.UTF8.GetByteCount(f.Body ?? "")
            : f.SizeBytes);

        public string TotalSizeDisplay => SystemInfoService.FormatBytes(TotalBytes);

        public IEnumerable<NexusFile> Recent(int take = 8)
            => Active.OrderByDescending(f => f.ModifiedUtc).Take(take);

        public IEnumerable<NexusFile> Frequent(int take = 8)
            => Active.Where(f => f.OpenCount > 0)
                     .OrderByDescending(f => f.OpenCount)
                     .ThenByDescending(f => f.LastOpenedUtc ?? DateTime.MinValue)
                     .Take(take);

        public IEnumerable<NexusFile> Pinned() => Active.Where(f => f.IsPinned);

        public NexusFolder FolderOf(NexusFile file)
            => file?.FolderId == null ? null : Folders.FirstOrDefault(f => f.Id == file.FolderId);

        public NexusFolder SystemFolder(FolderKind kind)
            => Folders.FirstOrDefault(f => f.Kind == kind);

        public string LocationOf(NexusFile file)
        {
            var folder = FolderOf(file);
            if (folder == null) return "NEXUS / UNFILED";

            var parts = new List<string> { folder.Name };
            var guard = 0;
            var current = folder;
            while (current?.ParentId != null && guard++ < 8)
            {
                current = Folders.FirstOrDefault(f => f.Id == current.ParentId);
                if (current == null) break;
                parts.Insert(0, current.Name);
            }
            return "NEXUS / " + string.Join(" / ", parts).ToUpperInvariant();
        }

        public NexusFile Find(string name)
            => Active.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? Active.FirstOrDefault(f => f.Name != null &&
                      f.Name.IndexOf(name ?? "", StringComparison.OrdinalIgnoreCase) >= 0);

        // ---------- import ----------

        /// <summary>
        /// Copies files into the vault and catalogues them. Returns how many were taken in.
        /// Used by both the upload button and drag-and-drop.
        /// </summary>
        public int Import(IEnumerable<string> paths, Guid? folderId)
        {
            if (paths == null) return 0;

            var imported = 0;
            EnsureVault();

            foreach (var path in paths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        // A dropped folder brings its immediate contents in with it.
                        var target = CreateFolder(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), folderId);
                        imported += Import(Directory.GetFiles(path), target?.Id);
                        continue;
                    }

                    if (!File.Exists(path)) continue;

                    var info = new FileInfo(path);
                    var vaultName = Guid.NewGuid().ToString("N") + info.Extension;
                    File.Copy(path, Path.Combine(VaultRoot, vaultName), overwrite: false);

                    var record = new NexusFile
                    {
                        Name = info.Name,
                        Extension = info.Extension.ToLowerInvariant(),
                        SizeBytes = info.Length,
                        FolderId = folderId,
                        VaultName = vaultName,
                        OriginalPath = info.DirectoryName ?? "",
                        CreatedUtc = DateTime.UtcNow,
                        ModifiedUtc = DateTime.UtcNow
                    };

                    Files.Add(record);
                    imported++;

                    _bus.Publish(EventCategory.File, record.Name + " uploaded",
                        record.TypeLabel + " \u00B7 " + record.SizeDisplay + " \u00B7 " + LocationOf(record),
                        EventSeverity.Notice, "FILES");

                    _bus.Signal(TriggerType.FileUploaded, record.Name, record.TypeLabel, record);
                }
                catch (Exception ex)
                {
                    _bus.Publish(EventCategory.File, "Upload failed",
                        Path.GetFileName(path) + " \u00B7 " + ex.Message,
                        EventSeverity.Warning, "FILES");
                }
            }

            if (imported > 0) Touch();
            return imported;
        }

        // ---------- documents ----------

        public NexusFile CreateDocument(string name, Guid? folderId, string body = "")
        {
            var doc = new NexusFile
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Untitled document" : name.Trim(),
                Extension = ".nxdoc",
                IsDocument = true,
                Body = body ?? "",
                FolderId = folderId ?? SystemFolder(FolderKind.MyDocuments)?.Id,
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow
            };

            Files.Add(doc);
            _bus.Publish(EventCategory.File, "Document created", doc.Name, EventSeverity.Notice, "DOCUMENTS");
            Touch();
            return doc;
        }

        public void SaveDocument(NexusFile document, string body)
        {
            if (document == null || !document.IsDocument) return;
            if (string.Equals(document.Body, body, StringComparison.Ordinal)) return;

            document.Body = body ?? "";
            document.ModifiedUtc = DateTime.UtcNow;

            _bus.Publish(EventCategory.File, "Document saved",
                document.Name + " \u00B7 " + document.WordCount + " words",
                EventSeverity.Info, "DOCUMENTS");
            Touch();
        }

        // ---------- organisation ----------

        public NexusFolder CreateFolder(string name, Guid? parentId, FolderKind kind = FolderKind.Custom)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            var folder = new NexusFolder
            {
                Name = name.Trim(),
                ParentId = parentId,
                Kind = kind
            };

            Folders.Add(folder);
            _bus.Publish(EventCategory.File, "Folder created", folder.Name, EventSeverity.Info, "FILES");
            Touch();
            return folder;
        }

        public void RenameFolder(NexusFolder folder, string name)
        {
            if (folder == null || folder.IsSystem || string.IsNullOrWhiteSpace(name)) return;

            var previous = folder.Name;
            folder.Name = name.Trim();
            _bus.Publish(EventCategory.File, "Folder renamed",
                previous + " \u2192 " + folder.Name, EventSeverity.Info, "FILES");
            Touch();
        }

        public void DeleteFolder(NexusFolder folder)
        {
            if (folder == null || folder.IsSystem) return;

            foreach (var file in Files.Where(f => f.FolderId == folder.Id).ToList())
                MoveToTrash(file);

            foreach (var child in Folders.Where(f => f.ParentId == folder.Id).ToList())
                child.ParentId = folder.ParentId;

            Folders.Remove(folder);
            _bus.Publish(EventCategory.File, "Folder removed", folder.Name, EventSeverity.Warning, "FILES");
            Touch();
        }

        public void Move(NexusFile file, Guid? folderId)
        {
            if (file == null) return;

            file.FolderId = folderId;
            file.IsTrashed = false;
            file.ModifiedUtc = DateTime.UtcNow;

            _bus.Publish(EventCategory.File, "File moved",
                file.Name + " \u2192 " + LocationOf(file), EventSeverity.Info, "FILES");
            Touch();
        }

        public void Rename(NexusFile file, string name)
        {
            if (file == null || string.IsNullOrWhiteSpace(name)) return;

            var previous = file.Name;
            var trimmed = name.Trim();

            // Keep the extension stable so the type and preview logic stay correct.
            if (!file.IsDocument && !string.IsNullOrEmpty(file.Extension) &&
                !trimmed.EndsWith(file.Extension, StringComparison.OrdinalIgnoreCase))
                trimmed += file.Extension;

            file.Name = trimmed;
            file.ModifiedUtc = DateTime.UtcNow;

            _bus.Publish(EventCategory.File, "File renamed",
                previous + " \u2192 " + file.Name, EventSeverity.Info, "FILES");
            Touch();
        }

        public void TogglePin(NexusFile file)
        {
            if (file == null) return;

            file.IsPinned = !file.IsPinned;
            _bus.Publish(EventCategory.File, file.IsPinned ? "File pinned" : "File unpinned",
                file.Name, EventSeverity.Info, "FILES");
            Touch();
        }

        public void MoveToTrash(NexusFile file)
        {
            if (file == null || file.IsTrashed) return;

            file.IsTrashed = true;
            file.IsPinned = false;
            _bus.Publish(EventCategory.File, "File moved to trash", file.Name, EventSeverity.Warning, "FILES");
            Touch();
        }

        public void Restore(NexusFile file)
        {
            if (file == null || !file.IsTrashed) return;

            file.IsTrashed = false;
            _bus.Publish(EventCategory.File, "File restored", file.Name, EventSeverity.Notice, "FILES");
            Touch();
        }

        /// <summary>Permanently removes a trashed record and its vault copy.</summary>
        public void Purge(NexusFile file)
        {
            if (file == null) return;

            DeleteVaultCopy(file);
            Files.Remove(file);
            _bus.Publish(EventCategory.File, "File permanently deleted", file.Name,
                         EventSeverity.Warning, "FILES");
            Touch();
        }

        public int EmptyTrash()
        {
            var doomed = Files.Where(f => f.IsTrashed).ToList();
            foreach (var file in doomed)
            {
                DeleteVaultCopy(file);
                Files.Remove(file);
            }

            if (doomed.Count > 0)
            {
                _bus.Publish(EventCategory.File, "Trash emptied",
                    doomed.Count + " file(s) permanently deleted", EventSeverity.Warning, "FILES");
                Touch();
            }
            return doomed.Count;
        }

        // ---------- access ----------

        public string PathOf(NexusFile file)
            => file == null || file.IsDocument || string.IsNullOrEmpty(file.VaultName)
                ? null
                : Path.Combine(VaultRoot, file.VaultName);

        /// <summary>Reads a text file out of the vault for the in-app preview.</summary>
        public string ReadText(NexusFile file, int maxChars = 40000)
        {
            if (file == null) return "";
            if (file.IsDocument) return file.Body ?? "";

            var path = PathOf(file);
            if (path == null || !File.Exists(path)) return "";

            try
            {
                var text = File.ReadAllText(path);
                return text.Length > maxChars
                    ? text.Substring(0, maxChars) + "\n\n\u2014 preview truncated \u2014"
                    : text;
            }
            catch (Exception ex)
            {
                return "Could not read this file: " + ex.Message;
            }
        }

        /// <summary>Hands the file to the operating system's default handler.</summary>
        public bool OpenExternally(NexusFile file)
        {
            var path = PathOf(file);
            if (path == null || !File.Exists(path))
            {
                _bus.Publish(EventCategory.File, "Open failed",
                    (file?.Name ?? "File") + " is not present in the vault.",
                    EventSeverity.Warning, "FILES");
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                RecordOpen(file);
                return true;
            }
            catch (Exception ex)
            {
                _bus.Publish(EventCategory.File, "Open failed",
                    file.Name + " \u00B7 " + ex.Message, EventSeverity.Warning, "FILES");
                return false;
            }
        }

        /// <summary>Copies a file back out of the vault to a location the operator chooses.</summary>
        public bool ExportTo(NexusFile file, string destination)
        {
            if (file == null || string.IsNullOrWhiteSpace(destination)) return false;

            try
            {
                if (file.IsDocument)
                    File.WriteAllText(destination, file.Body ?? "");
                else
                {
                    var source = PathOf(file);
                    if (source == null || !File.Exists(source)) return false;
                    File.Copy(source, destination, overwrite: true);
                }

                RecordOpen(file);
                _bus.Publish(EventCategory.File, "File exported",
                    file.Name + " \u2192 " + destination, EventSeverity.Notice, "FILES");
                return true;
            }
            catch (Exception ex)
            {
                _bus.Publish(EventCategory.File, "Export failed",
                    file.Name + " \u00B7 " + ex.Message, EventSeverity.Warning, "FILES");
                return false;
            }
        }

        /// <summary>Counts an access so the frequently-used list reflects real usage.</summary>
        public void RecordOpen(NexusFile file)
        {
            if (file == null) return;

            file.OpenCount = file.OpenCount + 1;
            file.LastOpenedUtc = DateTime.UtcNow;
            Touch();
        }

        public void RefreshCounts()
        {
            foreach (var folder in Folders)
                folder.ItemCount = Files.Count(f => f.FolderId == folder.Id && !f.IsTrashed);

            var trash = SystemFolder(FolderKind.Trash);
            if (trash != null) trash.ItemCount = TrashCount;

            foreach (var file in Files) file.RefreshTimeDependent();
        }

        public void Touch()
        {
            RefreshCounts();
            Changed?.Invoke();
            _store.RequestSave();
        }

        // ---------- setup ----------

        private void EnsureVault()
        {
            if (!Directory.Exists(VaultRoot)) Directory.CreateDirectory(VaultRoot);
        }

        private void DeleteVaultCopy(NexusFile file)
        {
            try
            {
                var path = PathOf(file);
                if (path != null && File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        public void SeedDefaults()
        {
            EnsureVault();

            void Shelf(string name, FolderKind kind)
                => Folders.Add(new NexusFolder { Name = name, Kind = kind, IsSystem = true });

            Shelf("My Documents", FolderKind.MyDocuments);
            Shelf("Shared", FolderKind.Shared);
            Shelf("Projects", FolderKind.Projects);
            Shelf("Templates", FolderKind.Templates);
            Shelf("Trash", FolderKind.Trash);

            var templates = SystemFolder(FolderKind.Templates);
            CreateDocument("Incident report", templates?.Id,
                "INCIDENT REPORT\n\nSummary\n\nImpact\n\nTimeline\n\nRoot cause\n\nRemediation\n\nFollow-up actions\n");
            CreateDocument("Change record", templates?.Id,
                "CHANGE RECORD\n\nChange\n\nSystems affected\n\nAuthorised by\n\nBackout plan\n\nVerification\n");
            CreateDocument("Operations handover", templates?.Id,
                "OPERATIONS HANDOVER\n\nOpen alerts\n\nServices requiring attention\n\nScheduled work\n\nNotes for next operator\n");

            _bus.Publish(EventCategory.System, "Document library provisioned",
                Folders.Count + " shelves \u00B7 " + Files.Count + " templates",
                EventSeverity.Notice, "FILES");
            Touch();
        }
    }
}
