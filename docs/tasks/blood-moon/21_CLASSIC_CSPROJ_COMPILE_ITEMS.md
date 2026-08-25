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

The next local Visual Studio compile exposed two source regressions introduced during the boss-parking split:

```text
CS0136 BloodMoonBossParkingStart.cs: local 'record' shadowed a later local in the same method scope
CS0103 BloodMoonBosses.cs: TryValidateDiscoverySender was referenced but not carried over from the pre-split implementation
```

They were corrected in:

```text
597c928c11fd67be60eff63e615b8bbc29d7243c
fix: repair Blood Moon boss parking compilation
```

The correction:

- renames the existing-record `out` variable to `existingRecord`, leaving the new parking transaction local as `record`;
- restores `TryValidateDiscoverySender` with the previous server/listen-host and routed-peer identity validation, including active participant validation and server-authoritative reporter position lookup.

When splitting a production class into partial files, static review must compare the complete pre-split member set against the resulting partial member set so private helpers are not silently dropped.

## Verification boundary

The assistant does not build or launch the Valheim mod. The correction was verified statically against the repository project file, current source, and the pre-split `BloodMoonBosses.cs` implementation. Local Visual Studio compilation remains the acceptance check for compiler errors.
