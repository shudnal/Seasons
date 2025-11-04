using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Seasons
{
#nullable enable

    internal class CustomConfigs
    {
        internal class ConfigurationManagerAttributes
        {
            /// <summary>
            /// Custom setting editor (OnGUI code that replaces the default editor provided by ConfigurationManager).
            /// See below for a deeper explanation. Using a custom drawer will cause many of the other fields to do nothing.
            /// </summary>
            [UsedImplicitly]
            public System.Action<BepInEx.Configuration.ConfigEntryBase>? CustomDrawer;
            [UsedImplicitly]
            public bool? ShowRangeAsPercent = false;
        }

        internal static object? configManager;

        internal static Type? configManagerStyles;

        internal static GUIStyle GetStyle(GUIStyle other)
        {
            if (configManagerStyles == null)
                return other;

            FieldInfo fieldFontSize = AccessTools.Field(configManagerStyles, "fontSize");
            if (fieldFontSize == null)
                return other;

            return new GUIStyle(other)
            {
                fontSize = (int)fieldFontSize.GetValue(configManagerStyles)
            };
        }

        internal static void Awake()
        {
            Assembly? bepinexConfigManager = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ConfigurationManager");
            Type? configManagerType = bepinexConfigManager?.GetType("ConfigurationManager.ConfigurationManager");
            configManager = configManagerType == null ? null : BepInEx.Bootstrap.Chainloader.ManagerObject.GetComponent(configManagerType);

            configManagerStyles = bepinexConfigManager?.GetType("ConfigurationManager.ConfigurationManagerStyles");
        }

        internal static Action<ConfigEntryBase> DrawSeparatedStrings(string splitString)
        {
            return cfg =>
            {
                bool locked = cfg.Description.Tags.Select(a => a.GetType().Name == "ConfigurationManagerAttributes" ? (bool?)a.GetType().GetField("ReadOnly")?.GetValue(a) : null).FirstOrDefault(v => v != null) ?? false;

                bool wasUpdated = false;

                GUILayout.BeginVertical();

                List<string> newStrings = new List<string>();
                List<string> strings = ((string)cfg.BoxedValue).Split(new string[] { splitString }, StringSplitOptions.None).ToList();

                for (int i = 0; i < strings.Count; i++)
                {
                    GUILayout.BeginHorizontal();

                    string val = strings[i];

                    string newVal = GUILayout.TextField(val, GetStyle(GUI.skin.textArea), GUILayout.ExpandWidth(true));

                    if (newVal != val && !locked)
                        wasUpdated = true;

                    if (GUILayout.Button("x", new GUIStyle(GetStyle(GUI.skin.button)) { fixedWidth = 21 }) && !locked)
                        wasUpdated = true;
                    else
                        newStrings.Add(newVal);

                    if (GUILayout.Button("+", new GUIStyle(GetStyle(GUI.skin.button)) { fixedWidth = 21 }) && !locked)
                    {
                        wasUpdated = true;
                        newStrings.Add("");
                    }

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();

                if (wasUpdated)
                    cfg.BoxedValue = String.Join(splitString, newStrings);
            };
        }

        internal static Action<ConfigEntryBase> DrawOrderedFixedStrings(string splitString)
        {
            return cfg =>
            {
                bool locked = cfg.Description.Tags.Select(a => a.GetType().Name == "ConfigurationManagerAttributes" ? (bool?)a.GetType().GetField("ReadOnly")?.GetValue(a) : null).FirstOrDefault(v => v != null) ?? false;

                bool wasUpdated = false;

                GUILayout.BeginVertical();

                string[] strings = ((string)cfg.BoxedValue).Split(new string[] { splitString }, StringSplitOptions.None).ToArray();

                for (int i = 0; i < strings.Length; i++)
                {
                    GUILayout.BeginHorizontal();

                    string val = strings[i];

                    GUILayout.Label(val, GetStyle(GUI.skin.textArea), GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("ʌ", new GUIStyle(GetStyle(GUI.skin.button)) { fixedWidth = 21 }) && !locked)
                    {
                        if (wasUpdated = i > 0)
                            (strings[i], strings[i - 1]) = (strings[i - 1], strings[i]);
                    }

                    if (GUILayout.Button("v", new GUIStyle(GetStyle(GUI.skin.button)) { fixedWidth = 21 }) && !locked)
                    {
                        if (wasUpdated = i < strings.Length - 1)
                            (strings[i], strings[i + 1]) = (strings[i + 1], strings[i]);
                    }

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();

                if (wasUpdated)
                    cfg.BoxedValue = string.Join(splitString, strings);
            };
        }
    }
}
