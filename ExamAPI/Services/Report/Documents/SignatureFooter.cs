using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ExamAPI.Services.Report.Documents
{
    /// <summary>
    /// Marksheet footer: the result date on the left, then the Controller of Examinations and the
    /// Principal, each with their signature image from College Details above a rule. A missing image
    /// leaves the space blank so the sheet can still be signed by hand.
    /// </summary>
    internal static class SignatureFooter
    {
        private const float SignatureHeight = 34;

        public static void Compose(IContainer container, DateTime resultDate, byte[]? controllerSignature, byte[]? principalSignature)
        {
            container.PaddingTop(20).Row(row =>
            {
                row.RelativeItem().AlignLeft().AlignBottom().Text($"Date: {resultDate:dd/MM/yyyy}").FontSize(10);
                row.ConstantItem(150).Element(c => Signatory(c, controllerSignature, "Controller of Examinations"));
                row.ConstantItem(30);
                row.ConstantItem(130).Element(c => Signatory(c, principalSignature, "Principal"));
            });
        }

        private static void Signatory(IContainer container, byte[]? signature, string title)
        {
            container.Column(col =>
            {
                var slot = col.Item().Height(SignatureHeight);
                if (signature is { Length: > 0 })
                    slot.AlignCenter().AlignBottom().Image(signature).FitArea();

                col.Item().BorderTop(0.75f).PaddingTop(3).AlignCenter().Text(title).FontSize(9).Bold();
            });
        }
    }
}
