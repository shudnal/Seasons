# Blood Moon: PR #42 conformance review handoff

## 0. Scope and checkpoint

Date: 2026-09-08.

Repository: `shudnal/Seasons`.

Working branch: `feat/blood-moon`. Target branch: `master`. Keep PR #42 draft, open and unmerged.

This checkpoint completes the interrupted finalization of the project-conformance matrix task. It is not a new gameplay design and does not replace `25_PROJECT_CONFORMANCE_MATRIX.md`.

- Initial head inspected: `9b1255f040b3695ceed6fb0aeb596f9c90a65350`.
- Corrective code commit: `14dda220258463e886e1b6ede6e405cd301515d0`.
- Full-PR comparison base observed: `eff54feeb573813cb8f41844f563823d609cb71b` (`master`).
- The exact final head submitted to Codex and its result belong in the PR timeline. A subsequent documentation commit is not implicitly covered by an older exact-head review.

## 1. What was already completed in Git

The accepted mechanics are stored in `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md` and the subsystem documents it indexes. They are not dependent on retrieving an old chat or the rejected external design drafts.

The previous pass added `25_PROJECT_CONFORMANCE_MATRIX.md`, updated the authoritative index, and rewrote `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md` as the current owner-side runtime checklist. The matrix records five conformance corrections:

| Commit | Correction |
| --- | --- |
| `54d70665a54205e2f263e75f3ea5dedfec4d7a5b` | Apply stale Blood projectile damage protection to optional generic destructible targets. |
| `56d65a0f7c0e81583ddfa15fb33d7fc3526d978a` | Restore temporary Bloodlust movement fields only once across postfix/finalizer paths. |
| `79e40c2e73059c4da8422fd28b9e3c664c0b8b6d` | Restore temporary environment overlay fields only once across postfix/finalizer paths. |
| `5475e795c81f03e205403b3269a9f8ee9d294335` | Do not globally suppress unrelated vanilla skill gains during resolution. |
| `838a1e18abbb44375d20f2c914ed154e5a281d36` | Preserve required forward phase-transition side effects when time jumps beyond the forced end. |

These changes remain present. This continuation does not claim to have repeated the previous complete source audit or validated its conclusions in Valheim. Codex must independently challenge the matrix against the effective code, rather than treat its `PASS` labels as proof.

## 2. Interrupted finalization regressions and repair

Two later finalization commits must not be treated as a valid baseline:

- `335efa469b0dcc25f18155bdfe3a258b28d85c2a` changed the manifest from the owner's existing `1.9.0` to `1.8.2` and removed its final newline.
- `9b1255f040b3695ceed6fb0aeb596f9c90a65350` also changed the plugin version to `1.8.2` and introduced two unresolved identifiers: `BepIncompatibility` and `cropsToSurviveWinter`.

`14dda220258463e886e1b6ede6e405cd301515d0` restores the exact file blobs from `a8a40675764d6cf1bdbaf61d6eafa1353fcbd9db`:

| Path | Restored Git blob |
| --- | --- |
| `Seasons.cs` | `66fefba18617b5f1ec3d824d7754ea21416c8461` |
| `package/thunderstore/Seasons/manifest.json` | `432ce1be0118d047a1ef6ee82431440cef4fb055` |

The correct identifiers are `BepInIncompatibility` and `cropsToSurviveInWinter`. Both plugin and manifest retain the owner's `1.9.0`, introduced before the conformance pass. This is restoration of the existing branch version, not a new release/version decision.

The restored complete tree is `a0bb254ac8ad2ad1dadd11ea72b6b517f7063240`, identical to the tree at `a8a40675764d6cf1bdbaf61d6eafa1353fcbd9db`. GitHub comparison reports no changed files between those two commits. History is preserved through a normal forward commit; no force push or branch reset was used. All Blood Moon code, Marketplace corrections, matrix and acceptance documents are preserved.

Do not repeat the mistaken interpretation that "do not change version" means resetting this development branch to `master`'s `1.8.2`.

## 3. Review evidence and pending gate

At the initial inspection, the latest completed Codex response was issue comment `5572373473`, for `3f3f794da36568f5d627acf2bd8fb97db50ff053`. Its request was explicitly focused on Marketplace 9.9.4. It predates the conformance-audit changes and is not approval of the final Blood Moon implementation.

The outstanding gate is a new **complete project-conformance review of the full PR**, not a review limited to the restoration commit, documentation delta, or Marketplace adapter.

Required reading:

1. `docs/tasks/CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.
2. `25_PROJECT_CONFORMANCE_MATRIX.md` and this checkpoint.
3. The complete authoritative subsystem set listed by the index, with particular attention to `16`, `20`, `22`, `23` and `24`.
4. `08_EDGE_CASES_ACCEPTANCE_AND_REPORT.md` for the remaining runtime evidence requirements.

Review the complete effective `master...feat/blood-moon` behavior and every major matrix section. Follow all internal Harmony adapters that alter the apparent base-method behavior. Read game classes in `shudnal/assemblies_combined` first; the existing source evidence checkpoint is `cf2cda3a4c5c05e62cb8052a61753e5dcaecc28e`.

Require a reproducible supported-client path for each finding: authoritative requirement, affected code/adapter, triggering state or message ordering, consequence, and suggested correction. Legitimate latency, reordering, ownership migration, reconnect, persistence failure and ordinary mod interoperability are in scope. Fabricated RPCs, deliberately tampered markers and other modified-client-only anti-cheat scenarios remain outside the accepted scope in `16`.

Current decisions must remain intact: next ordinary-sleep DreamText; no global input suppression; sticky genuine success; no automatic-display reward; per-zone owner spawning; existing ordinary loot versus no-loot marked extras; source-station Blood Craft requirements; established source-centred progress sharing; and forward-only clock reconciliation without gameplay rollback.

Inert presentation/input compatibility shells and their planned maintainability cleanup are not authorization to revive the superseded handshake/input-lock design. Music assets, automatic inferred enemy-pool progression and owner-controlled visual/balance tuning remain separately classified in the matrix.

## 4. Evidence limits and continuation

This continuation used repository metadata, file contents, commit diffs and exact Git blob/tree comparison. It did not compile Seasons, run automated mod tests, launch Valheim, or claim a successful new owner-side build. No CI result was returned by the combined-status lookup for the initial head.

A submitted review request, an acknowledgement/reaction, a running review and a completed review are different states. Record the exact reviewed commit and actual response. Do not accept a generic "no major issues" response as full matrix coverage unless the reviewer explicitly confirms that scope, as required by matrix section 16.

After a completed full-scope review, triage confirmed findings in this same branch, update the affected matrix evidence, and request another review of the exact changed head when code changes. Owner-side compilation and the runtime checklist remain separate acceptance gates. Do not merge PR #42 or prepare a release without the owner.
