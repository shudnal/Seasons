# Blood Moon — acceptance, edge cases and implementation report

Обязательная часть `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

## 1. Lifecycle/network

Проверить:

- feature disabled;
- single-player;
- listen server;
- dedicated server;
- first enable before/inside event window;
- restart in Forewarning/Marked/Active/Resolving;
- time jump across phases;
- empty server at 23:00;
- late join/disconnect/reconnect;
- stale CCS revision;
- stale/duplicate RPC;
- duplicate enemy death report;
- cleanup twice;
- config disable mid-event;
- world unload in every phase.

## 2. CCS/RPC

- late join получает complete current state;
- equal snapshot не создаёт лишнюю обработку;
- participant transition обновляет public routing snapshot;
- no SequencedCustomSyncedValue dependency;
- targeted progress не broadcast всем;
- Defeated client notification относится только к sender Player;
- group coordinator reassignment не создаёт duplicate spawn;
- fade ACK timeout не блокирует world;
- resync восстанавливает UI/state.

## 3. Visual/system

- forewarning имеет non-text visual effect;
- Marked 18:00;
- Active 23:00;
- end 05:45;
- environment overlay order with seasonal luminance;
- force-environment lease conflict;
- missing `Fader`;
- missing `Ashlands_FaderFX`;
- cloud cleanup;
- current RandEvent stopped;
- new RandEvent blocked;
- RandEvent restored;
- sleep blocked/restored;
- no vanilla SoftDeath icon.

## 4. Existing monsters

- hostile ground MonsterAI;
- passive animal;
- tamed;
- boss;
- neutral/aggravated Dvergr;
- Players/PlayerSpawned/TrainingDummy faction;
- starred/unique hostile;
- modded MonsterAI;
- newly loaded/spawned ordinary monster;
- owner migration;
- survivor after event;
- killed existing monster ordinary loot/ragdoll;
- no max-health/shared-prefab mutation;
- no persistent hunt/alert ZDO mutation;
- no stale event VFX/target cache.

## 5. Additional spawns

- zone-owner coordinator;
- marker before participation;
- no ordinary loot;
- fast ragdoll;
- marked ZDO deletion;
- stale cleanup after restart;
- copied collection during deletion;
- no ordinary ZDO deletion;
- raised/lowered cap semantics;
- coordinator disconnect/reassignment;
- surface spawn;
- sea with no land spawn;
- interior CreatureSpawner candidate;
- no interior candidate.

## 6. Damage/AI

- blood enemy targets only Fighting/GoalReached;
- no static target;
- no building/tamed/NPC/boss/monster target;
- no flee/idle with valid target;
- NoMonsterArea bypass;
- ordinary hazard movement not unintentionally broken;
- participant melee/projectile/thrown/AOE only damages Blood enemy;
- forbidden target gets no status/stagger/push/skill credit;
- traps/turrets/environment cannot farm;
- immutable projectile eventId;
- owner exit: delayed damage allowed, no credit;
- global end: stale projectile blocked;
- GoalReached lower target priority with fallback when sole target.

## 7. Defeated/recovery

- direct Blood hit;
- fall after knockback;
- environmental HP loss;
- lava;
- drowning;
- grounded;
- airborne >15 sec;
- swimming;
- attached;
- mounted;
- negative-tick vanilla DoT classes;
- unknown modded DoT retained;
- no `Player.OnDeath`;
- no TombStone/death point/ragdoll/respawn;
- health/stamina/eitr full;
- food/adrenaline unchanged;
- no transform/velocity reset;
- Stage 1 <=15 sec;
- Stage 2 exactly 10 sec at 0.25;
- morning occurs during recovery;
- disconnect/reconnect during recovery;
- direct forced `Player.OnDeath` remains vanilla.

## 8. GoalReached + later exit

Проверить:

```text
GoalReached → normal morning
GoalReached → Defeated
GoalReached → Withdrawn
GoalReached → disconnect
```

Во всех случаях:

- `GoalReached=true` сохраняется;
- completion reward сохраняется;
- ExitReason записывается отдельно;
- DreamText/statistics могут учитывать оба факта.

## 9. Boss offering/parking

### OfferingBowl

- inventory offering blocked;
- item-stand altar blocked;
- authoritative RPC race blocked;
- item-producing bowl работает;
- attachments remain;
- pre-18 queued spawn completes.

### Parking

- persistent outdoor vanilla boss;
- boss owner client/server/listen host;
- higher OwnerRevision propagation;
- queued old-owner DataRevision race;
- ForceSend/sector invalidation;
- multiple bosses/deterministic slots;
- restore with no Player nearby;
- restart while parked;
- stale marker;
- crash after marker/before move;
- crash after restore/before marker clear;
- no y<-5000 rescue;
- HUD/music/forced event clears naturally;
- exact runtime animation/target restoration not required.

### Unsupported boss

- interior boss;
- nonpersistent boss;
- affected Player `Withdrawn`;
- unrelated groups remain active;
- boss combat remains vanilla.

## 10. Blood Craft

- only known recipes;
- custom tabs untouched;
- clone identity;
- no shared Recipe mutation;
- permanent recipe still available;
- permanent item upgrade not free;
- temporary/permanent stack never merges;
- drop destroys item;
- external inventory/world sink blocked;
- stale load cleanup;
- custom equipment slots cleaned;
- Defeated/Withdrawn personal cleanup;
- consumed food/mead effect remains;
- projectile/AOE attribution.

## 11. Skills

- configured aliases parsed;
- invalid aliases logged/ignored;
- only combat skills by default;
- top five by base contribution;
- +25 completion budget;
- per-skill +10 cap;
- no overflow redistribution;
- partial progress scaling excludes auto-complete;
- live x3 bonus stops after +10 bonus level-equivalent;
- Hybrid caps;
- Blocking/BloodMagic/ElementalMagic from actual RaiseSkill calls.

## 12. Build/review report

Итоговый отчёт должен содержать:

- branch/head commits;
- changed files;
- architecture summary;
- exact `assemblies_combined` commit;
- CCS/RPC split;
- boss parking protocol;
- AI/damage patch points;
- build result;
- scenarios actually tested;
- untested multiplayer/VFX explicitly;
- config/live-reload behavior;
- compatibility findings;
- known limitations;
- draft PR;
- Codex review findings and resolutions;
- exact continuation point.

## 13. Definition of done

Реализация готова к runtime playtest, когда:

- весь end-to-end lifecycle собран;
- server/client state восстанавливается;
- debug commands позволяют быстро запускать любую phase;
- core mechanics не являются no-op placeholders;
- no personal-layer code;
- no material event rewards;
- existing loot vs extra no-loot различается правильно;
- Defeated и boss parking защищены idempotency;
- build clean;
- docs соответствуют коду;
- draft PR открыт и reviewed;
- PR не merged.
