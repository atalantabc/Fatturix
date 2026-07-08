using System;

namespace FattureViewer.Models
{
    public enum RuleAction
    {
        ExtractDir,
        PassiveDir,
        ArchiveDir,
        ZipWorkDir,
        Copy,
        Move,
        CreateDir
    }

    public class ConfigRule
    {
        public RuleAction Action { get; set; }
        
        /// <summary>
        /// Used for EXTRACT_DIR and CREATE_DIR as the path.
        /// Used for COPY and MOVE as the source pattern (e.g. "*.xml").
        /// </summary>
        public string SourceOrPath { get; set; } = string.Empty;

        /// <summary>
        /// Destination directory for COPY and MOVE commands.
        /// </summary>
        public string Destination { get; set; } = string.Empty;

        /// <summary>
        /// Optional rename pattern (e.g. "{year}_{filename}").
        /// </summary>
        public string RenamePattern { get; set; } = string.Empty;
    }
}
