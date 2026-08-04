using System;
using System.Collections.Generic;
using System.IO;
using FattureViewer.Models;
using Microsoft.Win32;

namespace FattureViewer.Services
{
    public static class AppSettingsService
    {
        private const string LegacyRegistryPath =
            @"HKEY_CURRENT_USER\Software\FattureViewer";
        private const string CCaseRegistryPath =
            @"HKEY_CURRENT_USER\Software\FattureViewer\Profiles\CCASE";
        private const string StorageDirectoryValue = "InternalStorageDirectory";
        private const string SupplierStorageDirectoryValue =
            "SupplierStorageDirectory";
        private const string CustomerStorageDirectoryValue =
            "CustomerStorageDirectory";
        private const string PassiveDirectoryValue = "FatturePassiveDirectory";
        private const string ZipImportEnabledValue = "ZipImportEnabled";
        private const string AdminDeletionEnabledValue = "AdminDeletionEnabled";
        private const string StorageMarkerFileName = ".fattureviewer-profile";

        public static string GetStorageDirectory()
        {
            return GetStorageDirectory(InvoiceSection.Suppliers);
        }

        public static string GetStorageDirectory(InvoiceSection section)
        {
            string? configured = Registry.GetValue(
                AppProfileService.Current.RegistryPath,
                GetStorageValueName(section),
                null) as string;
            string directory = string.IsNullOrWhiteSpace(configured)
                ? GetDefaultStorageDirectory(AppProfileService.Current, section)
                : configured;
            directory = NormalizeStorageDirectory(directory);
            EnsureStorageOwnership(directory, GetStorageOwner(
                AppProfileService.Current,
                section));
            return directory;
        }

        public static string GetDefaultStorageDirectory(
            AppProfileDefinition profile,
            InvoiceSection section)
        {
            if (profile.Kind == AppProfileKind.Omt)
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage");

            return Path.Combine(
                AppProfileService.GetProfileDataDirectory(profile),
                "Database",
                section == InvoiceSection.Suppliers
                    ? "Fornitori"
                    : "Clienti");
        }

        public static bool IsStorageConfigured(InvoiceSection section)
        {
            return Registry.GetValue(
                       AppProfileService.Current.RegistryPath,
                       GetStorageValueName(section),
                       null) is string value &&
                   !string.IsNullOrWhiteSpace(value);
        }

        public static void SetStorageDirectory(string directory)
        {
            SetStorageDirectory(InvoiceSection.Suppliers, directory);
        }

        public static void SetStorageDirectory(
            InvoiceSection section,
            string directory)
        {
            string normalized = NormalizeStorageDirectory(directory);
            EnsureStorageOwnership(normalized, GetStorageOwner(
                AppProfileService.Current,
                section));
            Registry.SetValue(
                AppProfileService.Current.RegistryPath,
                GetStorageValueName(section),
                normalized,
                RegistryValueKind.String);
        }

        public static string GetPassiveDirectory()
        {
            return Registry.GetValue(
                       AppProfileService.Current.RegistryPath,
                       PassiveDirectoryValue,
                       "") as string ??
                   string.Empty;
        }

        public static void SetPassiveDirectory(string directory)
        {
            Registry.SetValue(
                AppProfileService.Current.RegistryPath,
                PassiveDirectoryValue,
                directory,
                RegistryValueKind.String);
        }

        public static bool IsZipImportEnabled()
        {
            object? value = Registry.GetValue(
                AppProfileService.Current.RegistryPath,
                ZipImportEnabledValue,
                1);
            return value switch
            {
                int number => number != 0,
                string text when int.TryParse(text, out int number) => number != 0,
                _ => true
            };
        }

        public static void SetZipImportEnabled(bool enabled)
        {
            Registry.SetValue(
                AppProfileService.Current.RegistryPath,
                ZipImportEnabledValue,
                enabled ? 1 : 0,
                RegistryValueKind.DWord);
        }

        public static bool IsAdminDeletionEnabled()
        {
            object? value = Registry.GetValue(
                AppProfileService.Current.RegistryPath,
                AdminDeletionEnabledValue,
                0);
            return value switch
            {
                int number => number != 0,
                string text when int.TryParse(text, out int number) => number != 0,
                _ => false
            };
        }

        public static void SetAdminDeletionEnabled(bool enabled)
        {
            Registry.SetValue(
                AppProfileService.Current.RegistryPath,
                AdminDeletionEnabledValue,
                enabled ? 1 : 0,
                RegistryValueKind.DWord);
        }

        public static string NormalizeStorageDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException(
                    "Il percorso non può essere vuoto.",
                    nameof(directory));

            return Path.GetFullPath(directory.Trim());
        }

        public static void EnsureStorageOwnership(
            string directory,
            string expectedOwner)
        {
            if (AppProfileService.Current.Kind == AppProfileKind.CCase)
                TryMarkLegacyOmtStorage();

            string normalized = NormalizeStorageDirectory(directory);
            foreach ((string knownPath, string owner) in GetKnownStorageDirectories())
            {
                if (!owner.Equals(expectedOwner, StringComparison.OrdinalIgnoreCase) &&
                    PathsEqual(normalized, knownPath))
                    throw new InvalidOperationException(
                        $"Questa cartella appartiene già al profilo {owner}.");
            }

            Directory.CreateDirectory(normalized);
            string markerPath = Path.Combine(normalized, StorageMarkerFileName);
            if (File.Exists(markerPath))
            {
                string owner = File.ReadAllText(markerPath).Trim();
                if (!owner.Equals(expectedOwner, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Questa cartella appartiene già al profilo {owner}.");
                return;
            }
            File.WriteAllText(markerPath, expectedOwner);
        }

        private static void TryMarkLegacyOmtStorage()
        {
            try
            {
                AppProfileDefinition omt =
                    AppProfileService.Create(AppProfileKind.Omt);
                string? configured = Registry.GetValue(
                    LegacyRegistryPath,
                    StorageDirectoryValue,
                    null) as string;
                string directory = string.IsNullOrWhiteSpace(configured)
                    ? GetDefaultStorageDirectory(omt, InvoiceSection.Suppliers)
                    : NormalizeStorageDirectory(configured);
                if (!Directory.Exists(directory))
                    return;

                string markerPath = Path.Combine(
                    directory,
                    StorageMarkerFileName);
                if (!File.Exists(markerPath))
                    File.WriteAllText(markerPath, "OMT");
            }
            catch
            {
                // Un percorso OMT temporaneamente offline non deve bloccare C.CASE.
            }
        }

        private static string GetStorageValueName(InvoiceSection section)
        {
            if (AppProfileService.Current.Kind == AppProfileKind.Omt)
                return StorageDirectoryValue;
            return section == InvoiceSection.Suppliers
                ? SupplierStorageDirectoryValue
                : CustomerStorageDirectoryValue;
        }

        private static string GetStorageOwner(
            AppProfileDefinition profile,
            InvoiceSection section)
        {
            if (profile.Kind == AppProfileKind.Omt)
                return "OMT";
            return section == InvoiceSection.Suppliers
                ? "C.CASE-FORNITORI"
                : "C.CASE-CLIENTI";
        }

        private static IEnumerable<(string Path, string Owner)>
            GetKnownStorageDirectories()
        {
            AppProfileDefinition omt = AppProfileService.Create(AppProfileKind.Omt);
            string? omtPath = Registry.GetValue(
                LegacyRegistryPath,
                StorageDirectoryValue,
                null) as string;
            yield return (
                string.IsNullOrWhiteSpace(omtPath)
                    ? GetDefaultStorageDirectory(omt, InvoiceSection.Suppliers)
                    : omtPath,
                "OMT");

            AppProfileDefinition ccase = AppProfileService.Create(AppProfileKind.CCase);
            string? supplierPath = Registry.GetValue(
                CCaseRegistryPath,
                SupplierStorageDirectoryValue,
                null) as string;
            yield return (
                string.IsNullOrWhiteSpace(supplierPath)
                    ? GetDefaultStorageDirectory(ccase, InvoiceSection.Suppliers)
                    : supplierPath,
                "C.CASE-FORNITORI");

            string? customerPath = Registry.GetValue(
                CCaseRegistryPath,
                CustomerStorageDirectoryValue,
                null) as string;
            yield return (
                string.IsNullOrWhiteSpace(customerPath)
                    ? GetDefaultStorageDirectory(ccase, InvoiceSection.Customers)
                    : customerPath,
                "C.CASE-CLIENTI");
        }

        private static bool PathsEqual(string first, string second)
        {
            try
            {
                return string.Equals(
                    NormalizeStorageDirectory(first)
                        .TrimEnd(Path.DirectorySeparatorChar),
                    NormalizeStorageDirectory(second)
                        .TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
