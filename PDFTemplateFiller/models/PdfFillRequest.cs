namespace PDFTemplateFiller.models
{
    /// <summary>
    /// Top-level payload describing how a PDF template should be populated.
    ///
    /// Expected JSON shape:
    /// <code>
    /// {
    ///   "fields": {
    ///     "CustomerName": "Jane Doe",
    ///     "InvoiceNumber": "INV-2026-001"
    ///   },
    ///   "tables": [
    ///     {
    ///       "name": "OrderItems",
    ///       "page": 1,
    ///       "x": 50,
    ///       "y": 400,
    ///       "width": 500,
    ///       "rowHeight": 18,
    ///       "fontSize": 9,
    ///       "drawBorders": true,
    ///       "columns": [
    ///         { "header": "Item", "width": 260, "alignment": "Left" },
    ///         { "header": "Qty",  "width": 60,  "alignment": "Right" },
    ///         { "header": "Price","width": 180, "alignment": "Right" }
    ///       ],
    ///       "rows": [
    ///         ["Widget A", "2", "$10.00"],
    ///         ["Widget B", "1", "$25.00"]
    ///       ]
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </summary>
    public sealed class PdfFillRequest
    {
        /// <summary>
        /// Simple key/value placeholders. Every occurrence of "{{key}}" found as a single,
        /// unbroken text run inside the PDF's content stream is replaced with the given value.
        ///
        /// Known limitation: if the PDF-generation tool that produced the template split the
        /// placeholder text across multiple show-text operators (common with justified text,
        /// kerning, or certain fonts), the replacement will not be found. Keep template
        /// placeholders short, on their own line/run, and avoid heavy text justification
        /// around them to maximize the chance of a clean match.
        /// </summary>
        public Dictionary<string, string> Fields { get; set; } = new();

        /// <summary>
        /// Optional per-field text alignment, keyed by the same field name used in <see cref="Fields"/>
        /// (without the surrounding "{{ }}"). Values: "Left" (default if omitted), "Right", "Center".
        ///
        /// Why this exists: a placeholder's own bounding box only tells you where ITS text started
        /// - it says nothing about whether that position was meant to be a left edge, a right edge,
        /// or a center point (e.g. a right-aligned total amount has a bounding box that starts far
        /// to the left of the label above it, simply because the placeholder text itself was wide).
        /// A replacement value of a different length than the placeholder can only be positioned
        /// correctly relative to the ORIGINAL visual alignment if you tell us what that alignment
        /// was - there is no way to infer it purely from the template's glyph positions.
        /// </summary>
        public Dictionary<string, string>? FieldAlignments { get; set; } = new();

        /// <summary>
        /// Structured, multi-row content (tables) drawn on top of the template at explicit
        /// coordinates. See <see cref="PdfTableDefinition"/> for why coordinates are required
        /// instead of a "{{table:Name}}" placeholder.
        /// </summary>
        public List<PdfTableDefinition> Tables { get; set; } = new();
    }
}