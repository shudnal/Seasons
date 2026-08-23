# Blood Moon — edge cases, acceptance and report

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 23. Edge cases первого среза

Обязательно проверить кодом и/или ручным сценарием:

- feature disabled;
- одиночная игра;
- listen server;
- dedicated server;
- первый запуск до forewarning;
- первый запуск внутри forewarning;
- каждую forewarning-ночь есть видимое внутриигровое изменение, а не только текст;
- visual forewarning корректно усиливается по дням и очищается утром;
- первый запуск между 18:00 и 23:00;
- первый запуск во время Active;
- restart в Marked;
- restart в Active;
- restart в Resolving;
- server time jump через один или несколько transitions;
- изменение календарного конфига во время события;
- отключение feature во время события;
- текущий RandEvent в 18:00;
- попытка нового RandEvent в suppression window;
- другой force environment до Blood Moon;
- другой мод меняет force environment после Blood Moon;
- отсутствие `Fader` или `Ashlands_FaderFX`;
- world unload во всех фазах;
- игрок вошёл поздно;
- игрок вышел;
- игрок переподключился после disconnect outcome;
- CombatEntrySnapshot записан для игрока в поле;
- CombatEntrySnapshot записан/помечен корректно на корабле, в данже и attached;
- игрок умер;
- первый срез не телепортирует игрока по CombatEntrySnapshot и честно сохраняет vanilla death limitation;
- игрок умер в момент PrepareResolution;
- игрок достиг 100% одновременно со смертью;
- два kill reports одного ZDO;
- owner event enemy мигрировал между пирами;
- все игроки достигли 100%;
- все игроки умерли/вышли;
- в 23:00 никого нет;
- игрок в данже;
- игрок на корабле;
- игрок attached;
- игрок в воде/воздухе;
- участник и посторонний игрок стоят рядом;
- tamed/постройка попали в AoE врага;
- cleanup вызван дважды;
- stale RPC/snapshot от предыдущего event ID.

---
# 24. Acceptance criteria первого среза

Задача считается выполненной только если:

1. Проектная структура и state machines добавлены без превращения `Seasons.cs` в монолит.
2. Событие можно принудительно прогнать debug-командами без ожидания года.
3. Сервер авторитетно переводит событие через все фазы.
4. Выбранная CCS/RPC схема объяснена и соответствует фактическим гарантиям CCS.
5. Late join получает самодостаточный snapshot.
6. Установка функциональности в уже активное окно безопасно пропускает текущий год.
7. Состояние корректно восстанавливается либо безопасно разрешается после restart.
8. До финальной ночи forewarning имеет видимый не текстовый внутриигровой эффект, усиливающийся по ночам.
9. В 18:00 текущий RandEvent останавливается, новые полностью блокируются.
10. С 18:00 до 23:00 текущая погода плавно краснеет линейно.
11. В 23:00 устанавливается собственный Fader-based forced environment с проверенными wind/sun параметрами.
12. Cloud VFX клонирован из ванильного prefab без сторонних ассетов и корректно очищается.
13. В Active у игрока видны Blood Moon status и SoftDeath.
14. При входе в Fighting сохраняется persistent `CombatEntrySnapshot`, но первый срез не выполняет недоговорённый teleport/restore.
15. Смерть не уменьшает skills и завершает участие.
16. Один тип event enemy появляется с group/server cap и корректным ZDO marker.
17. Event enemy выбирает только допустимых участников.
18. Event enemy не повреждает базу, crops, tamed, обычных существ и посторонних игроков.
19. Target и damage rules проходят через одну централизованную interaction-policy, а не расходятся между patches.
20. Event enemy не оставляет обычный loot и долгий ragdoll.
21. Kill засчитывается сервером один раз и изменяет authoritative progress.
22. На 100% full buff остаётся, а aggro снижается только при наличии незавершившихся участников.
23. Auto-complete не превращается в реальный combat contribution/Success.
24. При всех терминальных outcomes событие завершается досрочно.
25. В 05:45 событие завершается принудительно.
26. Resolution имеет fade barrier/timeout, cleanup, time advance к 06:00, DreamText и Rested reset.
27. Нет map markers.
28. Нет предметных/материальных/world-state rewards.
29. Нет version bump/release changes.
30. Все новые файлы включены в `.csproj`.
31. Выполнена доступная сборка; если локальные игровые references отсутствуют, это честно описано с точным списком непроверенного.
32. Подготовлен manual multiplayer test checklist.
33. Открыт draft PR и проведён отдельный Codex code review.
34. PR не слит.

---
# 25. Рекомендуемая разбивка коммитов

Не обязательные точные названия, но изменения должны оставаться обозримыми.

## Commit 1 — authoritative lifecycle

- configs;
- schedule/event ID;
- state records/transitions;
- CCS/RPC decision and global snapshot;
- persistence/recovery;
- CombatEntrySnapshot;
- centralized interaction-policy boundary;
- debug commands;
- базовые localization tokens.

## Commit 2 — presentation and suppression

- progressive non-text forewarning visuals;
- Marked/Active status;
- SoftDeath;
- sleep block;
- RandEvent suppression;
- environment clone/overlay/force lease;
- cloud VFX;
- resolution fade/time/DreamText/Rested.

## Commit 3 — first combat loop

- minimal hidden groups;
- spawner;
- enemy marker;
- target/damage rules;
- no loot/ragdoll cleanup;
- owner reports/dedup;
- progress/100%/early completion.

## Commit 4 — validation fixes

- сборка;
- edge-case fixes;
- documentation of manual tests;
- cleanup after code review findings.

Не создавать один огромный неразделимый commit.

---
# 26. Итоговый отчёт Codex

После реализации выдать:

- список коммитов;
- краткое описание архитектуры;
- выбранную схему CCS/RPC и причины;
- список изменённых/новых файлов;
- результат сборки;
- какие сценарии проверены автоматически/статически;
- manual test checklist для реальной игры;
- известные ограничения первого среза, включая временный vanilla death flow и отсутствие полного parallel-world/Blood Craft;
- ссылку/номер draft PR;
- итог Codex code review;
- какие замечания исправлены;
- какие замечания отклонены и почему;
- точную следующую точку продолжения.

Не утверждать, что runtime multiplayer/VFX проверены, если они не запускались в реальной игре.
