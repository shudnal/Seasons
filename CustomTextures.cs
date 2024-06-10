using BepInEx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static Seasons.Seasons;

namespace Seasons
{
    internal class CustomTextures
    {
        public const string texturesSubdirectory = "Custom textures";
        public const string defaultsSubdirectory = "Defaults";

        public const string versionFileName = "version";

        public static readonly Dictionary<string, Dictionary<Season, Dictionary<int, Texture2D>>> textures = new Dictionary<string, Dictionary<Season, Dictionary<int, Texture2D>>>();
        public static Dictionary<string, Tuple<Season, int>> seasonVariantsFileNames;

        public static void SetupConfigWatcher()
        {
            string filter = $"*.png";

            FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(GetSubdirectory(), filter);
            fileSystemWatcher.Changed += new FileSystemEventHandler(UpdateTexturesOnChange);
            fileSystemWatcher.Created += new FileSystemEventHandler(UpdateTexturesOnChange);
            fileSystemWatcher.Renamed += new RenamedEventHandler(UpdateTexturesOnChange);
            fileSystemWatcher.Deleted += new FileSystemEventHandler(UpdateTexturesOnChange);
            fileSystemWatcher.IncludeSubdirectories = true;
            fileSystemWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fileSystemWatcher.EnableRaisingEvents = true;

            UpdateTexturesOnChange();
        }

        public static void UpdateTexturesOnChange(object sender = null, FileSystemEventArgs eargs = null)
        {
            UpdateTextures();

            PrefabVariantController.UpdatePrefabColors();
            ClutterVariantController.instance?.UpdateColors();
        }

        public static void UpdateTextures()
        {
            foreach (var tex in textures)
                foreach (var seasonaltex in tex.Value)
                    foreach (var varianttex in seasonaltex.Value)
                        UnityEngine.Object.Destroy(varianttex.Value);

            textures.Clear();

            if (!customTextures.Value)
                return;

            LoadCustomTextures(GetDefaultsSubdirectory());

            LoadCustomTextures(GetSubdirectory());

            if (textures.Count > 0)
                LogInfo($"Loaded {textures.Count} custom textures.");
        }

        public static bool HaveCustomTexture(string textureName, Season season, int variant, TextureProperties properties, out Texture2D texture)
        {
            texture = null;

            if (textureName == null)
                return false;

            bool result = textures.TryGetValue(textureName, out Dictionary<Season, Dictionary<int, Texture2D>> seasonalTex) && 
                seasonalTex.TryGetValue(season, out Dictionary<int, Texture2D> variantTex) && 
                 variantTex.TryGetValue(variant, out texture);

            if (result && texture != null && texture.isReadable)
            {
                Color32[] pixels = texture.GetPixels32();
                UnityEngine.Object.Destroy(texture);

                texture = properties.CreateTexture();
                texture.SetPixels32(pixels);
                texture.Apply(true, true);

                textures[textureName][season][variant] = texture;
            }

            return result;
        }

        public static void LoadCustomTextures(string path)
        {
            if (!Directory.Exists(path))
                return;

            foreach (DirectoryInfo directory in new DirectoryInfo(path).GetDirectories())
            {
                if (directory.Name == versionFileName || directory.Name == defaultsSubdirectory)
                    continue;

                string textureName = directory.Name;
                textures.Remove(textureName);

                foreach (FileInfo file in directory.GetFiles())
                {
                    if (!TryGetSeasonVariant(file.Name, out Season season, out int variant))
                        continue;

                    // Texture load as readable to later apply format and make unreadable on first check
                    Texture2D texture = new Texture2D(2, 2);
                    if (!texture.LoadImage(File.ReadAllBytes(file.FullName)))
                    {
                        UnityEngine.Object.Destroy(texture);
                        continue;
                    }

                    texture.name = textureName;

                    if (!textures.ContainsKey(textureName))
                        textures.Add(textureName, new Dictionary<Season, Dictionary<int, Texture2D>>());

                    if (!textures[textureName].ContainsKey(season))
                        textures[textureName].Add(season, new Dictionary<int, Texture2D>());

                    textures[textureName][season][variant] = texture;
                }
            }
        }

        public static void SaveDefaults()
        {
            string folder = GetDefaultsSubdirectory();

            if (Directory.Exists(folder))
            {
                string versionFile = Directory.GetFiles(folder, versionFileName).FirstOrDefault();
                if (versionFile != null)
                    if (File.ReadAllText(versionFile) == pluginVersion)
                        return;

                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);

            Assembly executingAssembly = Assembly.GetExecutingAssembly();

            string separator = ".Textures.";

            foreach (string textureFileName in executingAssembly.GetManifestResourceNames().Where(str => str.IndexOf(separator) != -1))
            {
                string path = textureFileName.Substring(textureFileName.IndexOf(separator) + separator.Length);
                int pos = path.IndexOf('.');

                if (pos == -1)
                    continue;

                string textureName = path.Substring(0, pos);
                string filename = path.Substring(pos + 1);
                if (!TryGetSeasonVariant(filename, out Season season, out int variant))
                    continue;

                Stream resourceStream = executingAssembly.GetManifestResourceStream(textureFileName);

                byte[] data = new byte[resourceStream.Length];
                resourceStream.Read(data, 0, data.Length);

                File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(folder, textureName)).FullName, filename), data);
            }

            File.WriteAllText(Path.Combine(folder, versionFileName), pluginVersion);
        }

        public static string GetSubdirectory()
        {
            string folder = Path.Combine(configDirectory, texturesSubdirectory);
            Directory.CreateDirectory(folder);

            return folder;
        }

        public static string GetDefaultsSubdirectory()
        {
            return Path.Combine(GetSubdirectory(), defaultsSubdirectory);
        }

        public static bool TryGetSeasonVariant(string filename, out Season season, out int variant)
        {
            if (seasonVariantsFileNames == null)
            {
                seasonVariantsFileNames = new Dictionary<string, Tuple<Season, int>>();
                foreach (Season enumSeason in Enum.GetValues(typeof(Season)))
                    for (int i = 0; i < seasonColorVariants; i++)
                        seasonVariantsFileNames[CachedData.SeasonFileName(enumSeason, i)] = Tuple.Create(enumSeason, i);
            }

            season = Season.Spring; variant = 0;
            if (!seasonVariantsFileNames.ContainsKey(filename))
                return false;

            season = seasonVariantsFileNames[filename].Item1;
            variant = seasonVariantsFileNames[filename].Item2;

            return true;
        }
    }
}
