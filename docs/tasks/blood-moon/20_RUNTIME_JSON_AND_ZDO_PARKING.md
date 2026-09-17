# Blood Moon runtime JSON and ZDO parking correction

## Status

Implementation task for runtime failures discovered after Blood Moon could enter the gameplay phases.

Target branch: `feat/blood-moon`

Starting point: `ecf818c161b04b70c63dc297421b3bbb002a9497`

Implementation commits:

- `3d13562e7e834cb8112df3ae07668c4c0898c701` — task and requirements;
- `cb39053e79f4c05d8f2e46dc9c3704f3ef7eddca` — explicit Blood Moon JSON contracts, Unity converters, ZDO-backed boss recovery;
- `11c54fc99ead162080c21446cea992eae3b9fb31` — full parking-record reassertion during ownership transfer;
- `2cf145bbc9023ceb2f04a83b7732db177486a312` — direct ZDO diagnostics and runtime-only diagnostic projection.

Code implementation is complete under static review. Runtime acceptance remains to be performed in Valheim from the branch head produced after this document update.

This task is authoritative for the corrections described below and supersedes any earlier implication that parked boss transforms or parked boss identifiers must be duplicated in the Blood Moon sidecar JSON.

## 1. Observed failure

The first active-event save failed with:

```text
Newtonsoft.Json.JsonSerializationException: Self referencing loop detected for property 'normalized' with type 'UnityEngine.Vector3'. Path 'Groups.1.Anchor.normalized'.
```

The former serializer reflected all public members of Unity structs. `Vector3.normalized` recursively produces another `Vector3`, so runtime state could not be saved after a group or spawn lease contained an anchor.

The same implicit-contract approach also exposed computed properties and made network payloads depend on runtime model shape.

## 2. Serialization boundary requirements

### 2.1. Explicit contracts

All Blood Moon types written to persistence JSON or network JSON must use explicit contracts.

- Public Blood Moon DTO/state types use `[JsonObject(MemberSerialization.OptIn)]` and `[JsonProperty]`.
- Private nested persistence/transport types are covered by an explicit field allowlist in the Blood Moon contract resolver.
- Only intentionally selected members are serialized.
- Computed properties are not serialized.
- Adding a public field or property to a runtime type must not silently change a durable or network schema.
- `ReferenceLoopHandling.Ignore` is prohibited; loop detection remains fail-fast.
- `TypeNameHandling` remains disabled and no `$type`, `$id`, or `$ref` metadata is emitted.

### 2.2. Unity value types

A dedicated JSON converter serializes `Vector3` only as finite `x`, `y`, and `z` components.

A dedicated JSON converter serializes `Quaternion` only as finite `x`, `y`, `z`, and `w` components. The converter does not normalize the value or convert it to Euler angles.

Converters must:

- reject missing required components;
- reject non-numeric tokens;
- reject `NaN` and infinity;
- ignore unknown additional members for forward compatibility;
- never recursively call the serializer for the same Unity value type.

Finite-number converters also reject non-finite standalone `float` and `double` values anywhere in Blood Moon JSON contracts.

### 2.3. Central serializer

Blood Moon JSON operations use one internal serializer facade with separate persistence and compact network settings.

No Blood Moon code calls `Newtonsoft.Json.JsonConvert` directly outside `BloodMoonJson`. Existing unqualified `JsonConvert` call sites are routed through a namespace-local compatibility facade so every current persistence/network path uses the controlled settings while unrelated Seasons JSON remains unchanged.

Persistence settings retain:

- indented output;
- `ObjectCreationHandling.Replace`;
- invariant culture;
- explicit Unity converters;
- `ReferenceLoopHandling.Error`;
- `TypeNameHandling.None`;
- ignored unknown members for schema-1 forward/legacy tolerance;
- bounded serializer depth.

### 2.4. Minimal routing payload

The public participant routing snapshot serializes only:

- player identity and name;
- participant phase;
- exit reason;
- `GoalReached`;
- `AutoCompleted`;
- `JoinedLate`.

Progress, timestamps, skill dictionaries, fade acknowledgement, and computed properties remain outside the public routing snapshot. Existing runtime consumers continue to receive `BloodMoonParticipantState` instances through an explicit routing surrogate mapping.

### 2.5. Schema compatibility

`BloodMoonStateSchema.Current` remains `1` for this correction.

- Existing dormant schema-1 snapshots remain readable.
- Unknown legacy members are ignored.
- Successfully written active schema-1 snapshots containing reflected `Vector3` members could not have been produced by the failing implementation.
- The component object forms `{x,y,z}` and `{x,y,z,w}` are the canonical schema-1 Unity value representation.

## 3. Parked boss authority requirements

### 3.1. Single durable source

Parked boss recovery data belongs only to the boss ZDO.

The Blood Moon sidecar JSON must not contain:

- parked boss identifiers;
- original boss position;
- original boss rotation;
- original owner or revision diagnostics;
- loaded/restored runtime flags.

The former durable `BloodMoonBossParkingState` and serialized `BloodMoonEventState.ParkedBosses` collection are removed.

A compatibility-only `[JsonIgnore]` accessor may remain on `BloodMoonEventState` for existing status output. It returns an `IReadOnlyDictionary<string, BloodMoonBossParkingDiagnostic>` rebuilt from current ZDO markers on every access. The accessor and diagnostic objects must not own, cache, or persist recovery data.

Runtime ownership-transfer records and reassertion timers may remain in memory for a bounded confirmation window, but they are not durable state and are cleared on world/session reset.

### 3.2. Required ZDO markers

Before moving a boss, the server records on the same ZDO:

- parking schema;
- parked event ID;
- original position;
- original rotation;
- original prefab hash;
- parking timestamp.

The parked event marker activates the record only after the other required values have been written.

The server then takes ownership, clears serialized velocity, moves the same persistent ZDO to its deterministic far position, synchronizes a loaded listen-host instance when present, and force-sends the ZDO.

During the ownership-transfer confirmation window, the server reasserts the full marker record, ownership, zero velocity, and deterministic parking position. This closes the race in which a late packet from the former owner could otherwise restore an older ZDO payload and erase some or all parking markers.

### 3.3. Recovery scan and validation

Recovery scans `ZDOMan.m_objectsByID.Values`; it does not depend on a sidecar list.

For every active parking marker, recovery validates:

1. the parking schema is supported;
2. original position exists and is finite;
3. original rotation exists and is finite;
4. original prefab hash exists and matches the current ZDO prefab;
5. the ZDO is persistent;
6. the resolved prefab contains a `Character` identified as a boss;
7. the current position is classified as the deterministic parking slot, elsewhere beyond the world edge, inside the world, or temporarily unclassifiable.

A non-boss or unverifiable prefab is never moved by generic marker recovery.

Position classification is not a replacement for the marker transaction. It distinguishes a completed park from a crash window:

- matching active event plus a valid record: reassert the deterministic parking position, including when the crash happened before the initial move;
- stale or non-combat event plus a valid record: restore the original transform;
- marker still present after the transform was already restored inside the world: finish marker cleanup idempotently;
- malformed record while still beyond the world edge or temporarily unclassifiable: preserve it and log an actionable error rather than guessing a transform;
- malformed marker on a verified boss already inside the world: clear the incomplete marker without moving the boss;
- marker on a non-boss or unverifiable prefab: preserve it and report the validation failure.

Schema-1 parking records are upgraded in place to schema 2. Schema 1 never altered boss rotation while parking, so the current ZDO rotation is copied into the new original-rotation marker during that upgrade.

### 3.4. Late recovery

The controller performs normal server recovery when Blood Moon state is loaded. Boss recovery additionally retries after `ZNetScene.Awake`, because prefab validation is not possible before the named prefab registry exists.

The late recovery helper:

- runs only on server authority;
- waits until Blood Moon state, `ZDOMan`, and `ZNetScene` are available;
- performs one complete marked-ZDO scan;
- destroys itself after successful recovery.

### 3.5. Restore transaction

Restore operates directly on the marked ZDO:

1. take server ownership;
2. clear serialized linear and angular velocity;
3. restore original rotation;
4. restore original position;
5. synchronize a loaded instance and rigidbody;
6. force-send the restored transform;
7. clear non-activating parking metadata and force-send it;
8. clear the parked event marker last and force-send the cleanup.

Recovery and restore remain idempotent across a crash between any two steps.

### 3.6. Diagnostics

`dump-bosses` reads current marked ZDOs directly and reports:

- ZDO ID;
- event ID;
- boss validation result;
- record validity;
- validation error;
- current location classification;
- original position and rotation when readable;
- original prefab marker and current prefab;
- current owner, owner revision, and data revision;
- whether the object is currently loaded.

The diagnostic projection is not a transaction collection and is never serialized.

## 4. Persistence transaction requirements

The persistence save path serializes the complete JSON string before creating or overwriting `.new`.

If serialization fails:

- canonical and `.old` snapshots are not rotated;
- no empty temporary file is promoted;
- the exception remains visible with the JSON member path.

Load continues to evaluate canonical, `.new`, and `.old` candidates independently and only normalizes a fully deserialized candidate.

## 5. Implemented scope

The implementation includes:

- central Blood Moon JSON settings and Unity converters;
- explicit opt-in/allowlist contracts for persisted and network JSON types;
- finite `float`, `double`, `Vector3`, and `Quaternion` validation;
- explicit participant routing surrogate;
- central routing of every Blood Moon JSON call path;
- removal of durable parked boss data from `BloodMoonEventState` and persistence normalization;
- original position and rotation stored on the boss ZDO;
- ZDO-only parking, restore, recovery, legacy upgrade, and diagnostics;
- ownership-transfer reassertion of the complete marker record;
- late recovery after `ZNetScene` initialization;
- state schema `1` and network protocol `2` retained.

Plugin version, public README, Thunderstore changelog, manifest, and packaging are unchanged.

No additional branch was created.

## 6. Static acceptance result

Static review confirms:

- active state anchors are handled by the explicit `Vector3` converter rather than reflected Unity properties;
- JSON contracts exclude `normalized`, `magnitude`, `sqrMagnitude`, `eulerAngles`, computed state properties, and type/reference metadata;
- persistence and Blood Moon JSON network paths route through the central facade;
- public routing JSON contains only the approved routing fields;
- sidecar state contains no parked boss collection or boss transform;
- parking writes original position and original rotation to the boss ZDO;
- restart recovery discovers marked bosses solely by scanning ZDOs;
- stale marked bosses restore position and rotation from their own ZDO record;
- malformed or non-boss marked ZDOs are not moved blindly;
- diagnostics report ZDO-backed parking records;
- source and task changes contain no Cyrillic text.

Per project policy, the mod was not built or launched in this environment.

## 7. Required runtime acceptance

Run from the resulting `feat/blood-moon` head in Valheim:

1. Enter an event with at least one populated group and spawn lease. Confirm the sidecar saves without `normalized` errors.
2. Inspect the JSON. Confirm anchors contain only `x`, `y`, `z` and no computed Unity/state properties or `$type`/`$id`/`$ref` metadata.
3. Execute `seasons bloodmoon dump-sync`. Confirm participant routing does not expose progress, timestamps, skill dictionaries, or fade acknowledgement.
4. Park an outdoor persistent boss. Execute `seasons bloodmoon dump-bosses` and confirm a valid boss/record, deterministic parking location, original position/rotation, and matching prefab hashes.
5. End or resolve the event. Confirm the boss returns to the exact original position and rotation and its parking marker disappears.
6. Restart while a boss is parked and the event remains combat-live. Confirm recovery rediscovers the marker and keeps the boss parked.
7. Restart after making the marked event stale or non-combat. Confirm recovery restores the boss from its ZDO record.
8. Exercise the schema-1 marker upgrade with a pre-correction parked ZDO if such a world backup exists. Confirm schema 2 and original rotation are written before recovery continues.
9. Corrupt or remove one required marker on a parked far-world boss. Confirm the boss is preserved and an actionable error is logged instead of moving it to a guessed position.
10. Confirm canonical, `.new`, and `.old` state recovery still selects the newest valid complete snapshot when one candidate is malformed.
