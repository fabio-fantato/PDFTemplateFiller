namespace PDFTemplateFiller.models
{
    /// <summary>
    /// Describes a table (or any repeating multi-line block) that should be drawn on top of the
    /// PDF template starting at a fixed anchor position.
    ///
    /// Design note: PDFsharp cannot search an existing PDF's content stream for a placeholder and
    /// then "grow" a table in place - PDF content streams are flat drawing instructions, not a
    /// reflow-capable document model. Because of that, the anchor position (Page/X/Y) must be
    /// supplied explicitly by the caller (e.g. measured once from the template in a PDF editor),
    /// rather than being discovered automatically from a "{{table:Name}}" placeholder in the file.
    /// </summary>
    public sealed class PdfTableDefinition
    {
        /// <summary>
        /// Free-text identifier for this table, used only for error messages/logging.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 1-based page number (matching the page numbering of the incoming PDF) where the table starts.
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// X coordinate (in points) of the table's top-left corner, measured from the left edge of the page.
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y coordinate (in points) of the table's top-left corner, measured from the TOP of the page
        /// (this class converts to PDFsharp's bottom-left origin internally).
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// Total width of the table in points. Should roughly equal the sum of all column widths.
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// Height of every row (header and data) in points.
        /// </summary>
        public double RowHeight { get; set; } = 20;

        /// <summary>
        /// Whether to draw grid lines around cells.
        /// </summary>
        public bool DrawBorders { get; set; } = true;

        /// <summary>
        /// Font size, in points, used for cell text.
        /// </summary>
        public double FontSize { get; set; } = 9;

        /// <summary>
        /// Column definitions, left to right.
        /// </summary>
        public List<PdfTableColumn> Columns { get; set; } = new();

        /// <summary>
        /// Row data. Each inner list must have the same number of items as <see cref="Columns"/>.
        /// </summary>
        public List<List<string>> Rows { get; set; } = new();

        /// <summary>
        /// If the rows do not fit within the remaining space on <see cref="Page"/>, continuation
        /// pages are appended, cloning the same template page as a visual background so the
        /// letterhead/branding is preserved. Set to false to instead clip/truncate silently.
        /// </summary>
        public bool AllowPageOverflow { get; set; } = true;
    }
}
