using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using RepoSteamNetworking.API;
using RepoSteamNetworking.Networking;
using UnityEngine;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

namespace TFS_Mimics
{
    public partial class TFS_Mimics : IInRoomCallbacks
    {
        // ─── Host Authority Loop ──────────────────────────────────────────────────

        private IEnumerator HostAuthorityLoopCoroutine()
        {
            _hostAuthorityLoopRunning = true;
            DLog("HostAuthorityLoop: started");

            while (photonView != null && SemiFunc.IsMasterClientOrSingleplayer())
            {
                var minDelay = (float)(Plugin.configMinDelay?.Value ?? 10);
                var maxDelay = (float)(Plugin.configMaxDelay?.Value ?? 20);
                // Clamp so min <= max; fall back to configHostAuthorityInterval only when both are missing
                if (maxDelay < minDelay) maxDelay = minDelay;
                var delay = UnityEngine.Random.Range(minDelay, maxDelay);
                DLog($"HostAuthorityLoop: next tick in {delay:F1}s");
                _nextTickAt = Time.time + delay;
                yield return new WaitForSeconds(delay);
                _nextTickAt = -1f;

                if (!SemiFunc.RunIsLevel()) continue;

                HostAuthorityTick();
            }

            _hostAuthorityLoopRunning = false;
            DLog("HostAuthorityLoop: stopped");
        }

        private void HostAuthorityTick()
        {
            var allPlayers = PhotonNetwork.PlayerList;
            if (allPlayers == null || allPlayers.Length == 0) return;

            var allActors = new HashSet<int>(allPlayers.Select(p => p.ActorNumber));

            // --- Eligible sounds: those that ALL players have ---
            var eligibleSounds = soundReadinessMap
                .Where(kv => allActors.All(a => kv.Value.Contains(a)))
                .Select(kv => kv.Key)
                .ToList();

            if (eligibleSounds.Count == 0)
            {
                DLog($"HostAuthorityTick: no eligible sounds (map={soundReadinessMap.Count} entries, players={allActors.Count})");
                return;
            }

            // --- Build proximity maps ---
            var nearRadius = (float)(Plugin.configPlaybackNearRadius?.Value ?? 15);
            var avatars = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
            var enemies = GetEnemiesList().Where(e => e != null).ToList();

            if (enemies.Count == 0) return;

            // Build enemy data list — skip enemies currently playing audio
            var enemyData = new List<(GameObject go, int viewId, Vector3 pos)>();
            foreach (var enemy in enemies)
            {
                var viewId = GetEnemyNetViewId(enemy);
                if (viewId < 0) continue;
                var targetKey = GetPlaybackTargetKey(enemy, null);
                if (targetKey != 0 && playbackBusyUntilByTargetKey.TryGetValue(targetKey, out var busyUntil) && busyUntil > Time.time)
                    continue;
                var pos = GetEnemyDistancePosition(enemy, null);
                enemyData.Add((enemy, viewId, pos));
            }

            if (enemyData.Count == 0) return;

            // enemyNearbyActors: viewId → actors within radius
            var enemyNearbyActors = new Dictionary<int, List<int>>();
            foreach (var (_, viewId, _) in enemyData)
                enemyNearbyActors[viewId] = new List<int>();

            // playerNearbyEnemies: actorNumber → enemies within radius
            var playerNearbyEnemies = new Dictionary<int, List<int>>();

            foreach (var avatar in avatars)
            {
                if (avatar?.photonView?.Owner == null) continue;
                var actorNumber = avatar.photonView.Owner.ActorNumber;
                var playerPos = avatar.transform.position;

                foreach (var (_, viewId, pos) in enemyData)
                {
                    if (Vector3.Distance(playerPos, pos) <= nearRadius)
                    {
                        if (!playerNearbyEnemies.TryGetValue(actorNumber, out var pList))
                        {
                            pList = new List<int>();
                            playerNearbyEnemies[actorNumber] = pList;
                        }
                        pList.Add(viewId);
                        enemyNearbyActors[viewId].Add(actorNumber);
                    }
                }
            }

            if (playerNearbyEnemies.Count == 0) return;

            // --- Select sound ---
            var soundGuid = eligibleSounds[UnityEngine.Random.Range(0, eligibleSounds.Count)];

            // --- Greedy Set Cover: minimum mobs to cover all players with nearby mobs ---
            var playersToCover = new HashSet<int>(playerNearbyEnemies.Keys);
            var selectedViewIds = new List<int>();

            // Work on a mutable copy so we can remove covered enemies
            var remaining = enemyNearbyActors
                .Where(kv => kv.Value.Count > 0)
                .ToDictionary(kv => kv.Key, kv => new List<int>(kv.Value));

            while (playersToCover.Count > 0 && remaining.Count > 0)
            {
                var bestViewId = -1;
                var bestCoverage = 0;

                foreach (var (viewId, actors) in remaining)
                {
                    var coverage = actors.Count(a => playersToCover.Contains(a));
                    if (coverage > bestCoverage)
                    {
                        bestCoverage = coverage;
                        bestViewId = viewId;
                    }
                }

                if (bestViewId < 0 || bestCoverage == 0) break;

                selectedViewIds.Add(bestViewId);
                foreach (var actor in remaining[bestViewId])
                    playersToCover.Remove(actor);
                remaining.Remove(bestViewId);
            }

            if (selectedViewIds.Count == 0) return;

            var hostActor = PhotonNetwork.LocalPlayer?.ActorNumber ?? -1;
            var filterEnabled = Plugin.configPlaybackVoiceFilterEnabled == null || Plugin.configPlaybackVoiceFilterEnabled.Value;
            // Host picks filter mode once; -1 = no filter, 0/1/2 = specific effect
            var voiceFilterMode = (filterEnabled && UnityEngine.Random.value > 0.9f)
                ? UnityEngine.Random.Range(0, 3)
                : -1;
            var cmd = new SyncPlayCommandPacket
            {
                SoundGuid = soundGuid,
                EnemyViewIds = selectedViewIds.ToArray(),
                HostActorNumber = hostActor,
                VoiceFilterMode = voiceFilterMode
            };

            DLog($"HostAuthorityTick: guid={soundGuid} enemies=[{string.Join(",", selectedViewIds)}] players={playerNearbyEnemies.Count}");

            // Send to all non-host clients, then execute locally on host
            RepoSteamNetwork.SendPacket(cmd, NetworkDestination.ClientsOnly);
            HandleSyncPlayCommand(cmd);
        }

        // ─── IInRoomCallbacks ─────────────────────────────────────────────────────

        public void OnPlayerEnteredRoom(Player newPlayer) { }

        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            if (!PhotonNetwork.IsMasterClient) return;
            var actor = otherPlayer.ActorNumber;
            foreach (var set in soundReadinessMap.Values)
                set.Remove(actor);
            DLog($"OnPlayerLeftRoom: removed actor={actor} from soundReadinessMap");
        }

        public void OnMasterClientSwitched(Player newMasterClient)
        {
            if (newMasterClient.ActorNumber == PhotonNetwork.LocalPlayer?.ActorNumber && !_hostAuthorityLoopRunning)
            {
                DLog("OnMasterClientSwitched: became master client, starting host authority loop");
                StartCoroutine(HostAuthorityLoopCoroutine());
            }
        }
        public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged) { }
        public void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps) { }
    }
}
