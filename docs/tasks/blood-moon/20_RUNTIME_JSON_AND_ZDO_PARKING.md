# Blood Moon runtime JSON and ZDO parking correction

## Status

Implementation task for runtime failures discovered after Blood Moon could enter the gameplay phases.

Target branch: `feat/blood-moon`

Starting point: `ecf818c161b04b70c63dc297421b3bbb002a9497`

This task is authoritative for the corrections described below and supersedes any earlier implication that parked boss transforms or parked boss identifiers must be duplicated in the Blood Moon sidecar JSON.

## 1. Observed failure

The first active-event save failed with:

```text
Newtonsoft.Json.JsonSerializationException: Self referencing loop detected for property 'normalized' with type 'UnityEngine.Vector3'. Path 'Groups.1.Anchor.normalized'.
```

The current serializer reflects all public members of Unity structs. `Vector3.normalized` recursively produces another `Vector3`, so runtime state cannot be saved after a group or spawn lease contains an anchor.

The same implicit-contract approach also exposes computed properties and makes network payloads depend on runtime model shape.

## 2. Serialization boundary requirements

### 2.1. Explicit contracts

All Blood Moon types written to persistence JSON or network JSON must use explicit opt-in contracts.

- Only members intentionally marked for JSON are serialized.
- Computed properties are not serialized.
- Adding a public field or property to a runtime type must not silently change a durable or network schema.
- `ReferenceLoopHandling.Ignore` is prohibited; loop detection remains fail-fast.
- `TypeNameHandling` remains disabled and no `$type`, `$id`, or `$ref` metadata is emitted.

### 2.2. Unity value types

A dedicated JSON converter must serialize `Vector3` only as finite `x`, `y`, and `z` components.

A dedicated JSON converter must serialize `Quaternion` only as finite `x`, `y`, `z`, and `w` components. The converter must not normalize the value or convert it to Euler angles.

Converters must:

- reject missing required components;
- reject non-numeric tokens;
- reject `NaN` and infinity;
- ignore unknown additional members for forward compatibility;
- never recursively call the serializer for the same Unity value type.

### 2.3. Central serializer

Blood Moon JSON operations must use one internal serializer facade with separate persistence and compact network settings.

Direct Blood Moon calls to `JsonConvert.SerializeObject` and `JsonConvert.DeserializeObject` outside that facade must be removed.

Persistence settings retain:

- indented output;
- `ObjectCreationHandling.Replace`;
- invariant culture;
- explicit Unity converters;
- `ReferenceLoopHandling.Error`;
- `TypeNameHandling.None`.

### 2.4. Minimal routing payload

The public participant routing snapshot must serialize only:

- player identity and name;
- participant phase;
- exit reason;
- `GoalReached`;
- `AutoCompleted`;
- `JoinedLate`.

Progress, timestamps, skill dictionaries, fade acknowledgement, and computed properties remain outside the public routing snapshot. Existing runtime consumers may continue to receive `BloodMoonParticipantState` instances through an explicit routing DTO mapping.

### 2.5. Schema compatibility

`BloodMoonStateSchema.Current` remains `1` for this correction.

- Existing dormant schema-1 snapshots remain readable.
- Unknown legacy members are ignored.
- Successfully written active schema-1 snapshots containing reflected `Vector3` members could not have been produced by the failing implementation.
- The component object forms `{x,y,z}` and `{x,y,z,w}` become the canonical schema-1 Unity value representation.

## 3. Parked boss authority requirements

### 3.1. Single durable source

Parked boss recovery data belongs only to the boss ZDO.

The Blood Moon sidecar JSON must not contain:

- parked boss identifiers;
- original boss position;
- original boss rotation;
- original owner or revision diagnostics;
- loaded/restored runtime flags.

Remove `BloodMoonBossParkingState` and `BloodMoonEventState.ParkedBosses`.

Runtime reassertion timers may remain in memory, but they are not durable state.

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

### 3.3. Recovery scan and validation

Recovery scans `ZDOMan.m_objectsByID.Values`; it does not depend on a sidecar list.

For every active parking marker, recovery must validate:

1. the parking schema is supported;
2. original position exists and is finite;
3. original rotation exists and is finite;
4. original prefab hash exists and matches the current ZDO prefab;
5. the resolved prefab contains a `Character` identified as a boss;
6. the current position is classified as the deterministic parking slot, elsewhere beyond the world edge, inside the world, or temporarily unclassifiable.

A non-boss or unverifiable prefab is never moved by generic marker recovery.

Position classification is not a replacement for the marker transaction. It distinguishes a completed park from a crash window:

- matching active event plus a valid record: reassert the deterministic parking position, including when the crash happened before the initial move;
- stale or non-combat event plus a valid record: restore the original transform;
- marker still present after the transform was already restored inside the world: finish marker cleanup idempotently;
- malformed record while still beyond the world edge: preserve it and log an actionable error rather than guessing a transform.

### 3.4. Restore transaction

Restore operates directly on the marked ZDO:

1. take server ownership;
2. clear serialized linear and angular velocity;
3. restore original rotation;
4. restore original position;
5. synchronize a loaded instance and rigidbody;
6. force-send the restored transform;
7. clear parking metadata with the parked event marker committed last;
8. force-send the cleanup.

Recovery and restore must remain idempotent across a crash between any two steps.

### 3.5. Diagnostics

`dump-bosses` reads current marked ZDOs directly and reports at least:

- ZDO ID;
- event ID;
- boss validation result;
- record validity;
- current location classification;
- original position and rotation when readable;
- prefab marker and current prefab.

It must not read a removed sidecar transaction collection.

## 4. Persistence transaction requirements

The persistence save path must serialize the complete JSON string before writing `.new`.

If serialization fails:

- canonical and `.old` snapshots are not rotated;
- no empty temporary file is promoted;
- the exception remains visible with the JSON member path.

Load continues to evaluate canonical, `.new`, and `.old` candidates independently and only normalizes a fully deserialized candidate.

## 5. Implementation scope

Expected code changes include:

- add a central Blood Moon JSON serializer and Unity converters;
- opt in all persisted and network snapshot members;
- add an explicit participant routing surrogate;
- replace direct Blood Moon `JsonConvert` calls;
- remove parked boss data from `BloodMoonEventState` and persistence normalization;
- store and restore original rotation through a ZDO marker;
- refactor parking, restore, recovery, and diagnostics to operate directly on ZDO records;
- preserve state schema `1` and network protocol `2` unless implementation reveals an actual wire incompatibility.

Do not change plugin version, public README, Thunderstore changelog, manifest, or packaging.

Do not create another branch.

## 6. Acceptance criteria

The correction is complete when static review confirms:

- active state containing non-zero group and lease anchors serializes without reflected Unity properties;
- JSON output contains no `normalized`, `magnitude`, `sqrMagnitude`, `eulerAngles`, computed state properties, or type/reference metadata;
- persistence and every Blood Moon JSON network path use the central facade;
- public routing JSON contains only the approved routing fields;
- sidecar state contains no parked boss collection or boss transform;
- parking writes both original position and original rotation to the boss ZDO;
- restart recovery can rediscover marked bosses solely by scanning ZDOs;
- stale marked bosses restore position and rotation from their own ZDO record;
- malformed or non-boss marked ZDOs are not moved blindly;
- diagnostics report ZDO-backed parking records;
- no accidental Cyrillic text is introduced into source files or this task file.

Per project policy, do not build or launch the Valheim mod in this environment. Runtime verification is performed in the game after the commits are delivered.
