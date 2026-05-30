using System;
using System.Collections.Generic;
using UnityEngine;

namespace TFS_Mimics
{
    /// <summary>
    /// Public API for other BepInEx mods to register custom audio clips with Mimics.
    /// Registered clips are played through nearby enemies exactly like built-in custom audio.
    /// <br/><br/>
    /// <b>Usage example:</b>
    /// <code>
    ///     // In your mod's Awake or Start:
    ///     MimicsAPI.RegisterClip("com.myname.mymod", "MyMod", myAudioClip);
    ///     // or register a whole bundle:
    ///     MimicsAPI.RegisterClips("com.myname.mymod", "MyMod", myClipsArray);
    /// </code>
    /// It is safe to call these methods before Mimics loads — clips are buffered and applied
    /// when the Mimics component becomes ready.
    /// </summary>
    public static class MimicsAPI
    {
        // ─── Internal pending queue ───────────────────────────────────────────────
        private sealed class PendingEntry
        {
            public string ModGuid;
            public string ModDisplayName;
            public AudioClip Clip;
        }

        private static readonly List<PendingEntry> _pending = new List<PendingEntry>();
        private static readonly object _lock = new object();

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Register a single <see cref="AudioClip"/> to be played by Mimics enemies.
        /// </summary>
        /// <param name="modGuid">
        ///   Your mod's BepInEx GUID (e.g. <c>"com.myname.mymod"</c>).
        ///   Used as a stable key; must be unique to your mod.
        /// </param>
        /// <param name="modDisplayName">
        ///   Short human-readable name displayed next to the clip in the Mimics sound list
        ///   (e.g. <c>"MyMod"</c>).
        /// </param>
        /// <param name="clip">The clip to register. Must not be <c>null</c>.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="modGuid"/> is null or whitespace.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="clip"/> is null.</exception>
        public static void RegisterClip(string modGuid, string modDisplayName, AudioClip clip)
        {
            if (string.IsNullOrWhiteSpace(modGuid))
                throw new ArgumentException("modGuid must not be empty.", nameof(modGuid));
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));

            var displayName = string.IsNullOrWhiteSpace(modDisplayName) ? modGuid : modDisplayName;

            lock (_lock)
            {
                _pending.Add(new PendingEntry
                {
                    ModGuid = modGuid,
                    ModDisplayName = displayName,
                    Clip = clip,
                });
            }

            // If a Mimics instance is already running, consume immediately.
            TFS_Mimics.Instance?.ConsumeApiClips();
        }

        /// <summary>
        /// Register multiple <see cref="AudioClip"/> objects at once.
        /// <c>null</c> clips in the collection are silently skipped.
        /// </summary>
        /// <param name="modGuid">Your mod's BepInEx GUID.</param>
        /// <param name="modDisplayName">Short human-readable name for the Mimics sound list.</param>
        /// <param name="clips">The clips to register.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="clips"/> is null.</exception>
        public static void RegisterClips(string modGuid, string modDisplayName, IEnumerable<AudioClip> clips)
        {
            if (clips == null)
                throw new ArgumentNullException(nameof(clips));

            foreach (var clip in clips)
            {
                if (clip != null)
                    RegisterClip(modGuid, modDisplayName, clip);
            }
        }

        // ─── Internal drain ───────────────────────────────────────────────────────

        /// <summary>
        /// Removes and returns all pending entries. Called by <see cref="TFS_Mimics"/>.
        /// Returns <c>null</c> when the queue is empty.
        /// </summary>
        internal static List<(string modGuid, string modDisplayName, AudioClip clip)> TakePending()
        {
            lock (_lock)
            {
                if (_pending.Count == 0) return null;

                var result = new List<(string, string, AudioClip)>(_pending.Count);
                foreach (var p in _pending)
                    result.Add((p.ModGuid, p.ModDisplayName, p.Clip));

                _pending.Clear();
                return result;
            }
        }
    }
}
