# Blood Moon sleep DreamText and input policy

## Status

Authoritative correction discovered during owner-side runtime testing on 2026-08-26.

Target branch: `feat/blood-moon`.

Code correction:

```text
d20f332147cdd3d9bde82146a2c8450d2fac3496
fix: defer Blood Moon dreams to vanilla sleep UI
```

This document supersedes earlier wording in `04_COMBAT_PROGRESS_AND_RESOLUTION.md` and `13_IMPLEMENTATION_REPORT.md` that described an immediate/forced DreamText presentation, a presentation handshake, or Blood Moon-owned input release around that presentation.

## 1. Observed runtime regression

Owner-side testing showed that ordinary configured Valheim actions such as movement, attack, and inventory still worked, while number-row hotkeys (`1` through `8`) and mod-defined hotkeys read through `ZInput` stopped working.

The regression came from Blood Moon patching `Player.TakeInput` and forcing its result to `false` while the resolution/dream guard was active.

That was too broad. `Player.TakeInput` is a gameplay input gate used by vanilla control processing, but independent vanilla/mod hotkey consumers may read `ZInput` directly. Blood Moon must not globally redefine that gate for presentation purposes.

## 2. Input policy

Blood Moon does not own gameplay input and does not patch `Player.TakeInput`.

Requirements:

- no Blood Moon patch may globally suppress `Player.TakeInput` for fade, DreamText, chronicle, or outcome delivery;
- number-row hotkeys remain vanilla behavior;
- mod-defined `ZInput` hotkeys remain available according to their own mod/UI rules;
- resolution fade is visual-only;
- ordinary game UI remains responsible for its own normal input blocking;
- any future Blood Moon interaction restriction must be scoped to the exact interaction being restricted, not implemented as a global input gate.

`BloodMoonFadeInputGuard` currently remains only as a compatibility no-op for existing internal call sites; it contains no Harmony patch and cannot suppress input.

## 3. Vanilla DreamText evidence

Verified first against:

```text
repository: https://github.com/shudnal/assemblies_combined
commit: cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e
files: assembly_valheim/SleepText.cs, assembly_valheim/DreamTexts.cs
```

Current vanilla flow:

1. `SleepText.OnEnable()` starts the ordinary sleep presentation.
2. It schedules `ShowDreamText` after 4 seconds.
3. `ShowDreamText()` obtains one dream, writes `m_dreamField`, enables it, then uses the normal 1.5-second fade-in and 6.5-second hide scheduling.
4. `DreamTexts.GetRandomDreamText()` is only used by this sleep DreamText path in current game source.

Therefore Blood Moon does not need a separate cloned `SleepText`, black overlay, custom presenter MonoBehaviour, input guard, or immediate morning presentation.

## 4. Correct Blood Moon DreamText lifecycle

Outcome delivery stores a profile-backed pending Blood Moon dream for `(worldUid, eventId)`.

It does **not** display that dream immediately when resolution publishes outcomes.

At the next ordinary sleep:

1. vanilla `SleepText.OnEnable()` runs normally;
2. vanilla reaches `SleepText.ShowDreamText()` at its normal time;
3. if the local profile has a pending Blood Moon dream for the current world, Blood Moon supplies the oldest pending event dream;
4. the existing vanilla `m_dreamField`, `DelayedCrossFadeStart`, and `HideDreamText` timing are used;
5. that pending record is marked presented and removed;
6. normal profile persistence/acknowledgement continues;
7. if several Blood Moon dreams are pending, only one is consumed per ordinary sleep, oldest event first.

If no Blood Moon dream is pending, vanilla random DreamText behavior is untouched.

## 5. Resolution and outcome queue

Dream presentation is no longer a resolution dependency.

The server must not wait for DreamText presentation start/completion before releasing the Blood Moon resolution state.

The existing durable outcome queue may retain an outcome until its profile-backed presentation/application evidence is acknowledged, but that durability mechanism must not hold the world in `Resolving` until the player sleeps.

The old `BloodMoonOutcomePresentationHandshake` remains temporarily as an inert compatibility shell for existing controller call sites; it registers no RPC and `CanRelease` always succeeds. It should be removed when the controller cleanup is next edited directly.

## 6. Runtime acceptance

Owner-side verification should confirm:

- movement/attack/inventory still work normally;
- number-row hotkeys `1` through `8` work normally;
- mod-defined hotkeys obtained through `ZInput` work normally;
- Blood Moon resolution does not create a forced DreamText screen;
- Blood Moon does not patch `Player.TakeInput`;
- the next ordinary sleep uses the normal vanilla sleeping screen and timing;
- one pending Blood Moon DreamText replaces that sleep's random dream;
- subsequent sleeps consume additional pending Blood Moon dreams one at a time;
- with no pending Blood Moon dream, ordinary vanilla dreams remain unchanged.

Per project policy, the assistant does not build or launch the Valheim mod. Local Visual Studio compilation and in-game runtime verification remain owner-side acceptance checks.
