using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using ExamAPI.Services.Result.Engine.ActionHandlers;
using ExamAPI.Models;

namespace ExamAPI.Tests
{
    public class AddGraceHandlerTests
    {
        [Fact]
        public async Task ExecuteAsync_AppliesGraceToHeadWiseSubject()
        {
            // Arrange
            var handler = new AddGraceHandler();
            
            // Create a fake student marks record (Scored 38, needs 40 to pass, meaning 2 grace marks needed)
            var studentMarks = new StudentMarks
            {
                Id = Guid.NewGuid(),
                Head = "TH",
                RawMarks = 38,
                Marks = 38,
                Remark = "Fail",
                CreditMaster = new SubjectCreditMaster 
                {
                    Credits = new List<SubjectCredits>
                    {
                        new SubjectCredits { Head = "TH", HeadPass = "40", HeadOutOf = "100" }
                    }
                }
            };

            var marksMaster = new MarksMaster
            {
                StudentMarks = new List<StudentMarks> { studentMarks }
            };

            // Create a rule action that gives up to 5 fixed grace marks
            var ruleAction = new RuleAction
            {
                ActionType = "AddGrace",
                Param1Type = "Fixed",
                Param1Value = 5,
                MaxLimit = 5
            };

            // Act
            // Symbol '$' represents the Ordinance symbol for this grace rule
            await handler.ExecuteAsync(marksMaster, ruleAction, "$");

            // Assert
            Assert.Equal(40, studentMarks.Marks); // 38 + 2 grace = 40
            Assert.Equal("2$", studentMarks.Grace); // Should record "2$" as the grace applied
        }

        [Fact]
        public async Task ExecuteAsync_DoesNotApplyGraceIfExceedsLimit()
        {
            // Arrange
            var handler = new AddGraceHandler();
            
            // Student scored 30, needs 40 to pass (10 grace needed)
            var studentMarks = new StudentMarks
            {
                Id = Guid.NewGuid(),
                Head = "TH",
                RawMarks = 30,
                Marks = 30,
                Remark = "Fail",
                CreditMaster = new SubjectCreditMaster 
                {
                    Credits = new List<SubjectCredits>
                    {
                        new SubjectCredits { Head = "TH", HeadPass = "40", HeadOutOf = "100" }
                    }
                }
            };

            var marksMaster = new MarksMaster
            {
                StudentMarks = new List<StudentMarks> { studentMarks }
            };

            // Rule only gives max 5 grace marks
            var ruleAction = new RuleAction
            {
                ActionType = "AddGrace",
                Param1Type = "Fixed",
                Param1Value = 5,
                MaxLimit = 5
            };

            // Act
            await handler.ExecuteAsync(marksMaster, ruleAction, "$");

            // Assert
            Assert.Equal(30, studentMarks.Marks); // Marks should remain unchanged
            Assert.Null(studentMarks.Grace); // No grace should be applied
        }
    }
}
