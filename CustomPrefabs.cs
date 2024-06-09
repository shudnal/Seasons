using HarmonyLib;
using UnityEngine;

namespace Seasons
{
    internal class CustomPrefabs
    {
        private const string c_rootObjectName = "_shudnalRoot";
        private const string c_rootPrefabsName = "Prefabs";

        private static GameObject rootObject;
        private static GameObject rootPrefabs;

        public static bool prefabInit = false;

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

            prefabInit = true;
            GameObject clonedPrefab = UnityEngine.Object.Instantiate(prefabToClone, rootPrefabs.transform, false);
            prefabInit = false;
            clonedPrefab.name = prefabName;

            return clonedPrefab;
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
            private static bool Prefix() => !prefabInit;
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Start))]
        public static class ItemDrop_Start_AddPrefab
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix() => !prefabInit;
        }

    }
}
