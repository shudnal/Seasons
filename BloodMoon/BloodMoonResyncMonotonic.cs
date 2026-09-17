using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace Seasons.BloodMoon
{
    [HarmonyPatch(typeof(BloodMoonNetwork), "ApplyResyncEnvelope")]
    internal static class BloodMoonResyncMonotonicPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return true;

            try
            {
                JObject envelope = JObject.Parse(payload);
                JObject incomingGlobal = envelope["Global"] as JObject;
                JObject incomingParticipants = envelope["Participants"] as JObject;
                if (incomingGlobal == null || incomingParticipants == null)
                    return true;

                long incomingEventId = incomingGlobal.Value<long?>("EventId") ?? -1L;
                if ((incomingParticipants.Value<long?>("EventId") ?? long.MinValue) != incomingEventId)
                    return true;

                BloodMoonGlobalSnapshot currentGlobal = BloodMoonNetwork.ClientGlobal;
                BloodMoonParticipantSnapshot currentParticipants = BloodMoonNetwork.ClientParticipants;
                bool sameGlobalEvent = currentGlobal != null && currentGlobal.EventId == incomingEventId;
                bool sameParticipantEvent = currentParticipants != null && currentParticipants.EventId == incomingEventId;

                int incomingGlobalRevision = incomingGlobal.Value<int?>("Revision") ?? 0;
                if (sameGlobalEvent && currentGlobal.Revision > incomingGlobalRevision)
                    envelope["Global"] = JToken.FromObject(currentGlobal);

                int incomingParticipantRevision = incomingParticipants.Value<int?>("Revision") ?? 0;
                if (sameParticipantEvent && currentParticipants.Revision > incomingParticipantRevision)
                    envelope["Participants"] = JToken.FromObject(currentParticipants);

                BloodMoonParticipantDetailSnapshot currentOwn = BloodMoonParticipantDetails.ClientOwn;
                if (currentOwn != null && currentOwn.EventId == incomingEventId && currentOwn.PlayerId != 0L)
                {
                    JObject incomingOwn = envelope["OwnDetail"] as JObject;
                    int incomingOwnRevision = incomingOwn?.Value<int?>("Revision") ?? int.MinValue;
                    if (currentOwn.Revision > incomingOwnRevision)
                        envelope["OwnDetail"] = JToken.FromObject(currentOwn);
                }

                payload = envelope.ToString(Formatting.None);
            }
            catch (Exception ex)
            {
                Seasons.LogWarning($"[BloodMoon.Sync] Failed to normalize explicit resync revisions: {ex.Message}");
            }

            return true;
        }
    }
}
