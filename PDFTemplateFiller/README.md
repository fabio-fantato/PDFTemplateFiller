# PDFTemplateFiller

OutSystems ODC Custom Code (External Library) that fills a PDF template with real data, using
PDFsharp (MIT license, v6.2.4).

## What it does

Given:
- A PDF template as binary data.
- A JSON payload describing values to insert.

It returns the filled PDF as binary data, via the `FillPdfTemplate` OutSystems Server Action
exposed by `IPdfTemplateFillerActions`.

Two mechanisms are combined, because a single one cannot satisfy both "use `{{key}}` notation"
and "support tables":

1. **Simple fields** (`fields` in the JSON) — literal `{{key}}` tokens are replaced directly
   inside the PDF's content stream. Fast, matches the notation, but only reliable for short,
   single-line, single-font-run placeholders. See the header comment in
   `services/ContentStreamTextReplacer.cs` for exactly when this can silently fail to match.

2. **Tables / multi-line blocks** (`tables` in the JSON) — drawn on top of the template at
   explicit page/X/Y coordinates using PDFsharp's `XGraphics`, with automatic pagination if a
   table has more rows than fit on its starting page. See `services/PdfTableRenderer.cs`.

## Why not just scan the PDF for `{{table:Name}}` and auto-detect the position?

PDF content streams are flat drawing instructions, not a document object model — there is no
built-in way to ask "where on the page does this text token render?" without a full
text-extraction-with-positioning pass (which PDFsharp does not expose out of the box).
Requiring explicit coordinates is more setup work up front, but it is honest about what
PDFsharp can reliably do, rather than pretending automatic table placement works when it does not.

## Project structure

```
PDFTemplateFiller/
├── interfaces/IPdfTemplateFillerActions.cs   OSInterface/OSAction - the ODC-exposed contract
├── actions/PdfTemplateFillerActions.cs       Implementation (out resultFile/success/errorMessage)
├── services/                                 Internal PDF manipulation logic (not exposed to ODC)
│   ├── IPdfTemplateFillerService.cs
│   ├── PdfTemplateFillerService.cs           Orchestrates: parse JSON -> replace fields -> render tables
│   ├── ContentStreamTextReplacer.cs          "{{key}}" substitution inside content streams
│   └── PdfTableRenderer.cs                   Table/multi-line block drawing + pagination
└── models/                                   Plain DTOs for JSON (de)serialization, not OSStructures
    ├── PdfFillRequest.cs
    ├── PdfTableDefinition.cs
    └── PdfTableColumn.cs
```

## Verified facts (checked July 2026, with sources)

- `.NET 8.0` is the current target framework documented for OutSystems ODC Custom Code
  libraries (`success.outsystems.com/documentation/outsystems_developer_cloud/building_apps/
  extend_your_apps_with_custom_code/upgrading_custom_code_libraries_to_net_8/`). No newer
  version is documented for ODC as of this writing, even though PDFsharp itself now also
  supports net9.0/net10.0.
- `OutSystems.ExternalLibraries.SDK` version `1.5.0` is current on NuGet
  (`nuget.org/packages/OutSystems.ExternalLibraries.SDK`).
- `PDFsharp` version `6.2.4` is current on NuGet (`nuget.org/packages/PDFSharp`), MIT licensed.
- `PdfDictionary.PdfStream.UnfilteredValue` is **get-only** in PDFsharp (confirmed against the
  empira/PDFsharp source on GitHub) — to read AND write back content-stream bytes, call
  `stream.TryUnfilter()` first, then read/write through the get/set `Value` property instead.
- `XPdfForm.FromStream(Stream)` exists and is used here (instead of `XPdfForm.FromFile` with a
  temp file) so continuation-page cloning works entirely in memory, since ODC's hosting
  environment may have a read-only or restricted filesystem.

## Testing checklist before production use

- [ ] Confirm `dotnet restore && dotnet build` succeeds with the pinned package versions above.
- [ ] Test a template with a `{{key}}` placeholder produced by your actual PDF-generation
      pipeline (Word export, LibreOffice export, etc.) — font/kerning behavior varies by tool.
- [ ] Test a table that overflows onto a second page, and visually confirm the letterhead
      /background repeats correctly on the continuation page.
- [ ] Upload the compiled library to the ODC Portal and confirm the `[OSInterface]` icon/name/
      description render as expected before consuming it from an app.
