using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace FattureViewer.Services
{
    public enum AppProfileKind
    {
        Omt,
        CCase,
        Fuchs
    }

    public sealed class AppProfileDefinition
    {
        public AppProfileKind Kind { get; init; }
        public string Id { get; init; } = "omt";
        public string DisplayName { get; init; } = "OMT";
        public bool SupportsPassiveArchive { get; init; }
        public string CompanyVatNumber { get; init; } = string.Empty;
        public string RegistryPath { get; init; } =
            @"HKEY_CURRENT_USER\Software\FattureViewer";
    }

    public static class AppProfileService
    {
        public static AppProfileDefinition Current { get; } =
            ParseProfile(Environment.GetCommandLineArgs());

        public static AppProfileDefinition ParseProfile(
            IReadOnlyList<string> arguments)
        {
            for (int index = 0; index < arguments.Count - 1; index++)
            {
                if (!arguments[index].Equals(
                        "--profile",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                string profileId = arguments[index + 1];
                if (profileId.Equals("ccase", StringComparison.OrdinalIgnoreCase))
                    return Create(AppProfileKind.CCase);
                if (profileId.Equals("fuchs", StringComparison.OrdinalIgnoreCase))
                    return Create(AppProfileKind.Fuchs);
                break;
            }
            return Create(AppProfileKind.Omt);
        }

        public static AppProfileDefinition Create(AppProfileKind kind)
        {
            return kind switch
            {
                AppProfileKind.CCase => new AppProfileDefinition
                {
                    Kind = kind,
                    Id = "ccase",
                    DisplayName = "C.CASE SRL",
                    SupportsPassiveArchive = false,
                    CompanyVatNumber = "IT03415270168",
                    RegistryPath =
                        @"HKEY_CURRENT_USER\Software\FattureViewer\Profiles\CCASE"
                },
                AppProfileKind.Fuchs => new AppProfileDefinition
                {
                    Kind = kind,
                    Id = "fuchs",
                    DisplayName = "Fuchs Stahlrohre Srl",
                    SupportsPassiveArchive = false,
                    CompanyVatNumber = "IT04127340166",
                    RegistryPath =
                        @"HKEY_CURRENT_USER\Software\FattureViewer\Profiles\FUCHS"
                },
                _ => new AppProfileDefinition
                {
                    Kind = AppProfileKind.Omt,
                    Id = "omt",
                    DisplayName = "OMT di Terzi Gian Antonio",
                    SupportsPassiveArchive = true,
                    CompanyVatNumber = "IT02745400164",
                    RegistryPath =
                        @"HKEY_CURRENT_USER\Software\FattureViewer"
                }
            };
        }

        public static string GetProfileDataDirectory(
            AppProfileDefinition? profile = null)
        {
            profile ??= Current;
            return profile.Kind == AppProfileKind.Omt
                ? AppDomain.CurrentDomain.BaseDirectory
                : Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "FattureViewer",
                    "Profiles",
                    profile.Id.ToUpperInvariant());
        }

        public static string GetPasswordFilePath(
            AppProfileDefinition? profile = null)
        {
            return Path.Combine(
                GetProfileDataDirectory(profile),
                "password.dat");
        }

        public static string GetSessionDirectory()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "FattureViewer",
                Current.Id,
                "Session");
        }

        public static void ApplyWindowsIdentity()
        {
            if (OperatingSystem.IsWindows())
                SetCurrentProcessExplicitAppUserModelID(
                    $"FattureViewer.{Current.Id}");
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(
            string appId);
    }
}
