# Blood Moon — authoritative state and schedule

Part of `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production implementation is blocked by `09_PREIMPLEMENTATION_DECISIONS_AND_SPIKES.md`. The isolated spike task is `10_PARALLEL_LAYER_RUNTIME_SPIKE.md`.

# 3. Architecture

## 3.1. Server authority

The server is authoritative for:

- `eventId`, frozen schedule and event phase;
- enrollment and participant phase/outcome;
- context-gate eligibility;
- first-contact validation;
- hidden combat groups and spawn budgets;
- blood-enemy identity;
- progress, GoalReached, Defeated and disconnect outcomes;
- early/forced resolution.

A client owner may apply a safe provisional local transition before the round-trip when required to avoid a first-hit or death race, but it must report the event/object identities. The server validates, deduplicates and publishes the final state.

## 3.2. Event phase

```csharp
internal enum BloodMoonEventPhase
{
    Dormant,
    Forewarning,
    Marked,
    Active,
    AutoCompleting,
    Resolving,
    Resolved,
    Skipped
}
```

`Enabled` is a config feature gate, not a phase.

## 3.3. Participant phase

```csharp
internal enum BloodMoonParticipantPhase
{
    None,
    Marked,
    AwaitingContact,
    Fighting,
    GoalReached,
    Ejected,
    Resolved
}
```

Meaning:

- `Marked`: preparation before 23:00;
- `AwaitingContact`: Blood Moon invasion is visible, but ordinary world remains visible/interactable and full blood-only isolation has not started;
- `Fighting`: accepted first blood interaction occurred and full layer is active;
- `GoalReached`: combat progress is 100%; full layer and combat buff remain;
- `Ejected`: personal participation ended and real-world layer is restored;
- `Resolved`: morning outcome is published and temporary state is cleared.

## 3.4. Participant outcome

```csharp
internal enum BloodMoonParticipantOutcome
{
    None,
    Success,
    Defeated,
    Disconnected,
    HiddenAtBase,
    HiddenInWild,
    LateWitness,
    Skipped
}
```

`Defeated` is an illusory combat result. It must not imply or call vanilla `Player.OnDeath`.

A separate withdrawal outcome may be added after the edge/unsupported-context spikes. Do not overload `Defeated` until that decision is made.

## 3.5. Engagement gate

Context is a separate dimension, not another participant phase:

```csharp
internal enum BloodMoonEngagementGate
{
    None,
    Teleporting,
    Interior,
    ShipOrOcean,
    BossEncounter
}
```

The gate controls blood spawn/contact eligibility while preserving participant identity.

## 3.6. Resolution steps

```csharp
internal enum BloodMoonResolutionStep
{
    None,
    FreezingEnrollment,
    StoppingSpawns,
    AwaitingClientFade,
    CleaningEnemies,
    CleaningTemporaryState,
    AdvancingTime,
    PublishingOutcomes,
    ReleasingClients,
    Complete
}
```

Every step is idempotent and restart-safe.

## 3.7. Combat groups

Groups are derived server records, not a `Forming/Merging/Splitting` state machine:

```csharp
internal sealed class BloodMoonCombatGroup
{
    internal long Id;
    internal HashSet<long> PlayerIds;
    internal int AliveEnemies;
    internal int AliveBudget;
}
```

Combat-capable members may include `AwaitingContact`, `Fighting` and `GoalReached`, but only participants without an engagement gate may anchor spawns. `Ejected`, `Resolved` and terminal disconnected players are excluded.

## 3.8. Central interaction policy

All target, damage, local visibility, collision and interaction patches must delegate to one policy boundary. Candidate API:

```csharp
internal static class BloodMoonInteractionRules
{
    internal static bool IsEventEnemy(Character character);
    internal static bool IsFullLayerParticipant(Player player);
    internal static bool CanSee(Player localPlayer, Component entity);
    internal static bool CanCollide(Player localPlayer, Character entity);
    internal static bool CanTarget(Character attacker, Character target);
    internal static bool CanDamage(object source, IDamageable target, HitData hit);
    internal static bool CanInteract(Player player, Component target);
    internal static bool IsFirstContactCandidate(HitData hit, Character attacker, Character target);
}
```

Exact signatures are determined by the spike. Do not maintain separate contradictory allowlists in AI, projectile, melee and damage patches.

## 3.9. Idempotency

The following must be safe on repetition:

- event/participant transition;
- first-contact report;
- Defeated report;
- kill report;
- disconnect callback;
- recovery snapshot;
- local layer application/removal;
- collision-pair restoration;
- fade ACK;
- cleanup;
- stale packet from another `eventId`.

---

# 4. Event identity

Annual event identity is deterministic:

```csharp
long eventId = eventWorldDay;
```

Include it in:

- runtime/persistence state;
- sync/RPC payloads;
- participant and first-contact records;
- blood enemy, projectile, AoE and future Blood Craft markers;
- deduplication and chronicle records.

Reject stale identities.

---

# 5. Calendar and absolute schedule

## 5.1. Autumn days

Defaults:

- Forewarning begins on autumn day 6;
- final Blood Moon night is autumn day 9;
- if final day exceeds actual autumn length, use the final valid autumn night;
- one event per game year.

## 5.2. Final-night times

```text
18:00 — Marked, sleep/random-event suppression, linear red overlay
23:00 — Active; eligible participant → AwaitingContact; force environment
04:15 — default auto-complete start (1.5 in-game hours before end)
05:45 — forced resolution
06:00 — morning target
```

At event creation freeze absolute timestamps derived from authoritative server time and the frozen day length:

```text
visualStartSeconds
combatStartSeconds
autoCompleteStartSeconds
forcedEndSeconds
morningTargetSeconds
```

Do not drive authority from `m_smoothDayFraction`; it is presentation-only.

Time jumps must advance through required transitions or enter safe resolution without replaying an already resolved event.

Calendar config changes during an event apply to the next year.
