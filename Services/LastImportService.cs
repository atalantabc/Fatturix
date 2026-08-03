using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public sealed class LastImportRecord
    {
        public int FormatVersion { get; set; } = 1;
        public InvoiceSection Section { get; set; }
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DatabaseImportChanges DatabaseChanges { get; set; } = new();
        public List<string> ImportedFileNames { get; set; } = new();
        public int Year { get; set; }
        public int Month { get; set; }
        public string ZipFileName { get; set; } = string.Empty;
        public string PassiveRoot { get; set; } = string.Empty;
        public string ArchiveRoot { get; set; } = string.Empty;
        public string ArchivePeriodDirectory { get; set; } = string.Empty;
        public string ArchivedZipPath { get; set; } = string.Empty;
        public List<string> PassiveFiles { get; set; } = new();
        public List<string> ArchiveFiles { get; set; } = new();
    }

    public sealed class LastImportService
    {
        private readonly string _recordPath;

        public LastImportService(string storageDirectory)
        {
            _recordPath = Path.Combine(
                Path.GetFullPath(storageDirectory),
                "last-import.dat");
        }

        public LastImportRecord? Load()
        {
            if (!File.Exists(_recordPath))
                return null;

            try
            {
                byte[] encrypted = File.ReadAllBytes(_recordPath);
                byte[] json = ProtectedData.Unprotect(
                    encrypted,
                    null,
                    DataProtectionScope.CurrentUser);
                LastImportRecord? record =
                    JsonSerializer.Deserialize<LastImportRecord>(json);
                if (record == null || record.FormatVersion != 1)
                    throw new InvalidDataException("Registro ultima importazione non valido.");
                return record;
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException)
            {
                throw new InvalidDataException(
                    "Registro ultima importazione non leggibile.",
                    ex);
            }
        }

        public void Save(LastImportRecord record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recordPath)!);
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(record);
            byte[] encrypted = ProtectedData.Protect(
                json,
                null,
                DataProtectionScope.CurrentUser);
            string temporaryPath = _recordPath + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, encrypted);
                File.Move(temporaryPath, _recordPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        public void Clear()
        {
            if (File.Exists(_recordPath))
                File.Delete(_recordPath);
        }

        public static void Rollback(
            LastImportRecord record,
            InvoiceSection expectedSection,
            DatabaseService database)
        {
            Validate(record, expectedSection);
            database.RollbackImport(record.DatabaseChanges);

            foreach (string path in record.PassiveFiles
                         .Concat(record.ArchiveFiles)
                         .Append(record.ArchivedZipPath)
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(path))
                    File.Delete(path);
            }

            DeleteEmptyParents(record.PassiveFiles, record.PassiveRoot);
            DeleteEmptyParents(
                record.ArchiveFiles.Append(record.ArchivedZipPath),
                record.ArchiveRoot);
            DeleteIfEmpty(record.ArchivePeriodDirectory, record.ArchiveRoot);
        }

        private static void Validate(
            LastImportRecord record,
            InvoiceSection expectedSection)
        {
            if (record.Section != expectedSection)
                throw new InvalidOperationException(
                    "L'ultima importazione appartiene all'altra sezione.");

            bool hasFiles = record.PassiveFiles.Count > 0 ||
                            record.ArchiveFiles.Count > 0 ||
                            !string.IsNullOrWhiteSpace(record.ArchivedZipPath);
            if (!hasFiles)
                return;

            if (record.Section != InvoiceSection.Suppliers ||
                record.Year < 2000 ||
                record.Month is < 1 or > 12)
                throw new InvalidDataException("Dati rollback Fornitori non validi.");

            string passiveRoot = NormalizeRoot(record.PassiveRoot);
            string archiveRoot = NormalizeRoot(record.ArchiveRoot);
            string expectedArchiveRoot = Path.GetFullPath(
                Path.Combine(passiveRoot, "Archivio"));
            if (!PathsEqual(archiveRoot, expectedArchiveRoot))
                throw new InvalidDataException("Cartella Archivio non valida.");

            ValidateFiles(record.PassiveFiles, passiveRoot, ".xml", ".p7m");
            ValidateFiles(record.ArchiveFiles, archiveRoot, ".xml", ".p7m");
            if (!string.IsNullOrWhiteSpace(record.ArchivedZipPath))
                ValidateFiles(
                    new[] { record.ArchivedZipPath },
                    archiveRoot,
                    ".zip");

            string expectedPeriod = Path.GetFullPath(Path.Combine(
                archiveRoot,
                $"{record.Year}-{record.Month}"));
            if (!PathsEqual(record.ArchivePeriodDirectory, expectedPeriod))
                throw new InvalidDataException("Periodo Archivio non valido.");
        }

        private static void ValidateFiles(
            IEnumerable<string> paths,
            string root,
            params string[] extensions)
        {
            foreach (string path in paths)
            {
                string fullPath = Path.GetFullPath(path);
                if (!IsDescendant(fullPath, root) ||
                    !extensions.Contains(
                        Path.GetExtension(fullPath),
                        StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Il registro contiene un percorso non sicuro.");
            }
        }

        private static void DeleteEmptyParents(
            IEnumerable<string> files,
            string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return;
            string fullRoot = NormalizeRoot(root);
            foreach (string file in files)
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(file));
                while (!string.IsNullOrWhiteSpace(directory) &&
                       IsDescendant(directory, fullRoot))
                {
                    if (!Directory.Exists(directory) ||
                        Directory.EnumerateFileSystemEntries(directory).Any())
                        break;
                    Directory.Delete(directory);
                    directory = Path.GetDirectoryName(directory);
                }
            }
        }

        private static void DeleteIfEmpty(string directory, string root)
        {
            if (string.IsNullOrWhiteSpace(directory) ||
                string.IsNullOrWhiteSpace(root))
                return;
            string fullDirectory = Path.GetFullPath(directory);
            string fullRoot = NormalizeRoot(root);
            if (IsDescendant(fullDirectory, fullRoot) &&
                Directory.Exists(fullDirectory) &&
                !Directory.EnumerateFileSystemEntries(fullDirectory).Any())
                Directory.Delete(fullDirectory);
        }

        private static bool IsDescendant(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string prefix = NormalizeRoot(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Percorso radice mancante.");
            return Path.GetFullPath(path);
        }

        private static bool PathsEqual(string first, string second)
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
