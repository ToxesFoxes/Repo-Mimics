using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace TFS_Mimics.patches
{
    [HarmonyPatch(typeof(EnemyDirector))]
    internal class EnemyDirectorStartPatch
    {
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("TFS_Mimics");

        private static HashSet<string> filterEnemies = new HashSet<string>();
        public static IReadOnlyCollection<string> KnownEnemyNames => filterEnemies;
        private static bool setupComplete;
        private static ConfigFile configFile;

        private static void DLog(string message)
        {
            if (Plugin.configDebugVerbose != null && Plugin.configDebugVerbose.Value)
            {
                Log.LogInfo(message);
            }
        }

        public static void Initialize(ConfigFile config)
        {
            configFile = config;
        }

        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        public static void SetupEnemies(EnemyDirector __instance)
        {
            if (setupComplete || configFile == null)
            {
                return;
            }

            var pools = new[]
            {
                __instance.enemiesDifficulty1,
                __instance.enemiesDifficulty2,
                __instance.enemiesDifficulty3
            };

            filterEnemies = new HashSet<string>();
            foreach (var list in pools)
            {
                if (list == null)
                {
                    continue;
                }

                foreach (var enemy in list)
                {
                    if (enemy == null || enemy.spawnObjects == null || enemy.spawnObjects.Count == 0)
                    {
                        continue;
                    }

                    var first = enemy.spawnObjects[0];
                    if (first == null)
                    {
                        continue;
                    }

                    var firstName = first.PrefabName;
                    if (string.IsNullOrWhiteSpace(firstName))
                    {
                        continue;
                    }

                    var name = firstName;
                    if (firstName.Contains("Director") && enemy.spawnObjects.Count > 1 && enemy.spawnObjects[1] != null && !string.IsNullOrWhiteSpace(enemy.spawnObjects[1].PrefabName))
                    {
                        name = enemy.spawnObjects[1].PrefabName;
                    }

                    filterEnemies.Add(name);
                }
            }

            foreach (var enemyName in filterEnemies)
            {
                Plugin.enemyConfigEntries[enemyName] = configFile.Bind(
                    "Enemies",
                    enemyName,
                    true,
                    "Enables/disables mimic for " + enemyName
                );
            }

            var orderedNames = filterEnemies
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n)
                .ToList();

            Log.LogInfo($"Registered enemy entities: count={orderedNames.Count}");
            Log.LogInfo("Registered enemy entities list: " + string.Join(", ", orderedNames));

            if (Plugin.configDebugVerbose != null && Plugin.configDebugVerbose.Value)
            {
                foreach (var enemyName in orderedNames)
                {
                    Log.LogInfo("Registered enemy entity -> " + enemyName);
                }
            }

            setupComplete = true;
            DLog("Enemy filter config initialized.");
        }
    }
}