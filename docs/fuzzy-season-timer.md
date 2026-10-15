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
