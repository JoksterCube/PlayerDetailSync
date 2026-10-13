using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using BepInEx;

namespace JoksterCube.PlayerDetailSync.Api;

// Remembers which "first" kill webhooks were already sent so they survive server restarts.
internal static class WebhookHistory
{
    private const string FileName = "JoksterCube.PlayerDetailSync.webhooks.json";
    private static readonly HashSet<string> FirstBossKills = new(StringComparer.Ordinal);
    private static readonly HashSet<string> FirstMinibossKills = new(StringComparer.Ordinal);
    private static bool _loaded;

    private static string FilePath => Path.Combine(Paths.ConfigPath, FileName);

    internal static bool TryAddFirstBossKill(string playerId, string key) =>
        TryAdd(FirstBossKills, playerId + "|" + key.ToLowerInvariant());

    internal static bool TryAddFirstMinibossKill(string playerId) => TryAdd(FirstMinibossKills, playerId);

    private static bool TryAdd(HashSet<string> set, string value)
    {
        EnsureLoaded();
        if (!set.Add(value)) return false;
        Save();
        return true;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(FilePath)) return;
        try
        {
            using FileStream stream = File.OpenRead(FilePath);
            if (new DataContractJsonSerializer(typeof(Data)).ReadObject(stream) is not Data data) return;
            FirstBossKills.UnionWith(data.FirstBossKills ?? Array.Empty<string>());
            FirstMinibossKills.UnionWith(data.FirstMinibossKills ?? Array.Empty<string>());
        }
        catch (Exception exception)
        {
            Plugin.ModLogger.LogWarning($"Could not load webhook history: {exception.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            string temporaryPath = FilePath + ".tmp";
            var data = new Data
            {
                FirstBossKills = new List<string>(FirstBossKills).ToArray(),
                FirstMinibossKills = new List<string>(FirstMinibossKills).ToArray()
            };
            using (FileStream stream = File.Create(temporaryPath))
                new DataContractJsonSerializer(typeof(Data)).WriteObject(stream, data);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temporaryPath, FilePath);
        }
        catch (Exception exception)
        {
            Plugin.ModLogger.LogWarning($"Could not save webhook history: {exception.Message}");
        }
    }

    [DataContract]
    private sealed class Data
    {
        [DataMember(Name = "firstBossKills", Order = 1)]
        public string[]? FirstBossKills { get; set; }

        [DataMember(Name = "firstMinibossKills", Order = 2)]
        public string[]? FirstMinibossKills { get; set; }
    }
}
