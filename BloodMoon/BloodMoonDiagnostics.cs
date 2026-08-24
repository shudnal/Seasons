using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Seasons.Seasons;
using static Terminal;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonDiagnostics
    {
        private static bool initialized;

        internal static void Initialize()
        {
            if (initialized)
                return;
            initialized = true;

            new ConsoleCommand(
                "seasons",
                "Seasons commands. Blood Moon diagnostics: seasons bloodmoon <command>",
                Execute,
                optionsFetcher: RootOptions,
                alwaysRefreshTabOptions: true);
        }

        private static void Execute(ConsoleEventArgs args)
        {
            if (args.Length < 2 || !string.Equals(args[1], "bloodmoon", StringComparison.OrdinalIgnoreCase))
            {
                Print(args, "Usage: seasons bloodmoon <command>");
                return;
            }

            if (args.Length < 3)
            {
                PrintHelp(args);
                return;
            }

            string command = args[2].ToLowerInvariant();
            switch (command)
            {
                case "status":
                    Status(args);
                    break;
                case "start":
                    Start(args);
                    break;
                case "setprogress":
                    SetProgress(args);
                    break;
                case "goalreached":
                    GoalReached(args);
                    break;
                case "defeat":
                    Defeat(args);
                    break;
                case "withdraw":
                    Withdraw(args);
                    break;
                case "spawn":
                    Spawn(args);
                    break;
                case "parkboss":
                    ParkBoss(args);
                    break;
                case "restoreboss":
                    RestoreBosses(args);
                    break;
                case "resolve":
                    Resolve(args);
                    break;
                case "cleanup":
                    Cleanup(args);
                    break;
                case "dump-participants":
                    DumpParticipants(args);
                    break;
                case "dump-groups":
                    DumpGroups(args);
                    break;
                case "dump-spawn-zones":
                    DumpSpawnZones(args);
                    break;
                case "dump-monsters":
                    DumpMonsters(args);
                    break;
                case "dump-bosses":
                    DumpBosses(args);
                    break;
                case "dump-sync":
                    DumpSync(args);
                    break;
                default:
                    PrintHelp(args);
                    break;
            }
        }

        private static void Status(ConsoleEventArgs args)
        {
            BloodMoonController controller = BloodMoonController.Instance;
            Print(args, controller?.DumpState() ?? "Blood Moon controller is not initialized.");
            Print(args, $"clientEvent={BloodMoonNetwork.ClientGlobal.EventId} clientPhase={BloodMoonNetwork.ClientGlobal.Phase} clientRevision={BloodMoonNetwork.ClientGlobal.Revision}");
            Print(args, BloodMoonSkills.DumpLocal());
        }

        private static void Start(ConsoleEventArgs args)
        {
            if (!RequireServer(args) || args.Length < 4)
                return;

            BloodMoonEventPhase phase;
            switch (args[3].ToLowerInvariant())
            {
                case "forewarning":
                    phase = BloodMoonEventPhase.Forewarning;
                    break;
                case "marked":
                    phase = BloodMoonEventPhase.Marked;
                    break;
                case "active":
                    phase = BloodMoonEventPhase.Active;
                    break;
                default:
                    Print(args, "Usage: seasons bloodmoon start <forewarning|marked|active>");
                    return;
            }

            BloodMoonController.Instance?.DebugSetPhase(phase);
            Print(args, $"Requested Blood Moon phase {phase}.");
        }

        private static void SetProgress(ConsoleEventArgs args)
        {
            if (!RequireServer(args) || args.Length < 4 || !args.TryParameterFloat(3, out float progress))
            {
                Print(args, "Usage: seasons bloodmoon setprogress <0..100>");
                return;
            }

            if (!TryGetTargetPlayerId(out long playerId))
            {
                Print(args, "No enrolled participant is available.");
                return;
            }
            BloodMoonController.Instance?.DebugSetProgress(playerId, Mathf.Clamp(progress, 0f, 100f));
            Print(args, $"Set participant {playerId} combat progress to {Mathf.Clamp(progress, 0f, 100f):0.##}%.");
        }

        private static void GoalReached(ConsoleEventArgs args)
        {
            if (!RequireServer(args) || !TryGetTargetPlayerId(out long playerId))
                return;
            BloodMoonController.Instance?.DebugGoalReached(playerId);
            Print(args, $"Marked participant {playerId} as GoalReached.");
        }

        private static void Defeat(ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null || !BloodMoonInteractionRules.IsActiveParticipant(player))
            {
                Print(args, "Defeat diagnostics require the local player to be an active participant.");
                return;
            }
            if (!BloodMoonRecovery.TryInterceptDefeat(player))
            {
                Print(args, "Local participant is already exited or cannot be defeated.");
                return;
            }
            BloodMoonNetwork.SendDefeated(BloodMoonNetwork.ClientGlobal.EventId, player.GetPlayerID());
            Print(args, $"Triggered Defeated for local participant {player.GetPlayerID()}.");
        }

        private static void Withdraw(ConsoleEventArgs args)
        {
            if (!RequireServer(args) || !TryGetTargetPlayerId(out long playerId))
                return;
            BloodMoonController.Instance?.WithdrawLocalOrRequested(playerId);
            Print(args, $"Triggered Withdrawn for participant {playerId}.");
        }

        private static void Spawn(ConsoleEventArgs args)
        {
            if (args.Length < 4)
            {
                Print(args, "Usage: seasons bloodmoon spawn <prefab>");
                return;
            }
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null)
            {
                Print(args, "Debug spawning requires a local world owner/client.");
                return;
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(args[3]);
            if (prefab == null || prefab.GetComponent<MonsterAI>() == null)
            {
                Print(args, $"Prefab '{args[3]}' is not a MonsterAI prefab.");
                return;
            }

            BloodMoonEventState state = BloodMoonController.Instance?.State;
            long eventId = state?.EventId ?? BloodMoonNetwork.ClientGlobal.EventId;
            if (eventId < 0L)
            {
                Print(args, "No Blood Moon event is active.");
                return;
            }

            Vector3 point = player.transform.position + player.transform.forward * 12f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.FindFloor(point, out float floor))
                point.y = floor + 0.5f;
            GameObject spawned = UnityEngine.Object.Instantiate(prefab, point, Quaternion.identity);
            ZNetView nview = spawned.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                UnityEngine.Object.Destroy(spawned);
                Print(args, "Spawned object has no valid ZNetView.");
                return;
            }

            long groupId = state == null ? -1L : BloodMoonGroups.FindForPlayer(state, player.GetPlayerID())?.GroupId ?? -1L;
            ZDO zdo = nview.GetZDO();
            zdo.Set(BloodMoonSpawner.EventMarker, eventId);
            zdo.Set(BloodMoonSpawner.GroupMarker, groupId);
            zdo.Set(BloodMoonSpawner.RoleMarker, (int)(player.InInterior() ? BloodMoonExtraEnemyRole.Interior : BloodMoonExtraEnemyRole.Surface));
            if (state != null && ZNet.instance != null && ZNet.instance.IsServer())
            {
                state.ExtraEnemyZdos.Add(zdo.m_uid.ToString());
                BloodMoonPersistence.Save(state);
                BloodMoonController.Instance.PublishState(force: true);
            }
            Print(args, $"Spawned marked {prefab.name} as {zdo.m_uid} for event {eventId}.");
        }

        private static void ParkBoss(ConsoleEventArgs args)
        {
            if (!RequireServer(args))
                return;
            BloodMoonController controller = BloodMoonController.Instance;
            BloodMoonEventState state = controller?.State;
            if (state == null)
            {
                Print(args, "Blood Moon server state is not loaded.");
                return;
            }

            Vector3 point = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : state.Groups.Values.FirstOrDefault()?.Anchor ?? Vector3.zero;
            bool result = BloodMoonBosses.ParkNearest(state, point, seasonState.GetTotalSeconds());
            Print(args, result ? "Parked nearest eligible boss." : "No eligible persistent outdoor boss found.");
        }

        private static void RestoreBosses(ConsoleEventArgs args)
        {
            if (!RequireServer(args))
                return;
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null)
                return;
            BloodMoonBosses.RestoreAll(state);
            BloodMoonController.Instance.PublishState(force: true);
            Print(args, "Restored all Blood Moon parked bosses.");
        }

        private static void Resolve(ConsoleEventArgs args)
        {
            if (!RequireServer(args))
                return;
            BloodMoonController controller = BloodMoonController.Instance;
            if (controller?.State == null)
                return;
            controller.BeginResolution("diagnostic command", seasonState.GetTotalSeconds());
            Print(args, "Started Blood Moon resolution.");
        }

        private static void Cleanup(ConsoleEventArgs args)
        {
            if (!RequireServer(args))
                return;
            BloodMoonController.Instance?.DebugCleanup();
            Print(args, "Ran Blood Moon cleanup.");
        }

        private static void DumpParticipants(ConsoleEventArgs args)
        {
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            IEnumerable<BloodMoonParticipantState> participants = state?.Participants.Values ?? BloodMoonNetwork.ClientParticipants.Participants ?? Enumerable.Empty<BloodMoonParticipantState>();
            foreach (BloodMoonParticipantState participant in participants.OrderBy(item => item.PlayerId))
            {
                Print(args, $"player={participant.PlayerId} name='{participant.PlayerName}' phase={participant.Phase} exit={participant.ExitReason} goal={participant.GoalReached} auto={participant.AutoCompleted} points={participant.CombatPoints:0.###} display={participant.DisplayProgress:0.##}% contribution={participant.Contribution:0.###} skillSeq={participant.LastSkillReportSequence}");
            }
        }

        private static void DumpGroups(ConsoleEventArgs args)
        {
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null)
            {
                Print(args, "Group state is server-only and is unavailable here.");
                return;
            }
            foreach (BloodMoonGroupState group in state.Groups.Values.OrderBy(item => item.GroupId))
                Print(args, $"group={group.GroupId} revision={group.Revision} members=[{string.Join(",", group.MemberPlayerIds)}] anchor={group.Anchor} points={group.CombatPoints:0.###} extras={group.ExtraEnemyCount}");
        }

        private static void DumpSpawnZones(ConsoleEventArgs args)
        {
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null)
            {
                Print(args, "Spawn lease state is server-only and is unavailable here.");
                return;
            }
            foreach (BloodMoonSpawnLeaseState lease in state.SpawnLeases.Values.OrderBy(item => item.GroupId).ThenBy(item => item.ZoneX).ThenBy(item => item.ZoneY))
                Print(args, $"group={lease.GroupId} groupRev={lease.GroupRevision} zone={lease.ZoneX},{lease.ZoneY} peer={lease.OwnerPeerId} leaseRev={lease.LeaseRevision} allowance={lease.Allowance} cap={lease.GroupCap}/{lease.ServerHardCap} expires={lease.ExpiresAt:0.###}");
        }

        private static void DumpMonsters(ConsoleEventArgs args)
        {
            foreach (Character character in Character.GetAllCharacters().Where(character => character != null && character.GetBaseAI() is MonsterAI).OrderBy(character => character.name))
            {
                Print(args, $"zdo={character.GetZDOID()} prefab='{Utils.GetPrefabName(character.gameObject)}' faction={character.GetFaction()} boss={character.IsBoss()} tamed={character.IsTamed()} eligible={BloodMoonInteractionRules.IsEligibleExistingMonster(character)} blood={BloodMoonInteractionRules.IsBloodEnemy(character)} extra={BloodMoonInteractionRules.IsBloodMoonSpawned(character)} pos={character.transform.position}");
            }
        }

        private static void DumpBosses(ConsoleEventArgs args)
        {
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            if (state == null)
            {
                Print(args, "Boss transaction state is server-only and is unavailable here.");
                return;
            }
            foreach (BloodMoonBossParkingState boss in state.ParkedBosses.Values.OrderBy(item => item.ZdoId))
                Print(args, $"zdo={boss.ZdoId} original={boss.OriginalPosition} restored={boss.Restored} loaded={boss.WasLoaded} ownerRev={boss.OriginalOwnerRevision} dataRev={boss.OriginalDataRevision}");
        }

        private static void DumpSync(ConsoleEventArgs args)
        {
            BloodMoonGlobalSnapshot global = BloodMoonNetwork.ClientGlobal;
            BloodMoonParticipantSnapshot participants = BloodMoonNetwork.ClientParticipants;
            Print(args, $"global: schema={global.Schema} event={global.EventId} phase={global.Phase} step={global.ResolutionStep} revision={global.Revision} behavior={global.BloodBehaviorEnabled} spawnsStopped={global.SpawnsStopped} serverTime={global.ServerTime:0.###}");
            Print(args, $"participants: schema={participants.Schema} event={participants.EventId} revision={participants.Revision} count={participants.Participants?.Count ?? 0}");
            Print(args, BloodMoonSkills.DumpLocal());
        }

        private static bool RequireServer(ConsoleEventArgs args)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer() && BloodMoonController.Instance?.State != null)
                return true;
            Print(args, "This Blood Moon command requires server authority and a loaded world.");
            return false;
        }

        private static bool TryGetTargetPlayerId(out long playerId)
        {
            playerId = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            if (playerId != 0L)
                return true;
            BloodMoonEventState state = BloodMoonController.Instance?.State;
            BloodMoonParticipantState participant = state?.Participants.Values.FirstOrDefault(item => item.IsCombatActive || item.Phase == BloodMoonParticipantPhase.Marked);
            if (participant == null)
                return false;
            playerId = participant.PlayerId;
            return true;
        }

        private static List<string> RootOptions()
        {
            return new List<string> { "bloodmoon" };
        }

        private static void PrintHelp(ConsoleEventArgs args)
        {
            Print(args, "Blood Moon commands:");
            Print(args, "seasons bloodmoon status");
            Print(args, "seasons bloodmoon start <forewarning|marked|active>");
            Print(args, "seasons bloodmoon setprogress <0..100>");
            Print(args, "seasons bloodmoon goalreached");
            Print(args, "seasons bloodmoon defeat");
            Print(args, "seasons bloodmoon withdraw");
            Print(args, "seasons bloodmoon spawn <prefab>");
            Print(args, "seasons bloodmoon parkboss");
            Print(args, "seasons bloodmoon restoreboss");
            Print(args, "seasons bloodmoon resolve");
            Print(args, "seasons bloodmoon cleanup");
            Print(args, "seasons bloodmoon dump-participants|dump-groups|dump-spawn-zones|dump-monsters|dump-bosses|dump-sync");
        }

        private static void Print(ConsoleEventArgs args, string text)
        {
            args.Context?.AddString(text);
            LogInfo($"[BloodMoon.Diagnostics] {text}");
        }
    }
}
