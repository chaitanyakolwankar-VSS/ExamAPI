using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ExamAPI.Services.Report.Documents
{
    /// <summary>
    /// Shared PDF header layout for college branding (DEC-14, DEC-19). Fallback order: the college
    /// banner as a centred full-width letterhead (replaces the name text), else the logo left of the
    /// title, else just the title text. The title stays centred either way.
    /// </summary>
    internal static class BrandedHeader
    {
        public static void Compose(
            IContainer container, byte[]? banner, float bannerMaxHeight,
            byte[]? logo, float logoHeight, Action<IContainer> title)
        {
            if (banner is { Length: > 0 })
            {
                // Fit the width, capped at bannerMaxHeight, aspect ratio kept, centred.
                container.Height(bannerMaxHeight).AlignCenter().AlignMiddle().Image(banner).FitArea();
                return;
            }

            Compose(container, logo, logoHeight, title);
        }

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
