using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace RenameGuard.Core
{
    public sealed class RenameHistoryStore
    {
        private readonly string rootDirectory;
        private readonly string historyPath;
        private readonly string settingsPath;
        private readonly JavaScriptSerializer serializer;
        private readonly object gate = new object();
        private List<RenameHistoryRecord> records;

        public string RecoveryWarning { get; private set; }
        public string RootDirectory { get { return rootDirectory; } }

        public RenameHistoryStore(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentNullException("directory");
            rootDirectory = Path.GetFullPath(directory);
            historyPath = Path.Combine(rootDirectory, "history.json");
            settingsPath = Path.Combine(rootDirectory, "settings.json");
            serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = Int32.MaxValue;
            RecoverReplacementBackup(historyPath);
            records = LoadHistory();
        }

        public static RenameHistoryStore CreateForCurrentUser()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new RenameHistoryStore(Path.Combine(local, "RenameGuard"));
        }

        public IList<RenameHistoryRecord> GetRecords()
        {
            lock (gate)
            {
                return records.Select(CloneRecord).ToList();
            }
        }

        public RenameHistoryRecord GetLastUndoable()
        {
            lock (gate)
            {
                RenameHistoryRecord record = records.LastOrDefault(delegate(RenameHistoryRecord item)
                {
                    return item != null && !item.Undone && item.Entries != null && item.Entries.Count > 0;
                });
                return record == null ? null : CloneRecord(record);
            }
        }

        public void Append(RenameHistoryRecord record, int historyLimit)
        {
            if (record == null) throw new ArgumentNullException("record");
            ValidateHistoryLimit(historyLimit);
            lock (gate)
            {
                List<RenameHistoryRecord> updated = records.Select(CloneRecord).ToList();
                updated.Add(CloneRecord(record));
                Trim(updated, historyLimit);
                PersistHistory(updated);
                records = updated;
            }
        }

        public bool MarkUndone(string id, int historyLimit)
        {
            ValidateHistoryLimit(historyLimit);
            lock (gate)
            {
                List<RenameHistoryRecord> updated = records.Select(CloneRecord).ToList();
                RenameHistoryRecord match = updated.LastOrDefault(delegate(RenameHistoryRecord item)
                {
                    return item != null && String.Equals(item.Id, id, StringComparison.Ordinal);
                });
                if (match == null || match.Undone) return false;
                match.Undone = true;
                Trim(updated, historyLimit);
                PersistHistory(updated);
                records = updated;
                return true;
            }
        }

        public RenameSettings LoadSettings()
        {
            lock (gate)
            {
                RecoverReplacementBackup(settingsPath);
                if (!File.Exists(settingsPath)) return new RenameSettings();
                try
                {
                    RenameSettings settings = serializer.Deserialize<RenameSettings>(File.ReadAllText(settingsPath, Encoding.UTF8));
                    if (settings == null) throw new InvalidDataException("settings.json is empty");
                    if (settings.SearchText == null) settings.SearchText = String.Empty;
                    if (settings.ReplaceText == null) settings.ReplaceText = String.Empty;
                    if (settings.Prefix == null) settings.Prefix = String.Empty;
                    if (settings.Suffix == null) settings.Suffix = String.Empty;
                    if (settings.NumberSeparator == null) settings.NumberSeparator = "_";
                    if (settings.HistoryLimit < 10 || settings.HistoryLimit > 200) settings.HistoryLimit = 50;
                    if (settings.NumberDigits < 1 || settings.NumberDigits > 12) settings.NumberDigits = 3;
                    if (settings.NumberStart < 0) settings.NumberStart = 1;
                    return settings;
                }
                catch (Exception ex)
                {
                    RecoveryWarning = CombineWarning(RecoveryWarning, BackupBroken(settingsPath, ex));
                    return new RenameSettings();
                }
            }
        }

        public void SaveSettings(RenameSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (settings.HistoryLimit < 10 || settings.HistoryLimit > 200)
                throw new ArgumentOutOfRangeException("settings", "History limit must be between 10 and 200.");
            lock (gate)
            {
                PersistJson(settingsPath, settings);
            }
        }

        private List<RenameHistoryRecord> LoadHistory()
        {
            if (!File.Exists(historyPath)) return new List<RenameHistoryRecord>();
            try
            {
                HistoryContainer data = serializer.Deserialize<HistoryContainer>(File.ReadAllText(historyPath, Encoding.UTF8));
                if (data == null || data.Records == null) throw new InvalidDataException("history.json has an invalid shape");
                List<RenameHistoryRecord> loaded = data.Records;
                if (loaded.Any(delegate(RenameHistoryRecord item) { return item == null || item.Entries == null; }))
                    throw new InvalidDataException("history.json contains an invalid record");
                return loaded;
            }
            catch (Exception ex)
            {
                RecoveryWarning = BackupBroken(historyPath, ex);
                return new List<RenameHistoryRecord>();
            }
        }

        private void PersistHistory(List<RenameHistoryRecord> updated)
        {
            HistoryContainer data = new HistoryContainer();
            data.Records = updated;
            PersistJson(historyPath, data);
        }

        private void PersistJson(string path, object value)
        {
            Directory.CreateDirectory(rootDirectory);
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            string backup = null;
            bool committed = false;
            try
            {
                File.WriteAllText(temporary, serializer.Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    backup = path + ".previous-" + Guid.NewGuid().ToString("N");
                    File.Copy(path, backup);
                    File.Delete(path);
                }
                File.Move(temporary, path);
                committed = true;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    try { File.Delete(temporary); }
                    catch { }
                }
                if (!String.IsNullOrEmpty(backup) && File.Exists(backup))
                {
                    if (committed && File.Exists(path))
                    {
                        try { File.Delete(backup); }
                        catch { }
                    }
                    else if (!File.Exists(path))
                    {
                        try { File.Move(backup, path); }
                        catch { }
                    }
                }
            }
        }

        private static void RecoverReplacementBackup(string path)
        {
            if (File.Exists(path)) return;
            string directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileName(path) + ".previous-*";
            if (!Directory.Exists(directory)) return;
            FileInfo backup = new DirectoryInfo(directory).GetFiles(fileName)
                .OrderByDescending(delegate(FileInfo item) { return item.LastWriteTimeUtc; })
                .FirstOrDefault();
            if (backup != null)
            {
                try { File.Move(backup.FullName, path); }
                catch { }
            }
        }

        private string BackupBroken(string path, Exception failure)
        {
            string backup = path + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".broken";
            int suffix = 1;
            while (File.Exists(backup))
            {
                backup = path + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + suffix.ToString() + ".broken";
                suffix++;
            }
            try
            {
                File.Move(path, backup);
                return "不正なJSONを退避しました: " + backup;
            }
            catch (Exception backupFailure)
            {
                return "JSONを読み込めず、退避も失敗しました: " + path + " (" + failure.Message + "; " + backupFailure.Message + ")";
            }
        }

        private static void ValidateHistoryLimit(int limit)
        {
            if (limit < 10 || limit > 200) throw new ArgumentOutOfRangeException("limit", "History limit must be between 10 and 200.");
        }

        private static void Trim(List<RenameHistoryRecord> list, int limit)
        {
            while (list.Count > limit) list.RemoveAt(0);
        }

        private static string CombineWarning(string first, string second)
        {
            if (String.IsNullOrEmpty(first)) return second;
            if (String.IsNullOrEmpty(second)) return first;
            return first + Environment.NewLine + second;
        }

        private static RenameHistoryRecord CloneRecord(RenameHistoryRecord record)
        {
            RenameHistoryRecord clone = new RenameHistoryRecord();
            clone.Id = record.Id;
            clone.TimestampUtc = record.TimestampUtc;
            clone.Undone = record.Undone;
            clone.Entries = record.Entries == null
                ? new List<HistoryEntry>()
                : record.Entries.Select(delegate(HistoryEntry entry)
                {
                    return new HistoryEntry(entry.OriginalPath, entry.RenamedPath);
                }).ToList();
            return clone;
        }

        private sealed class HistoryContainer
        {
            public List<RenameHistoryRecord> Records { get; set; }

            public HistoryContainer()
            {
                Records = new List<RenameHistoryRecord>();
            }
        }
    }

    public sealed class RenameCoordinator
    {
        private readonly RenameHistoryStore history;
        private readonly TransactionalRenamer renamer;

        public RenameCoordinator(RenameHistoryStore historyStore, TransactionalRenamer transactionalRenamer)
        {
            if (historyStore == null) throw new ArgumentNullException("historyStore");
            if (transactionalRenamer == null) throw new ArgumentNullException("transactionalRenamer");
            history = historyStore;
            renamer = transactionalRenamer;
        }

        public RenameHistoryRecord Rename(IEnumerable<RenamePair> requestedPairs, int historyLimit)
        {
            List<RenamePair> pairs = requestedPairs == null ? new List<RenamePair>() : requestedPairs.ToList();
            RenameValidation validation = RenamePlanner.ValidatePairs(pairs);
            if (!validation.IsValid) throw new InvalidOperationException(String.Join(Environment.NewLine, validation.Errors.ToArray()));
            pairs = pairs.Where(delegate(RenamePair pair)
            {
                return !String.Equals(Path.GetFullPath(pair.SourcePath), Path.GetFullPath(pair.TargetPath), StringComparison.Ordinal);
            }).ToList();
            if (pairs.Count == 0) return null;

            renamer.Execute(pairs);
            RenameHistoryRecord record = new RenameHistoryRecord();
            record.Id = Guid.NewGuid().ToString("N");
            record.TimestampUtc = DateTime.UtcNow.ToString("o");
            record.Entries = pairs.Select(delegate(RenamePair pair)
            {
                return new HistoryEntry(Path.GetFullPath(pair.SourcePath), Path.GetFullPath(pair.TargetPath));
            }).ToList();

            try
            {
                history.Append(record, historyLimit);
            }
            catch (Exception persistFailure)
            {
                try
                {
                    renamer.Execute(record.Entries.Select(delegate(HistoryEntry entry)
                    {
                        return new RenamePair(entry.RenamedPath, entry.OriginalPath);
                    }));
                }
                catch (Exception rollbackFailure)
                {
                    throw new RenameTransactionException(
                        "履歴を保存できず、ファイル名の戻し処理も失敗しました。履歴とファイルの場所を確認してください。",
                        new AggregateException(persistFailure, rollbackFailure), false,
                        new List<string> { rollbackFailure.Message });
                }
                throw new IOException("履歴を保存できなかったため、変更したファイルを元に戻しました。", persistFailure);
            }
            return record;
        }

        public RenameHistoryRecord UndoLatest(int historyLimit)
        {
            RenameHistoryRecord record = history.GetLastUndoable();
            if (record == null) return null;
            List<RenamePair> pairs = record.Entries.Select(delegate(HistoryEntry entry)
            {
                return new RenamePair(entry.RenamedPath, entry.OriginalPath);
            }).ToList();
            RenameValidation validation = RenamePlanner.ValidatePairs(pairs);
            if (!validation.IsValid) throw new InvalidOperationException(String.Join(Environment.NewLine, validation.Errors.ToArray()));
            renamer.Execute(pairs);
            try
            {
                if (!history.MarkUndone(record.Id, historyLimit))
                    throw new InvalidOperationException("履歴の対象が見つからないか、既にUndo済みです。");
            }
            catch (Exception persistFailure)
            {
                try
                {
                    renamer.Execute(pairs.Select(delegate(RenamePair pair)
                    {
                        return new RenamePair(pair.TargetPath, pair.SourcePath);
                    }));
                }
                catch (Exception rollbackFailure)
                {
                    throw new RenameTransactionException(
                        "Undo履歴を保存できず、変更を戻す処理も失敗しました。ファイルの場所を確認してください。",
                        new AggregateException(persistFailure, rollbackFailure), false,
                        new List<string> { rollbackFailure.Message });
                }
                throw new IOException("Undo履歴を保存できなかったため、Undoを取り消しました。", persistFailure);
            }
            return record;
        }
    }
}
