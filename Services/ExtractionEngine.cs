using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public class ExtractionEngine
    {
        public static string ProcessZip(string zipFilePath, string configContent, int importYear = 0, int importMonth = 0)
        {
            if (!zipFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sono supportati solo file .zip.");

            var rules = ConfigParser.Parse(configContent);
            
            // 1. Find Base Extract Dir
            var extractRule = rules.FirstOrDefault(r => r.Action == RuleAction.ExtractDir);
            string baseExtractDir = ResolveConfiguredPath(extractRule != null ? extractRule.SourceOrPath : Path.Combine(Path.GetTempPath(), "FattureEstratte_" + Guid.NewGuid().ToString()));
            string runExtractDir = Path.Combine(baseExtractDir, Path.GetFileNameWithoutExtension(zipFilePath) + "_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(runExtractDir);

            // 2. Extract ZIP
            using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // Directory
                    if (IsInBackup(entry.FullName)) continue;
                    
                    string destinationPath = Path.GetFullPath(Path.Combine(runExtractDir, entry.FullName));
                    if (!destinationPath.StartsWith(runExtractDir, StringComparison.OrdinalIgnoreCase))
                        continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    entry.ExtractToFile(destinationPath, true);
                }
            }

            // 3. Apply Rules
            foreach (var rule in rules)
            {
                if (rule.Action == RuleAction.ExtractDir || rule.Action == RuleAction.PassiveDir ||
                    rule.Action == RuleAction.ArchiveDir || rule.Action == RuleAction.ZipWorkDir) continue;

                if (rule.Action == RuleAction.CreateDir)
                {
                    string path = ResolveConfiguredPath(rule.SourceOrPath);
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }
                    continue;
                }

                // Copy or Move
                if (rule.Action == RuleAction.Copy || rule.Action == RuleAction.Move)
                {
                    string searchPattern = rule.SourceOrPath;
                    if (string.IsNullOrEmpty(searchPattern)) searchPattern = "*.*";

                    string configuredDestination = ResolveConfiguredPath(rule.Destination);
                    if (!Directory.Exists(configuredDestination))
                    {
                        Directory.CreateDirectory(configuredDestination);
                    }

                    var files = Directory.GetFiles(runExtractDir, searchPattern, SearchOption.AllDirectories)
                                         .Where(IsAllowedInvoiceFile);
                    foreach (var file in files)
                    {
                        string originalName = Path.GetFileName(file);
                        string finalName = originalName;
                        string finalDestination = configuredDestination;

                        // Resolve variables for RENAME
                        if (!string.IsNullOrEmpty(rule.RenamePattern))
                        {
                            finalName = ResolveRenamePattern(rule.RenamePattern, file, originalName, importYear, importMonth);
                        }

                        // Resolve variables for DESTINATION
                        if (!string.IsNullOrEmpty(finalDestination))
                        {
                            finalDestination = ResolveRenamePattern(finalDestination, file, originalName, importYear, importMonth);
                        }

                        string destPath = GetAvailablePath(Path.Combine(finalDestination, finalName));
                        
                        try 
                        {
                            if (!Directory.Exists(finalDestination))
                            {
                                Directory.CreateDirectory(finalDestination);
                            }

                            if (rule.Action == RuleAction.Copy)
                            {
                                File.Copy(file, destPath, true);
                            }
                            else if (rule.Action == RuleAction.Move)
                            {
                                if (File.Exists(destPath)) File.Delete(destPath);
                                File.Move(file, destPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error {(rule.Action == RuleAction.Copy ? "copying" : "moving")} file {file} to {destPath}: {ex.Message}");
                        }
                    }
                }
            }

            return runExtractDir;
        }

        public static IEnumerable<string> GetInvoiceFiles(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return Enumerable.Empty<string>();

            return Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                            .Where(IsAllowedInvoiceFile);
        }

        public static string GetConfiguredPath(IEnumerable<ConfigRule> rules, RuleAction action, string defaultPath)
        {
            return ResolveConfiguredPath(rules.FirstOrDefault(r => r.Action == action)?.SourceOrPath ?? defaultPath);
        }

        public static string ResolveConfiguredPath(string path)
        {
            if (Path.IsPathRooted(path))
                return path;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        public static string GetAvailablePath(string path)
        {
            if (!File.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            int index = 2;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"{name}_{index}{ext}");
                index++;
            } while (File.Exists(candidate));

            return candidate;
        }

        private static bool IsAllowedInvoiceFile(string path)
        {
            string ext = Path.GetExtension(path);
            return !IsInBackup(path) &&
                   (ext.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".p7m", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsInBackup(string path)
        {
            return path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                       .Any(p => p.Equals("Backup", StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveRenamePattern(string pattern, string filePath, string originalName, int importYear = 0, int importMonth = 0)
        {
            string result = pattern;
            result = result.Replace("{filename}", originalName, StringComparison.OrdinalIgnoreCase);

            if (result.Contains("{year}", StringComparison.OrdinalIgnoreCase) || 
                result.Contains("{month}", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("{day}", StringComparison.OrdinalIgnoreCase) ||
                result.Contains("{company}", StringComparison.OrdinalIgnoreCase))
            {
                // We need to parse the invoice to get these details
                var invoiceData = InvoiceParser.ParseInvoice(filePath);
                int year = importYear > 0 ? importYear : invoiceData.Year;
                int month = importMonth > 0 ? importMonth : invoiceData.Month;
                
                result = Regex.Replace(result, "{year}", year > 0 ? year.ToString() : "0000", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, "{month}", month > 0 ? month.ToString("D2") : "00", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, "{day}", invoiceData.Day > 0 ? invoiceData.Day.ToString("D2") : "00", RegexOptions.IgnoreCase);
                
                string companySafe = string.IsNullOrEmpty(invoiceData.SenderName) ? "Sconosciuta" : string.Join("_", invoiceData.SenderName.Split(Path.GetInvalidFileNameChars()));
                result = Regex.Replace(result, "{company}", companySafe, RegexOptions.IgnoreCase);
            }

            return result;
        }
    }
}
