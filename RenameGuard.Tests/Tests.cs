using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RenameGuard.Core;

internal static class Tests
{
    private static string testRoot;
    private static int passed;

    private static int Main(string[] args)
    {
        testRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Environment.CurrentDirectory, "work", "testdata");
        Directory.CreateDirectory(testRoot);
        Run("rules keep extension and follow fixed order", TestRulesAndExtension);
        Run("replacement uses literal text and case setting", TestLiteralCase);
        Run("unchanged selected file blocks a target collision", TestStationaryCollision);
        Run("invalid targets reject the entire batch", TestBatchPrevalidation);
        Run("two-phase transaction safely swaps names", TestSwap);
        Run("failed commit rolls every file back", TestRollback);
        Run("history persists and Undo restores files", TestHistoryUndo);
        Run("Undo conflict changes no file", TestUndoConflict);
        Run("corrupt history is backed up", TestCorruptHistoryBackup);
        Run("settings round-trip and history bounds", TestSettingsAndBounds);
        Run("1000-file numbered batch renames and undoes", TestThousandFileBatch);
        Console.WriteLine("PASS: " + passed + " tests");
        return 0;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + name + Environment.NewLine + ex);
            Environment.Exit(1);
        }
    }

    private static void TestRulesAndExtension()
    {
        WithDirectory(delegate(string dir)
        {
            string source = WriteFile(dir, "  Cat Pic.txt", "data");
            RenameRules rules = new RenameRules();
            rules.TrimWhitespace = true;
            rules.ReplaceEnabled = true;
            rules.SearchText = "cat";
            rules.ReplaceText = "dog";
            rules.CaseSensitive = false;
            rules.Prefix = "pre-";
            rules.Suffix = "-done";
            rules.NumberingEnabled = true;
            rules.NumberStart = 7;
            rules.NumberDigits = 3;
            rules.NumberSeparator = "_";
            RenamePlan plan = RenamePlanner.Build(new[] { new RenameSource(source) }, rules);
            Equal(0, plan.ErrorCount, "unexpected plan error");
            Equal("pre-dog Pic-done_007.txt", plan.Items[0].ProposedName, "rule order or extension preservation changed");
            Equal(true, plan.CanExecute, "valid plan should execute");
        });
    }

    private static void TestLiteralCase()
    {
        WithDirectory(delegate(string dir)
        {
            string source = WriteFile(dir, "a.b.txt", "data");
            RenameRules rules = new RenameRules();
            rules.ReplaceEnabled = true;
            rules.SearchText = ".";
            rules.ReplaceText = "x";
            rules.CaseSensitive = true;
            RenamePlan plan = RenamePlanner.Build(new[] { new RenameSource(source) }, rules);
            Equal("axb.txt", plan.Items[0].ProposedName, "search must be literal and extension retained");

            rules.SearchText = "A";
            rules.ReplaceText = "z";
            plan = RenamePlanner.Build(new[] { new RenameSource(source) }, rules);
            Equal("a.b.txt", plan.Items[0].ProposedName, "case-sensitive search unexpectedly matched");
            rules.CaseSensitive = false;
            plan = RenamePlanner.Build(new[] { new RenameSource(source) }, rules);
            Equal("z.b.txt", plan.Items[0].ProposedName, "case-insensitive search did not match");
        });
    }

    private static void TestBatchPrevalidation()
    {
        WithDirectory(delegate(string dir)
        {
            string first = WriteFile(dir, "first.txt", "first");
            string second = WriteFile(dir, "second.txt", "second");
            bool invalidName = false;
            try { RenamePlanner.ValidateFileName("CON.txt"); }
            catch (InvalidOperationException) { invalidName = true; }
            Equal(true, invalidName, "reserved device name was accepted");

            RenameHistoryStore store = new RenameHistoryStore(Path.Combine(dir, "state"));
            RenameCoordinator coordinator = new RenameCoordinator(store, new TransactionalRenamer());
            bool rejected = false;
            try
            {
                coordinator.Rename(new[]
                {
                    new RenamePair(first, Path.Combine(dir, "good.txt")),
                    new RenamePair(second, Path.Combine(dir, "NUL.txt"))
                }, 50);
            }
            catch (InvalidOperationException) { rejected = true; }
            Equal(true, rejected, "invalid batch was not rejected");
            Equal(true, File.Exists(first), "first file moved despite invalid batch");
            Equal(true, File.Exists(second), "second file moved despite invalid batch");
            Equal(false, File.Exists(Path.Combine(dir, "good.txt")), "partial destination was created");
        });
    }

    private static void TestStationaryCollision()
    {
        WithDirectory(delegate(string dir)
        {
            string unchanged = WriteFile(dir, "keep.txt", "keep");
            string changed = WriteFile(dir, "rename.txt", "rename");
            RenameRules rules = new RenameRules();
            rules.ReplaceEnabled = true;
            rules.SearchText = "rename";
            rules.ReplaceText = "keep";
            RenamePlan plan = RenamePlanner.Build(new[] { new RenameSource(unchanged), new RenameSource(changed) }, rules);
            Equal(1, plan.ErrorCount, "stationary selected file should occupy its name");
            Equal(false, plan.CanExecute, "collision with unchanged file should block the batch");
        });
    }

    private static void TestSwap()
    {
        WithDirectory(delegate(string dir)
        {
            string one = WriteFile(dir, "one.txt", "one");
            string two = WriteFile(dir, "two.txt", "two");
            new TransactionalRenamer().Execute(new[] { new RenamePair(one, two), new RenamePair(two, one) });
            Equal("two", File.ReadAllText(one), "swap failed for first path");
            Equal("one", File.ReadAllText(two), "swap failed for second path");
        });
    }

    private static void TestRollback()
    {
        WithDirectory(delegate(string dir)
        {
            string one = WriteFile(dir, "one.txt", "one");
            string two = WriteFile(dir, "two.txt", "two");
            TransactionalRenamer renamer = new TransactionalRenamer(new FailingMover(3));
            bool rolledBack = false;
            try { renamer.Execute(new[] { new RenamePair(one, Path.Combine(dir, "new-one.txt")), new RenamePair(two, Path.Combine(dir, "new-two.txt")) }); }
            catch (RenameTransactionException ex) { rolledBack = ex.RollbackSucceeded; }
            Equal(true, rolledBack, "failed operation did not report a complete rollback");
            Equal(true, File.Exists(one), "first source was not restored");
            Equal(true, File.Exists(two), "second source was not restored");
            Equal(false, File.Exists(Path.Combine(dir, "new-one.txt")), "first destination survived rollback");
            Equal(false, File.Exists(Path.Combine(dir, "new-two.txt")), "second destination survived rollback");
            Equal("one", File.ReadAllText(one), "first file contents changed");
            Equal("two", File.ReadAllText(two), "second file contents changed");
        });
    }

    private static void TestHistoryUndo()
    {
        WithDirectory(delegate(string dir)
        {
            string source = WriteFile(dir, "before.txt", "data");
            string target = Path.Combine(dir, "after.txt");
            string state = Path.Combine(dir, "state");
            RenameCoordinator coordinator = new RenameCoordinator(new RenameHistoryStore(state), new TransactionalRenamer());
            RenameHistoryRecord record = coordinator.Rename(new[] { new RenamePair(source, target) }, 50);
            Equal(1, record.Entries.Count, "history entry count");
            Equal(true, File.Exists(target), "rename did not happen");

            RenameHistoryStore reloadedStore = new RenameHistoryStore(state);
            Equal(1, reloadedStore.GetRecords().Count, "history did not persist across store restart");
            RenameCoordinator reloadedCoordinator = new RenameCoordinator(reloadedStore, new TransactionalRenamer());
            reloadedCoordinator.UndoLatest(50);
            Equal(true, File.Exists(source), "Undo did not restore the original path");
            Equal(false, File.Exists(target), "Undo left the renamed path behind");
            Equal(true, reloadedStore.GetRecords()[0].Undone, "Undo flag was not persisted");
        });
    }

    private static void TestUndoConflict()
    {
        WithDirectory(delegate(string dir)
        {
            string source = WriteFile(dir, "before.txt", "original");
            string target = Path.Combine(dir, "after.txt");
            RenameHistoryStore store = new RenameHistoryStore(Path.Combine(dir, "state"));
            RenameCoordinator coordinator = new RenameCoordinator(store, new TransactionalRenamer());
            coordinator.Rename(new[] { new RenamePair(source, target) }, 50);
            WriteFile(dir, "before.txt", "external");
            bool rejected = false;
            try { coordinator.UndoLatest(50); }
            catch (InvalidOperationException) { rejected = true; }
            Equal(true, rejected, "Undo ignored an occupied original path");
            Equal("original", File.ReadAllText(target), "Undo conflict modified the renamed file");
            Equal("external", File.ReadAllText(source), "Undo conflict modified the colliding file");
        });
    }

    private static void TestCorruptHistoryBackup()
    {
        WithDirectory(delegate(string dir)
        {
            string state = Path.Combine(dir, "state");
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "history.json"), "not json");
            RenameHistoryStore store = new RenameHistoryStore(state);
            Equal(0, store.GetRecords().Count, "corrupt history should load empty");
            Equal(true, Directory.GetFiles(state, "history.json.*.broken").Length == 1, "corrupt file was not backed up");
            Equal(true, !String.IsNullOrEmpty(store.RecoveryWarning), "recovery warning was not set");
        });
    }

    private static void TestSettingsAndBounds()
    {
        WithDirectory(delegate(string dir)
        {
            string state = Path.Combine(dir, "state");
            RenameHistoryStore store = new RenameHistoryStore(state);
            RenameSettings settings = new RenameSettings();
            settings.Prefix = "pre_";
            settings.HistoryLimit = 125;
            store.SaveSettings(settings);
            RenameSettings loaded = new RenameHistoryStore(state).LoadSettings();
            Equal("pre_", loaded.Prefix, "settings did not persist");
            Equal(125, loaded.HistoryLimit, "history limit did not persist");
            bool outOfRangeRejected = false;
            try { store.SaveSettings(new RenameSettings { HistoryLimit = 9 }); }
            catch (ArgumentOutOfRangeException) { outOfRangeRejected = true; }
            Equal(true, outOfRangeRejected, "invalid history limit accepted");
        });
    }

    private static void TestThousandFileBatch()
    {
        WithDirectory(delegate(string dir)
        {
            List<RenameSource> sources = new List<RenameSource>();
            for (int i = 0; i < 1000; i++)
            {
                string name = "item-" + i.ToString("D4") + ".txt";
                string path = WriteFile(dir, name, "content-" + i.ToString());
                sources.Add(new RenameSource(path));
            }

            RenameRules rules = new RenameRules();
            rules.NumberingEnabled = true;
            rules.NumberStart = 1;
            rules.NumberDigits = 4;
            rules.NumberSeparator = "_";
            RenamePlan plan = RenamePlanner.Build(sources, rules);
            Equal(1000, plan.IncludedCount, "selected item count");
            Equal(1000, plan.ChangedCount, "changed item count");
            Equal(0, plan.ErrorCount, "batch validation errors");
            Equal("item-0000_0001.txt", plan.Items[0].ProposedName, "first number is wrong");
            Equal("item-0999_1000.txt", plan.Items[999].ProposedName, "last number is wrong");

            string state = Path.Combine(dir, "state");
            RenameHistoryStore store = new RenameHistoryStore(state);
            RenameCoordinator coordinator = new RenameCoordinator(store, new TransactionalRenamer());
            List<RenamePair> pairs = plan.Items.Select(delegate(RenamePlanItem item)
            {
                return new RenamePair(item.SourcePath, item.TargetPath);
            }).ToList();
            RenameHistoryRecord record = coordinator.Rename(pairs, 50);
            Equal(1000, record.Entries.Count, "history did not keep the full batch");
            Equal(1000, pairs.Count(delegate(RenamePair pair) { return File.Exists(pair.TargetPath); }), "not all numbered targets exist");
            Equal(0, pairs.Count(delegate(RenamePair pair) { return File.Exists(pair.SourcePath); }), "old names remained after rename");

            coordinator.UndoLatest(50);
            Equal(1000, pairs.Count(delegate(RenamePair pair) { return File.Exists(pair.SourcePath); }), "not all original names were restored");
            Equal(0, pairs.Count(delegate(RenamePair pair) { return File.Exists(pair.TargetPath); }), "numbered names remained after Undo");
            for (int i = 0; i < 1000; i++)
                Equal("content-" + i.ToString(), File.ReadAllText(sources[i].Path), "file contents changed during batch for index " + i.ToString());
        });
    }

    private static string WriteFile(string dir, string name, string contents)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static void WithDirectory(Action<string> action)
    {
        string path = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { action(path); }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Object.Equals(expected, actual)) throw new InvalidOperationException(message + "; expected " + expected + ", actual " + actual);
    }

    private sealed class FailingMover : IFileMover
    {
        private readonly PhysicalFileMover inner = new PhysicalFileMover();
        private readonly int failAt;
        private int moveCount;
        private bool hasFailed;

        public FailingMover(int failOnMove)
        {
            failAt = failOnMove;
        }

        public bool FileExists(string path) { return inner.FileExists(path); }
        public bool DirectoryExists(string path) { return inner.DirectoryExists(path); }
        public void MoveFile(string source, string target)
        {
            moveCount++;
            if (!hasFailed && moveCount == failAt)
            {
                hasFailed = true;
                throw new IOException("simulated move failure");
            }
            inner.MoveFile(source, target);
        }
    }
}
