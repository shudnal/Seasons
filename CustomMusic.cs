
using HarmonyLib;
using BepInEx;
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

        internal static void SetupConfigWatcher()
        {
            string filter = $"*.*";

            FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(GetSubdirectory(), filter);
            fileSystemWatcher.Changed += new FileSystemEventHandler(UpdateClipOnChange);
            fileSystemWatcher.Created += new FileSystemEventHandler(UpdateClipOnChange);
            fileSystemWatcher.Renamed += new RenamedEventHandler(UpdateClipOnChange);
            fileSystemWatcher.Deleted += new FileSystemEventHandler(UpdateClipOnChange);
            fileSystemWatcher.IncludeSubdirectories = true;
            fileSystemWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fileSystemWatcher.EnableRaisingEvents = true;

            UpdateCustomMusic();

            CheckMusicList();

            SeasonEnvironment.ClearCachedObjects();
        }

        internal static void CheckMusicList()
        {
            if (!MusicMan.instance)
                return;

            foreach (KeyValuePair<string, AudioClip> clip in audioClips)
                (MusicMan.instance.m_music.Find(music => music.m_name == clip.Key) ?? GetNewMusic(clip.Key)).m_clips = new AudioClip[1] { clip.Value };
        }

        private static MusicMan.NamedMusic GetNewMusic(string name)
        {
            MusicSettings musicSettings = clipSettings.GetValueSafe(name) ?? new MusicSettings();

            MusicMan.NamedMusic music = new MusicMan.NamedMusic()
            {
                m_name = name,
                m_ambientMusic = musicSettings.m_ambientMusic,
                m_resume = musicSettings.m_resume,
                m_alwaysFadeout = musicSettings.m_alwaysFadeout,
                m_enabled = musicSettings.m_enabled,
                m_fadeInTime = musicSettings.m_fadeInTime,
                m_loop = musicSettings.m_loop,
                m_volume = musicSettings.m_volume,
            };
            
            MusicMan.instance.m_music.Add(music);
            
            return music;
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
            UpdateFile(eargs.Name, eargs.FullPath);
            if (eargs is RenamedEventArgs)
            {
                audioClips.Remove(Path.GetFileNameWithoutExtension((eargs as RenamedEventArgs).OldName));
            }

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
            bool removed = audioClips.Remove(clipName);

            if (!TryGetAudioClip(fileName, out AudioClip audioClip))
                return;

            audioClips.Add(clipName, audioClip);
            LogInfo($"Custom music {(removed ? "updated" : "added")}: {clipName}");
        }

        private static void UpdateSettings(string clipName, string fileName)
        {
            bool removed = clipSettings.Remove(clipName);

            if (!TryGetMusicSettings(fileName, out MusicSettings musicSettings))
                return;

            clipSettings.Add(clipName, musicSettings);
            LogInfo($"Custom music settings {(removed ? "updated" : "added")}: {clipName}");
        }

        internal static bool TryGetAudioClip(string path, out AudioClip audioClip)
        {
            audioClip = null;

            string uri = "file:///" + path.Replace("\\", "/");
            UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.UNKNOWN);
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
