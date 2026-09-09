# Blood Moon classic project compile item rule

## Status

Runtime-fix follow-up discovered during local Visual Studio compilation on 2026-08-25.

Target branch: `feat/blood-moon`.

Code fix commit before this document:

```text
51cfa291fe8906cec3f4261b3dec8225f0ece6ca
fix: list Blood Moon compile items explicitly
```

## Observed failure

After new Blood Moon source files were added through Git, Visual Studio reported `CS0246` for types defined in those new files, including:

```text
BloodMoonParticipantDetailSnapshot
BloodMoonGlobalSnapshot
BloodMoonParticipantSnapshot
BloodMoonBossParkingDiagnostic
```

The files existed in the repository, but the classic non-SDK `Seasons.csproj` used:

```xml
<Compile Include="BloodMoon\**\*.cs" />
```

A project that was already loaded in Visual Studio could retain the previously evaluated wildcard item set after a Git update, so newly added files were not necessarily present in design-time compilation until the project was re-evaluated.

## Decision

Blood Moon source files are listed explicitly in `Seasons.csproj`.

Requirements from now on:

- do not restore a wildcard `Compile` item for `BloodMoon`;
- every new `BloodMoon/*.cs` file must be added explicitly to `Seasons.csproj` in the same commit that creates the file;
- every removed or renamed Blood Moon source file must update `Seasons.csproj` in the same commit;
- keep Blood Moon compile items sorted by filename;
- repository review must compare the physical `BloodMoon/*.cs` set with the explicit project item set after source-file additions/removals;
- this project-file change is part of source correctness, not packaging or release metadata.

## Current correction

`Seasons.csproj` now explicitly lists all 76 Blood Moon source files present at the correction point, including the recently added JSON boundary, snapshot, and ZDO boss-parking files.

This specifically restores compilation visibility for:

```text
BloodMoon/BloodMoonSnapshots.cs
BloodMoon/BloodMoonBossParkingDiagnostic.cs
BloodMoon/BloodMoonJson.cs
BloodMoon/BloodMoonJsonContracts.cs
BloodMoon/BloodMoonJsonNumbers.cs
BloodMoon/BloodMoonJsonUnity.cs
BloodMoon/BloodMoonBossParkingRecord.cs
BloodMoon/BloodMoonBossParkingStart.cs
BloodMoon/BloodMoonBossParkingValidation.cs
BloodMoon/BloodMoonBossPatches.cs
BloodMoon/BloodMoonBossRestore.cs
BloodMoon/BloodMoonBossRuntime.cs
```

## Follow-up compiler corrections

Local compilation after the project-item fix exposed two actual source regressions introduced by the boss partial-class split:

```text
CS0136 BloodMoonBossParkingStart.cs: local 'record' shadowed an out variable in an enclosing scope
CS0103 BloodMoonBosses.cs: TryValidateDiscoverySender was referenced but no longer existed
```

Fixed in:

```text
597c928c11fd67be60eff63e615b8bbc29d7243c
fix: repair Blood Moon boss parking compilation
```

The first fix renamed the existing-marker `out ParkingRecord` variable so the later new-record declaration has a distinct scope name.

The second restored `TryValidateDiscoverySender` from the pre-split boss implementation. The helper still validates active participation, listen-host identity, remote peer character ZDO identity, and reads the reporter position from the authoritative player ZDO.

When splitting a production class into partial files, static review must compare not only public/internal call sites but also the complete set of private helpers and nested types from the pre-split file.

## Verification boundary

The assistant does not build or launch the Valheim mod. The correction was verified statically against the repository project file and the current `BloodMoon` directory listing. Local Visual Studio compilation remains the acceptance check for compiler errors.
