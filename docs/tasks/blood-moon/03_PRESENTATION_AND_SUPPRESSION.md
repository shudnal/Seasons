# Blood Moon — presentation, context and suppression

Part of `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production implementation is blocked by files `09` and `10`.

# 9. Forewarning and Marked

## 9.1. Forewarning nights

Text alone is insufficient. Every configured Forewarning night must have at least one in-world non-text signal:

- low-intensity Blood Moon environment overlay and/or cloud VFX;
- night-only;
- strength grows toward the final autumn night;
- no force environment;
- no blood enemies, combat modifiers, SoftDeath or sleep block;
- no detailed tutorial.

A rare DreamText may complement but not replace the visual signal. A confusing or unsuccessful first annual encounter is an acceptable Valheim outcome; the player can learn for the next cycle.

## 9.2. Marked at 18:00

At Marked entry:

1. enroll current Players;
2. stop/suppress ordinary random events;
3. block sleep;
4. add/update Blood Moon status;
5. begin linear red transition;
6. publish authoritative state;
7. log transition once.

The status explains:

- combat begins at 23:00;
- sleep is unavailable;
- defeat is safe from skill loss;
- future Blood Craft is not advertised until actually implemented.

---

# 10. Active presentation and context gates

## 10.1. Supported outdoor context

At 23:00 eligible Player enters `AwaitingContact`:

- blood enemies become visible and may target the Player;
- ordinary world remains visible/interactable until accepted first contact;
- first contact switches to `Fighting` and full blood-only layer;
- no transform change.

## 10.2. Interior/dungeon

- show Blood Moon redness/atmosphere;
- use the selected forced-environment presentation only if it does not break interior rendering;
- no blood enemy spawn;
- no first contact/full layer;
- re-evaluate after returning outdoors before forced end.

## 10.3. Ship/ocean

- show redness/forced environment;
- no blood enemies/full layer;
- do not detach Player or move ship;
- re-evaluate on supported land.

## 10.4. Generic attached

- do not force detach;
- may remain `AwaitingContact`;
- first blood hit may enter `Fighting` without changing attach state;
- runtime spike must verify collision/animation behavior.

## 10.5. Mounted

Do not force `StopDoodadControl`: vanilla saddle release eventually calls `AttachStop`, which moves Player to the mount’s detach offset.

Preferred experiment is a `BloodBoundMount` bridge:

- rider or current mount receiving a blood hit enters rider into Fighting;
- mount takes zero blood damage;
- mount remains visible/controllable to rider;
- blood enemies target rider only;
- ordinary enemies cannot attack mount while it is bridged;
- mount does not attack blood enemies or generate progress;
- voluntary dismount releases bridge.

If unreliable, mounted Player remains engagement-deferred until voluntary dismount. Do not silently introduce forced movement.

## 10.6. Active boss encounter

Local product signal:

```csharp
EnemyHud.instance != null && EnemyHud.instance.ShowingBossHud()
```

While true:

- defer blood enemy spawn/contact/full layer;
- leave boss combat and bookkeeping intact;
- preserve boss environment/music where possible and apply only compatible red overlay;
- report context to server only as a low-trust delay signal.

After boss HUD stays absent for a short stability delay, re-evaluate eligibility.

## 10.7. Edge of world

Before vanilla tidal/edge death becomes relevant, eject/withdraw Player based on:

```csharp
ZoneSystemVariantController.IsBeyondWorldEdge(position, safetyOffset)
```

Do not wait for `HitData.HitType.EdgeOfWorld`. The exact non-defeat outcome name remains open until the spike report.

---

# 11. RandEventSystem suppression

Blood Moon is not a `RandomEvent`.

From 18:00 until resolution:

- stop current ordinary random event through vanilla methods where possible;
- block new ordinary/standalone random events;
- prevent ordinary random-event env/music override from defeating Blood Moon;
- do not corrupt boss/event-zone bookkeeping;
- restore vanilla behavior idempotently at cleanup without forcing an immediate raid.

Patches are active only while authoritative suppression is true and must clear on disable/world unload/errors.

No map circles or event markers.

---

# 12. Environment and VFX

## 12.1. Blood Moon environment

Clone vanilla `Fader` without mutating the original. Give the copy a unique internal name.

For every `Color` field:

```csharp
color.r = 1f;
```

Set:

```csharp
m_windMin = 1f;
m_windMax = 2f;
m_sunAngle = 70f;
```

Values have already been checked in game by the owner.

## 12.2. Overlay 18:00–23:00

Linear factor:

```text
18:00 = 0
23:00 = 1
```

Integrate into existing transient `EnvMan.SetEnv` pipeline:

```text
original environment
→ existing seasonal luminance
→ Blood Moon target blend
→ vanilla SetEnv
→ restore original fields
```

The overlay must not depend on `controlLightings` or texture controllers. Save/restore wind and sun-angle fields too. Avoid reflection in the hot path.

## 12.3. Forced environment after 23:00

In supported Active contexts, use the custom forced environment until resolution. Implement lease/ownership:

- remember previous force;
- clear/restore only if current force still equals Blood Moon’s value;
- do not overwrite another mod’s later force change.

Boss context is the exception: preserve boss readability/bookkeeping and use compatible overlay until boss HUD disappears.

## 12.4. Clouds

Clone vanilla `Ashlands_FaderFX`; keep `cloud` and `cloud (1)` visual branches. Use `ParticleSystem.main.startColor`, set red channel to `1f`, and scale saved emission by current visual factor. Stop/clear under fade. Do not search/create every frame.

Final music/SFX are deferred.

---

# 13. Status presentation

Blood Moon status must distinguish:

- `Marked` — countdown and sleep block;
- `AwaitingContact` — invasion visible, first accepted contact begins personal fight;
- `Fighting` — progress and modifiers;
- `GoalReached` — 100%, full buff remains, help others;
- `Ejected` — blood combat ended, ordinary world restored;
- recovery grace — full protection until stabilized, then 10 seconds at 75% reduction.

Add vanilla `SoftDeath` during active combat only as a familiar visual indication. It is not the mechanism preventing skill loss. Dream collapse avoids `Player.OnDeath` entirely.
