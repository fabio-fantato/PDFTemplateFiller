# Upgrade Plan

## Summary
Projects already target net10.0. Plan will verify build and run tests to confirm upgrade status.

## Tasks
1. 01-verify-build — Verify solution builds
   - Done when: `dotnet build` succeeds for the solution in Release configuration.
2. 02-run-tests — Run unit tests
   - Done when: `dotnet test` succeeds and tests pass.

## Notes
- No code changes expected; if build or tests fail, investigate and fix as separate tasks.
