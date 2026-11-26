# Fuzzy season timer

Select `Fuzzy` in `Season - Buff / Timer format` to replace the numeric status-effect timer with a stage label. Select `Fuzzy` in `Raven menu Timer format` to show the season name and its stage in the tooltip. The existing defaults have not changed.

The default sequence is **Arrives / Reigns / Fades**. These labels describe time within the current season. They do not change weather, snow, temperature, or any other season rule.

## Custom labels

`Season - Buff / Fuzzy timer stages` is a client setting. Its default value is:

```ini
Fuzzy timer stages = $seasons_timer_fuzzy_arrives,$seasons_timer_fuzzy_reigns,$seasons_timer_fuzzy_fades
```

Each non-empty entry covers an equal share of the season. Three entries give thirds; four entries give quarters. A single entry stays visible throughout the season.

Entries can contain localization tokens, plain text, or both:

```ini
Fuzzy timer stages = $seasons_timer_fuzzy_arrives,In full swing,$seasons_timer_fuzzy_fades
```

Spaces around entries and empty entries are ignored. An empty list uses the three default tokens. Commas separate entries; to use a comma inside a label, place that label in a localization token. Entries are split before localization, so commas in a translated token do not create extra stages.

The three built-in tokens are translated in every language column of `EmbeddedLocalizations.csv`. The existing pre-build `UpdateTranslations` target generates embedded JSON from that CSV. Do not maintain a separate set of JSON changes for these tokens.

## Morning announcement

Three independent client settings control Fuzzy announcements in the
`Season - Buff` section:

| Setting | Default | Behavior |
| --- | --- | --- |
| `Show Fuzzy season period on morning` | `false` | Enable morning Fuzzy announcements |
| `Show Fuzzy as second morning line` | `false` | Append Fuzzy below another center-screen morning message; otherwise only show it if no other morning message was displayed |
| `Show Fuzzy only when period changes` | `true` | Announce a stage only on the first morning when it differs from the previous morning; disabling this allows announcements every morning |

The existing `Show seasonal messages on morning` setting always takes
priority when enabled: on the first and last day of a season it replaces
the vanilla day number with `Season has come` or `Season is coming`.
No Fuzzy line is added on those days, even when the second-line option
is enabled and the period changed.

On other days, Fuzzy is considered only when its main switch is on and,
by default, the period changed since the previous morning. If the game
or another mod already displayed a center-screen morning message (such as
the day number), Fuzzy is shown **only** when the second-line setting is on.
If the day number was suppressed and no other center-screen message was
displayed, Fuzzy appears by itself, for example `Весна: В разгаре`.

The change-only check uses the exact timestamp at which the current
Fuzzy interval started compared with the previous morning's calendar time.
It accounts for changing morning times when Seasons varies the night length,
rather than assuming yesterday's morning was exactly one day-length ago.
This does not round the season into whole-day stages. If several labels
start between two mornings, only the currently active one is eligible.
The first day of a new season counts as a new period, including when
only one label is configured. When seasonal first-day announcements are
enabled, they still take priority.

The text uses the same configured labels, localization and cache as
the status-effect Fuzzy timer. Neither status-effect nor Raven timer
must be set to Fuzzy. The morning hook compares the center-message
fade queue before and after the original `OnMorning` call, and requires
non-empty center text; it does not mistake stale text from a previous
message or an empty suppressed message for a displayed day number. Normal Fuzzy announcements do not clear the top-left message
queue or add another message-log entry. No extra per-frame work is added.

## Timing and cache

The selected stage uses the same season duration, time source, current day, pending-transition rule, and morning offset as the numeric timer. Calendar values are not rounded to days or integer seconds.

For duration `T`, remaining time `R`, and `N` labels:

```text
progress = clamp(1 - R / T, 0, 1)
index = min(N - 1, floor(progress * N))
```

A boundary belongs to the next stage. A delayed season transition keeps the final label until the calendar is updated. There is no separate real-time countdown or stage scheduler.

The shared cache stores the localized label and its lower/upper time bounds. A normal request reads the current calendar time and compares those bounds. It does not parse the configuration, resolve localization, or calculate the season end again. Crossing a boundary calculates the current stage directly, including when several stages were skipped or time moved backward.

Existing lifecycle and configuration events invalidate the cache: season/day updates, season and world settings, day length, night-length rules, season overrides, timer/status-effect settings, status-effect setup, language reload, world startup/shutdown, and explicit time skips. Invalidating the cache does not start background work. The next Fuzzy text request refreshes it.

## Manual checks

Build and run these checks in Valheim. No game build or runtime test was performed when this change was prepared.

1. Keep the default numeric formats, then select Fuzzy independently for the status icon and Raven tooltip. Confirm that stats and visibility settings still work.
2. Check the exact one-third and two-third boundaries, including a short season and a duration not divisible by three. Confirm that a boundary is not rounded to a full day.
3. Use four labels, one label, mixed tokens and literal text, empty entries, and an empty list. Change the list while it is visible.
4. Move time forward and backward across stages, sleep, change season/day overrides, and change the day/season duration. Confirm that the next text request uses the new state.
5. Switch language, leave/rejoin the world, and switch worlds. Check both UI locations. The last stage must not loop back to the first while a season transition is delayed.
6. Check a configured real-world calendar. Fuzzy must use that calendar rather than the time since the client joined.
7. With both morning options at their defaults, verify the new Fuzzy switch alone does not replace a visible day number. Suppress the native day message and check that Fuzzy appears on the first morning after a stage change but not on subsequent mornings.
8. Enable the second-line option: check that the Fuzzy line appears below the day number only on a new period morning. Disable change-only to show the extra line every ordinary morning. The Fuzzy icon/Raven formats must remain independent.
9. Enable seasonal morning announcements and test the first and last days with both Fuzzy options enabled: only the seasonal announcement should appear. Disable the seasonal option and verify the first-day Fuzzy stage is eligible again. Repeat with a one-label list and with multiple stage changes inside one day.
10. Check hidden or replaced vanilla day messages from another mod, including a blank suppressed message. With the second-line option off, Fuzzy must appear only when no other non-empty center-screen morning message was actually shown.
11. Use a season with changing night lengths and a Fuzzy stage boundary close to morning. Verify that yesterday's actual morning fraction determines the change-only result, rather than today's fraction applied to both days.
