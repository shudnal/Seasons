using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonStaleAttribution
    {
        private const string EventMarker = "Seasons.BloodMoon.ProjectileEventId";
        private const string SourceTypeMarker = "Seasons.BloodMoon.ProjectileSourceType";

        [ThreadStatic]
        private static int staleProjectileHitDepth;

        internal static bool BlockingProjectileDamage => staleProjectileHitDepth > 0;

        internal static bool IsStale(UnityEngine.Object source, ZNetView nview)
        {
            bool combatLive = BloodMoonInteractionRules.IsEventCombatLive;
            long currentEventId = BloodMoonNetwork.ClientGlobal.EventId;

            if (source != null && BloodMoonHitAttribution.TryGet(source, nview, out BloodMoonHitAttributionData attribution))
                return !combatLive || attribution.EventId != currentEventId;

            if (nview == null || !nview.IsValid())
                return false;

            ZDO zdo = nview.GetZDO();
            if (zdo == null)
                return false;

            long eventId = zdo.GetLong(EventMarker, -1L);
            int sourceType = zdo.GetInt(SourceTypeMarker, 0);
            if (eventId < 0L || sourceType == (int)BloodMoonCombatSourceType.None)
                return false;

            return !combatLive || eventId != currentEventId;
        }

        internal static void BeginProjectileHit()
        {
            staleProjectileHitDepth++;
        }

        internal static void EndProjectileHit()
        {
            staleProjectileHitDepth = Math.Max(0, staleProjectileHitDepth - 1);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class BloodMoonStaleProjectileGuardPatch
    {
        private static readonly List<GameObject> EmptySpawnList = new List<GameObject>();

        private sealed class State
        {
            internal bool Active;
            internal float HealthReturn;
            internal float RaiseSkillAmount;
            internal float Adrenaline;
            internal GameObject SpawnOnHit;
            internal List<GameObject> RandomSpawnOnHit;
            internal OnProjectileHit OnHit;
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Projectile __instance, out State __state)
        {
            __state = null;
            if (__instance == null || !BloodMoonStaleAttribution.IsStale(__instance, __instance.m_nview))
                return;

            __state = new State
            {
                Active = true,
                HealthReturn = __instance.m_healthReturn,
                RaiseSkillAmount = __instance.m_raiseSkillAmount,
                Adrenaline = __instance.m_adrenaline,
                SpawnOnHit = __instance.m_spawnOnHit,
                RandomSpawnOnHit = __instance.m_randomSpawnOnHit,
                OnHit = __instance.m_onHit
            };

            BloodMoonStaleAttribution.BeginProjectileHit();
            __instance.m_healthReturn = 0f;
            __instance.m_raiseSkillAmount = 0f;
            __instance.m_adrenaline = 0f;
            __instance.m_spawnOnHit = null;
            __instance.m_randomSpawnOnHit = EmptySpawnList;
            __instance.m_onHit = null;
        }

        private static Exception Finalizer(Exception __exception, Projectile __instance, State __state)
        {
            if (__state?.Active == true)
            {
                try
                {
                    if (__instance != null)
                    {
                        __instance.m_healthReturn = __state.HealthReturn;
                        __instance.m_raiseSkillAmount = __state.RaiseSkillAmount;
                        __instance.m_adrenaline = __state.Adrenaline;
                        __instance.m_spawnOnHit = __state.SpawnOnHit;
                        __instance.m_randomSpawnOnHit = __state.RandomSpawnOnHit;
                        __instance.m_onHit = __state.OnHit;
                    }
                }
                finally
                {
                    BloodMoonStaleAttribution.EndProjectileHit();
                }
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.FixedUpdate))]
    internal static class BloodMoonStaleProjectileTtlSpawnGuardPatch
    {
        private static readonly List<GameObject> EmptySpawnList = new List<GameObject>();

        private sealed class State
        {
            internal bool Active;
            internal GameObject SpawnOnHit;
            internal List<GameObject> RandomSpawnOnHit;
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Projectile __instance, out State __state)
        {
            __state = null;
            if (__instance == null || !__instance.m_spawnOnTtl || !BloodMoonStaleAttribution.IsStale(__instance, __instance.m_nview))
                return;

            __state = new State
            {
                Active = true,
                SpawnOnHit = __instance.m_spawnOnHit,
                RandomSpawnOnHit = __instance.m_randomSpawnOnHit
            };
            __instance.m_spawnOnHit = null;
            __instance.m_randomSpawnOnHit = EmptySpawnList;
        }

        private static Exception Finalizer(Exception __exception, Projectile __instance, State __state)
        {
            if (__state?.Active == true && __instance != null)
            {
                __instance.m_spawnOnHit = __state.SpawnOnHit;
                __instance.m_randomSpawnOnHit = __state.RandomSpawnOnHit;
            }
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class BloodMoonStaleProjectileDamageGuardPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types =
            {
                typeof(Character), typeof(WearNTear), typeof(Destructible), typeof(MineRock), typeof(MineRock5),
                typeof(TreeBase), typeof(TreeLog), typeof(HitArea), typeof(Raven)
            };
            foreach (Type type in types)
            {
                MethodInfo method = AccessTools.Method(type, "Damage", new[] { typeof(HitData) });
                if (method != null)
                    yield return method;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            return !BloodMoonStaleAttribution.BlockingProjectileDamage;
        }
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.ShouldHit))]
    internal static class BloodMoonStaleAoeShouldHitGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Aoe __instance, ref bool __result)
        {
            if (!BloodMoonStaleAttribution.IsStale(__instance, __instance?.m_nview))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Aoe), nameof(Aoe.OnHit))]
    internal static class BloodMoonStaleAoeOnHitGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Aoe __instance)
        {
            return !BloodMoonStaleAttribution.IsStale(__instance, __instance?.m_nview);
        }
    }
}
