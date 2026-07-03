# 02-run-tests: Run unit tests

- Done when: `dotnet test` succeeds and tests pass.

## Scope Inventory
- Projects affected: PDFTemplateFillerTest.csproj (tests), depends on PDFTemplateFiller.csproj

## Research Summary
- Assessment shows test project targets net10.0 and is SDK-style.
- No package compatibility issues reported.

## Execution Steps
1. Run `dotnet test` for the solution or test project.
2. Capture test results and ensure all tests pass.

## Done when
- `dotnet test` returns success and no failing tests.
