using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using RenameGuard.Core;

namespace RenameGuard.App
{
    internal sealed class RenameRow : INotifyPropertyChanged
    {
        private bool included;
        private string sourcePath;
        private string currentName;
        private string previewName;
        private string status;
        private string error;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler IncludedChanged;

        public bool Included
        {
            get { return included; }
            set
            {
                if (included == value) return;
                included = value;
                Raise("Included");
                EventHandler handler = IncludedChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public string SourcePath
        {
            get { return sourcePath; }
            set { sourcePath = value; Raise("SourcePath"); }
        }

        public string CurrentName
        {
            get { return currentName; }
            set { currentName = value; Raise("CurrentName"); }
        }

        public string PreviewName
        {
            get { return previewName; }
            set { previewName = value; Raise("PreviewName"); }
        }

        public string Status
        {
            get { return status; }
            set { status = value; Raise("Status"); }
        }

        public string Error
        {
            get { return error; }
            set { error = value; Raise("Error"); }
        }

        public RenameRow(string path)
        {
            sourcePath = path;
            included = true;
            currentName = Path.GetFileName(path);
            previewName = currentName;
            status = String.Empty;
            error = String.Empty;
        }

        private void Raise(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal sealed class MainWindow : Window
    {
        private readonly ObservableCollection<RenameRow> rows;
        private readonly RenameHistoryStore history;
        private readonly RenameCoordinator coordinator;
        private readonly DataGrid fileGrid;
        private readonly TextBlock summaryText;
        private readonly TextBlock statusText;
        private readonly Button runButton;
        private readonly Button undoButton;
        private readonly CheckBox trimCheck;
        private readonly CheckBox replaceCheck;
        private readonly TextBox searchText;
        private readonly TextBox replaceText;
        private readonly CheckBox caseSensitiveCheck;
        private readonly TextBox prefixText;
        private readonly TextBox suffixText;
        private readonly CheckBox numberingCheck;
        private readonly TextBox numberStartText;
        private readonly TextBox numberDigitsText;
        private readonly TextBox numberSeparatorText;
        private readonly TextBox historyLimitText;
        private RenamePlan currentPlan;

        public MainWindow()
        {
            Title = "RenameGuard";
            Width = 1080;
            Height = 760;
            MinWidth = 820;
            MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 250));
            AllowDrop = true;
            PreviewDragOver += OnPreviewDragOver;
            PreviewDrop += OnPreviewDrop;

            history = RenameHistoryStore.CreateForCurrentUser();
            coordinator = new RenameCoordinator(history, new TransactionalRenamer());
            RenameSettings saved = history.LoadSettings();
            rows = new ObservableCollection<RenameRow>();

            DockPanel root = new DockPanel();
            root.LastChildFill = true;
            Content = root;

            StackPanel toolbar = new StackPanel();
            toolbar.Orientation = Orientation.Horizontal;
            toolbar.Margin = new Thickness(12, 12, 12, 8);
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            Button addFiles = MakeButton("\u30d5\u30a1\u30a4\u30eb\u8ffd\u52a0", 104);
            addFiles.Click += OnAddFiles;
            toolbar.Children.Add(addFiles);
            Button addFolder = MakeButton("\u30d5\u30a9\u30eb\u30c0\u30fc\u8ffd\u52a0", 118);
            addFolder.Click += OnAddFolder;
            toolbar.Children.Add(addFolder);
            Button removeSelected = MakeButton("\u9078\u629e\u3092\u524a\u9664", 104);
            removeSelected.Click += OnRemoveSelected;
            toolbar.Children.Add(removeSelected);
            Button clearAll = MakeButton("\u3059\u3079\u3066\u30af\u30ea\u30a2", 108);
            clearAll.Click += OnClearAll;
            toolbar.Children.Add(clearAll);
            toolbar.Children.Add(new TextBlock
            {
                Text = "\u307e\u305f\u306f\u3053\u3053\u306b\u30d5\u30a1\u30a4\u30eb\u3084\u30d5\u30a9\u30eb\u30c0\u30fc\u3092\u30c9\u30ed\u30c3\u30d7",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(90, 100, 110))
            });

            GroupBox ruleBox = new GroupBox();
            ruleBox.Header = "\u5909\u66f4\u30eb\u30fc\u30eb\uff08\u8868\u793a\u9806\u306b\u9069\u7528\u3001\u62e1\u5f35\u5b50\u306f\u4fdd\u6301\uff09";
            ruleBox.Margin = new Thickness(12, 0, 12, 8);
            DockPanel.SetDock(ruleBox, Dock.Top);
            root.Children.Add(ruleBox);

            StackPanel rulePanel = new StackPanel();
            rulePanel.Margin = new Thickness(10, 6, 10, 8);
            ruleBox.Content = rulePanel;

            StackPanel trimRow = MakeRuleRow();
            trimCheck = new CheckBox { Content = "\u524d\u5f8c\u306e\u7a7a\u767d\u3092\u53d6\u308a\u9664\u304f", VerticalAlignment = VerticalAlignment.Center };
            trimCheck.IsChecked = saved.TrimWhitespace;
            trimRow.Children.Add(trimCheck);
            rulePanel.Children.Add(trimRow);

            StackPanel replaceRow = MakeRuleRow();
            replaceCheck = new CheckBox { Content = "\u6587\u5b57\u7f6e\u63db", VerticalAlignment = VerticalAlignment.Center, Width = 94 };
            replaceCheck.IsChecked = saved.ReplaceEnabled;
            replaceRow.Children.Add(replaceCheck);
            replaceRow.Children.Add(MakeLabel("\u691c\u7d22", 46));
            searchText = MakeTextBox(saved.SearchText, 150);
            replaceRow.Children.Add(searchText);
            replaceRow.Children.Add(MakeLabel("\u7f6e\u63db\u5f8c", 66));
            replaceText = MakeTextBox(saved.ReplaceText, 150);
            replaceRow.Children.Add(replaceText);
            caseSensitiveCheck = new CheckBox { Content = "\u5927\u6587\u5b57\u5c0f\u6587\u5b57\u3092\u533a\u5225", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            caseSensitiveCheck.IsChecked = saved.CaseSensitive;
            replaceRow.Children.Add(caseSensitiveCheck);
            rulePanel.Children.Add(replaceRow);

            StackPanel prefixRow = MakeRuleRow();
            prefixRow.Children.Add(MakeLabel("\u63a5\u982d\u8f9e", 94));
            prefixText = MakeTextBox(saved.Prefix, 250);
            prefixRow.Children.Add(prefixText);
            prefixRow.Children.Add(MakeLabel("\u63a5\u5c3e\u8f9e", 66));
            suffixText = MakeTextBox(saved.Suffix, 250);
            prefixRow.Children.Add(suffixText);
            rulePanel.Children.Add(prefixRow);

            StackPanel numberRow = MakeRuleRow();
            numberingCheck = new CheckBox { Content = "\u9023\u756a\u3092\u4ed8\u52a0", VerticalAlignment = VerticalAlignment.Center, Width = 96 };
            numberingCheck.IsChecked = saved.NumberingEnabled;
            numberRow.Children.Add(numberingCheck);
            numberRow.Children.Add(MakeLabel("\u958b\u59cb\u5024", 62));
            numberStartText = MakeTextBox(saved.NumberStart.ToString(), 66);
            numberRow.Children.Add(numberStartText);
            numberRow.Children.Add(MakeLabel("\u6841\u6570", 48));
            numberDigitsText = MakeTextBox(saved.NumberDigits.ToString(), 48);
            numberRow.Children.Add(numberDigitsText);
            numberRow.Children.Add(MakeLabel("\u533a\u5207\u308a\u6587\u5b57", 94));
            numberSeparatorText = MakeTextBox(saved.NumberSeparator, 68);
            numberRow.Children.Add(numberSeparatorText);
            numberRow.Children.Add(MakeLabel("\u5c65\u6b74\u4ef6\u6570\uff0810\u301c200\uff09", 148));
            historyLimitText = MakeTextBox(saved.HistoryLimit.ToString(), 56);
            numberRow.Children.Add(historyLimitText);
            rulePanel.Children.Add(numberRow);

            fileGrid = new DataGrid();
            fileGrid.Margin = new Thickness(12, 0, 12, 8);
            fileGrid.AutoGenerateColumns = false;
            fileGrid.CanUserAddRows = false;
            fileGrid.CanUserDeleteRows = false;
            fileGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
            fileGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            fileGrid.SelectionMode = DataGridSelectionMode.Extended;
            fileGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
            fileGrid.ItemsSource = rows;
            fileGrid.CanUserSortColumns = false;
            fileGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "\u5bfe\u8c61",
                Binding = new Binding("Included") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 58,
                IsReadOnly = false
            });
            fileGrid.Columns.Add(new DataGridTextColumn { Header = "\u73fe\u5728\u306e\u540d\u524d", Binding = new Binding("CurrentName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            fileGrid.Columns.Add(new DataGridTextColumn { Header = "\u5909\u66f4\u5f8c\u306e\u30d7\u30ec\u30d3\u30e5\u30fc", Binding = new Binding("PreviewName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
            fileGrid.Columns.Add(new DataGridTextColumn { Header = "\u30d5\u30a9\u30eb\u30c0\u30fc", Binding = new Binding("SourcePath"), Width = new DataGridLength(2, DataGridLengthUnitType.Star), IsReadOnly = true });
            DataGridTextColumn statusColumn = new DataGridTextColumn { Header = "\u72b6\u614b", Binding = new Binding("Status"), Width = 170, IsReadOnly = true };
            fileGrid.Columns.Add(statusColumn);
            StackPanel bottom = new StackPanel();
            bottom.Margin = new Thickness(12, 0, 12, 12);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            DockPanel actionRow = new DockPanel();
            actionRow.LastChildFill = true;
            bottom.Children.Add(actionRow);
            StackPanel actionButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(actionButtons, Dock.Right);
            actionRow.Children.Add(actionButtons);
            undoButton = MakeButton("\u76f4\u8fd1\u3092Undo", 118);
            undoButton.Click += OnUndo;
            actionButtons.Children.Add(undoButton);
            runButton = MakeButton("\u540d\u524d\u3092\u5909\u66f4", 140);
            runButton.FontWeight = FontWeights.SemiBold;
            runButton.Background = new SolidColorBrush(Color.FromRgb(29, 93, 166));
            runButton.Foreground = Brushes.White;
            runButton.Click += OnRun;
            actionButtons.Children.Add(runButton);
            summaryText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            actionRow.Children.Add(summaryText);
            statusText = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(90, 100, 110)),
                TextWrapping = TextWrapping.Wrap
            };
            bottom.Children.Add(statusText);
            root.Children.Add(fileGrid);

            trimCheck.Checked += OnRuleChanged;
            trimCheck.Unchecked += OnRuleChanged;
            replaceCheck.Checked += OnRuleChanged;
            replaceCheck.Unchecked += OnRuleChanged;
            caseSensitiveCheck.Checked += OnRuleChanged;
            caseSensitiveCheck.Unchecked += OnRuleChanged;
            numberingCheck.Checked += OnRuleChanged;
            numberingCheck.Unchecked += OnRuleChanged;
            searchText.TextChanged += OnRuleChanged;
            replaceText.TextChanged += OnRuleChanged;
            prefixText.TextChanged += OnRuleChanged;
            suffixText.TextChanged += OnRuleChanged;
            numberStartText.TextChanged += OnRuleChanged;
            numberDigitsText.TextChanged += OnRuleChanged;
            numberSeparatorText.TextChanged += OnRuleChanged;
            historyLimitText.TextChanged += OnRuleChanged;

            foreach (RenameRow row in rows) row.IncludedChanged += OnIncludedChanged;
            currentPlan = RenamePlanner.Build(new List<RenameSource>(), CurrentRules());
            RefreshPreview(false);
            undoButton.IsEnabled = history.GetLastUndoable() != null;
            if (!String.IsNullOrEmpty(history.RecoveryWarning))
                statusText.Text = history.RecoveryWarning;
            if (!String.IsNullOrEmpty(history.RecoveryWarning))
                Loaded += delegate { MessageBox.Show(this, history.RecoveryWarning, "RenameGuard", MessageBoxButton.OK, MessageBoxImage.Warning); };
            Closing += OnClosing;
        }

        private void OnAddFiles(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "\u30d5\u30a1\u30a4\u30eb\u3092\u9078\u629e";
            dialog.Filter = "\u3059\u3079\u3066\u306e\u30d5\u30a1\u30a4\u30eb (*.*)|*.*";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
        }

        private void OnAddFolder(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "\u76f4\u4e0b\u306e\u30d5\u30a1\u30a4\u30eb\u3092\u4e00\u89a7\u306b\u8ffd\u52a0\u3057\u307e\u3059\u3002\u4e0b\u4f4d\u30d5\u30a9\u30eb\u30c0\u30fc\u306f\u691c\u7d22\u3057\u307e\u305b\u3093。";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                try
                {
                    AddPaths(Directory.GetFiles(dialog.SelectedPath, "*", SearchOption.TopDirectoryOnly));
                }
                catch (Exception ex)
                {
                    SetStatus("\u30d5\u30a9\u30eb\u30c0\u30fc\u3092\u8aad\u3081\u307e\u305b\u3093: " + ex.Message, true);
                }
            }
        }

        private void OnRemoveSelected(object sender, RoutedEventArgs e)
        {
            List<RenameRow> selected = fileGrid.SelectedItems.Cast<RenameRow>().ToList();
            foreach (RenameRow row in selected) rows.Remove(row);
            RefreshPreview(true);
        }

        private void OnClearAll(object sender, RoutedEventArgs e)
        {
            rows.Clear();
            RefreshPreview(true);
        }

        private void OnRun(object sender, RoutedEventArgs e)
        {
            RefreshPreview(false);
            int limit;
            if (!TryGetHistoryLimit(out limit))
            {
                SetStatus("\u5c65\u6b74\u4ef6\u6570\u309210\u301c200\u306e\u7bc4\u56f2\u3067\u5165\u529b\u3057\u3066\u304f\u3060\u3055\u3044\u3002", true);
                return;
            }
            if (currentPlan.ErrorCount > 0)
            {
                string details = String.Join(Environment.NewLine, currentPlan.Errors.Take(12).ToArray());
                MessageBox.Show(this,
                    "\u5168\u4ef6\u306e\u5909\u66f4\u3092\u4e2d\u6b62\u3057\u307e\u3057\u305f\u3002\u554f\u984c\u3092\u89e3\u6d88\u3057\u3066\u304b\u3089\u518d\u5ea6\u5b9f\u884c\u3057\u3066\u304f\u3060\u3055\u3044\u3002" + Environment.NewLine + Environment.NewLine + details,
                    "RenameGuard", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (currentPlan.ChangedCount == 0)
            {
                SetStatus("\u5909\u66f4\u3059\u308b\u540d\u524d\u304c\u3042\u308a\u307e\u305b\u3093\u3002", false);
                return;
            }

            string confirmation = String.Format("{0}\u4ef6\u306e\u30d5\u30a1\u30a4\u30eb\u540d\u3092\u5909\u66f4\u3057\u307e\u3059\u3002\u5b9f\u884c\u3057\u307e\u3059\u304b\uff1f", currentPlan.ChangedCount);
            if (MessageBox.Show(this, confirmation, "RenameGuard", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            List<RenamePair> pairs = currentPlan.Items.Where(delegate(RenamePlanItem item)
            {
                return item.Included && item.IsValid && item.IsChanged;
            }).Select(delegate(RenamePlanItem item)
            {
                return new RenamePair(item.SourcePath, item.TargetPath);
            }).ToList();

            try
            {
                RenameHistoryRecord record = coordinator.Rename(pairs, limit);
                Dictionary<string, string> newPaths = record.Entries.ToDictionary(delegate(HistoryEntry entry) { return entry.OriginalPath; }, delegate(HistoryEntry entry) { return entry.RenamedPath; }, StringComparer.OrdinalIgnoreCase);
                foreach (RenameRow row in rows)
                {
                    string replacement;
                    if (newPaths.TryGetValue(row.SourcePath, out replacement)) row.SourcePath = replacement;
                }
                RefreshPreview(false);
                undoButton.IsEnabled = history.GetLastUndoable() != null;
                SetStatus(String.Format("{0}\u4ef6\u3092\u5909\u66f4\u3057\u3001\u5c65\u6b74\u306b\u4fdd\u5b58\u3057\u307e\u3057\u305f\u3002", record.Entries.Count), false);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                MessageBox.Show(this, ex.Message, "RenameGuard", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnUndo(object sender, RoutedEventArgs e)
        {
            int limit;
            if (!TryGetHistoryLimit(out limit))
            {
                SetStatus("\u5c65\u6b74\u4ef6\u6570\u309210\u301c200\u306e\u7bc4\u56f2\u3067\u5165\u529b\u3057\u3066\u304f\u3060\u3055\u3044\u3002", true);
                return;
            }
            RenameHistoryRecord record = history.GetLastUndoable();
            if (record == null)
            {
                undoButton.IsEnabled = false;
                SetStatus("Undo\u3067\u304d\u308b\u5c65\u6b74\u304c\u3042\u308a\u307e\u305b\u3093\u3002", false);
                return;
            }

            if (MessageBox.Show(this, String.Format("\u6700\u8fd1\u306e\u5909\u66f4\uff08{0}\u4ef6\uff09\u3092\u5143\u306b\u623b\u3057\u307e\u3059\u304b\uff1f", record.Entries.Count),
                "RenameGuard", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                record = coordinator.UndoLatest(limit);
                Dictionary<string, string> oldPaths = record.Entries.ToDictionary(delegate(HistoryEntry entry) { return entry.RenamedPath; }, delegate(HistoryEntry entry) { return entry.OriginalPath; }, StringComparer.OrdinalIgnoreCase);
                foreach (RenameRow row in rows)
                {
                    string replacement;
                    if (oldPaths.TryGetValue(row.SourcePath, out replacement)) row.SourcePath = replacement;
                }
                RefreshPreview(false);
                undoButton.IsEnabled = history.GetLastUndoable() != null;
                SetStatus(String.Format("{0}\u4ef6\u306e\u5909\u66f4\u3092Undo\u3057\u307e\u3057\u305f\u3002", record.Entries.Count), false);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                MessageBox.Show(this, ex.Message, "RenameGuard", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnRuleChanged(object sender, RoutedEventArgs e)
        {
            RefreshPreview(true);
        }

        private void OnIncludedChanged(object sender, EventArgs e)
        {
            RefreshPreview(true);
        }

        private void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnPreviewDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) AddPaths(paths);
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            HashSet<string> existing = new HashSet<string>(rows.Select(delegate(RenameRow row) { return row.SourcePath; }), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (string path in paths ?? new string[0])
            {
                if (String.IsNullOrWhiteSpace(path)) continue;
                try
                {
                    string full = Path.GetFullPath(path);
                    if (File.Exists(full))
                    {
                        if (existing.Add(full))
                        {
                            AddRow(full);
                            added++;
                        }
                    }
                    else if (Directory.Exists(full))
                    {
                        foreach (string child in Directory.GetFiles(full, "*", SearchOption.TopDirectoryOnly))
                        {
                            string normalized = Path.GetFullPath(child);
                            if (existing.Add(normalized))
                            {
                                AddRow(normalized);
                                added++;
                            }
                        }
                    }
                    else
                    {
                        SetStatus("\u30d1\u30b9\u3092\u958b\u3051\u307e\u305b\u3093: " + full, true);
                    }
                }
                catch (Exception ex)
                {
                    SetStatus("\u30d1\u30b9\u3092\u8ffd\u52a0\u3067\u304d\u307e\u305b\u3093: " + ex.Message, true);
                }
            }
            RefreshPreview(true);
            if (added > 0) SetStatus(String.Format("{0}\u4ef6\u3092\u8ffd\u52a0\u3057\u307e\u3057\u305f\u3002\uff08\u30d5\u30a9\u30eb\u30c0\u30fc\u306f\u76f4\u4e0b\u306e\u307f\uff09", added), false);
        }

        private void AddRow(string path)
        {
            RenameRow row = new RenameRow(path);
            row.IncludedChanged += OnIncludedChanged;
            rows.Add(row);
        }

        private void RefreshPreview(bool saveSettings)
        {
            List<RenameSource> sources = rows.Select(delegate(RenameRow row)
            {
                return new RenameSource { Path = row.SourcePath, Included = row.Included };
            }).ToList();
            currentPlan = RenamePlanner.Build(sources, CurrentRules());
            for (int i = 0; i < rows.Count && i < currentPlan.Items.Count; i++)
            {
                RenameRow row = rows[i];
                RenamePlanItem item = currentPlan.Items[i];
                row.CurrentName = item.CurrentName;
                row.PreviewName = item.ProposedName;
                row.Error = item.Error;
                row.Status = !row.Included ? "\u9664\u5916" : (!item.IsValid ? "\u30a8\u30e9\u30fc: " + item.Error : (!item.IsChanged ? "\u5909\u66f4\u306a\u3057" : "\u6e96\u5099OK"));
            }
            int excluded = rows.Count - currentPlan.IncludedCount;
            summaryText.Text = String.Format("\u9078\u629e {0} \u4ef6  \u30fb  \u5909\u66f4 {1} \u4ef6  \u30fb  \u30a8\u30e9\u30fc {2} \u4ef6  \u30fb  \u9664\u5916 {3} \u4ef6",
                currentPlan.IncludedCount, currentPlan.ChangedCount, currentPlan.ErrorCount, excluded);
            runButton.IsEnabled = currentPlan.CanExecute && IsHistoryLimitValid();
            fileGrid.Items.Refresh();
            if (saveSettings) SaveSettings();
        }

        private RenameRules CurrentRules()
        {
            long start;
            int digits;
            if (!Int64.TryParse(numberStartText == null ? "1" : numberStartText.Text, out start)) start = -1;
            if (!Int32.TryParse(numberDigitsText == null ? "3" : numberDigitsText.Text, out digits)) digits = 0;
            RenameRules rules = new RenameRules();
            rules.TrimWhitespace = trimCheck != null && trimCheck.IsChecked == true;
            rules.ReplaceEnabled = replaceCheck != null && replaceCheck.IsChecked == true;
            rules.SearchText = searchText == null ? String.Empty : searchText.Text;
            rules.ReplaceText = replaceText == null ? String.Empty : replaceText.Text;
            rules.CaseSensitive = caseSensitiveCheck == null || caseSensitiveCheck.IsChecked == true;
            rules.Prefix = prefixText == null ? String.Empty : prefixText.Text;
            rules.Suffix = suffixText == null ? String.Empty : suffixText.Text;
            rules.NumberingEnabled = numberingCheck != null && numberingCheck.IsChecked == true;
            rules.NumberStart = start;
            rules.NumberDigits = digits;
            rules.NumberSeparator = numberSeparatorText == null ? "_" : numberSeparatorText.Text;
            return rules;
        }

        private void SaveSettings()
        {
            int limit;
            if (!TryGetHistoryLimit(out limit)) return;
            RenameRules rules = CurrentRules();
            RenameSettings settings = new RenameSettings();
            settings.TrimWhitespace = rules.TrimWhitespace;
            settings.ReplaceEnabled = rules.ReplaceEnabled;
            settings.SearchText = rules.SearchText;
            settings.ReplaceText = rules.ReplaceText;
            settings.CaseSensitive = rules.CaseSensitive;
            settings.Prefix = rules.Prefix;
            settings.Suffix = rules.Suffix;
            settings.NumberingEnabled = rules.NumberingEnabled;
            settings.NumberStart = rules.NumberStart < 0 ? 1 : rules.NumberStart;
            settings.NumberDigits = rules.NumberDigits < 1 || rules.NumberDigits > 12 ? 3 : rules.NumberDigits;
            settings.NumberSeparator = rules.NumberSeparator;
            settings.HistoryLimit = limit;
            try
            {
                history.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                SetStatus("\u8a2d\u5b9a\u3092\u4fdd\u5b58\u3067\u304d\u307e\u305b\u3093: " + ex.Message, true);
            }
        }

        private bool TryGetHistoryLimit(out int limit)
        {
            if (!Int32.TryParse(historyLimitText == null ? "50" : historyLimitText.Text, out limit)) return false;
            return limit >= 10 && limit <= 200;
        }

        private bool IsHistoryLimitValid()
        {
            int ignored;
            return TryGetHistoryLimit(out ignored);
        }

        private void SetStatus(string text, bool isError)
        {
            statusText.Text = text;
            statusText.Foreground = isError ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(90, 100, 110));
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            SaveSettings();
        }

        private static Button MakeButton(string text, double width)
        {
            Button button = new Button();
            button.Content = text;
            button.MinWidth = width;
            button.Height = 34;
            button.Margin = new Thickness(0, 0, 8, 0);
            button.Padding = new Thickness(10, 4, 10, 4);
            return button;
        }

        private static StackPanel MakeRuleRow()
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 3)
            };
        }

        private static TextBlock MakeLabel(string text, double width)
        {
            return new TextBlock
            {
                Text = text,
                Width = width,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
        }

        private static TextBox MakeTextBox(string text, double width)
        {
            return new TextBox
            {
                Text = text ?? String.Empty,
                Width = width,
                Height = 26,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 6, 0)
            };
        }
    }
}
