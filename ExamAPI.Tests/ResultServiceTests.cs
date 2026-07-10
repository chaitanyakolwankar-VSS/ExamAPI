using System;
using System.Reflection;
using Xunit;
using ExamAPI.Services.Result;
using ExamAPI.Services.Result.Engine;
using ExamAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Moq;

namespace ExamAPI.Tests
{
    public class ResultServiceTests
    {
        private readonly ResultService _resultService;

        public ResultServiceTests()
        {
            // Arrange: Setup in-memory DB and dummy registry so we can instantiate the service
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
                
            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
            var context = new ApplicationDbContext(options, mockHttpContextAccessor.Object);
            var registry = new EngineRegistry(Array.Empty<IFactProvider>(), Array.Empty<IActionHandler>());
            
            _resultService = new ResultService(context, registry);
        }

        [Theory]
        [InlineData(40, "==", "40", true)]
        [InlineData(40, "Equals", "40", true)]
        [InlineData(39, ">=", "40", false)]
        [InlineData(40, ">=", "40", true)]
        [InlineData(45, ">", "40", true)]
        [InlineData(35, "<", "40", true)]
        [InlineData(40, "!=", "40", false)]
        [InlineData(39, "NotEquals", "40", true)]
        public void CompareValues_EvaluatesCorrectly(double factValue, string op, string targetValueStr, bool expected)
        {
            // Act
            // Since CompareValues is private, we use Reflection to call it for testing purposes
            var methodInfo = typeof(ResultService).GetMethod("CompareValues", BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (methodInfo == null)
            {
                Assert.Fail("Could not find private method CompareValues on ResultService.");
            }

            var result = (bool)methodInfo.Invoke(_resultService, new object[] { factValue, op, targetValueStr })!;

            // Assert
            Assert.Equal(expected, result);
        }
    }
}
