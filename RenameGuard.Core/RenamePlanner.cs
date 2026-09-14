using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RenameGuard.Core
{
    public static class RenamePlanner
    {
        private static readonly char[] InvalidNameCharacters = new char[]
        {
            '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0'
        };

        private static readonly Regex ReservedDeviceName = new Regex(
            @"^(CON|PRN|AUX|NUL|COM[1-9\u00b9\u00b2\u00b3]|LPT[1-9\u00b9\u00b2\u00b3])(?:\..*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static RenamePlan Build(IEnumerable<RenameSource> sources, RenameRules rules)
        {
            RenamePlan plan = new RenamePlan();
            if (rules == null) rules = new RenameRules();
            List<RenameSource> sourceList = sources == null ? new List<RenameSource>() : sources.ToList();
            HashSet<string> seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            decimal number = rules.NumberStart;

            foreach (RenameSource source in sourceList)
            {
                RenamePlanItem item = new RenamePlanItem();
                item.Included = source != null && source.Included;
                item.SourcePath = source == null ? String.Empty : (source.Path ?? String.Empty);
                item.CurrentName = String.IsNullOrEmpty(item.SourcePath) ? String.Empty : Path.GetFileName(item.SourcePath);
                item.ProposedName = item.CurrentName;
                plan.Items.Add(item);

                if (!item.Included) continue;
                if (String.IsNullOrWhiteSpace(item.SourcePath))
                {
                    SetError(item, "ファイルのパスが空です。");
                    continue;
                }

                try
                {
                    item.SourcePath = Path.GetFullPath(item.SourcePath);
                    item.CurrentName = Path.GetFileName(item.SourcePath);
                    item.ProposedName = item.CurrentName;
                }
                catch (Exception ex)
                {
                    SetError(item, "パスを解釈できません: " + ex.Message);
                    continue;
                }

                if (!seenSources.Add(item.SourcePath))
                    SetError(item, "同じファイルが重複しています。");
                if (!File.Exists(item.SourcePath)) SetError(item, "ファイルが見つかりません。");
                if (Directory.Exists(item.SourcePath)) SetError(item, "フォルダーは一覧に直接追加できません。");
            }

            string ruleError = ValidateRules(rules);
            if (!String.IsNullOrEmpty(ruleError))
            {
                foreach (RenamePlanItem item in plan.Items)
                    if (item.Included) SetError(item, ruleError);
            }

            foreach (RenamePlanItem item in plan.Items)
            {
                if (!item.Included || !item.IsValid) continue;

                try
                {
                    string extension = Path.GetExtension(item.CurrentName);
                    string stem = extension.Length == 0
                        ? item.CurrentName
                        : item.CurrentName.Substring(0, item.CurrentName.Length - extension.Length);

                    if (rules.TrimWhitespace) stem = stem.Trim();
                    if (rules.ReplaceEnabled)
                    {
                        RegexOptions options = RegexOptions.CultureInvariant;
                        if (!rules.CaseSensitive) options |= RegexOptions.IgnoreCase;
                        stem = Regex.Replace(stem, Regex.Escape(rules.SearchText), delegate(Match match)
                        {
                            return rules.ReplaceText ?? String.Empty;
                        }, options);
                    }

                    stem = (rules.Prefix ?? String.Empty) + stem + (rules.Suffix ?? String.Empty);
                    if (rules.NumberingEnabled)
                    {
                        stem += (rules.NumberSeparator ?? String.Empty) + number.ToString(CultureInfo.InvariantCulture).PadLeft(rules.NumberDigits, '0');
                        number++;
                    }

                    item.ProposedName = stem + extension;
                    ValidateFileName(item.ProposedName);
                    string directory = Path.GetDirectoryName(item.SourcePath);
                    item.TargetPath = Path.GetFullPath(Path.Combine(directory, item.ProposedName));
                    if (item.TargetPath.Length > 259)
                        throw new InvalidOperationException("変更後のパスが長すぎます（上限 259 文字）。");
                    item.IsChanged = !String.Equals(item.SourcePath, item.TargetPath, StringComparison.Ordinal);
                }
                catch (Exception ex)
                {
                    SetError(item, ex.Message);
                }
            }

            HashSet<string> movingSources = new HashSet<string>(plan.Items.Where(delegate(RenamePlanItem item)
            {
                return item.Included && item.IsValid && item.IsChanged;
            }).Select(delegate(RenamePlanItem item) { return item.SourcePath; }), StringComparer.OrdinalIgnoreCase);
            MarkTargetConflicts(plan, movingSources);
            foreach (RenamePlanItem item in plan.Items)
                if (item.Included && !item.IsValid) plan.Errors.Add(item.CurrentName + ": " + item.Error);
            return plan;
        }

        public static RenameValidation ValidatePairs(IEnumerable<RenamePair> pairs)
        {
            RenameValidation result = new RenameValidation();
            List<RenamePair> list = pairs == null ? new List<RenamePair>() : pairs.ToList();
            HashSet<string> sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<RenamePair> normalized = new List<RenamePair>();

            foreach (RenamePair pair in list)
            {
                if (pair == null || String.IsNullOrWhiteSpace(pair.SourcePath) || String.IsNullOrWhiteSpace(pair.TargetPath))
                {
                    result.Errors.Add("ファイルパスが不正な件数があります。");
                    continue;
                }

                string source;
                string target;
                try
                {
                    source = Path.GetFullPath(pair.SourcePath);
                    target = Path.GetFullPath(pair.TargetPath);
                }
                catch (Exception ex)
                {
                    result.Errors.Add("パスを解釈できません: " + ex.Message);
                    continue;
                }

                if (!sources.Add(source)) result.Errors.Add("元ファイルが重複しています: " + source);
                if (!targets.Add(target)) result.Errors.Add("変更先が重複しています: " + target);
                if (!File.Exists(source)) result.Errors.Add("元ファイルが見つかりません: " + source);
                if (!String.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase))
                    result.Errors.Add("同じフォルダー内での名前変更のみ対応しています: " + target);

                try
                {
                    ValidateFileName(Path.GetFileName(target));
                    if (target.Length > 259) result.Errors.Add("変更先のパスが長すぎます: " + target);
                }
                catch (Exception ex)
                {
                    result.Errors.Add("変更先の名前が不正です: " + target + " (" + ex.Message + ")");
                }

                normalized.Add(new RenamePair(source, target));
            }

            foreach (RenamePair pair in normalized)
            {
                if ((File.Exists(pair.TargetPath) || Directory.Exists(pair.TargetPath)) && !sources.Contains(pair.TargetPath))
                    result.Errors.Add("変更先に別のファイルまたはフォルダーがあります: " + pair.TargetPath);
            }
            return result;
        }

        public static void ValidateFileName(string name)
        {
            if (String.IsNullOrEmpty(name) || name == "." || name == "..")
                throw new InvalidOperationException("変更後の名前が空です。");
            if (name.IndexOfAny(InvalidNameCharacters) >= 0)
                throw new InvalidOperationException("変更後の名前にWindowsで使えない文字があります。");
            if (name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
                throw new InvalidOperationException("名前の最後にピリオドや空白を使えません。");
            if (ReservedDeviceName.IsMatch(name))
                throw new InvalidOperationException("予約済みのデバイス名です。");
        }

        private static string ValidateRules(RenameRules rules)
        {
            if (rules.ReplaceEnabled && String.IsNullOrEmpty(rules.SearchText))
                return "置換を使う場合は、検索文字を入力してください。";
            if (rules.NumberingEnabled && rules.NumberStart < 0)
                return "連番の開始値は0以上にしてください。";
            if (rules.NumberingEnabled && (rules.NumberDigits < 1 || rules.NumberDigits > 12))
                return "連番の桁数は1〜12にしてください。";
            return String.Empty;
        }

        private static void MarkTargetConflicts(RenamePlan plan, HashSet<string> movingSources)
        {
            Dictionary<string, List<RenamePlanItem>> groups = new Dictionary<string, List<RenamePlanItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (RenamePlanItem item in plan.Items)
            {
                if (!item.Included || !item.IsValid || !item.IsChanged || String.IsNullOrEmpty(item.TargetPath)) continue;
                List<RenamePlanItem> group;
                if (!groups.TryGetValue(item.TargetPath, out group))
                {
                    group = new List<RenamePlanItem>();
                    groups.Add(item.TargetPath, group);
                }
                group.Add(item);
            }

            foreach (KeyValuePair<string, List<RenamePlanItem>> pair in groups)
            {
                if (pair.Value.Count > 1)
                {
                    foreach (RenamePlanItem item in pair.Value)
                        SetError(item, "複数のファイルが同じ変更先になっています。");
                    continue;
                }

                RenamePlanItem single = pair.Value[0];
                if ((File.Exists(single.TargetPath) || Directory.Exists(single.TargetPath)) && !movingSources.Contains(single.TargetPath))
                    SetError(single, "変更先に別のファイルまたはフォルダーがあります。");
            }
        }

        private static void SetError(RenamePlanItem item, string message)
        {
            item.IsValid = false;
            if (String.IsNullOrEmpty(item.Error)) item.Error = message;
        }
    }
}
