using HarmonyLib;

namespace Seasons.BloodMoon
{
    // Validate BloodEnemy identity before the attribution RPC can enqueue a pending hit.
    // The package cursor is restored so the original handler consumes the same payload unchanged.
    [HarmonyPatch(typeof(BloodMoonHitAttribution), "OnAttributionRpc")]
    internal static class BloodMoonAttributionSourceGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(long sender, ZPackage pkg)
        {
            if (pkg == null || ZDOMan.instance == null)
                return true;

            long position = pkg.m_stream.Position;
            try
            {
                if (pkg.ReadInt() != BloodMoonNetwork.ProtocolVersion)
                    return true;

                long eventId = pkg.ReadLong();
                _ = pkg.ReadZDOID(); // target
                ZDOID sourceId = pkg.ReadZDOID();
                BloodMoonCombatSourceType sourceType = (BloodMoonCombatSourceType)pkg.ReadInt();
                long sourcePlayerId = pkg.ReadLong();

                if (sourceType != BloodMoonCombatSourceType.BloodEnemy)
                    return true;
                if (eventId != BloodMoonNetwork.ClientGlobal.EventId || sourceId.IsNone() || sourcePlayerId != 0L)
                    return false;

                ZDO sourceZdo = ZDOMan.instance.GetZDO(sourceId);
                return sourceZdo != null && sourceZdo.GetOwner() == sender &&
                    BloodMoonEnemyDeathReports.IsEligibleBloodEnemyZdo(eventId, sourceZdo);
            }
            catch
            {
                // Let the original handler own malformed-package diagnostics/handling.
                return true;
            }
            finally
            {
                pkg.m_stream.Position = position;
            }
        }
    }
}
