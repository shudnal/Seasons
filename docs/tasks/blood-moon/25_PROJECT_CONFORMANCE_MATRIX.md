# Blood Moon — project conformance matrix

## 0. Purpose and authority

This document is the current project-conformance checkpoint for draft PR #42 on `feat/blood-moon`.

It answers a different question from an ordinary code review:

```text
accepted project behavior
-> effective implementation
-> conformance status / remaining evidence gate
```

This matrix is based on the repository contract, not chat memory. The starting index is:

```text
docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md
```

The original product/subsystem contract remains `01` through `12`, subject to explicit later accepted corrections. Later documents do not all have equal authority:

- `13_IMPLEMENTATION_REPORT.md`, `14_CODE_REVIEW_STATUS.md` and `15_RELEASE_READINESS.md` are historical implementation/review evidence for the heads they describe. They are **not** a current product contract when later decisions supersede them.
- `16_CLIENT_TRUST_BOUNDARY.md` is authoritative for compatible-client trust and anti-cheat scope.
- `20_RUNTIME_JSON_AND_ZDO_PARKING.md` is authoritative for Blood Moon JSON and ZDO parking persistence.
- `22_DREAMTEXT_AND_INPUT_POLICY.md` is authoritative for DreamText and input. It supersedes every earlier immediate-DreamText, presentation-handshake and Blood-Moon-owned input-lock description, including stale wording in `04`, `13`, `14` and `15`.
- `23_PR42_RUNTIME_HARDENING_AND_CLOCK_POLICY.md` is authoritative for `skiptime`, backward time, admin restart semantics and the runtime-hardening decisions recorded there.
- `24_MARKETPLACE_9_9_4_COMPATIBILITY.md` supersedes the Marketplace 9.8.9 evidence section in `23` for the current Marketplace API contract.
- this file (`25`) is the current cross-subsystem conformance matrix and the authoritative clarification for the specific stale/ambiguous documentation items called out below.

Game-method verification continues to use `https://github.com/shudnal/assemblies_combined` first.

This audit is static source inspection. The owner has already exercised the feature in Valheim and reported no remaining gross runtime exception in the ordinary tested path, but the assistant has not built or run the mod. New code changes made by this audit require another owner-side compile/runtime pass.

## 1. Status legend

| Status | Meaning |
| --- | --- |
| `PASS` | Effective source behavior matches the accepted contract. |
| `PASS / RUNTIME` | Static implementation matches; owner-side runtime/multiplayer evidence is still required. |
| `FIXED IN AUDIT` | A concrete conformance/compatibility defect was found during this matrix pass and corrected. |
| `DOC CLARIFIED` | Effective implementation is accepted, but older repository wording was incomplete or obsolete; this matrix records current meaning. |
| `TECH DEBT` | Behavior is currently correct, but implementation structure is worth simplifying later. |
| `FUTURE BY DESIGN` | Explicitly outside the current implementation slice. |
| `VISUAL OWNER` | Intentional owner-side visual/balance/polish work, not a missing core mechanic. |
| `OUT OF SCOPE` | Explicitly not part of the product contract. |

## 2. Calendar, state and clock control

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Annual late-autumn event with frozen schedule and deterministic `eventId = eventWorldDay` | `BloodMoonSchedule`, persisted `BloodMoonScheduleSnapshot`, explicit event state | `PASS` | Verify annual rollover in long-running world. |
| Defaults: forewarning day 6, final day 9, Marked 18:00, Active 23:00, Auto 04:15, forced end 05:45, morning 06:00 | Config + frozen schedule use these defaults | `PASS` | Balance remains configurable. |
| First enable inside current event window does not surprise-start that year's event | First observed enable inside current window records `Skipped` | `PASS / RUNTIME` | Test first install before and inside window. |
| Explicit event/participant/resolution state machines | Separate enums and persisted state records | `PASS` | None. |
| `GoalReached` is sticky and independent from later exit reason | Separate `GoalReached` + `ExitReason`; terminal exit does not revoke success | `PASS` | Test GoalReached -> Defeated/Withdrawn/disconnect. |
| Forward `skiptime` reconciles through legal transitions and preserves required transition side effects | Large forward jumps now force one reconciliation pass through Marked -> Active -> AutoCompleting before normal resolution is allowed | `FIXED IN AUDIT` | Audit fix `838a1e18abbb44375d20f2c914ed154e5a281d36`. Test jump from Forewarning directly beyond 05:45 and beyond 06:00. |
| Backward time does not undo gameplay or replay an older event | Phase progression is forward-only; `LastCreatedEventId`/`LastResolvedEventId` are high-water marks | `PASS / RUNTIME` | Test rewind across phase/year boundaries. |
| Admin can explicitly start diagnostic phases, but a live event is not silently rolled backward | `start` is forward-only for a live state; cleanup is required before an earlier diagnostic restart | `PASS` | Repeated same-day debug runs retain durable reward receipts by design. |
| `start marked` preserves preparation duration | Debug schedule is anchored to requested phase with normal relative intervals | `PASS / RUNTIME` | Verify no immediate Active transition. |

## 3. Networking, persistence and trust

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Recoverable global/public current state uses normal CCS `CustomSyncedValue`, not a sequenced event queue | Global and participant routing snapshots use normal CCS JSON | `PASS` | Late-join test still required. |
| Private/addressed operations use own RPC | Participant detail/resync, Defeated, spawn leases/reports, boss discovery, damage credit, outcomes, etc. are targeted RPC | `PASS` | Real latency/reorder testing. |
| Remote clients do not own authoritative `BloodMoonController.State` | Controller differentiates client world initialization from server state ownership | `PASS / RUNTIME` | Dedicated-client join/rejoin. |
| Same-session/world-switch snapshots and actions do not leak | Session reset + monotonic resync/fade handling | `PASS / RUNTIME` | Same-process multi-world test. |
| Compatible clients are trusted for facts they legitimately own; this is not an adversarial anti-cheat system | Validation follows `16_CLIENT_TRUST_BOUNDARY.md` | `PASS` | Modified-client anti-cheat is not required. |
| World state is world-UID-bound and recoverable after crash/restart | Atomic/newest-valid JSON plus ZDO markers for world entities | `PASS / RUNTIME` | Restart every major phase/resolution step. |
| Blood Moon JSON avoids Unity self-reference serialization | Controlled JSON settings + explicit Vector3/Quaternion/finite converters | `PASS` | Corrupt/NaN persistence cases remain useful tests. |

## 4. Groups and additional spawning

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Hidden server combat groups with merge/split hysteresis and stable IDs | `BloodMoonGroups` rebuilds every ~4 s with stable overlap-based IDs | `PASS / RUNTIME` | Multi-player merge/split. |
| There is no single group-wide spawn coordinator | Server grants per-zone leases; actual spawn occurs only on current owner of each loaded zone | `PASS` | Old “coordinator” wording in earlier acceptance docs is obsolete. |
| Owner migration invalidates only the affected zone lease/revision | Zone ownership/revision tracking and per-zone lease replacement | `PASS / RUNTIME` | Test in-flight owner migration. |
| Same-revision renewal cannot replenish already spent allowance | Client lease history retains remaining allowance; a new grant needs a newer revision | `PASS / RUNTIME` | Delay reports and resend same revision. |
| Group/server caps include pending reports and do not delete live extras when config cap is lowered | Server budget reconciliation includes pending/live counts; lower caps stop new allowance | `PASS / RUNTIME` | Hot-lower cap test. |
| Surface spawn uses local terrain/nav/path and can ignore ordinary base/no-monster suppression | Dedicated Blood Moon scheduler on zone owner | `PASS / RUNTIME` | Terrain/camera/path cases. |
| Interior extras use only loaded authored `CreatureSpawner` positions and never call its `Spawn()` bookkeeping | Mixed-context spawner reads loaded positions and validates path/context | `PASS / RUNTIME` | Multiple dungeons / no valid candidate. |
| Extra provenance is immutable enough to validate a moving creature | Event/group/role + original spawn-zone markers are written immediately | `PASS / RUNTIME` | Cross-sector movement before report acceptance. |
| Current slice may use an explicit configured bootstrap prefab | Frozen spawn-pool identity per event | `PASS` | Automatic inferred pool is future work. |
| Automatic inferred pool from raids/keys/trophies/achievements | Not implemented intentionally | `FUTURE BY DESIGN` | Contract remains in `06`. |

## 5. Existing monsters and AI

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Existing Blood enemy eligibility is dynamic: live MonsterAI, non-boss, non-tamed, allowed faction, vanilla hostility to an active participant | Central `BloodMoonInteractionRules` + vanilla hostility bypass context | `PASS / RUNTIME` | Neutral/aggravated Dvergr, modded factions. |
| Existing creatures get no conversion marker | Only event-created extras carry `SpawnedEventId` | `PASS` | None. |
| Existing monsters retain normal loot/ragdoll and survivors remain in world | No-loot/fast-ragdoll hooks key on nonnegative extra provenance only | `PASS / RUNTIME` | Kill ordinary monster outside/inside event. |
| No persistent HuntPlayer/alert/max-health/shared-prefab mutation | AI behavior is transient Harmony policy | `PASS` | Inspect ZDO before/after during runtime. |
| Blood enemies do not target buildings/tamed/NPC/boss/other monsters | Static-target pressure disabled; creature target filtered through central policy | `PASS / RUNTIME` | Base attack test. |
| GoalReached targets are lower priority while unfinished Fighting participants exist | Target selection prefers Fighting list, falls back to GoalReached | `PASS / RUNTIME` | Multi-player mixed progress. |

## 6. Damage, projectile, AOE and attribution

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Owner-side `Character.RPC_Damage` is final defense before status/aggravation/push/armor side effects | Early prefix performs Blood permission and recovery checks | `PASS` | Cross-peer attack matrix. |
| Direct attacks are filtered early | Attack melee/area context + Character early guard | `PASS / RUNTIME` | Nested attack/proc combinations. |
| Forbidden Character collider is not a successful hit and does not stop a Blood projectile | Projectile prefix rejects forbidden Character before vanilla hit processing | `PASS / RUNTIME` | Explicitly accepted; do not “fix” to conventional collision behavior. |
| World geometry still physically stops/attaches/destroys projectile while receiving no Blood world damage | Non-Character collision keeps vanilla `Projectile.OnHit`; world destructible guards suppress damage | `PASS / RUNTIME` | Walls, terrain, spawn-on-hit projectile. |
| Stale event projectile keeps physical/item lifecycle but no stale damage/credit/harmful spawned branches | Dedicated stale attribution guards | `PASS / RUNTIME` | Permanent thrown weapon after event rollover. |
| Stale projectile also cannot damage mod-added generic `IDestructible` implementations | Generic optional target guard now uses the same invocation-scoped stale projectile flag as vanilla destructibles | `FIXED IN AUDIT` | Audit fix `54d70665a54205e2f263e75f3ea5dedfec4d7a5b`. Test with a modded destructible if available. |
| Projectile/AOE attribution captures immutable event/source identity at creation | Runtime/ZDO attribution stores event/source/player/character | `PASS / RUNTIME` | Source unload/owner exit/reorder. |
| Delayed source after owner exit may still damage a valid target but cannot earn progress | Damage authorization and credit policy separate permission from current credit eligibility | `PASS / RUNTIME` | Exit while projectile is in flight. |
| Trap/turret/environment cannot farm Blood enemies by default | Null/unapproved source cannot damage Blood enemy through final policy | `PASS / RUNTIME` | Environmental/trap/turret matrix. |

## 7. Progress, Bloodlust and skills

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Combat points, displayed progress and contribution are separate | Separate persisted participant fields | `PASS` | None. |
| Auto-complete raises display floor only; it never creates GoalReached/reward/Bloodlust power | Automatic floor only updates `DisplayProgress`; earned factor comes from `CombatPoints` | `PASS / RUNTIME` | 04:15–05:45 test. |
| Death progress is server-computed and exactly-once | Server validates Blood enemy/dead state/matched credit, computes points, dedupes enemy ZDOID | `PASS / RUNTIME` | Cross-owner lethal hit + reordered RPC. |
| Current progress-share center | Current production behavior shares from the credited source player's position within the current hidden group and configured radius | `DOC CLARIFIED` | Older `02` payload text mentioning client `groupId`/`position` is obsolete after trust hardening. Changing the radius center is a future gameplay decision, not part of this audit. |
| Player Bloodlust uses genuine earned combat progress, with GoalReached pinned to full | `BloodMoonBloodlust.GetEarnedFactor` | `PASS` | 0/50/100/GoalReached runtime. |
| Outgoing/incoming endpoint modifiers apply only to allowed Blood combat routes | Damage patches layer enemy and Bloodlust multipliers only on accepted participant/Blood enemy routes | `PASS / RUNTIME` | Verify stacking with other damage mods. |
| Movement modifier is runtime-only and does not permanently mutate Player fields | Walking/swimming fields temporarily scaled around vanilla calculations | `PASS / RUNTIME` | Crouch/run/swim and other movement mods. |
| Temporary movement restoration must not overwrite another Harmony postfix | Invocation state now records restoration and restores once across postfix/finalizer | `FIXED IN AUDIT` | Audit fix `56d65a0f7c0e81583ddfa15fb33d7fc3526d978a`. |
| Lifesteal is based on confirmed actual HP loss and server cap, not nominal hit | Source authorization + target-owner actual damage confirmation + rolling HPS cap + monotonic grant | `PASS / RUNTIME` | AOE, overkill, zero/blocked damage. |
| Normal vanilla skill gain must not be globally suppressed by resolution | Blood Moon-specific accounting is phase-gated; the broad `Player.RaiseSkill` Resolving blocker was removed | `FIXED IN AUDIT` | Audit fix `5475e795c81f03e205403b3269a9f8ee9d294335`. Verify Run/Jump/other ordinary skill gain under resolution fade. |
| Live skill bonus and completion reward follow configured modes/budgets | Actual `Player.RaiseSkill` tracking, nonlinear level-equivalent accounting, top-five/+25/+10 rules | `PASS / RUNTIME` | Reconnect baseline and level-boundary cases. |

## 8. Defeated, Withdrawn and recovery

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Local Player owner intercepts lethal state before `Player.OnDeath` | `Character.CheckDeath` interception only for local active participant while Blood behavior is live | `PASS / RUNTIME` | Direct and environmental lethal paths. |
| No grave/deathpoint/ragdoll/respawn/skill loss, no transform reset | Local recovery restores resources and skips vanilla death path | `PASS / RUNTIME` | Verify tombstone/death stats. |
| Health/stamina/eitr full; food/adrenaline unchanged | Recovery implementation follows contract | `PASS / RUNTIME` | Magic/stamina edge cases. |
| DoT cleanup is narrowly limited to accepted vanilla damaging effects | Burning/Poison/Smoke and damaging ticking `SE_Stats` only | `PASS / RUNTIME` | Unknown mod DoT retained. |
| Stage 1 full protection until stable, max 15 s; Stage 2 10 s at 0.25 | Realtime recovery scheduler | `PASS / RUNTIME` | Airborne/swim/attached/mounted. |
| Recovery can continue after global morning resolution | Local recovery has independent lifecycle | `PASS / RUNTIME` | Defeat just before forced end. |
| Terminal `Withdrawn` does not move Player and does not permit re-entry | Exit state + personal cleanup only | `PASS` | World edge / boss cases. |
| World-edge boundary uses dynamic Seasons water-edge, not fixed radius | `BloodMoonWorldEdge` + `ZoneSystemVariantController.IsBeyondWorldEdge` | `PASS / RUNTIME` | Modified-world radius. |

## 9. Boss offerings and parking

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Boss-producing `OfferingBowl` is blocked from Marked, item-producing bowls are untouched | Interact/UseItem/authoritative spawn guards + server-authorized Forewarning relay | `PASS / RUNTIME` | Inventory and item-stand altars. |
| Request accepted by server before cutoff may complete after Marked; request first reaching server after cutoff is blocked | Accepted request token survives owner migration/phase cutover | `PASS / RUNTIME` | Test immediately around 18:00. |
| Only alive persistent outdoor boss is parkable | Server validates raw ZDO/prefab/persistence/interior | `PASS` | Modded bosses need concrete reports. |
| Parking is marker-first, server-owner, far-XZ, finite-Y, ForceSend + short reassert window | Raw-ZDO parking transaction | `PASS / RUNTIME` | Owner-revision race/listen host. |
| Restore returns same persistent ZDO to durable original transform and clears marker last | Schema-2 parking record + restore scan | `PASS / RUNTIME` | Crash at transaction boundaries. |
| Interior/nonpersistent boss withdraws only affected participants in same encounter/navigation context | Loaded encounter report + server ZDO validation + same surface/interior and same interior Location + radius | `PASS / RUNTIME` | `07` wording about “boss HUD/live encounter” is clarified: HUD is not authority; loaded proximity/context is the current accepted encounter evidence. |
| Boss exact runtime animation/target/coroutine/HUD state need not be restored | Same-ZDO restoration is the correctness contract | `PASS` | None. |
| Same-location withdrawal context currently uses an internal Harmony adapter over the private base method | Effective behavior matches contract but should eventually be folded into the production method if that area is edited again | `TECH DEBT` | `BloodMoonBossWithdrawalContext.cs`. |

## 10. Blood Craft and world preservation

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Blood Craft is temporary, known-recipe-only and not a reward | Runtime clones only for known enabled eligible combat recipes | `PASS / RUNTIME` | Modded known recipe coverage. |
| Original permanent recipe remains normal | Source recipe is not mutated; separate clone is added | `PASS` | None. |
| “Requirements free” means **material requirements free**, not portable crafting | Clone resources are empty, while source crafting/repair station type and required station level are preserved/enforced | `DOC CLARIFIED` | This is the accepted meaning; older `07` wording was incomplete. |
| Temporary upgrades are free but still respect source station/quality progression | Source recipe station checks + normal station level calculation | `PASS / RUNTIME` | Upgrade through max quality. |
| Temporary identity is world/event/owner/source-recipe bound | Schema 3 stores `Schema`, `WorldUid`, `EventId`, `OwnerPlayerId`, `SourceRecipe` | `DOC CLARIFIED` | Older `07` marker list predates hardening. |
| Temporary/permanent, different event, different owner or different source recipe cannot merge | Explicit stack compatibility checks | `PASS / RUNTIME` | Autostack/custom inventories. |
| Temporary item is inventory-only; world drops and external consumers cannot retain it | Common `ItemDrop.DropItem` fail-safe + container/world sink guards + load cleanup | `PASS / RUNTIME` | Third-party sinks that bypass vanilla boundaries may need future compatibility. |
| Permitted combat use remains possible | Equip/ammo/food/drink/throw paths remain allowed while materialization is blocked | `PASS / RUNTIME` | Thrown weapons and ammo. |
| Temporary summons carry event/owner provenance and are cleaned on personal/global cleanup | `BloodMoonSummons` markers and server/local cleanup | `PASS / RUNTIME` | Different SpawnAbility archetypes. |
| Consumed food/mead effect may remain after item cleanup | Item cleanup does not remove consumed status effects | `PASS` | Runtime confirmation. |

## 11. Presentation, suppression and compatibility

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Forewarning is visual, not text-only | Night-only red overlay ramps during forewarning | `PASS / VISUAL OWNER` | Intensity/taste is owner-side polish. |
| Marked 18:00 linearly ramps current environment toward Blood Moon | Transient `EnvMan.SetEnv` overlay | `PASS / VISUAL OWNER` | Biome/interior visual tuning. |
| Active uses cloned `Fader` environment with red Color channels, wind 1..2 and sun angle 70 | `Seasons_BloodMoon` clone; current `EnvSetup` Color fields verified against `assemblies_combined` | `PASS / VISUAL OWNER` | No omitted current Color field found in static check. |
| Ashlands Fader cloud clone keeps intended cloud objects and scales emission | Cloned/pruned `Ashlands_FaderFX` path | `PASS / VISUAL OWNER` | Missing-asset/fallback runtime. |
| Forced environment behaves as a lease and does not clobber a mod that displaced it | Reacquisition captures current displaced value; release restores only when Blood Moon still owns force env | `PASS / RUNTIME` | Test with another force-env mod. |
| Transient environment field restoration must compose with other Harmony postfixes | Overlay restoration is now invocation-idempotent across postfix/finalizer | `FIXED IN AUDIT` | Audit fix `79e40c2e73059c4da8422fd28b9e3c664c0b8b6d`. |
| Current ordinary RandEvent is stopped at Marked and new random events are suppressed until restore | `BloodMoonRandEventSuppression` phase-scoped acquire/release | `PASS / RUNTIME` | World teardown + forced event. |
| Sleep is blocked while enrolled/marked/combat-active and restored after terminal/resolution state | Bed interaction guard | `PASS / RUNTIME` | Multi-player sleep. |
| DreamText uses next **ordinary vanilla sleep**, one pending Blood Moon dream at a time, oldest first | `BloodMoonDreams` hooks `SleepText.ShowDreamText`; pending record is profile-backed | `PASS / RUNTIME` | `22` is authoritative; immediate-DreamText text in `13`/`15` is historical and obsolete. |
| Blood Moon does not globally own Player input for outcome/fade presentation | `BloodMoonFadeInputGuard` is inert; no `Player.TakeInput` patch | `PASS / RUNTIME` | Number row and modded `ZInput` hotkeys. |
| Resolution fade is visual-only | Presentation fade remains, but input is not blocked | `PASS / VISUAL OWNER` | Fade duration/pacing can be tuned by owner. |
| Marketplace 9.9.4 territory redraw receives both baseline color arrays before `DoMapMagic` | Reflection adapter matches verified 9.9.4 decompile and updates map + height baselines | `PASS / RUNTIME` | `24` is authoritative; test winter/season switch/map regeneration. |
| Balrond ImmersiveLoading does not need a compatibility patch based on current evidence | No Balrond-specific code added | `PASS` | Closed unless a reproducible report appears. |

## 12. Resolution, outcomes and cleanup

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Resolution freezes enrollment/new spawns/results and proceeds idempotently | Explicit resolution state machine, stopped leases and transaction steps | `PASS / RUNTIME` | Restart each step. |
| Blood combat is frozen during early resolution while fade/cleanup transaction proceeds | Resolving damage/spawn guards | `PASS / RUNTIME` | Verify no new event combat credit. |
| This combat freeze must not block unrelated vanilla skill progress | Broad Resolving `Player.RaiseSkill` suppression removed | `FIXED IN AUDIT` | Same fix as section 7. |
| Marked extras and temporary summons/items are cleaned; existing ordinary monsters remain | Marker-scoped cleanup | `PASS / RUNTIME` | Pending replication at resolution. |
| Parked bosses and world systems are restored before final state | Resolution ordering restores bosses, Blood Craft, env and RandEvent | `PASS / RUNTIME` | Crash/restart during cleanup. |
| Ordinary-world net time advances toward frozen morning; real-time calendar does not rewrite Valheim net time | Separate calendar handling | `PASS / RUNTIME` | Both modes. |
| Outcome/chronicle/reward delivery is durable and idempotent | Independent per-player outcome queue + persistence acknowledgement | `PASS / RUNTIME` | Offline/later annual event/cloud profile. |
| Dream presentation itself is not a resolution dependency | `BloodMoonOutcomePresentationHandshake` is inert and `CanRelease` succeeds; current fixed resolution hold is visual pacing only | `PASS` | Eventually remove inert shell when controller is simplified. |

## 13. Diagnostics, scope and planned work

| Requirement | Effective implementation | Status | Remaining evidence / note |
| --- | --- | --- | --- |
| Admin/debug commands cover status/start/progress/exit/spawn/boss/resolve/cleanup/dumps | `BloodMoonDiagnostics` registers production-backed commands | `PASS / RUNTIME` | Exercise each command after new build. |
| Core diagnostics use structured event/player/group/zone/boss prefixes | Current logging follows structured prefixes in core paths | `PASS` | Per-hit logging remains config-gated. |
| No personal visibility/collision layer | No such architecture in branch | `PASS` | Do not reintroduce. |
| No permanent material/currency/cosmetic/world-state reward | Only combat skill XP + chronicle/pending dream are durable event outputs | `PASS` | Existing ordinary monster loot is intentionally vanilla, not an event reward. |
| Automatic enemy-pool progression | Explicitly deferred | `FUTURE BY DESIGN` | `06`. |
| Blood Moon music assets/tracks | Not supplied | `FUTURE BY DESIGN` | Owner/assets later. |
| Final VFX, Odin observer, wording/localization and balance | Core hooks exist where applicable; final presentation/balance is intentionally not frozen | `VISUAL OWNER` | Owner-side next phase. |
| Adversarial modified-client anti-cheat | Explicitly outside compatible-client trust boundary | `OUT OF SCOPE` | `16`. |

## 14. Confirmed corrections made by this conformance audit

The matrix pass found source-level issues that were not obvious from the owner's ordinary runtime path:

1. `54d70665a54205e2f263e75f3ea5dedfec4d7a5b` — stale Blood projectile damage is now blocked for optional generic/modded `IDestructible` targets by the same invocation flag used for vanilla targets.
2. `56d65a0f7c0e81583ddfa15fb33d7fc3526d978a` — Bloodlust movement field restoration is idempotent across Harmony postfix/finalizer paths.
3. `79e40c2e73059c4da8422fd28b9e3c664c0b8b6d` — transient `EnvMan.SetEnv` overlay restoration is idempotent across Harmony postfix/finalizer paths.
4. `5475e795c81f03e205403b3269a9f8ee9d294335` — Resolving no longer suppresses unrelated vanilla `Player.RaiseSkill` calls.
5. `838a1e18abbb44375d20f2c914ed154e5a281d36` — a large forward time jump preserves required Marked/Active/AutoCompleting transition side effects before normal forced-end resolution.

A progress-share-centre question was investigated but deliberately **not** changed: the authoritative combat contract does not currently require death-position-centred sharing. Current source-centred server-side sharing remains the established behavior until the owner makes a separate gameplay decision.

## 15. Remaining risks after static conformance audit

No missing core subsystem or accepted product mechanic was identified after the corrections above. The remaining gates are primarily:

- owner recompilation of the new exact head;
- single-player/listen/dedicated runtime matrix;
- latency/reorder/ownership migration behavior under real networking;
- persistence failure/restart boundaries;
- third-party custom inventory/world-sink compatibility as concrete reports appear;
- visual/VFX/fade intensity and final balance on the owner's side;
- maintainability cleanup of a few inert/self-Harmony compatibility adapters after behavior is stable.

This is not a runtime-success declaration.

## 16. Final review requirement

The next Codex request must be a **complete project-conformance review**, not a latest-delta review. Codex must read the repository index and this matrix first, apply the precedence rules above, review the full effective `master...feat/blood-moon` behavior including internal Harmony adapters, and independently challenge every major matrix section.

A “no major issues” result is only accepted if Codex explicitly states that it reviewed the complete PR against the project contract/matrix rather than only the latest changes.

PR #42 remains draft/open/unmerged until the owner decides otherwise.
