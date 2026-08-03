using System;
using System.IO;
using Microsoft.Win32;

namespace FattureViewer.Services
{
    public static class AppSettingsService
    {
        private const string RegistryPath = @"HKEY_CURRENT_USER\Software\FattureViewer";
        private const string StorageDirectoryValue = "InternalStorageDirectory";
        private const string ZipImportEnabledValue = "ZipImportEnabled";
        private const string AdminDeletionEnabledValue = "AdminDeletionEnabled";

        public static string GetStorageDirectory()
        {
            string? configured = Registry.GetValue(RegistryPath, StorageDirectoryValue, null) as string;
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage")
                : configured;
        }

        public static void SetStorageDirectory(string directory)
        {
            Registry.SetValue(RegistryPath, StorageDirectoryValue, directory, RegistryValueKind.String);
        }

        public static bool IsZipImportEnabled()
        {
            object? value = Registry.GetValue(RegistryPath, ZipImportEnabledValue, 1);
            return value switch
            {
                int number => number != 0,
                string text when int.TryParse(text, out int number) => number != 0,
                _ => true
            };
        }

        public static void SetZipImportEnabled(bool enabled)
        {
            Registry.SetValue(RegistryPath, ZipImportEnabledValue, enabled ? 1 : 0, RegistryValueKind.DWord);
        }

        public static bool IsAdminDeletionEnabled()
        {
            object? value = Registry.GetValue(
                RegistryPath,
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
                RegistryPath,
                AdminDeletionEnabledValue,
                enabled ? 1 : 0,
                RegistryValueKind.DWord);
        }

        public static string NormalizeStorageDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Il percorso non può essere vuoto.", nameof(directory));

            return Path.GetFullPath(directory.Trim());
        }
    }
}
