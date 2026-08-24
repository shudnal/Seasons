using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonGroups
    {
        internal static void Rebuild(BloodMoonEventState state, double now)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            if (state == null || controller == null)
                return;

            Dictionary<long, Vector3> positions = new Dictionary<long, Vector3>();
            foreach (BloodMoonParticipantState participant in state.Participants.Values.Where(item => item.IsCombatActive))
            {
                if (controller.TryGetConnectedPosition(participant.PlayerId, out Vector3 position))
                    positions[participant.PlayerId] = position;
            }

            List<HashSet<long>> components = BuildComponents(positions, state);
            Dictionary<long, BloodMoonGroupState> previous = state.Groups;
            Dictionary<long, BloodMoonGroupState> next = new Dictionary<long, BloodMoonGroupState>();
            HashSet<long> consumedPrevious = new HashSet<long>();

            foreach (HashSet<long> component in components.OrderByDescending(group => group.Count))
            {
                BloodMoonGroupState best = previous.Values
                    .Where(group => !consumedPrevious.Contains(group.GroupId))
                    .Select(group => new { Group = group, Overlap = group.MemberPlayerIds.Count(component.Contains) })
                    .Where(candidate => candidate.Overlap > 0)
                    .OrderByDescending(candidate => candidate.Overlap)
                    .ThenBy(candidate => candidate.Group.GroupId)
                    .Select(candidate => candidate.Group)
                    .FirstOrDefault();

                BloodMoonGroupState group;
                if (best != null)
                {
                    consumedPrevious.Add(best.GroupId);
                    group = best;
                    if (!group.MemberPlayerIds.OrderBy(id => id).SequenceEqual(component.OrderBy(id => id)))
                        group.Revision++;
                }
                else
                {
                    group = new BloodMoonGroupState
                    {
                        GroupId = ++state.GroupSequence,
                        Revision = 1
                    };
                }

                group.MemberPlayerIds = component.OrderBy(id => id).ToList();
                group.Anchor = Average(group.MemberPlayerIds.Where(positions.ContainsKey).Select(id => positions[id]));
                group.UpdatedAt = now;
                group.ExtraEnemyCount = state.ExtraEnemyZdos.Count(id => BloodMoonSpawner.GetMarkedGroupId(id) == group.GroupId);
                next[group.GroupId] = group;
            }

            state.Groups = next;
        }

        internal static BloodMoonGroupState FindForPlayer(BloodMoonEventState state, long playerId)
        {
            return state?.Groups.Values.FirstOrDefault(group => group.MemberPlayerIds.Contains(playerId));
        }

        internal static void RemovePlayer(BloodMoonEventState state, long playerId)
        {
            if (state == null)
                return;
            foreach (BloodMoonGroupState group in state.Groups.Values)
            {
                if (group.MemberPlayerIds.Remove(playerId))
                    group.Revision++;
            }
        }

        private static List<HashSet<long>> BuildComponents(Dictionary<long, Vector3> positions, BloodMoonEventState state)
        {
            Dictionary<long, HashSet<long>> graph = positions.Keys.ToDictionary(id => id, _ => new HashSet<long>());
            long[] ids = positions.Keys.ToArray();
            for (int i = 0; i < ids.Length; ++i)
            {
                for (int j = i + 1; j < ids.Length; ++j)
                {
                    long first = ids[i];
                    long second = ids[j];
                    float distance = Utils.DistanceXZ(positions[first], positions[second]);
                    bool sameExistingGroup = state.Groups.Values.Any(group => group.MemberPlayerIds.Contains(first) && group.MemberPlayerIds.Contains(second));
                    float threshold = sameExistingGroup ? BloodMoonConfig.GroupSplitDistance.Value : BloodMoonConfig.GroupMergeDistance.Value;
                    if (distance <= threshold)
                    {
                        graph[first].Add(second);
                        graph[second].Add(first);
                    }
                }
            }

            List<HashSet<long>> result = new List<HashSet<long>>();
            HashSet<long> visited = new HashSet<long>();
            foreach (long root in ids)
            {
                if (!visited.Add(root))
                    continue;
                HashSet<long> component = new HashSet<long> { root };
                Queue<long> queue = new Queue<long>();
                queue.Enqueue(root);
                while (queue.Count > 0)
                {
                    long current = queue.Dequeue();
                    foreach (long neighbor in graph[current])
                    {
                        if (!visited.Add(neighbor))
                            continue;
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
                result.Add(component);
            }
            return result;
        }

        private static Vector3 Average(IEnumerable<Vector3> points)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (Vector3 point in points)
            {
                sum += point;
                count++;
            }
            return count > 0 ? sum / count : Vector3.zero;
        }
    }
}
