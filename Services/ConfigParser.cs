using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public class ConfigParser
    {
        public static List<ConfigRule> Parse(string configContent)
        {
            var rules = new List<ConfigRule>();
            var lines = configContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
                    continue; // Skip empty lines and comments

                try
                {
                    rules.Add(ParseLine(trimmed));
                }
                catch (Exception ex)
                {
                    // Log or handle parsing error for this specific line
                    System.Diagnostics.Debug.WriteLine($"Error parsing line: {trimmed}. Exception: {ex.Message}");
                }
            }

            return rules;
        }

        private static ConfigRule ParseLine(string line)
        {
            var rule = new ConfigRule();

            if (line.StartsWith("EXTRACT_DIR ", StringComparison.OrdinalIgnoreCase))
            {
                rule.Action = RuleAction.ExtractDir;
                rule.SourceOrPath = ExtractStringContent(line, "EXTRACT_DIR ");
            }
            else if (line.StartsWith("PASSIVE_DIR ", StringComparison.OrdinalIgnoreCase))
            {
                rule.Action = RuleAction.PassiveDir;
                rule.SourceOrPath = ExtractStringContent(line, "PASSIVE_DIR ");
            }
            else if (line.StartsWith("ARCHIVE_DIR ", StringComparison.OrdinalIgnoreCase))
            {
                rule.Action = RuleAction.ArchiveDir;
                rule.SourceOrPath = ExtractStringContent(line, "ARCHIVE_DIR ");
            }
            else if (line.StartsWith("ZIP_WORK_DIR ", StringComparison.OrdinalIgnoreCase))
            {
                rule.Action = RuleAction.ZipWorkDir;
                rule.SourceOrPath = ExtractStringContent(line, "ZIP_WORK_DIR ");
            }
            else if (line.StartsWith("CREATE_DIR ", StringComparison.OrdinalIgnoreCase))
            {
                rule.Action = RuleAction.CreateDir;
                rule.SourceOrPath = ExtractStringContent(line, "CREATE_DIR ");
            }
            else if (line.StartsWith("COPY ", StringComparison.OrdinalIgnoreCase) || 
                     line.StartsWith("MOVE ", StringComparison.OrdinalIgnoreCase))
            {
                rule.Action = line.StartsWith("COPY", StringComparison.OrdinalIgnoreCase) ? RuleAction.Copy : RuleAction.Move;

                // Example: COPY "*.xml" TO "C:\dest" RENAME "pre_{filename}"
                var toMatch = Regex.Match(line, @" TO\s+""([^""]+)""", RegexOptions.IgnoreCase);
                var renameMatch = Regex.Match(line, @" RENAME\s+""([^""]+)""", RegexOptions.IgnoreCase);

                // Extract source pattern which is between action and TO
                int toIndex = line.IndexOf(" TO ", StringComparison.OrdinalIgnoreCase);
                if (toIndex == -1) throw new ArgumentException("Missing 'TO' keyword in COPY/MOVE command.");

                string actionStr = rule.Action == RuleAction.Copy ? "COPY " : "MOVE ";
                string sourcePart = line.Substring(actionStr.Length, toIndex - actionStr.Length).Trim();
                
                rule.SourceOrPath = sourcePart.Trim('"');
                rule.Destination = toMatch.Groups[1].Value;

                if (renameMatch.Success)
                {
                    rule.RenamePattern = renameMatch.Groups[1].Value;
                }
            }
            else
            {
                throw new ArgumentException($"Unknown command in line: {line}");
            }

            return rule;
        }

        private static string ExtractStringContent(string line, string prefix)
        {
            var content = line.Substring(prefix.Length).Trim();
            if (content.StartsWith("\"") && content.EndsWith("\""))
            {
                return content.Substring(1, content.Length - 2);
            }
            return content;
        }
    }
}
