using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace Seasons
{
    internal class CustomPrefabs
    {
        private const string c_rootObjectName = "_shudnalRoot";
        private const string c_rootPrefabsName = "Prefabs";

        private static GameObject rootObject;
        private static GameObject rootPrefabs;

        public static bool prefabInit = false;
        private static readonly HashSet<ItemDrop> suppressedItemDrops = new HashSet<ItemDrop>();

        private static void InitRootObject()
        {
            if (rootObject == null)
                rootObject = GameObject.Find(c_rootObjectName) ?? new GameObject(c_rootObjectName);

            UnityEngine.Object.DontDestroyOnLoad(rootObject);

            if (rootPrefabs == null)
            {
                rootPrefabs = rootObject.transform.Find(c_rootPrefabsName)?.gameObject;

                if (rootPrefabs == null)
                {
                    rootPrefabs = new GameObject(c_rootPrefabsName);
                    rootPrefabs.transform.SetParent(rootObject.transform, false);
                    rootPrefabs.SetActive(false);
                }
            }
        }

        internal static GameObject InitPrefabClone(GameObject prefabToClone, string prefabName)
        {
            InitRootObject();

            bool previousPrefabInit = prefabInit;
            prefabInit = true;
            try
            {
                GameObject clonedPrefab = UnityEngine.Object.Instantiate(prefabToClone, rootPrefabs.transform, false);
                clonedPrefab.name = prefabName;
                return clonedPrefab;
            }
            finally
            {
                prefabInit = previousPrefabInit;
            }
        }

        [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.Awake))]
        public static class ZNetView_Awake_AddPrefab
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix() => !prefabInit;
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.Awake))]
        public static class ZSyncTransform_Awake_AddPrefab
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix() => !prefabInit;
        }

        [HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.OnEnable))]
        public static class ZSyncTransform_OnEnable_AddPrefab
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix() => !prefabInit;
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Awake))]
        public static class ItemDrop_Awake_AddPrefab
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(ItemDrop __instance)
            {
                if (!prefabInit)
                    return true;
                suppressedItemDrops.Add(__instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnDestroy))]
        public static class ItemDrop_OnDestroy_AddPrefab
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(ItemDrop __instance) => !suppressedItemDrops.Remove(__instance);
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Start))]
        public static class ItemDrop_Start_AddPrefab
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix() => !prefabInit;
        }

    }
}
