using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ExamAPI.Services.Report.Documents
{
    /// <summary>
    /// Shared PDF header layout for college branding (DEC-14): the college logo left of the title
    /// when there is one, otherwise just the text. The title stays centred either way.
    /// </summary>
    internal static class BrandedHeader
    {
        public static void Compose(IContainer container, byte[]? logo, float logoHeight, Action<IContainer> title)
        {
            if (logo == null || logo.Length == 0)
            {
                container.Element(title);
                return;
            }

            var logoWidth = logoHeight * 1.5f;
            container.Row(row =>
            {
                row.ConstantItem(logoWidth).Height(logoHeight).AlignMiddle().AlignLeft().Image(logo).FitArea();
                row.RelativeItem().AlignMiddle().Element(title);
                // Mirror the logo column so the title remains centred on the page.
                row.ConstantItem(logoWidth);
            });
        }
    }
}
