# Blood Moon — parallel layer and Blood Craft contracts

Part of `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

> Production implementation is blocked by runtime spike files `09` and `10`.

# 22. Parallel-world contract

## 22.1. Layer classifications

Candidate centralized classifications:

```csharp
RealWorld
SharedPlayer
BloodAwaitingContact
BloodParticipant
BloodEnemy
BloodBoundMount
```

- `BloodAwaitingContact`: blood invasion is visible, but ordinary world remains visible until first accepted contact;
- `BloodParticipant`: full blood-only isolation for `Fighting`/`GoalReached`;
- `SharedPlayer`: players remain visible to both worlds;
- `BloodBoundMount`: rider’s currently mounted creature retained only as a movement bridge during full layer.

All local and network paths use one policy:

```text
CanSee
CanCollide
CanTarget
CanDamage
CanInteract
```

## 22.2. Interaction matrix after first contact

| Source | Target | Result |
|---|---|---|
| Fighting/GoalReached Player | blood enemy current event | visible, collidable and damage allowed |
| Fighting/GoalReached Player | ordinary enemy, boss, tamed | locally hidden/noninteractive, damage forbidden |
| blood-layer Player attack | building, crop, tree, ore, ordinary destructible | geometry remains, damage/resource action forbidden |
| blood-layer Player | another Player | visible; damage only via future optional PvP |
| blood enemy | current-event Fighting/GoalReached Player | target/damage allowed |
| blood enemy | AwaitingContact Player | may target/accepted first hit; first hit switches Player before resolution |
| blood enemy | Ejected/nonparticipant/ordinary world | target/damage forbidden |
| observer client | blood enemy | hidden, noncolliding with local Player/projectiles, no damage |
| ordinary enemy | blood-layer Player | target/damage forbidden |
| ordinary enemy | real-world actors | background/suspension policy decides normal simulation |

Observers still see participant Player fighting apparently empty space.

## 22.3. Do not disable network root

A client that must not see an entity may still own/simulate its ZDO for another peer.

Do not use `GameObject.SetActive(false)` or disable AI/root as the visibility solution.

Control separately:

- character visual/LOD;
- audio;
- EnemyHud/name;
- local Player ↔ Character body collision;
- hitboxes;
- melee/projectile/AoE candidate filtering;
- target selection;
- final damage;
- hover/interaction.

## 22.4. Collision transparency

`Physics.IgnoreCollision` for body colliders is insufficient. Hidden Character hitboxes can still block attacks.

Required paths:

- melee candidate filtering;
- area attack filtering;
- projectile hit filtering that continues to the next raycast hit after an incompatible hidden collider;
- AoE filtering before damage/status/stagger;
- final damage guard;
- event/layer attribution stored when projectile/AoE is created.

## 22.5. Ownership

Do not mass-call `SetOwner(0)`:

- vanilla `ReleaseNearbyZDOS` reassigns persistent ownerless ZDOs to active peers;
- owner revisions churn;
- owner-targeted RPC become ambiguous;
- nonpersistent modded objects risk orphan behavior.

Visibility is independent of ownership.

Baseline:

- retain current owner;
- participant or observer owner simulates entity;
- local presentation follows local layer;
- optional direct transfer to an eligible observer is an optimization, not a requirement.

## 22.6. Ordinary-world simulation

### Baseline

Ordinary world continues vanilla simulation but cannot see/damage blood-layer Player and is locally hidden from that Player.

### Conditional suspension experiment

Only after baseline isolation works:

- if ordinary Character is owned by a blood participant;
- and no ready real-world witness is within active area/witness radius;
- retain owner but suspend narrow AI movement/target acquisition;
- resume on witness appearance/ejection/resolve;
- exclude `BloodBoundMount`;
- use group interaction radius plus hysteresis.

`AwaitingContact` and `Ejected` count as real-world witnesses. This is not a full simulation freeze: physics, status timers and other components may continue.

Production fallback is background simulation if suspension is fragile.

---

# 23. Mounted bridge

Forced dismount is not accepted as the primary approach. Vanilla saddle release calls `AttachStop`, which moves the Player to mount detach offset.

Preferred spike:

- identify current saddle through `Player.GetDoodadController() is Sadle`;
- accepted blood hit on rider or current mount contacts rider;
- zero blood damage to mount;
- rider enters Fighting without dismount;
- mount remains visible/controllable to rider;
- observers see ordinary rider+mount;
- blood enemies target rider, not mount;
- ordinary enemies do not target/damage bridged mount;
- mount cannot damage blood enemies or grant progress;
- voluntary dismount releases bridge and ordinary mount becomes hidden/noninteractive for still-fighting Player.

If this cannot be isolated safely, mounted context remains deferred until voluntary dismount. Do not hide failure behind a forced transform change.

---

# 24. Blood Craft contract

Blood Craft is temporary build experimentation, not reward.

- available during Marked and authorized Blood Moon phases;
- only already known recipes;
- free temporary weapons, armor, trinkets, ammo and allowed consumables;
- no event currency/drop loop;
- temporary items do not break;
- free upgrade only for temporary item;
- normal item cannot be upgraded free;
- remaining temporary items/ammo/consumables are removed;
- effects of already consumed food/mead may remain.

## 24.1. Craft UI

### Custom tabs

If active tab is not vanilla Craft or Upgrade, do nothing.

### Upgrade

- do not duplicate list;
- highlight rows whose `RecipeDataPair.ItemData` is Blood Craft;
- red Upgrade button for selected temporary item;
- free upgrade;
- preserve marker/owner/event ID;
- normal item remains vanilla.

### Craft

- after vanilla known recipe list, add runtime Blood Craft clone for eligible recipe;
- keep original permanent recipe;
- clone has separate identity/localized marker and muted-red row/button;
- clone requirements are free;
- never mutate shared `Recipe`;
- clear clones on rebuild/close/unload;
- actual craft path validates clone identity, not UI color.

## 24.2. Marker and inventory invariant

```text
Seasons.BloodCraft.Schema
Seasons.BloodCraft.EventId
Seasons.BloodCraft.OwnerPlayerId
```

> A Blood Craft item may exist only in its owner Player’s supported inventory and only for its event ID.

- `ItemDrop.Awake` destroys a dropped temporary item;
- `Interactable.UseItem` rejects using it on world objects/stations/stands;
- player equip/fire/eat/drink remains allowed;
- clear stale markers on inventory load;
- reject container/ship storage/item stand/armor stand/trade/external inventory;
- dream collapse creates no TombStone; fallback vanilla death removes temporary items before transfer.

## 24.3. Stack merge

Vanilla stack compatibility does not reliably include `m_customData`. Explicitly reject merge:

- temporary + permanent;
- different event IDs;
- different owners.

Compare a narrow stack-compatibility transpiler/override against temporary extraction around merge; use virtual inventory only as fallback.

## 24.4. Damage routing

Layer membership is primary:

- normal and Blood Craft weapons of blood-layer Player damage only current-event blood entities;
- no damage to ordinary enemies, boss, tamed, crops, buildings, trees/ores or nonparticipants;
- projectile/AoE captures event/layer/source identity on creation;
- delayed hit is independent of later weapon/owner/phase.

## 24.5. Summons

- summon created by Fighting/GoalReached Player becomes current-event blood entity;
- attacks only blood enemies;
- hidden from real-world clients;
- removed on owner ejection/resolution;
- pre-existing summon/tamed remains ordinary;
- turrets/traps do not become blood automatically.

## 24.6. DoT

Vanilla damaging statuses lack enough source attribution. First prototype enemy has no persistent DoT.

At dream collapse clear current damaging DoTs as explicitly accepted, but never use `RemoveAllStatusEffects`. Production must decide a safe vanilla/modded classifier or blood-specific/source-aware statuses.

---

# 25. Context and interactions

Before first contact, ordinary interactions remain vanilla.

After full layer, geometry/doors/ladders and escape movement remain. Ordinary world-changing actions should be hidden or rejected until ejection/resolution:

- ordinary pickups;
- harvesting/mining/chopping;
- trader;
- external inventories;
- ordinary crafting/upgrade;
- attacks on resources/buildings.

Portals, ship/mount controls and entering unsupported context require the runtime spike result. Preserve agency and avoid a visually available action that silently half-works.
