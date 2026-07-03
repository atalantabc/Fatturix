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
        public static void ProcessZip(string zipFilePath, string configContent)
        {
            var rules = ConfigParser.Parse(configContent);
            
            // 1. Find Base Extract Dir
            var extractRule = rules.FirstOrDefault(r => r.Action == RuleAction.ExtractDir);
            string baseExtractDir = extractRule != null ? extractRule.SourceOrPath : Path.Combine(Path.GetTempPath(), "FattureEstratte_" + Guid.NewGuid().ToString());

            if (!Directory.Exists(baseExtractDir))
            {
                Directory.CreateDirectory(baseExtractDir);
            }

            // 2. Extract ZIP
            using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // Directory
                    
                    string destinationPath = Path.Combine(baseExtractDir, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    entry.ExtractToFile(destinationPath, true);
                }
            }

            // 3. Apply Rules
            foreach (var rule in rules)
            {
                if (rule.Action == RuleAction.ExtractDir) continue;

                if (rule.Action == RuleAction.CreateDir)
                {
                    if (!Directory.Exists(rule.SourceOrPath))
                    {
                        Directory.CreateDirectory(rule.SourceOrPath);
                    }
                    continue;
                }

                // Copy or Move
                if (rule.Action == RuleAction.Copy || rule.Action == RuleAction.Move)
                {
                    string searchPattern = rule.SourceOrPath;
                    if (string.IsNullOrEmpty(searchPattern)) searchPattern = "*.*";

                    if (!Directory.Exists(rule.Destination))
                    {
                        Directory.CreateDirectory(rule.Destination);
                    }

                    var files = Directory.GetFiles(baseExtractDir, searchPattern, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        string originalName = Path.GetFileName(file);
                        string finalName = originalName;
                        string finalDestination = rule.Destination;

                        // Resolve variables for RENAME
                        if (!string.IsNullOrEmpty(rule.RenamePattern))
                        {
                            finalName = ResolveRenamePattern(rule.RenamePattern, file, originalName);
                        }

                        // Resolve variables for DESTINATION
                        if (!string.IsNullOrEmpty(finalDestination))
                        {
                            finalDestination = ResolveRenamePattern(finalDestination, file, originalName);
                        }

                        string destPath = Path.Combine(finalDestination, finalName);
                        
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
        }

        private static string ResolveRenamePattern(string pattern, string filePath, string originalName)
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
                
                result = Regex.Replace(result, "{year}", invoiceData.Year > 0 ? invoiceData.Year.ToString() : "0000", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, "{month}", invoiceData.Month > 0 ? invoiceData.Month.ToString("D2") : "00", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, "{day}", invoiceData.Day > 0 ? invoiceData.Day.ToString("D2") : "00", RegexOptions.IgnoreCase);
                
                string companySafe = string.IsNullOrEmpty(invoiceData.CompanyName) ? "Sconosciuta" : string.Join("_", invoiceData.CompanyName.Split(Path.GetInvalidFileNameChars()));
                result = Regex.Replace(result, "{company}", companySafe, RegexOptions.IgnoreCase);
            }

            return result;
        }
    }
}
