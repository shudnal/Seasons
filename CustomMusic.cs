
using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using static Seasons.Seasons;

namespace Seasons
{
    internal static class CustomMusic
    {
        internal class MusicSettings
        {
            public float m_volume = 1f;

            public float m_fadeInTime = 3f;

            public bool m_alwaysFadeout = false;

            public bool m_loop = true;

            public bool m_resume = true;

            public bool m_enabled = true;

            public bool m_ambientMusic = true;
        }

        public const string subdirectory = "Custom music";
        public static readonly Dictionary<string, AudioClip> audioClips         = new Dictionary<string, AudioClip>();
        public static readonly Dictionary<string, MusicSettings> clipSettings   = new Dictionary<string, MusicSettings>();

        private sealed class MusicRegistration
        {
            public MusicMan.NamedMusic original;
            public MusicMan.NamedMusic applied;
            public MusicMan.NamedMusic originalHashEntry;
            public AudioClip clip;
            public MusicSettings settings;
        }

        private static MusicMan registeredManager;
        private static readonly Dictionary<string, MusicRegistration> registrations = new Dictionary<string, MusicRegistration>();
        private static FileSystemWatcher watcher;

        internal static void SetupConfigWatcher()
        {
            if (watcher == null)
            {
                watcher = new FileSystemWatcher(GetSubdirectory(), "*.*");
                watcher.Changed += UpdateClipOnChange;
                watcher.Created += UpdateClipOnChange;
                watcher.Renamed += UpdateClipOnChange;
                watcher.Deleted += UpdateClipOnChange;
                watcher.IncludeSubdirectories = true;
                watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
                watcher.EnableRaisingEvents = true;
                UpdateCustomMusic();
            }

            CheckMusicList();
            SeasonEnvironment.ClearCachedObjects();
        }

        internal static void CheckMusicList()
        {
            MusicMan manager = MusicMan.instance;
            if (!manager)
                return;

            if (registeredManager != manager)
            {
                registrations.Clear();
                registeredManager = manager;
            }

            HashSet<string> restartMusic = new HashSet<string>();
            foreach (KeyValuePair<string, MusicRegistration> pair in registrations.ToList())
            {
                MusicRegistration registration = pair.Value;
                audioClips.TryGetValue(pair.Key, out AudioClip currentClip);
                clipSettings.TryGetValue(pair.Key, out MusicSettings currentSettings);
                if ((currentClip != null || currentSettings != null) && ReferenceEquals(currentClip, registration.clip)
                    && ReferenceEquals(currentSettings, registration.settings) && manager.m_music.Contains(registration.applied))
                    continue;

                if (ReferenceEquals(manager.m_currentMusic, registration.applied) || ReferenceEquals(manager.m_queuedMusic, registration.applied))
                {
                    manager.StopMusic();
                    restartMusic.Add(pair.Key);
                }
                if (MusicMan_GetEnvironmentMusic_FrozenOceanNightMusic.WasReleased(registration.original))
                    registration.original = null;
                if (MusicMan_GetEnvironmentMusic_FrozenOceanNightMusic.WasReleased(registration.originalHashEntry))
                    registration.originalHashEntry = null;
                int index = manager.m_music.IndexOf(registration.applied);
                if (index >= 0)
                {
                    if (registration.original != null)
                        manager.m_music[index] = registration.original;
                    else
                        manager.m_music.RemoveAt(index);
                }

                int hash = pair.Key.GetStableHashCode();
                if (manager.m_musicHashes.TryGetValue(hash, out MusicMan.NamedMusic indexed) && ReferenceEquals(indexed, registration.applied))
                {
                    manager.m_musicHashes.Remove(hash);
                    if (registration.originalHashEntry != null && index >= 0)
                        manager.m_musicHashes[hash] = registration.originalHashEntry;
                }
                else if (!manager.m_musicHashes.ContainsKey(hash) && registration.originalHashEntry != null && index >= 0)
                {
                    manager.m_musicHashes[hash] = registration.originalHashEntry;
                }

                registrations.Remove(pair.Key);
            }

            foreach (string name in audioClips.Keys.Union(clipSettings.Keys))
            {
                if (registrations.TryGetValue(name, out MusicRegistration existing))
                {
                    int existingHash = name.GetStableHashCode();
                    if (!manager.m_musicHashes.ContainsKey(existingHash) && existing.applied.m_enabled
                        && existing.applied.m_clips != null && existing.applied.m_clips.Length > 0 && existing.applied.m_clips[0] != null)
                        manager.m_musicHashes[existingHash] = existing.applied;
                    continue;
                }

                MusicMan.NamedMusic original = manager.m_music.Find(music => music.m_name == name);
                if (original == null && !audioClips.ContainsKey(name))
                    continue;

                MusicMan.NamedMusic music = new MusicMan.NamedMusic { m_name = name };
                if (original != null)
                {
                    // Copy current fields, including native playback metadata, without changing another owner's entry.
                    foreach (System.Reflection.FieldInfo field in typeof(MusicMan.NamedMusic).GetFields())
                        field.SetValue(music, field.GetValue(original));
                }
                else
                {
                    ApplySettings(music, new MusicSettings());
                }

                if (audioClips.TryGetValue(name, out AudioClip clip))
                    music.m_clips = new[] { clip };
                if (clipSettings.TryGetValue(name, out MusicSettings settings))
                    ApplySettings(music, settings);

                int hash = name.GetStableHashCode();
                manager.m_musicHashes.TryGetValue(hash, out MusicMan.NamedMusic originalHashEntry);
                registrations[name] = new MusicRegistration { original = original, applied = music, originalHashEntry = originalHashEntry, clip = clip, settings = settings };
                if (original == null)
                    manager.m_music.Add(music);
                else
                    manager.m_music[manager.m_music.IndexOf(original)] = music;

                if (music.m_enabled && music.m_clips != null && music.m_clips.Length > 0 && music.m_clips[0] != null)
                    manager.m_musicHashes[hash] = music;
                else if (ReferenceEquals(originalHashEntry, original))
                    manager.m_musicHashes.Remove(hash);
            }

            foreach (string name in restartMusic)
                if (manager.FindMusic(name) != null)
                    manager.StartMusic(name);
        }

        private static void ApplySettings(MusicMan.NamedMusic music, MusicSettings settings)
        {
            music.m_ambientMusic = settings.m_ambientMusic;
            music.m_resume = settings.m_resume;
            music.m_alwaysFadeout = settings.m_alwaysFadeout;
            music.m_enabled = settings.m_enabled;
            music.m_fadeInTime = settings.m_fadeInTime;
            music.m_loop = settings.m_loop;
            music.m_volume = settings.m_volume;
        }

        private static string GetSubdirectory()
        {
            string folder = Path.Combine(configDirectory, subdirectory);
            Directory.CreateDirectory(folder);

            return folder;
        }

        private static void UpdateCustomMusic()
        {
            string path = GetSubdirectory();
            if (!Directory.Exists(path))
                return;

            foreach (FileInfo file in new DirectoryInfo(path).EnumerateFiles("*.*", SearchOption.AllDirectories).OrderBy(file => file.Extension.ToLower() != ".json"))
                UpdateFile(file.Name, file.FullName);
        }

        private static void UpdateClipOnChange(object sender, FileSystemEventArgs eargs)
        {
            if (eargs is RenamedEventArgs renamed)
            {
                string oldName = Path.GetFileNameWithoutExtension(renamed.OldName);
                if (Path.GetExtension(renamed.OldName).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    clipSettings.Remove(oldName);
                else
                    audioClips.Remove(oldName);
            }
            UpdateFile(eargs.Name, eargs.FullPath);

            CheckMusicList();

            SeasonEnvironment.ClearCachedObjects();
        }

        private static void UpdateFile(string fileName, string filePath)
        {
            if (Path.GetExtension(fileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
                UpdateSettings(Path.GetFileNameWithoutExtension(fileName), filePath);
            else
                UpdateClip(Path.GetFileNameWithoutExtension(fileName), filePath);
        }

        private static void UpdateClip(string clipName, string fileName)
        {
            bool removed = audioClips.ContainsKey(clipName);
            if (!File.Exists(fileName))
            {
                audioClips.Remove(clipName);
                return;
            }
            if (!TryGetAudioClip(fileName, out AudioClip audioClip))
                return;

            audioClips[clipName] = audioClip;
            LogInfo($"Custom music {(removed ? "updated" : "added")}: {clipName}");
        }

        private static void UpdateSettings(string clipName, string fileName)
        {
            bool removed = clipSettings.ContainsKey(clipName);
            if (!File.Exists(fileName))
            {
                clipSettings.Remove(clipName);
                return;
            }
            if (!TryGetMusicSettings(fileName, out MusicSettings musicSettings) || musicSettings == null)
                return;

            clipSettings[clipName] = musicSettings;
            LogInfo($"Custom music settings {(removed ? "updated" : "added")}: {clipName}");
        }

        internal static bool TryGetAudioClip(string path, out AudioClip audioClip)
        {
            audioClip = null;

            string uri = "file:///" + path.Replace("\\", "/");
            using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.UNKNOWN);
            if (request == null)
                return false;

            request.SendWebRequest();
            while (!request.isDone) { }

            if (request.error != null)
            {
                LogWarning($"Failed to load audio from {path}: {request.error}");
                return false;
            }

            audioClip = (request.downloadHandler as DownloadHandlerAudioClip)?.audioClip;
            if ((bool)audioClip)
            {
                audioClip.name = Path.GetFileNameWithoutExtension(path);
                return true;
            }

            return false;
        }

        internal static bool TryGetMusicSettings(string path, out MusicSettings musicSettings)
        {
            musicSettings = null;

            if (!File.Exists(path))
                return false;

            try
            {
                musicSettings = JsonUtility.FromJson<MusicSettings>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                LogWarning($"Error reading file ({path})! Error: {e.Message}");
                return false;
            }

            return true;
        }
    }
}
