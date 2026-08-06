using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace FattureViewer.Services
{
    public static class ProfileConfigurationService
    {
        private const string RegistryPath =
            @"HKEY_CURRENT_USER\Software\FattureViewer\ProfileConfiguration";
        private const string ConfiguredValue = "Configured";
        private const string EnabledProfilesValue = "EnabledProfiles";

        public static IReadOnlyList<AppProfileKind>? GetOrMigrate()
        {
            IReadOnlyList<AppProfileKind>? configured = GetConfiguredProfiles();
            if (configured != null)
                return configured;

            IReadOnlyList<AppProfileKind> detected = DetectExistingProfiles(
                HasExistingProfile(AppProfileKind.Omt),
                HasExistingProfile(AppProfileKind.CCase),
                HasExistingProfile(AppProfileKind.Fuchs));
            if (detected.Count == 0)
                return null;

            Save(detected);
            try
            {
                ProfileShortcutService.Sync(detected);
            }
            catch
            {
                // La migrazione dei dati non dipende dai collegamenti di Windows.
            }
            return detected;
        }

        public static IReadOnlyList<AppProfileKind>? GetConfiguredProfiles()
        {
            object? configured = Registry.GetValue(
                RegistryPath,
                ConfiguredValue,
                null);
            if (configured is not int number || number == 0)
                return null;

            string value = Registry.GetValue(
                RegistryPath,
                EnabledProfilesValue,
                string.Empty) as string ?? string.Empty;
            List<AppProfileKind> profiles = Parse(value);
            return IsValidCombination(profiles) ? profiles : null;
        }

        public static void Save(IEnumerable<AppProfileKind> profiles)
        {
            List<AppProfileKind> normalized = profiles.Distinct().ToList();
            if (!IsValidCombination(normalized))
                throw new InvalidOperationException(
                    "Seleziona OMT, C.CASE, entrambi, oppure soltanto Fuchs Stahlrohre.");

            Registry.SetValue(
                RegistryPath,
                EnabledProfilesValue,
                string.Join(";", normalized.Select(ProfileId)),
                RegistryValueKind.String);
            Registry.SetValue(
                RegistryPath,
                ConfiguredValue,
                1,
                RegistryValueKind.DWord);
        }

        public static bool IsEnabled(
            AppProfileKind profile,
            IEnumerable<AppProfileKind> enabledProfiles)
        {
            return enabledProfiles.Contains(profile);
        }

        public static bool IsValidCombination(IEnumerable<AppProfileKind> profiles)
        {
            AppProfileKind[] values = profiles.Distinct().ToArray();
            if (values.Length == 0)
                return false;
            if (values.Contains(AppProfileKind.Fuchs))
                return values.Length == 1;
            return values.All(value =>
                value is AppProfileKind.Omt or AppProfileKind.CCase);
        }

        public static IReadOnlyList<AppProfileKind> DetectExistingProfiles(
            bool omtExists,
            bool ccaseExists,
            bool fuchsExists)
        {
            if (fuchsExists)
                return new[] { AppProfileKind.Fuchs };

            var result = new List<AppProfileKind>();
            if (omtExists)
                result.Add(AppProfileKind.Omt);
            if (ccaseExists)
                result.Add(AppProfileKind.CCase);
            return result;
        }

        public static AppProfileKind GetPreferredProfile(
            IEnumerable<AppProfileKind> profiles)
        {
            var enabled = profiles.ToHashSet();
            if (enabled.Contains(AppProfileService.Current.Kind))
                return AppProfileService.Current.Kind;
            if (enabled.Contains(AppProfileKind.Omt))
                return AppProfileKind.Omt;
            if (enabled.Contains(AppProfileKind.CCase))
                return AppProfileKind.CCase;
            return AppProfileKind.Fuchs;
        }

        public static string ProfileId(AppProfileKind profile) =>
            AppProfileService.Create(profile).Id;

        private static List<AppProfileKind> Parse(string value)
        {
            var result = new List<AppProfileKind>();
            foreach (string item in value.Split(
                         ';',
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                if (item.Equals("omt", StringComparison.OrdinalIgnoreCase))
                    result.Add(AppProfileKind.Omt);
                else if (item.Equals("ccase", StringComparison.OrdinalIgnoreCase))
                    result.Add(AppProfileKind.CCase);
                else if (item.Equals("fuchs", StringComparison.OrdinalIgnoreCase))
                    result.Add(AppProfileKind.Fuchs);
            }
            return result.Distinct().ToList();
        }

        private static bool HasExistingProfile(AppProfileKind kind)
        {
            AppProfileDefinition profile = AppProfileService.Create(kind);
            if (File.Exists(AppProfileService.GetPasswordFilePath(profile)))
                return true;

            if (kind == AppProfileKind.Omt)
            {
                if (Registry.GetValue(
                        profile.RegistryPath,
                        "InternalStorageDirectory",
                        null) is string configured &&
                    !string.IsNullOrWhiteSpace(configured))
                    return true;

                string storage = AppSettingsService.GetDefaultStorageDirectory(
                    profile,
                    Models.InvoiceSection.Suppliers);
                return File.Exists(Path.Combine(storage, "fatture.db")) ||
                       File.Exists(Path.Combine(storage, "fattureClienti.db"));
            }

            return Registry.GetValue(
                       profile.RegistryPath,
                       "SupplierStorageDirectory",
                       null) is string supplier &&
                   !string.IsNullOrWhiteSpace(supplier) ||
                   Registry.GetValue(
                       profile.RegistryPath,
                       "CustomerStorageDirectory",
                       null) is string customer &&
                   !string.IsNullOrWhiteSpace(customer);
        }
    }
}
