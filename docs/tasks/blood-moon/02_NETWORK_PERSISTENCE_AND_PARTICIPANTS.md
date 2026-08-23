# Blood Moon — network, persistence and participant lifecycle

Part of `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production implementation is blocked by files `09` and `10`.

# 6. Network model

## 6.1. Required CCS analysis

Before production networking, inspect the exact Seasons/ConditionalConfigSync versions:

- `Utils/CustomSyncedValuesSynchronizer.cs`;
- `CustomSyncedValue<T>`;
- `SequencedCustomSyncedValue<T>`;
- initial/full sync;
- queue ordering and replacement semantics;
- server ownership and late-join behavior.

Document the selected scheme and do not duplicate guarantees already supplied by CCS.

## 6.2. Expected hybrid split

### Low-frequency current snapshot

Use CCS when suitable for a self-contained current state:

```text
protocolVersion
eventId
revision
event phase
frozen schedule
resolution step
suppression/visual state
```

### Targeted or owner reports

Use routed RPC where target or direction matters:

- first-contact owner report;
- Defeated/dream-collapse owner report;
- blood-enemy kill owner report;
- targeted participant progress/outcome;
- snapshot/resync request;
- boss-context delay signal;
- fade ACK;
- diagnostics.

Every report contains event and object identities. Clients do not submit trusted point values or final rewards.

## 6.3. Revision and stale data

Within an `eventId`, use a monotonic revision. Ignore:

- older revisions;
- stale event IDs;
- transitions invalid from current state.

Request a full snapshot after uncertainty or rejection.

## 6.4. Identity

Keep separate:

```text
stable profile/player ID
current ZNet peer/session UID
Player ZDOID
```

- stable ID: persistence/outcome/reconnect;
- peer UID: targeted RPC and optional ownership decisions;
- Player ZDOID: combat attribution.

Reconnect may change peer UID/ZDOID without creating a new annual participant.

## 6.5. Provisional local transitions

For first contact and dream collapse, waiting for server round-trip can make the first hit or real death incorrect.

Allowed pattern:

1. owner client validates local preconditions;
2. applies the minimum reversible local state;
3. sends report with `eventId`, revision and identities;
4. server validates/deduplicates;
5. server publishes authoritative state;
6. rejection restores from snapshot and must also restore renderers/collision pairs/status cleanly.

The spike must prove this path before production protocol is selected.

---

# 7. Persistence and recovery

Persist while an annual event is active:

```text
schema/protocol version
eventId and frozen schedule
event phase and resolution step
last started/resolved/skipped IDs
participant phase/outcome
stable player ID / last peer UID / Player ZDOID
engagement gate
FirstBloodContactRecord
combat progress/contribution
ejection/recovery state
revision
```

Do not persist position/rotation as an automatic restore anchor.

Use world UID, not only server name. Prefer a small world-bound marker plus an atomically replaced transient sidecar if that matches existing Seasons patterns.

## 7.1. First enable in an existing world

If Blood Moon is first enabled after the current year has already entered Forewarning/final-night window, mark the current annual event `Skipped` and wait for the next year. A debug command may explicitly override this for testing.

## 7.2. Restart during event

On valid snapshot:

1. restore event/participants;
2. compare authoritative time with frozen schedule;
3. remap stable Player IDs after reconnect;
4. clear stale blood entities from another event ID;
5. rebuild groups/context gates;
6. continue valid phase or resolve if forced end passed;
7. never duplicate contact, defeat, kill or reward.

Use reconnect grace so loading peers are not immediately classified as disconnected.

On invalid snapshot:

- log exact cause;
- remove temporary presentation/status/entities/force environment;
- mark annual event resolved/skipped without reward;
- do not replay it that year;
- never move a Player.

---

# 8. Participant lifecycle

## 8.1. Enrollment

- online Players at 18:00 enter `Marked`;
- late join before enrollment freeze joins current annual event;
- after `FreezingEnrollment`, Player is `LateWitness` and does not hold resolution;
- terminal outcome is not reset by reconnect;
- empty server remains open for late join until forced end, but does not spawn without eligible participants.

## 8.2. Active and context gate

At 23:00:

- Player gets Blood Moon presentation;
- context gate is evaluated;
- eligible Player enters `AwaitingContact`;
- interior/dungeon, ship/ocean and visible-boss contexts defer engagement and do not spawn blood enemies for that Player;
- generic attached and mounted contexts follow the spike-specific policy from file `10`.

## 8.3. First contact

Accepted contact includes incoming/outgoing accepted hit, block, parry or fully mitigated hit. It creates:

```text
eventId
stable Player ID
peer UID
Player ZDOID
timestamp
contact kind
blood enemy ZDOID
diagnostic position
```

It transitions exactly once:

```text
AwaitingContact → Fighting
```

The first incoming hit must already use Fighting-layer defensive routing.

## 8.4. Terminal outcomes

Terminal for early resolution:

- `Success` / `GoalReached`;
- `Defeated` / `Ejected`;
- `Disconnected`.

`GoalReached` remains in combat to help while unresolved participants exist.

`Ejected`:

- stays at the same transform and velocity;
- does not create TombStone/respawn;
- returns to ordinary presentation;
- is not targeted by blood enemies;
- cannot re-enter in the first release.

## 8.5. Dream collapse

Owner-side `Character.CheckDeath` is the candidate interception point after health reaches `<= 0` but before `Player.OnDeath`.

For `Fighting`/`GoalReached`:

- suppress `Player.OnDeath`;
- restore current maximum health/stamina/eitr;
- keep food/adrenaline;
- clear damaging DoT effects without removing unrelated buffs;
- set outcome `Defeated`, phase `Ejected`;
- start recovery protection;
- report to server exactly once.

Direct scripted/admin `Player.OnDeath` is not intercepted. Ordinary admin HP reduction naturally follows the CheckDeath path.

## 8.6. Re-entry

None in the first version. Any future re-entry is a separate product decision and must not be prebuilt into the participant state machine beyond allowing a future transition extension.
