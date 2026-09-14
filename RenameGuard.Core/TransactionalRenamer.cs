using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RenameGuard.Core
{
    public interface IFileMover
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        void MoveFile(string source, string target);
    }

    public sealed class PhysicalFileMover : IFileMover
    {
        public bool FileExists(string path) { return File.Exists(path); }
        public bool DirectoryExists(string path) { return Directory.Exists(path); }
        public void MoveFile(string source, string target) { File.Move(source, target); }
    }

    public sealed class TransactionalRenamer
    {
        private readonly IFileMover mover;

        public TransactionalRenamer() : this(new PhysicalFileMover())
        {
        }

        public TransactionalRenamer(IFileMover fileMover)
        {
            if (fileMover == null) throw new ArgumentNullException("fileMover");
            mover = fileMover;
        }

        public void Execute(IEnumerable<RenamePair> requestedPairs)
        {
            List<RenamePair> requested = requestedPairs == null
                ? new List<RenamePair>()
                : requestedPairs.ToList();

            RenameValidation validation = RenamePlanner.ValidatePairs(requested);
            if (!validation.IsValid)
                throw new InvalidOperationException(String.Join(Environment.NewLine, validation.Errors.ToArray()));
            List<RenamePair> pairs = requested.Where(delegate(RenamePair pair)
            {
                return !String.Equals(Path.GetFullPath(pair.SourcePath), Path.GetFullPath(pair.TargetPath), StringComparison.Ordinal);
            }).Select(delegate(RenamePair pair)
            {
                return new RenamePair(Path.GetFullPath(pair.SourcePath), Path.GetFullPath(pair.TargetPath));
            }).ToList();
            if (pairs.Count == 0) return;

            Dictionary<RenamePair, string> staging = new Dictionary<RenamePair, string>();
            HashSet<string> reservedTemps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> targets = new HashSet<string>(pairs.Select(delegate(RenamePair pair) { return pair.TargetPath; }), StringComparer.OrdinalIgnoreCase);
            foreach (RenamePair pair in pairs)
                staging.Add(pair, CreateTempPath(pair.SourcePath, reservedTemps, targets));

            List<RenamePair> staged = new List<RenamePair>();
            List<RenamePair> committed = new List<RenamePair>();
            try
            {
                foreach (RenamePair pair in pairs)
                {
                    mover.MoveFile(pair.SourcePath, staging[pair]);
                    staged.Add(pair);
                }

                foreach (RenamePair pair in pairs)
                {
                    mover.MoveFile(staging[pair], pair.TargetPath);
                    committed.Add(pair);
                }
            }
            catch (Exception failure)
            {
                List<string> recoveryErrors = RollBack(pairs, staging, staged, committed);
                bool rollbackSucceeded = recoveryErrors.Count == 0;
                string message = rollbackSucceeded
                    ? "名前変更に失敗し、全件を元の状態に戻しました。"
                    : "名前変更に失敗し、一部の戻し処理も失敗しました。ファイルの場所を確認してください。";
                throw new RenameTransactionException(message, failure, rollbackSucceeded, recoveryErrors);
            }
        }

        private List<string> RollBack(List<RenamePair> pairs, Dictionary<RenamePair, string> staging,
            List<RenamePair> staged, List<RenamePair> committed)
        {
            List<string> errors = new List<string>();
            for (int i = committed.Count - 1; i >= 0; i--)
            {
                RenamePair pair = committed[i];
                try
                {
                    if (mover.FileExists(pair.TargetPath)) mover.MoveFile(pair.TargetPath, staging[pair]);
                }
                catch (Exception ex)
                {
                    errors.Add(pair.TargetPath + " -> " + staging[pair] + ": " + ex.Message);
                }
            }

            for (int i = staged.Count - 1; i >= 0; i--)
            {
                RenamePair pair = staged[i];
                try
                {
                    if (mover.FileExists(staging[pair])) mover.MoveFile(staging[pair], pair.SourcePath);
                }
                catch (Exception ex)
                {
                    errors.Add(staging[pair] + " -> " + pair.SourcePath + ": " + ex.Message);
                }
            }
            return errors;
        }

        private string CreateTempPath(string sourcePath, HashSet<string> reserved, HashSet<string> targets)
        {
            string directory = Path.GetDirectoryName(sourcePath);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                string candidate = Path.Combine(directory, ".RenameGuard-" + Guid.NewGuid().ToString("N") + ".tmp");
                if (candidate.Length > 259) throw new IOException("一時名のパスが長すぎます。");
                if (reserved.Contains(candidate) || targets.Contains(candidate)) continue;
                if (mover.FileExists(candidate) || mover.DirectoryExists(candidate)) continue;
                reserved.Add(candidate);
                return candidate;
            }
            throw new IOException("一時名を作成できません。");
        }
    }
}
