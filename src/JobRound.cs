using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GlyphFinder
{
    internal enum TargetKind { None, Ip, Log, File }

    internal enum JobAccess { Unknown, Acquisition, Grant }

    internal sealed class JobType
    {
        public readonly string Name, Chinese, English;
        public readonly TargetKind Targets;
        public readonly bool Removes;

        public JobType(string name, TargetKind targets, bool removes, string chinese, string english)
        {
            Name = name;
            Targets = targets;
            Removes = removes;
            Chinese = chinese;
            English = english;
        }

        public bool NeedsServer { get { return Targets != TargetKind.None; } }
    }

    internal static class JobCatalog
    {
        // Paraphrased from the ACT 3 - Jobs guide. Names match the market listing for lookup.
        public static readonly JobType[] Types =
        {
            new JobType("File Decryption", TargetKind.None, false,
                "接单后打开 Downloads 里的加密文件，完成小游戏，再回市场交单。文件会自动删除。适用 ICE WALL BREAK 定位器。",
                "Open the encrypted file in Downloads and solve the minigame with ICE WALL BREAK Finder, then turn in at the market."),
            new JobType("IP Cleanup", TargetKind.Ip, true,
                "连接指定服务器，在 Transit Access 删除列出的 IP。",
                "Transit Access · Delete the listed IPs."),
            new JobType("IP (adding)", TargetKind.Ip, false,
                "连接指定服务器，在 Transit Access 添加所需 IP。市场的任务详情里每个 IP 都有复制按钮。",
                "Transit Access · Add the listed IPs. The market job details have a copy button for each IP."),
            new JobType("Log Deletion", TargetKind.Log, true,
                "连接指定服务器，在 Log 删除列出的日志。可能有重名，真正的那条通常大小与众不同（最小）。多条时动作要快。",
                "Log · Delete the listed log. Names can repeat; the real one usually has the odd (smallest) size. Be quick with several."),
            new JobType("Log Download", TargetKind.Log, false,
                "连接指定服务器，在 Log 找到并下载列出的日志。",
                "Log · Download the listed log."),
            new JobType("File Elimination", TargetKind.File, true,
                "连接指定服务器，在 Files 删除列出的文件。",
                "Files · Delete the listed file."),
            new JobType("File Download", TargetKind.File, false,
                "连接指定服务器，在 Files 下载列出的文件。带锁图标的其实是 Download And Decrypt。",
                "Files · Download the listed file. A padlock icon means it is a Download And Decrypt job."),
            new JobType("File Upload", TargetKind.File, false,
                "连接指定服务器，在 Files 点上传，选择接单时放进 Downloads 的文件。",
                "Files · Upload the file that was added to Downloads when you accepted the job."),
            new JobType("Download And Decrypt", TargetKind.File, false,
                "先在指定服务器的 Files 下载文件，再到 Downloads 打开并完成解密小游戏。",
                "Files → Downloads · Download the file, open it in Downloads, and solve the minigame.")
        };

        public static readonly string[] AccessNames = { "—", "Acquisition", "Grant" };

        public static JobType Find(string name)
        {
            return Types.FirstOrDefault(t => t.Name == name);
        }
    }

    internal sealed class JobEntry
    {
        public JobType Type;
        public string Server = "";
        public JobAccess Access;
        public List<string> Targets = new List<string>();
        public string Note = "";
        public bool Done;

        public string ServerKey { get { return Type.NeedsServer ? Server.Trim().ToUpperInvariant() : ""; } }
    }

    // One market round, kept in memory only. The market refreshes every few hours, so nothing is written to disk.
    internal sealed class JobRound
    {
        private static readonly Regex Ipv4 = new Regex(
            @"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?!\.?\d)");
        private static readonly Regex FileName = new Regex(
            @"(?<![\w.-])[\w-]+(?:\.[\w-]+)+(?![\w-])");
        private static readonly Regex OcrIpv4 = new Regex(
            @"(?<![\w.])(?:[0-9OoØøIl]{1,3}\.){3}[0-9OoØøIl]{1,3}(?!\w|\.\w)");
        private static readonly char[] Separators = { '\r', '\n', ',', ';', '，', '；', '、', '\t' };

        public readonly List<JobEntry> Entries = new List<JobEntry>();
        public DateTime? Started;
        public event EventHandler Changed;

        public int DoneCount { get { return Entries.Count(e => e.Done); } }

        public void Add(JobEntry entry)
        {
            if (Started == null) Started = DateTime.Now;
            Entries.Add(entry);
            OnChanged();
        }

        public void Remove(JobEntry entry)
        {
            if (Entries.Remove(entry)) OnChanged();
        }

        public void Clear()
        {
            Entries.Clear();
            Started = null;
            OnChanged();
        }

        public void OnChanged()
        {
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // Jobs on the same server are listed together so each server needs one visit. Downloads-only jobs go last.
        public List<JobEntry> RouteOrder()
        {
            var groups = new List<string>();
            foreach (JobEntry entry in Entries)
                if (!groups.Contains(entry.ServerKey)) groups.Add(entry.ServerKey);
            if (groups.Remove("")) groups.Add("");
            return groups.SelectMany(key => Entries.Where(e => e.ServerKey == key)).ToList();
        }

        public IEnumerable<string> Servers()
        {
            return Entries.Where(e => e.Type.NeedsServer && e.Server.Trim().Length > 0)
                .Select(e => e.Server.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public JobAccess AccessFor(string server)
        {
            string key = server.Trim().ToUpperInvariant();
            var known = Entries.Where(e => key.Length > 0 && e.ServerKey == key && e.Access != JobAccess.Unknown)
                .Select(e => e.Access).Distinct().ToArray();
            return known.Length == 1 ? known[0] : JobAccess.Unknown;
        }

        public static List<string> ParseTargets(string text, TargetKind kind)
        {
            text = text ?? "";
            if (kind == TargetKind.Ip)
            {
                List<string> found = Ipv4.Matches(text).Cast<Match>().Select(m => m.Value).Distinct().ToList();
                if (found.Count > 0) return found;
            }
            return text.Split(Separators).Select(s => s.Trim().TrimStart('·', '-', '*', '•').Trim())
                .Where(s => s.Length > 0).Distinct().ToList();
        }

        public static List<string> ExtractTargets(IEnumerable<string> lines, TargetKind kind)
        {
            // Each OCR line in the selected log target area is a title, which can contain spaces and no extension.
            if (kind == TargetKind.Log)
                return lines.Select(line => line.Trim()).Where(title => title.Any(char.IsLetterOrDigit))
                    .Where(title => title != "信息" && !string.Equals(title, "Info", StringComparison.OrdinalIgnoreCase))
                    .Distinct().ToList();

            return lines.SelectMany(line =>
            {
                // OCR can leave a space around the dot in an IP address or extension.
                string text = Regex.Replace(line, @"(?<=\w)\s*[.．。]\s*(?=\w)", ".");
                if (kind == TargetKind.Ip)
                    return OcrIpv4.Matches(text).Cast<Match>()
                        .Select(match => match.Value.Replace('O', '0').Replace('o', '0').Replace('Ø', '0').Replace('ø', '0')
                            .Replace('I', '1').Replace('l', '1'))
                        .Where(address => Ipv4.Match(address).Length == address.Length);
                return FileName.Matches(text).Cast<Match>().Select(match => match.Value)
                    .Where(name => name.Any(char.IsLetter) && !OcrIpv4.IsMatch(name));
            }).Distinct().ToList();
        }

        public static JobType IdentifyCardType(IEnumerable<string> heading)
        {
            var types = heading.Select(ReadCardType).Where(type => type != null).Distinct().ToList();
            return types.Count == 1 ? types[0] : null;
        }

        public static JobEntry ReadCard(JobType selected, IEnumerable<string> info, IEnumerable<string> description)
        {
            List<string> targets = ExtractTargets(description, selected.Targets);
            if (targets.Count == 0) return null;
            if (!selected.NeedsServer) return new JobEntry { Type = selected, Targets = targets };

            string[] values = info.ToArray();
            var servers = values.Select(ReadCardServer).Where(server => server != null).Distinct().ToList();
            var access = values.SelectMany(line => Regex.Matches(line, @"\b(?:Access\s*)?(Acquisition|Grant)\b",
                    RegexOptions.IgnoreCase).Cast<Match>())
                .Select(match => (JobAccess)Enum.Parse(typeof(JobAccess), match.Groups[1].Value, true))
                .Distinct().ToList();
            if (servers.Count != 1 || access.Count != 1) return null;
            return new JobEntry { Type = selected, Server = servers[0], Access = access[0], Targets = targets };
        }

        private static JobType ReadCardType(string line)
        {
            string key = Regex.Replace(line.ToUpperInvariant(), @"[^A-Z0-9]", "");
            switch (key)
            {
                case "IPINJECTION": return JobCatalog.Find("IP (adding)");
                case "DATADOWNLOAD": return JobCatalog.Find("File Download");
                case "DATAUPLOAD": return JobCatalog.Find("File Upload");
                case "DECRYPTEXTRACT":
                case "DECRYPT8EXTRACT": // Windows OCR can read the ampersand as an 8.
                    return JobCatalog.Find("Download And Decrypt");
                default: return JobCatalog.Types.FirstOrDefault(type =>
                    Regex.Replace(type.Name.ToUpperInvariant(), @"[^A-Z0-9]", "") == key);
            }
        }

        private static string ReadCardServer(string line)
        {
            string server = Regex.Replace(line.Trim(), @"^(?:servers?\b|服务器)\s*[:：]?\s*", "", RegexOptions.IgnoreCase);
            server = Regex.Replace(server.ToUpperInvariant(), @"\s*[-–—]\s*", "-");
            if (server.Length > 40 || !Regex.IsMatch(server, @"^[A-Z0-9]+(?:[ -][A-Z0-9]+)*$") ||
                !server.Any(char.IsDigit) || !server.Any(char.IsLetter) || Regex.IsMatch(server, @"^CR\s*[0-9]+$")) return null;
            // In a directional sector ID, the positions around L are numeric: EIL5 is E1L5.
            return Regex.Replace(server, @"(?<=-[NESW])[0-9IO]{1,2}L[0-9IO]{1,2}(?=$|[- ])",
                match => match.Value.Replace('I', '1').Replace('O', '0'));
        }

        public bool HasDuplicate(JobEntry candidate)
        {
            return Entries.Any(entry => entry.Type == candidate.Type && entry.ServerKey == candidate.ServerKey &&
                entry.Access == candidate.Access && entry.Note.Trim() == candidate.Note.Trim() &&
                new HashSet<string>(entry.Targets, StringComparer.Ordinal).SetEquals(candidate.Targets));
        }

        public static string GroupName(JobEntry entry, UiLanguage language)
        {
            return entry.Type.NeedsServer
                ? (entry.Server.Trim().Length > 0 ? entry.Server.Trim() : language.Text("（未填服务器）", "(no server)"))
                : "Downloads";
        }

        public string Summary(UiLanguage language)
        {
            if (Entries.Count == 0) return "";
            var text = new StringBuilder();
            text.AppendFormat(language.Text("本轮 {0} · 已完成 {1}/{2}", "Round {0} · {1}/{2} done"),
                Started.HasValue ? Started.Value.ToString("HH:mm") : "--:--", DoneCount, Entries.Count);
            string group = null;
            foreach (JobEntry entry in RouteOrder())
            {
                if (entry.ServerKey != group || group == null)
                {
                    group = entry.ServerKey;
                    text.AppendLine().AppendLine();
                    text.Append(GroupName(entry, language));
                }
                text.AppendLine();
                text.Append(entry.Done ? "  [x] " : "  [ ] ").Append(entry.Type.Name);
                if (entry.Access != JobAccess.Unknown) text.Append(" [").Append(entry.Access).Append(']');
                if (entry.Targets.Count > 0) text.Append(": ").Append(string.Join(", ", entry.Targets));
                if (entry.Note.Trim().Length > 0) text.Append(" — ").Append(entry.Note.Trim());
            }
            return text.ToString();
        }
    }
}
