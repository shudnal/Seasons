# Blood Moon: Ashlands / Deep North presentation policy

## Status

Accepted product decision for `feat/blood-moon` / PR #42.

This document is authoritative for the interaction between Blood Moon presentation and the late-game biomes `AshLands` and `DeepNorth`. It supplements the presentation contract in `03_PRESENTATION_AND_SUPPRESSION.md`, `05_SEASONS_INTEGRATION.md`, `23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md`, and the project-conformance matrix.

## Decision

Blood Moon remains a **global gameplay event** in Ashlands and Deep North, but its **environmental/atmospheric visual overrides are disabled** while the local player is in either of those biomes.

Ashlands and Deep North deliberately retain their native/fixed visual-weather identity. Blood Moon must not replace that identity merely because the event is globally active.

The biome exclusion is presentation-only. It does **not** create a new participant state and it is not a safe zone from Blood Moon gameplay.

## Gameplay that remains active in Ashlands / Deep North

Crossing into either excluded biome does not change or reset any of the following:

- enrollment and participant phase (`Marked`, `Fighting`, `GoalReached`, terminal outcomes);
- `SE_BloodMoon` status and its progress/icon text;
- Bloodlust damage, movement and lifesteal rules;
- Blood enemy classification, targeting and damage routing;
- combat progress, skill accounting and rewards;
- Blood Craft eligibility/lifetime rules;
- additional-enemy spawning/caps/leases;
- boss-offering restrictions and boss handling;
- Defeated / recovery semantics;
- resolution/outcome persistence.

The server state machine does not know or care that a participant crossed an Ashlands/Deep North border.

## Environmental presentation that is suppressed

While the local presentation biome is `AshLands` or `DeepNorth`, Blood Moon must not apply atmospheric overrides on top of the biome's native environment. This includes the current and planned presentation layer:

- the `Seasons_BloodMoon` forced environment;
- Blood Moon color interpolation over `EnvSetup`;
- the retained Fader-derived red fog/cloud particle layer;
- the Forewarning/Marked red screen/environment tint driven by the Blood Moon visual factor;
- future Blood Moon moon-color override;
- future Blood Moon moon-ray / directional-ray override;
- any future atmospheric override that is conceptually part of Blood Moon environment presentation.

UI/transition presentation that is not an environmental override is not covered by this exclusion. In particular, the Blood Moon status icon remains visible, and the resolution fade remains an event transition rather than biome weather.

## Forced environment role

`Seasons_BloodMoon` is not the complete Blood Moon visual design. Its intended role is primarily to suppress ordinary transient weather such as rain/snow during the active event and to provide the retained red-fog/cloud basis inherited from the Fader environment.

Additional presentation (for example moon color and rays) is expected to be implemented as Blood Moon-owned overrides on top of the ordinary `EnvMan` presentation. Those future overrides must use the same late-biome presentation gate defined here rather than adding independent biome checks.

## Boundary behavior

The transition is local, reversible and stateless with respect to gameplay.

### Entering Ashlands / Deep North

If Blood Moon atmospheric presentation is currently active:

1. release only the Blood Moon-owned forced-environment lease;
2. restore the force environment displaced by Blood Moon, if any; otherwise allow native biome weather/environment selection to resume;
3. set Blood Moon environmental visual factor to zero;
4. disable/clear Blood Moon red fog/cloud particles;
5. stop applying Blood Moon `EnvSetup` color/wind/sun-angle interpolation;
6. future moon/ray overrides must likewise restore native values.

Participant state, status, Bloodlust and combat continue unchanged.

### Leaving Ashlands / Deep North

If the same Blood Moon phase still requires environmental presentation:

1. recompute the current visual factor from the existing event schedule/phase;
2. if Active/AutoCompleting/early Resolving requires weather suppression, reacquire `Seasons_BloodMoon`;
3. resume Blood Moon red fog/color presentation at the **current** factor rather than restarting an animation from zero;
4. future moon/ray overrides must resume from the current Blood Moon presentation state.

No server RPC, participant transition, progress reset or re-enrollment is required.

## Runtime biome source

The implementation uses the local `EnvMan` current biome as the presentation authority, with local Player biome only as a readiness fallback. Current Valheim `EnvMan.FixedUpdate` computes the biome independently of `m_forceEnv`, so crossing the biome border remains observable while `Seasons_BloodMoon` is forced.

`Heightmap.Biome` is a `[Flags]` enum, therefore the exclusion is tested as a mask (`AshLands | DeepNorth`) rather than assuming only one exact enum value.

Game-source verification for this decision used `shudnal/assemblies_combined@cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e`, especially `EnvMan.cs` and `Heightmap.cs`.

## Implementation contract

`BloodMoonPresentationPolicy.AllowsEnvironmentalOverridesForCurrentBiome()` is the single client-side gate for Blood Moon atmospheric presentation.

Current code must satisfy both defenses:

- presentation orchestration releases/reacquires the Blood Moon forced environment when the gate changes;
- individual environmental paths (`AcquireForcedEnvironment`, `GetVisualFactor`/`ApplyOverlay`) also respect the gate so a caller cannot accidentally re-enable late-biome Blood Moon visuals.

Future moon/ray/environment presentation code must call the same policy rather than inventing a separate Ashlands/Deep North rule.

## Acceptance scenarios

Owner-side runtime verification should include at least:

1. Marked in a normal biome -> enter Ashlands -> red tint/fog disappears, `SE_BloodMoon` remains and preparation restrictions remain.
2. Active in a normal biome with rain/snow suppressed -> enter Ashlands -> native Ashlands environment resumes while combat/Bloodlust/status continue.
3. Active in Ashlands -> exit to a normal biome -> `Seasons_BloodMoon` and current Blood Moon visual factor resume without re-enrollment or progress reset.
4. Repeat the same two-way boundary crossing for Deep North.
5. GoalReached player crosses both directions -> status remains 100%, Bloodlust remains full while combat-active, only atmospheric presentation toggles.
6. Defeated/recovery near a boundary -> recovery/status semantics are unchanged by biome; only Blood Moon atmospheric presentation follows this policy.
7. Resolution begins while inside an excluded biome -> no Blood Moon environment is forced there; resolution fade/outcome flow still operates.
8. Cross out of an excluded biome during early Resolving before `RestoringWorldSystems` -> Blood Moon environment may resume because that phase still owns atmospheric presentation; after `RestoringWorldSystems` it must not reacquire.
9. Verify future moon-color/ray overrides against the same boundary scenarios when those visual corrections are implemented.

## Non-goals

This decision does not exclude Ashlands/Deep North from the Blood Moon event itself, does not make them shelters, and does not add biome-dependent group/spawn/reward semantics.
