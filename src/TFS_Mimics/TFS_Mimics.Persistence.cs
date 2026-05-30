using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace TFS_Mimics
{
    public partial class TFS_Mimics
    {
        private void EnsurePersistenceInitialized()
        {
            if (persistenceInitialized)
            {
                return;
            }

            persistenceInitialized = true;
            if (Plugin.configPersistAudioCache == null || !Plugin.configPersistAudioCache.Value)
            {
                return;
            }

            LoadPersistedAudioEntriesFromDisk();
        }

        private string GetAudioCacheDirectoryPath()
        {
            return Path.Combine(Paths.BepInExRootPath, "plugins", "ToxesFoxes-Mimics", "audio-cache");
        }

        private void LoadPersistedAudioEntriesFromDisk()
        {
            var dir = GetAudioCacheDirectoryPath();
            if (!Directory.Exists(dir))
            {
                return;
            }

            LoadPlayersIndexFromDisk();

            var files = Directory.GetFiles(dir, "audio_*_*.bin", SearchOption.AllDirectories);
            var loaded = 0;
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file) || loadedPersistedFiles.Contains(file))
                {
                    continue;
                }

                if (!TryReadAudioEntryFromDisk(file, out var entry) || entry == null)
                {
                    continue;
                }

                cachedAudio.Add(entry);
                RegisterPlayerInIndex(entry.SourcePlayerId, entry.SourceName);
                loadedPersistedFiles.Add(file);
                loaded++;
            }

            SavePlayersIndexToDisk();
            DLog($"Persistence load complete: loaded={loaded} totalCached={cachedAudio.Count} path={dir} {DebugContext()}");
        }

        private void SaveAudioEntryToDisk(CachedAudioEntry entry)
        {
            try
            {
                if (entry == null || entry.AudioData == null || entry.AudioData.Length == 0)
                {
                    return;
                }

                var dir = GetAudioCacheDirectoryPath();
                Directory.CreateDirectory(dir);

                var actorId = entry.SourceActor;
                var playerId = !string.IsNullOrWhiteSpace(entry.SourcePlayerId) ? entry.SourcePlayerId : $"actor_{actorId}";
                var safePlayerId = SanitizePlayerIdForFileName(playerId);
                var playerDir = Path.Combine(dir, safePlayerId);
                Directory.CreateDirectory(playerDir);
                var fileName = $"audio_{safePlayerId}_{Guid.NewGuid():N}.bin";
                var path = Path.Combine(playerDir, fileName);

                using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new BinaryWriter(fs))
                {
                    writer.Write("MIMC2");
                    writer.Write(actorId);
                    writer.Write(playerId);
                    writer.Write(entry.SampleRate);
                    writer.Write(entry.ApplyVoiceFilter);
                    writer.Write(entry.SourceName ?? string.Empty);
                    writer.Write(entry.AudioData.Length);
                    writer.Write(entry.AudioData);
                }

                RegisterPlayerInIndex(playerId, entry.SourceName);
                SavePlayersIndexToDisk();
                EnforcePlayerAudioFileLimit(playerDir);
                loadedPersistedFiles.Add(path);
                DLog($"Persistence save: file={fileName} playerId={playerId} actor={actorId} bytes={entry.AudioData.Length} {DebugContext()}");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Persistence save failed: {ex.Message} {DebugContext()}");
            }
        }

        private bool TryReadAudioEntryFromDisk(string path, out CachedAudioEntry entry)
        {
            entry = null;

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new BinaryReader(fs))
                {
                    var signature = reader.ReadString();
                    if (!string.Equals(signature, "MIMC1", StringComparison.Ordinal) && !string.Equals(signature, "MIMC2", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var actorId = reader.ReadInt32();
                    var playerId = string.Empty;
                    if (string.Equals(signature, "MIMC2", StringComparison.Ordinal))
                    {
                        playerId = reader.ReadString();
                    }

                    var sr = reader.ReadInt32();
                    var apply = reader.ReadBoolean();
                    var sourceName = reader.ReadString();
                    var len = reader.ReadInt32();
                    if (len <= 0 || len > 50 * 1024 * 1024)
                    {
                        return false;
                    }

                    var data = reader.ReadBytes(len);
                    if (data.Length != len)
                    {
                        return false;
                    }

                    if (actorId < 0 && TryParsePlayerIdFromAudioFileName(path, out var filePlayerId))
                    {
                        playerId = filePlayerId;
                    }

                    if (string.IsNullOrWhiteSpace(playerId))
                    {
                        playerId = actorId >= 0 ? $"actor_{actorId}" : "unknown";
                    }

                    entry = new CachedAudioEntry
                    {
                        AudioData = data,
                        ApplyVoiceFilter = apply,
                        SampleRate = sr,
                        SourceActor = actorId,
                        SourcePlayerId = playerId,
                        SourceName = sourceName,
                        ReceivedAt = Time.time
                    };

                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Persistence read failed file={Path.GetFileName(path)} error={ex.Message} {DebugContext()}");
                return false;
            }
        }

        private static string SanitizePlayerIdForFileName(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return "unknown";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(playerId.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
        }

        private static bool TryParsePlayerIdFromAudioFileName(string filePath, out string playerId)
        {
            playerId = string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            const string prefix = "audio_";
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var body = fileName.Substring(prefix.Length);
            var lastSeparator = body.LastIndexOf('_');
            if (lastSeparator <= 0)
            {
                return false;
            }

            playerId = body.Substring(0, lastSeparator);
            return !string.IsNullOrWhiteSpace(playerId);
        }

        private string GetPlayersIndexPath()
        {
            return Path.Combine(GetAudioCacheDirectoryPath(), "players.json");
        }

        private void RegisterPlayerInIndex(string playerId, string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return;
            }

            var normalizedName = string.IsNullOrWhiteSpace(playerName) ? "unknown" : playerName;
            if (!playerNameById.TryGetValue(playerId, out var existingName) || string.IsNullOrWhiteSpace(existingName) || string.Equals(existingName, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                playerNameById[playerId] = normalizedName;
            }
        }

        private void LoadPlayersIndexFromDisk()
        {
            // Index is rebuilt from persisted audio headers on load.
        }

        private void SavePlayersIndexToDisk()
        {
            try
            {
                var dir = GetAudioCacheDirectoryPath();
                Directory.CreateDirectory(dir);

                var ordered = playerNameById.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"players\": [");
                for (var i = 0; i < ordered.Count; i++)
                {
                    var kv = ordered[i];
                    var id = EscapeJsonString(kv.Key);
                    var name = EscapeJsonString(string.IsNullOrWhiteSpace(kv.Value) ? "unknown" : kv.Value);
                    var suffix = i < ordered.Count - 1 ? "," : string.Empty;
                    sb.AppendLine($"    {{ \"id\": \"{id}\", \"name\": \"{name}\" }}{suffix}");
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");

                File.WriteAllText(GetPlayersIndexPath(), sb.ToString());
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to save players index JSON: {ex.Message} {DebugContext()}");
            }
        }

        private static string EscapeJsonString(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private void EnforcePlayerAudioFileLimit(string playerDir)
        {
            if (string.IsNullOrWhiteSpace(playerDir) || !Directory.Exists(playerDir))
            {
                return;
            }

            var limit = Plugin.configPersistMaxFilesPerPlayer != null ? Plugin.configPersistMaxFilesPerPlayer.Value : 200;
            if (limit < 1)
            {
                return;
            }

            try
            {
                var files = Directory.GetFiles(playerDir, "audio_*_*.bin", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => File.GetCreationTimeUtc(path))
                    .ToList();

                var removeCount = files.Count - limit;
                if (removeCount <= 0)
                {
                    return;
                }

                for (var i = 0; i < removeCount; i++)
                {
                    var file = files[i];
                    File.Delete(file);
                    loadedPersistedFiles.Remove(file);
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to enforce per-player audio file limit: {ex.Message} dir={playerDir} {DebugContext()}");
            }
        }

        private static string GetPlayerPersistentId(Player player)
        {
            if (player == null)
            {
                return "unknown";
            }

            // 1. Try steamID field on PlayerAvatar — this is how the game stores it.
            //    The field is populated via AddToStatsManagerRPC on the local PlayerAvatar.
            var steamId = TryGetSteamIdFromPlayerAvatar(player);
            if (!string.IsNullOrWhiteSpace(steamId) && steamId != "0")
            {
                return steamId;
            }

            // 2. Photon UserId (set by some platforms)
            if (!string.IsNullOrWhiteSpace(player.UserId))
            {
                return player.UserId;
            }

            // 3. Custom properties fallback
            if (player.CustomProperties != null)
            {
                var keys = new[] { "steamid", "steam_id", "SteamId", "SteamID", "playerId", "PlayerId" };
                foreach (var key in keys)
                {
                    if (player.CustomProperties.TryGetValue(key, out var value) && value != null)
                    {
                        var text = value.ToString();
                        if (!string.IsNullOrWhiteSpace(text) && text != "0")
                        {
                            return text;
                        }
                    }
                }
            }

            return $"actor_{player.ActorNumber}";
        }

        private static FieldInfo _steamIdField;

        /// <summary>
        /// Reads PlayerAvatar.steamID for the given Photon player by finding the
        /// PlayerAvatar whose PhotonView.Owner matches.
        /// </summary>
        private static string TryGetSteamIdFromPlayerAvatar(Player player)
        {
            if (player == null) return null;

            var avatars = UnityEngine.Object.FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
            foreach (var avatar in avatars)
            {
                if (avatar == null) continue;

                var pv = avatar.GetComponent<PhotonView>();
                if (pv == null || pv.Owner == null) continue;
                if (pv.Owner.ActorNumber != player.ActorNumber) continue;

                // Cache the field info on first use
                if (_steamIdField == null)
                {
                    _steamIdField = typeof(PlayerAvatar).GetField("steamID",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                if (_steamIdField == null) return null;

                var id = _steamIdField.GetValue(avatar) as string;
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }

            return null;
        }

        private static string GetPlayerDisplayName(Player player)
        {
            if (player == null)
            {
                return "unknown";
            }

            if (!string.IsNullOrWhiteSpace(player.NickName))
            {
                return player.NickName;
            }

            return $"actor_{player.ActorNumber}";
        }

        private HashSet<string> GetOnlinePlayerIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (PhotonNetwork.PlayerList != null)
            {
                foreach (var player in PhotonNetwork.PlayerList)
                {
                    if (player == null)
                    {
                        continue;
                    }

                    ids.Add(GetPlayerPersistentId(player));
                }
            }

            return ids;
        }

        private void LogPlayerRecordingSummaryForWorldEntry()
        {
            var online = GetOnlinePlayerIds().OrderBy(x => x).ToList();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in cachedAudio)
            {
                if (entry == null)
                {
                    continue;
                }

                var id = !string.IsNullOrWhiteSpace(entry.SourcePlayerId) ? entry.SourcePlayerId : $"actor_{entry.SourceActor}";
                counts[id] = counts.TryGetValue(id, out var current)
                    ? current + 1
                    : 1;
            }

            var summary = counts.Count == 0
                ? "none"
                : string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));

            Log.LogInfo($"World entry player IDs online=[{string.Join(",", online)}] recordingsByPlayer=[{summary}] {DebugContext()}");
        }
    }
}