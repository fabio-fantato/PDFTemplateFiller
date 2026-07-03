# Upgrade Options

This file summarizes the upgrade options evaluated for the PDFTemplateFiller solution based on the assessment at `.github/upgrades/scenarios/dotnet-version-upgrade/assessment.md`.

## Context
- Assessment found 2 projects, both already targeting `net10.0`.
- No package compatibility or API issues detected.

## Options
1. No-op / Verify
   - Description: Keep target framework at `net10.0`. Run full solution build and tests to verify no runtime issues.
   - When to choose: Projects already on target TFM with no compatibility issues.
2. Revalidate & Update Packages
   - Description: Optionally update test and tooling packages to latest compatible versions (no breaking changes expected). Run build & tests.
   - When to choose: If you prefer to update CI/test tooling or minor package bumps.
3. Multi-targeting (not recommended)
   - Description: Multi-target projects (e.g., net10.0;net8.0) for compatibility. More work and not necessary here.
   - When to choose: Libraries that must maintain older TFMs for downstream consumers.

## Recommended Selection
- **Upgrade Strategy**: No-op / Verify (source: assessment)
- **Target Framework**: net10.0 (source: assessment)
- **Action**: Run `dotnet build` and unit tests, then mark upgrade complete. No code changes required.

## Notes
- If you want package updates, we can apply them in a dedicated small task.
- If you prefer Guided flow, switch Flow Mode to guided before proceeding.
