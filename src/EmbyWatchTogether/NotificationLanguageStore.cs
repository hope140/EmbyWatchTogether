using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.WatchTogether
{
    /// <summary>
    /// Persisted per-user notification language. Readers use an immutable
    /// published snapshot so notification dispatch never waits for file I/O.
    /// </summary>
    public sealed class NotificationLanguageStore
    {
        private readonly string _path;
        private readonly IJsonSerializer _serializer;
        private readonly object _writeLock = new object();
        private Dictionary<string, string> _languages = NewMap();
        private readonly bool _loadFailed;

        public NotificationLanguageStore(string path, IJsonSerializer serializer)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required", nameof(path));
            _path = path;
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            try
            {
                if (!File.Exists(_path)) return;
                var entries = _serializer.DeserializeFromString<List<NotificationLanguageEntry>>(File.ReadAllText(_path));
                if (entries == null) throw new InvalidDataException("Invalid notification language data.");
                var loaded = NewMap();
                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.UserId) || !IsStoredLanguage(entry.Language) ||
                        (entry.Script != null && (entry.Script != "Hant" || entry.Language != "zh")) || loaded.ContainsKey(entry.UserId))
                        throw new InvalidDataException("Invalid notification language entry.");
                    loaded.Add(entry.UserId, entry.Script == "Hant" ? "zh-Hant" : entry.Language);
                }
                _languages = loaded;
            }
            catch
            {
                // Do not overwrite unreadable/corrupt data; dispatch can still
                // use the historical Chinese default until the file is repaired.
                _loadFailed = true;
            }
        }

        public string GetLanguage(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return "zh";
            var snapshot = Volatile.Read(ref _languages);
            return snapshot.TryGetValue(userId, out var language) && NotificationText.IsLanguage(language) ? language : "zh";
        }

        public void SetLanguage(string userId, string language)
        {
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId is required", nameof(userId));
            if (!NotificationText.IsLanguage(language)) throw new ArgumentException("unsupported language", nameof(language));
            lock (_writeLock)
            {
                if (_loadFailed) throw new InvalidDataException("Notification language data could not be loaded.");
                var candidate = new Dictionary<string, string>(_languages, StringComparer.OrdinalIgnoreCase);
                if (candidate.TryGetValue(userId, out var existing) && existing == language) return;
                candidate[userId] = language;
                // Older versions understand zh/en only. Preserve a readable
                // zh value and store the Traditional script as optional metadata.
                var entries = candidate.Select(pair => new NotificationLanguageEntry {
                    UserId = pair.Key,
                    Language = pair.Value == "zh-Hant" ? "zh" : pair.Value,
                    Script = pair.Value == "zh-Hant" ? "Hant" : null
                }).ToList();
                string payload = _serializer.SerializeToString(entries);
                if (payload == null) throw new InvalidDataException("Notification language serialization failed.");
                string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    var directory = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(temp, payload);
                    if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak", true);
                    else File.Move(temp, _path);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
                Volatile.Write(ref _languages, candidate);
            }
        }

        private static bool IsStoredLanguage(string language) => language != "zh-Hant" && NotificationText.IsLanguage(language);
        private static Dictionary<string, string> NewMap() => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class NotificationLanguageEntry
    {
        public string UserId { get; set; }
        public string Language { get; set; }
        public string Script { get; set; }
    }
}
