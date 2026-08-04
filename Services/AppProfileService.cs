using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace FattureViewer.Services
{
    public enum AppProfileKind
    {
        Omt,
        CCase
    }

    public sealed class AppProfileDefinition
    {
        public AppProfileKind Kind { get; init; }
        public string Id { get; init; } = "omt";
        public string DisplayName { get; init; } = "OMT";
        public bool SupportsPassiveArchive { get; init; }
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

                if (arguments[index + 1].Equals(
                        "ccase",
                        StringComparison.OrdinalIgnoreCase))
                    return Create(AppProfileKind.CCase);
                break;
            }
            return Create(AppProfileKind.Omt);
        }

        public static AppProfileDefinition Create(AppProfileKind kind)
        {
            return kind == AppProfileKind.CCase
                ? new AppProfileDefinition
                {
                    Kind = kind,
                    Id = "ccase",
                    DisplayName = "C.CASE",
                    SupportsPassiveArchive = false,
                    RegistryPath =
                        @"HKEY_CURRENT_USER\Software\FattureViewer\Profiles\CCASE"
                }
                : new AppProfileDefinition
                {
                    Kind = AppProfileKind.Omt,
                    Id = "omt",
                    DisplayName = "OMT",
                    SupportsPassiveArchive = true,
                    RegistryPath =
                        @"HKEY_CURRENT_USER\Software\FattureViewer"
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
                    "CCASE");
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
