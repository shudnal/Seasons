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

Post-matrix project-conformance reviews and their exact corrective evidence are recorded separately:

- `26_PR42_CONFORMANCE_REVIEW_HANDOFF.md` — review sequence and continuation point;
- `27_CODEX_CONFORMANCE_REVIEW_FIXES_2026-09-08.md` — six findings from the first documentation-aware full-PR review;
- `28_CODEX_CONFORMANCE_REVIEW_ROUND2_FIXES_2026-09-08.md` — four findings from the second full-PR review, including durable skill ACK/drain, obsolete lease revocation and recovered forward-jump routing.

These later checkpoints refine implementation evidence without changing the accepted gameplay decisions represented by this matrix.

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

Every post-correction Codex request must be a **complete project-conformance review**, not a latest-delta review. Codex must read the repository index and this matrix first, then the current corrective checkpoints (`26`-`28` at this head), apply the precedence rules, review the full effective `master...feat/blood-moon` behavior including internal Harmony adapters, and independently challenge every major matrix section.

A “no major issues” result is only accepted if Codex explicitly states that it reviewed the complete PR against the project contract/matrix rather than only the latest changes.

PR #42 remains draft/open/unmerged until the owner decides otherwise.
