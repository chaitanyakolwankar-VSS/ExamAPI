using ExamAPI.Models;
using ExamAPI.Services.Common;
using ExamAPI.Services.Result.Engine.FactProviders;
using Xunit;

namespace ExamAPI.Tests
{
    public class ExamTypeKeysTests
    {
        [Theory]
        [InlineData("KT", "ATKT")]
        [InlineData("ATKT", "ATKT")]
        [InlineData("A.T.K.T", "ATKT")]
        [InlineData("a.t.k.t", "ATKT")]
        [InlineData(" kt ", "ATKT")]
        [InlineData("Regular", "REGULAR")]
        [InlineData("Reval", "REVAL")]
        [InlineData(null, "")]
        [InlineData("", "")]
        public void Canonical_CollapsesAtktSpellings(string? input, string expected) =>
            Assert.Equal(expected, ExamTypeKeys.Canonical(input));

        [Theory]
        [InlineData("KT", true)]
        [InlineData("ATKT", true)]
        [InlineData("A.T.K.T", true)]
        [InlineData("Regular", false)]
        [InlineData(null, false)]
        public void IsAtkt_MatchesEverySpelling(string? input, bool expected) =>
            Assert.Equal(expected, ExamTypeKeys.IsAtkt(input));
    }

    public class SemesterIdsTests
    {
        [Theory]
        [InlineData("Sem-10", "Sem-9", 1)]
        [InlineData("Sem-9", "Sem-10", -1)]
        [InlineData("Sem-6", "Sem-6", 0)]
        [InlineData("Sem-1", "Sem-2", -1)]
        public void Compare_IsNumeric(string a, string b, int expectedSign) =>
            Assert.Equal(expectedSign, Math.Sign(SemesterIds.Compare(a, b)));

        [Fact]
        public void Compare_FallsBackToStringCompareWhenNoNumber()
        {
            Assert.True(SemesterIds.Compare("Alpha", "Beta") < 0);
            Assert.True(SemesterIds.Compare("Sem-3", "Beta") > 0);
        }

        [Theory]
        [InlineData("Sem-10", 10)]
        [InlineData("Sem-6", 6)]
        [InlineData("Sem-", null)]
        [InlineData(null, null)]
        public void TrailingNumber_ParsesOrReturnsNull(string? input, int? expected) =>
            Assert.Equal(expected, SemesterIds.TrailingNumber(input));
    }

    public class QuotaFactProviderTests
    {
        [Theory]
        [InlineData("SP", 1.0)]
        [InlineData("sp", 1.0)]
        [InlineData("SPORTS", 1.0)]
        [InlineData("Sports", 1.0)]
        [InlineData("NSS", 0.0)]
        [InlineData(null, 0.0)]
        public async Task IsSports_AcceptsBothSpellings(string? quota, double expected)
        {
            var value = await new IsSportsProvider().GetValueAsync(null, new MarksMaster { QuotaType = quota });
            Assert.Equal(expected, value);
        }

        [Fact]
        public async Task Percentage_IsNotTruncatedToWholeNumber()
        {
            // 1 mark out of 3 is 33.33%, not the 33 that integer division produced.
            var creditsId = Guid.NewGuid();
            var master = new SubjectCreditMaster
            {
                CreditsId = creditsId,
                Credits = new List<SubjectCredits>
                {
                    new() { Id = Guid.NewGuid(), Head = "H1", HeadType = "ESE", HeadOutOf = "3", CreditsId = creditsId }
                }
            };
            var marks = new MarksMaster
            {
                StudentMarks = new List<StudentMarks>
                {
                    new() { Id = Guid.NewGuid(), Head = "H1", Marks = 1, CreditsId = creditsId, CreditMaster = master }
                }
            };

            var value = await new PercentageProvider().GetValueAsync(null, marks);

            Assert.Equal(100.0 / 3.0, value, 6);
        }
    }
}
