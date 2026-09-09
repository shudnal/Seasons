# Blood Moon — Seasons integration

Обязательная часть задачи `CHAT_2026-08-23_BLOOD_MOON_FIRST_VERTICAL_SLICE.md`.

# 21. Интеграция в текущий Seasons

Перед изменениями изучить минимум:

```text
Seasons.cs
SeasonsVars.cs
SeasonState/
SeasonState/EnvManPatches.cs
SeasonSettings/
SE_Season.cs
SummerHeat/
CustomMusic.cs
Utils/CustomSyncedValuesSynchronizer.cs
Seasons.csproj
EmbeddedLocalizations.csv
```

Рекомендуемая новая структура, которую можно корректировать под фактический стиль проекта:

```text
BloodMoon/
  BloodMoon.cs                    // feature lifecycle/composition root
  BloodMoonConfig.cs
  BloodMoonState.cs               // state enums + authoritative records
  BloodMoonSchedule.cs
  BloodMoonController.cs          // server state machine
  BloodMoonParticipant.cs
  BloodMoonSync.cs                // CCS/RPC hybrid
  BloodMoonPersistence.cs
  BloodMoonGroups.cs
  BloodMoonSpawner.cs
  BloodMoonEnemy.cs               // marker/helpers/AI eligibility
  BloodMoonEnvironment.cs
  BloodMoonCloudVisuals.cs
  BloodMoonStatusEffect.cs
  BloodMoonResolution.cs
  BloodMoonCommands.cs
  Patches/
```

Не обязательно дробить на такое количество файлов, если некоторые получатся искусственно пустыми. Но не помещать всю механику в `Seasons.cs` или один гигантский patch-файл.

Проект использует явный список `<Compile Include=...>`: добавить новые `.cs` в `Seasons.csproj`.

Соблюдать текущий target framework, language version, nullable/style и dependency conventions проекта.

---
