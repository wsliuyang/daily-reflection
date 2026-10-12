using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DailyReflection
{
    public sealed class Entry
    {
        public string Date { get; set; }
        public string Summary { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        [ScriptIgnore]
        public string FriendlyDate
        {
            get { return DateTime.ParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("yyyy 年 M 月 d 日", CultureInfo.GetCultureInfo("zh-CN")); }
        }
    }

    public sealed class JournalDocument
    {
        public int Version { get; set; }
        public List<Entry> Entries { get; set; }
    }

    public sealed class JournalStore
    {
        public string DirectoryPath { get; private set; }
        public string FilePath { get { return Path.Combine(DirectoryPath, "entries.json"); } }
        public string BackupPath { get { return Path.Combine(DirectoryPath, "entries.backup.json"); } }
        public string LockPath { get { return Path.Combine(DirectoryPath, "entries.lock"); } }
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };

        public JournalStore(string directory) { DirectoryPath = Path.GetFullPath(directory); }

        public static string Normalize(string text) { return Regex.Replace(text ?? "", @"\s+", " ").Trim(); }

        public List<Entry> Load()
        {
            if (!File.Exists(FilePath))
            {
                if (File.Exists(BackupPath)) throw new InvalidDataException("主记录文件缺失，但存在备份。请先恢复 entries.backup.json。");
                return new List<Entry>();
            }
            try
            {
                JournalDocument document = serializer.Deserialize<JournalDocument>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (document == null || document.Version != 1 || document.Entries == null) throw new InvalidDataException("记录格式不正确。");
                HashSet<string> dates = new HashSet<string>();
                foreach (Entry entry in document.Entries)
                {
                    DateTime parsed;
                    if (entry == null || !DateTime.TryParseExact(entry.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                        || !dates.Add(entry.Date) || String.IsNullOrWhiteSpace(entry.Summary)) throw new InvalidDataException("记录内容不完整或存在重复日期。");
                }
                return document.Entries.OrderByDescending(e => e.Date, StringComparer.Ordinal).ToList();
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException) throw;
                throw new InvalidDataException("记录文件无法读取。原文件未被修改，请检查或恢复备份。", ex);
            }
        }

        public Entry Save(DateTime date, string summary)
        {
            summary = Normalize(summary);
            if (summary.Length == 0) throw new ArgumentException("写一点今天的小事或心情吧。");
            if (summary.Length > 600) throw new ArgumentException("总结最多可以填写 600 个字符。");
            Directory.CreateDirectory(DirectoryPath);
            // Only one writer at a time. Re-read on every save to avoid losing other records.
            using (FileStream journalLock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                List<Entry> entries = Load();
                string dateKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Entry entry = entries.FirstOrDefault(e => e.Date == dateKey);
                string now = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
                if (entry == null)
                {
                    entry = new Entry { Date = dateKey, CreatedAt = now };
                    entries.Add(entry);
                }
                entry.Summary = summary;
                entry.UpdatedAt = now;
                JournalDocument document = new JournalDocument { Version = 1, Entries = entries.OrderByDescending(e => e.Date).ToList() };
                string temporary = Path.Combine(DirectoryPath, "entries." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(serializer.Serialize(document));
                    using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        output.Write(bytes, 0, bytes.Length);
                        output.Flush(true);
                    }
                    // Atomic replace on the same volume; keep the previous valid version.
                    if (File.Exists(FilePath)) File.Replace(temporary, FilePath, BackupPath, true);
                    else File.Move(temporary, FilePath);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return entry;
            }
        }

        public string ExportMarkdown()
        {
            StringBuilder text = new StringBuilder("# 每日一记吧\r\n\r\n");
            foreach (Entry entry in Load())
            {
                text.Append("## ").Append(entry.Date).Append("\r\n\r\n");
                text.Append("> ").Append(entry.Summary.Replace("\r", "").Replace("\n", "\r\n> ")).Append("\r\n\r\n");
            }
            return text.ToString();
        }
    }
}
