using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PDFTemplateFiller.models;

namespace PDFTemplateFiller.services
{
    /// <summary>
    /// Draws structured, multi-row tables on top of an existing PDF page using PDFsharp's
    /// XGraphics drawing surface. This is the mechanism used for anything that needs real layout
    /// (tables, multi-line blocks) since PDFsharp cannot re-flow existing page content.
    ///
    /// If a table has more rows than fit in the remaining vertical space on its starting page,
    /// this renderer appends new pages sized identically to the template's last page and continues
    /// drawing there, so long tables paginate cleanly instead of overflowing off the page edge.
    /// </summary>
    public static class PdfTableRenderer
    {
        public static void RenderTable(PdfDocument document, PdfTableDefinition table)
        {
            if (table.Page < 1 || table.Page > document.PageCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(table),
                    $"Table '{table.Name}' references page {table.Page}, but the document only has {document.PageCount} page(s).");
            }

            // A table with no column definitions has nothing meaningful to draw (no widths, no
            // alignment) - skip it rather than throw, consistent with treating an absent/malformed
            // optional structure as "nothing to do" rather than a hard failure.
            if (table.Columns is null || table.Columns.Count == 0)
            {
                return;
            }

            List<List<string>> rows = table.Rows ?? new List<List<string>>();

            var font = FontHelper.GetSafeXFont(document, "Helvetica", table.FontSize, isBold: false);
            var headerFont = FontHelper.GetSafeXFont(document, "Helvetica-Bold", table.FontSize, isBold: true);

            int templatePageIndex = table.Page - 1;
            PdfPage currentPage = document.Pages[templatePageIndex];
            XGraphics graphics = XGraphics.FromPdfPage(currentPage);

            // PDFsharp's XGraphics origin is the TOP-LEFT of the page by default (unlike raw PDF
            // content streams, which are bottom-left). This keeps the "Y measured from the top"
            // convention documented on PdfTableDefinition consistent for callers.
            double currentY = table.Y;
            double pageBottomMargin = 36; // 0.5 inch safety margin before we roll to a new page.
            double pageHeight = currentPage.Height.Point;

            if (table.Columns.Any(column => !string.IsNullOrEmpty(column.Header)))
            {
                currentY = DrawRow(
                    graphics,
                    table.Columns.Select(column => column.Header ?? string.Empty).ToList(),
                    table,
                    headerFont,
                    table.X,
                    currentY,
                    isHeader: true);
            }

            foreach (List<string>? rawRow in rows)
            {
                // A row with fewer cells than columns (missing trailing values), more cells than
                // columns (extras), or an entirely null row entry are all treated as "some data is
                // missing" rather than a caller error worth failing the whole PDF generation over -
                // missing cells render as blank, extra cells are ignored.
                List<string> row = NormalizeRow(rawRow, table.Columns.Count);

                bool rowFitsOnCurrentPage = currentY + table.RowHeight <= pageHeight - pageBottomMargin;

                if (!rowFitsOnCurrentPage)
                {
                    if (!table.AllowPageOverflow)
                    {
                        break; // Truncate silently, as requested by the caller.
                    }

                    currentPage = AppendContinuationPage(document, currentPage, templatePageIndex);
                    graphics = XGraphics.FromPdfPage(currentPage);
                    currentY = table.Y; // Restart from the same top offset on the new page.
                }

                currentY = DrawRow(graphics, row, table, font, table.X, currentY, isHeader: false);
            }
        }

        private static List<string> NormalizeRow(List<string>? rawRow, int expectedCellCount)
        {
            var normalized = new List<string>(expectedCellCount);
            for (int i = 0; i < expectedCellCount; i++)
            {
                string? value = rawRow is not null && i < rawRow.Count ? rawRow[i] : null;
                normalized.Add(value ?? string.Empty);
            }

            return normalized;
        }

        private static double DrawRow(
            XGraphics graphics,
            List<string> cellValues,
            PdfTableDefinition table,
            XFont font,
            double startX,
            double startY,
            bool isHeader)
        {
            double x = startX;
            var textFormat = new XStringFormat { LineAlignment = XLineAlignment.Center };

            for (int columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                PdfTableColumn column = table.Columns[columnIndex];
                var cellRect = new XRect(x, startY, column.Width, table.RowHeight);

                if (table.DrawBorders)
                {
                    graphics.DrawRectangle(XPens.Black, cellRect);
                }

                if (isHeader)
                {
                    graphics.DrawRectangle(XBrushes.LightGray, cellRect);
                    if (table.DrawBorders)
                    {
                        graphics.DrawRectangle(XPens.Black, cellRect);
                    }
                }

                textFormat.Alignment = column.Alignment switch
                {
                    "Right" => XStringAlignment.Far,
                    "Center" => XStringAlignment.Center,
                    _ => XStringAlignment.Near
                };

                // Small inset so text does not touch the cell border.
                var textRect = new XRect(cellRect.X + 3, cellRect.Y, cellRect.Width - 6, cellRect.Height);
                graphics.DrawString(cellValues[columnIndex], font, XBrushes.Black, textRect, textFormat);

                x += column.Width;
            }

            return startY + table.RowHeight;
        }

        /// <summary>
        /// Appends a new page to the document, cloning the visual background of the original
        /// template page at <paramref name="templatePageIndex"/> (always the SAME source page,
        /// even when chaining multiple continuation pages) so the letterhead/branding continues
        /// onto every extra page created for table overflow.
        ///
        /// This clones the page entirely in memory (MemoryStream + XPdfForm.FromStream) rather than
        /// round-tripping through a temp file on disk, since ODC's hosting environment may have a
        /// read-only or restricted filesystem.
        /// </summary>
        private static PdfPage AppendContinuationPage(PdfDocument document, PdfPage templatePage, int templatePageIndex)
        {
            // Snapshot the in-progress document to a MemoryStream so we can re-open it as a
            // separate, read-only XPdfForm "stamp" - this is the standard PDFsharp technique for
            // reusing an existing page's visual content on a new page of the SAME document.
            using var snapshotStream = new MemoryStream();
            document.Save(snapshotStream, closeStream: false);
            snapshotStream.Position = 0;

            // XPdfForm treats an existing page as a reusable image-like object that can be drawn
            // onto another page. PageNumber is 1-based, so convert from the 0-based page index.
            XPdfForm importedPageForm = XPdfForm.FromStream(snapshotStream);
            importedPageForm.PageNumber = templatePageIndex + 1;

            var newPage = document.AddPage();
            newPage.Width = templatePage.Width;
            newPage.Height = templatePage.Height;

            using XGraphics graphics = XGraphics.FromPdfPage(newPage);
            graphics.DrawImage(importedPageForm, 0, 0, newPage.Width.Point, newPage.Height.Point);

            return newPage;
        }
    }
}