namespace PDFTemplateFiller.models
{
    /// <summary>
    /// Defines a single column of a table that will be rendered on top of the PDF template.
    /// </summary>
    public sealed class PdfTableColumn
    {
        /// <summary>
        /// Text shown in the header row of the table (leave null/empty to omit the header row entirely).
        /// </summary>
        public string? Header { get; set; }

        /// <summary>
        /// Column width in points (1 point = 1/72 inch). The sum of all column widths
        /// should not exceed the table's total <see cref="PdfTableDefinition.Width"/>.
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// Text alignment for cells in this column. Supported values: "Left", "Center", "Right".
        /// </summary>
        public string Alignment { get; set; } = "Left";
    }
}
