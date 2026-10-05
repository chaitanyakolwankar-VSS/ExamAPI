using ClosedXML.Excel;

namespace ExamAPI.Services.Report
{
    /// <summary>Small ClosedXML helpers shared by the Excel exports.</summary>
    public static class ExcelStyles
    {
        /// <summary>
        /// ClosedXML adds this cell padding to every column width it writes. The exports' widths are the
        /// stored Excel values (what the earlier EPPlus code wrote), so <see cref="SetWidth"/> takes it off.
        /// </summary>
        public const double WidthPadding = 0.710625;

        /// <summary>A new workbook whose default font is Aptos Narrow 11, the font the exports have always used.</summary>
        public static XLWorkbook NewWorkbook()
        {
            var workbook = new XLWorkbook();
            workbook.Style.Font.FontName = "Aptos Narrow";
            workbook.Style.Font.FontSize = 11;
            return workbook;
        }

        /// <summary>Sets a column to the given stored Excel width.</summary>
        public static void SetWidth(IXLColumn column, double width) => column.Width = width - WidthPadding;

        /// <summary>The column's stored Excel width (the inverse of <see cref="SetWidth"/>).</summary>
        public static double StoredWidth(IXLColumn column) => column.Width + WidthPadding;

        /// <summary>
        /// Sizes columns to their longest text (stored widths; never below Excel's default 8.43). The width is
        /// estimated from the characters themselves rather than measured with a font, so the result does not
        /// depend on which fonts the server has (ClosedXML's own AdjustToContents does, and comes out too
        /// narrow without Aptos Narrow). Calibrated against the widths the earlier EPPlus exports produced:
        /// within about a character, never noticeably narrower. Like Excel's autofit, merged and hidden cells
        /// are skipped.
        /// </summary>
        public static void AutoFit(IXLWorksheet sheet, int firstColumn, int lastColumn, int firstRow, int lastRow)
        {
            for (var c = firstColumn; c <= lastColumn; c++)
            {
                var column = sheet.Column(c);
                if (column.IsHidden) continue;

                double widest = 0;
                for (var r = firstRow; r <= lastRow; r++)
                {
                    var cell = sheet.Cell(r, c);
                    if (cell.Value.IsBlank || cell.IsMerged()) continue;
                    var font = cell.Style.Font;
                    foreach (var line in cell.GetFormattedString().Split('\n'))
                        widest = Math.Max(widest, TextWidth(line, font.Bold, font.FontSize));
                }
                SetWidth(column, Math.Max(8.43, widest + 3));
            }
        }

        /// <summary>Approximate width of a line of text in Excel column units (11pt narrow sans-serif as the base).</summary>
        public static double TextWidth(string text, bool bold, double fontSize)
        {
            double w = 0;
            foreach (var ch in text)
            {
                w += ch switch
                {
                    >= 'A' and <= 'Z' => 1.3,
                    >= 'a' and <= 'z' => 0.95,
                    >= '0' and <= '9' => 1.1,
                    ' ' => 0.55,
                    '.' or ',' or ':' or ';' or '|' or '!' or '\'' => 0.5,
                    '@' => 1.8,
                    '%' or '&' => 1.5,
                    _ => 0.8,
                };
            }
            return w * (bold ? 1.08 : 1) * fontSize / 11;
        }

        /// <summary>Excel's "Normal" margins (inches), which a sheet without its own margins prints with.</summary>
        public static void NormalMargins(IXLWorksheet sheet, double left = 0.7, double right = 0.7)
        {
            var m = sheet.PageSetup.Margins;
            m.Left = left; m.Right = right; m.Top = 0.75; m.Bottom = 0.75; m.Header = 0.3; m.Footer = 0.3;
        }

        /// <summary>A thin border on all four sides of every cell the style covers; black unless a colour is given.</summary>
        public static void ThinBorders(IXLStyle style, XLColor? color = null)
        {
            var border = style.Border;
            border.TopBorder = border.BottomBorder = border.LeftBorder = border.RightBorder = XLBorderStyleValues.Thin;
            if (color != null)
            {
                border.TopBorderColor = border.BottomBorderColor = border.LeftBorderColor = border.RightBorderColor = color;
            }
        }

        public static byte[] ToBytes(XLWorkbook workbook)
        {
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        /// <summary>A cell's content as text, as the import reads it: numbers in invariant form, blank as null.</summary>
        public static string? Text(IXLCell cell)
        {
            var value = cell.Value;
            if (value.IsBlank) return null;
            if (value.IsNumber) return value.GetNumber().ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value.IsText) return value.GetText();
            return value.ToString();
        }
    }
}
