// Based on https://github.com/blaxxun-boop/LocalizationManager

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Splatform;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LocalizationManager;

#nullable enable

[PublicAPI]
public class Localizer
{
    private const string defaultLanguage = "English";

    private static readonly Dictionary<string, Dictionary<string, Func<string>>> PlaceholderProcessors = new();

    private static readonly Dictionary<string, Dictionary<string, string>> loadedTexts = new();

    private static readonly ConditionalWeakTable<Localization, string> localizationLanguage = new();

    private static readonly List<WeakReference<Localization>> localizationObjects = new();

    private static BaseUnityPlugin? _plugin;

    private static BaseUnityPlugin Plugin
    {
        get
        {
            if (_plugin is null)
            {
                IEnumerable<TypeInfo> types;
                try
                {
                    types = Assembly.GetExecutingAssembly().DefinedTypes.ToList();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).Select(t => t.GetTypeInfo());
                }
                _plugin = (BaseUnityPlugin)BepInEx.Bootstrap.Chainloader.ManagerObject.GetComponent(types.First(t => t.IsClass && typeof(BaseUnityPlugin).IsAssignableFrom(t)));
            }
            return _plugin;
        }
    }

    private static readonly List<string> fileExtensions = new() { ".json", ".yml" };

    private static void UpdatePlaceholderText(Localization localization, string key)
    {
        localizationLanguage.TryGetValue(localization, out string language);
        string text = loadedTexts[language][key];
        if (PlaceholderProcessors.TryGetValue(key, out Dictionary<string, Func<string>> textProcessors))
            text = textProcessors.Aggregate(text, (current, kv) => current.Replace("{" + kv.Key + "}", kv.Value()));

        localization.AddWord(key, text);
    }

    public static void AddPlaceholder<T>(string key, string placeholder, ConfigEntry<T> config, Func<T, string>? convertConfigValue = null) where T : notnull
    {
        convertConfigValue ??= val => val.ToString();
        if (!PlaceholderProcessors.ContainsKey(key))
            PlaceholderProcessors[key] = new Dictionary<string, Func<string>>();

        config.SettingChanged += (_, _) => UpdatePlaceholder();
        if (loadedTexts.ContainsKey(Localization.instance.GetSelectedLanguage()))
            UpdatePlaceholder();

        void UpdatePlaceholder()
        {
            PlaceholderProcessors[key][placeholder] = () => convertConfigValue(config.Value);
            UpdatePlaceholderText(Localization.instance, key);
        }
    }

    public static void AddText(string key, string text)
    {
        List<WeakReference<Localization>> remove = new();
        foreach (WeakReference<Localization> reference in localizationObjects)
            if (reference.TryGetTarget(out Localization localization))
            {
                Dictionary<string, string> texts = loadedTexts[localizationLanguage.GetOrCreateValue(localization)];
                if (!localization.m_translations.ContainsKey(key))
                {
                    texts[key] = text;
                    localization.AddWord(key, text);
                }
            }
            else
                remove.Add(reference);

        foreach (WeakReference<Localization> reference in remove)
            localizationObjects.Remove(reference);
    }

    public static IEnumerator Load()
    {
        yield return new WaitUntil(() => PlatformManager.DistributionPlatform != null && PlatformInitializer.PreferencesInitialized);

        // Prevent NRE if language has not been set explicitly yet
        // It will fall into English anyway
        if (string.IsNullOrEmpty(PlatformPrefs.GetString("language", "")))
            PlatformPrefs.SetString("language", defaultLanguage);

        LoadLocalization(Localization.instance, Localization.instance.GetSelectedLanguage());
    }

    private static void LoadLocalization(Localization __instance, string language)
    {
        if (!localizationLanguage.Remove(__instance))
            localizationObjects.Add(new WeakReference<Localization>(__instance));

        localizationLanguage.Add(__instance, language);

        var localizationFiles = new Dictionary<string, string>();

        string[] prefixes =
        {
            Plugin.Info.Metadata.Name + ".",
            Plugin.Info.Metadata.Name.Replace(" ", "") + "."
        };

        void Scan(string root, bool warn)
        {
            foreach (string file in Directory
                .GetFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => fileExtensions.Contains(Path.GetExtension(f))))
            {
                string name = Path.GetFileNameWithoutExtension(file);

                foreach (string prefix in prefixes)
                {
                    if (!name.StartsWith(prefix))
                        continue;

                    string key = name.Substring(prefix.Length);
                    if (string.IsNullOrWhiteSpace(key))
                        break;

                    if (localizationFiles.ContainsKey(key))
                    {
                        if (warn)
                            Seasons.Seasons.LogWarning(
                                $"Duplicate localization '{key}' for {Plugin.Info.Metadata.Name}. Skipping {file}"
                            );
                        break;
                    }

                    localizationFiles[key] = file;
                    break;
                }
            }
        }

        Scan(Paths.ConfigPath, true);
        Scan(Paths.PluginPath, false);

        if (LoadTranslationFromAssembly(defaultLanguage) is not { } englishAssemblyData)
            throw new Exception($"Found no English localizations in mod {Plugin.Info.Metadata.Name}. Expected an embedded resource Translations/English.json or Translations/English.yml.");

        Dictionary<string, string>? localizationTexts = JsonConvert.DeserializeObject<Dictionary<string, string>?>(System.Text.Encoding.UTF8.GetString(englishAssemblyData)) ?? throw new Exception($"Localization for mod {Plugin.Info.Metadata.Name} failed: Localization file was empty.");
        string? localizationData = null;
        if (language != defaultLanguage)
            if (localizationFiles.ContainsKey(language))
                localizationData = File.ReadAllText(localizationFiles[language]);
            else if (LoadTranslationFromAssembly(language) is { } languageAssemblyData)
                localizationData = System.Text.Encoding.UTF8.GetString(languageAssemblyData);
        if (localizationData is null && localizationFiles.ContainsKey(defaultLanguage))
            localizationData = File.ReadAllText(localizationFiles[defaultLanguage]);

        if (localizationData is not null)
            foreach (KeyValuePair<string, string> kv in JsonConvert.DeserializeObject<Dictionary<string, string>?>(localizationData) ?? new Dictionary<string, string>())
                localizationTexts[kv.Key] = kv.Value;

        loadedTexts[language] = localizationTexts;
        foreach (KeyValuePair<string, string> s in localizationTexts)
            UpdatePlaceholderText(__instance, s.Key);
    }

    static Localizer()
    {
        Harmony harmony = new("org.bepinex.helpers.LocalizationManager");
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Localization), nameof(Localization.LoadCSV)), postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Localizer), nameof(LoadLocalization))));
    }

    private static byte[]? LoadTranslationFromAssembly(string language)
    {
        foreach (string extension in fileExtensions)
            if (ReadEmbeddedFileBytes("Translations." + language + extension) is { } data)
                return data;

        return null;
    }

    public static byte[]? ReadEmbeddedFileBytes(string resourceFileName, Assembly? containingAssembly = null)
    {
        using MemoryStream stream = new();
        containingAssembly ??= Assembly.GetCallingAssembly();
        if (containingAssembly.GetManifestResourceNames().FirstOrDefault(str => str.EndsWith(resourceFileName, StringComparison.Ordinal)) is { } name)
            containingAssembly.GetManifestResourceStream(name)?.CopyTo(stream);

        return stream.Length == 0 ? null : stream.ToArray();
    }
}