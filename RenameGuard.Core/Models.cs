using System;
using System.Collections.Generic;

namespace RenameGuard.Core
{
    public sealed class RenameRules
    {
        public bool TrimWhitespace { get; set; }
        public bool ReplaceEnabled { get; set; }
        public string SearchText { get; set; }
        public string ReplaceText { get; set; }
        public bool CaseSensitive { get; set; }
        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public bool NumberingEnabled { get; set; }
        public long NumberStart { get; set; }
        public int NumberDigits { get; set; }
        public string NumberSeparator { get; set; }

        public RenameRules()
        {
            SearchText = String.Empty;
            ReplaceText = String.Empty;
            Prefix = String.Empty;
            Suffix = String.Empty;
            NumberSeparator = "_";
            CaseSensitive = true;
            NumberStart = 1;
            NumberDigits = 3;
        }
    }

    public sealed class RenameSource
    {
        public string Path { get; set; }
        public bool Included { get; set; }

        public RenameSource()
        {
            Included = true;
        }

        public RenameSource(string path)
        {
            Path = path;
            Included = true;
        }
    }

    public sealed class RenamePlanItem
    {
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        public string CurrentName { get; set; }
        public string ProposedName { get; set; }
        public bool Included { get; set; }
        public bool IsValid { get; set; }
        public bool IsChanged { get; set; }
        public string Error { get; set; }

        public RenamePlanItem()
        {
            Error = String.Empty;
            IsValid = true;
        }
    }

    public sealed class RenamePlan
    {
        public IList<RenamePlanItem> Items { get; private set; }
        public IList<string> Errors { get; private set; }

        public RenamePlan()
        {
            Items = new List<RenamePlanItem>();
            Errors = new List<string>();
        }

        public int IncludedCount
        {
            get
            {
                int count = 0;
                foreach (RenamePlanItem item in Items)
                    if (item.Included) count++;
                return count;
            }
        }

        public int ErrorCount
        {
            get
            {
                int count = 0;
                foreach (RenamePlanItem item in Items)
                    if (item.Included && !item.IsValid) count++;
                return count;
            }
        }

        public int ChangedCount
        {
            get
            {
                int count = 0;
                foreach (RenamePlanItem item in Items)
                    if (item.Included && item.IsValid && item.IsChanged) count++;
                return count;
            }
        }

        public bool CanExecute
        {
            get { return IncludedCount > 0 && ErrorCount == 0 && ChangedCount > 0; }
        }
    }

    public sealed class RenamePair
    {
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }

        public RenamePair()
        {
        }

        public RenamePair(string sourcePath, string targetPath)
        {
            SourcePath = sourcePath;
            TargetPath = targetPath;
        }
    }

    public sealed class HistoryEntry
    {
        public string OriginalPath { get; set; }
        public string RenamedPath { get; set; }

        public HistoryEntry()
        {
        }

        public HistoryEntry(string originalPath, string renamedPath)
        {
            OriginalPath = originalPath;
            RenamedPath = renamedPath;
        }
    }

    public sealed class RenameHistoryRecord
    {
        public string Id { get; set; }
        public string TimestampUtc { get; set; }
        public bool Undone { get; set; }
        public List<HistoryEntry> Entries { get; set; }

        public RenameHistoryRecord()
        {
            Entries = new List<HistoryEntry>();
        }
    }

    public sealed class RenameSettings
    {
        public bool TrimWhitespace { get; set; }
        public bool ReplaceEnabled { get; set; }
        public string SearchText { get; set; }
        public string ReplaceText { get; set; }
        public bool CaseSensitive { get; set; }
        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public bool NumberingEnabled { get; set; }
        public long NumberStart { get; set; }
        public int NumberDigits { get; set; }
        public string NumberSeparator { get; set; }
        public int HistoryLimit { get; set; }

        public RenameSettings()
        {
            SearchText = String.Empty;
            ReplaceText = String.Empty;
            Prefix = String.Empty;
            Suffix = String.Empty;
            NumberSeparator = "_";
            CaseSensitive = true;
            NumberStart = 1;
            NumberDigits = 3;
            HistoryLimit = 50;
        }
    }

    public sealed class RenameValidation
    {
        public IList<string> Errors { get; private set; }

        public RenameValidation()
        {
            Errors = new List<string>();
        }

        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }
    }

    public sealed class RenameTransactionException : Exception
    {
        public bool RollbackSucceeded { get; private set; }
        public IList<string> RecoveryErrors { get; private set; }

        public RenameTransactionException(string message, Exception inner, bool rollbackSucceeded, IList<string> recoveryErrors)
            : base(message, inner)
        {
            RollbackSucceeded = rollbackSucceeded;
            RecoveryErrors = recoveryErrors ?? new List<string>();
        }
    }
}
