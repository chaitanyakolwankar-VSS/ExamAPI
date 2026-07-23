using System;
using System.Reflection;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Moq;
using ExamAPI.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ExamAPI.Tests
{
    public class DbModelDebugTests
    {
        [Fact]
        public void DebugModelCreation()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=localhost;Database=DummyDb;Trusted_Connection=True;TrustServerCertificate=True;")
                .Options;

            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();

            using (var context = new ApplicationDbContext(options, mockHttpContextAccessor.Object))
            {
                Console.WriteLine("1. Loading DbContext Model...");
                var model = context.Model;
                Console.WriteLine("Current DbContext Model loaded.");

                Console.WriteLine("2. Compiling Snapshot Model...");
                var assembly = typeof(ApplicationDbContext).Assembly;
                var snapshotType = assembly.GetType("ExamAPI.Migrations.ApplicationDbContextModelSnapshot");
                if (snapshotType == null)
                {
                    throw new Exception("ApplicationDbContextModelSnapshot type not found in assembly!");
                }
                
                var snapshot = Activator.CreateInstance(snapshotType);
                
                var modelProp = snapshotType.GetProperty("Model", BindingFlags.Instance | BindingFlags.Public);
                if (modelProp == null)
                {
                    throw new Exception("Model property not found on snapshot!");
                }
                
                var snapshotModel = (IModel)modelProp.GetValue(snapshot);
                
                var runtimeInitializer = context.GetService<IModelRuntimeInitializer>();
                var finalizedSnapshotModel = runtimeInitializer.Initialize(snapshotModel);

                foreach (var entity in finalizedSnapshotModel.GetEntityTypes())
                {
                    foreach (var property in entity.GetProperties())
                    {
                        try
                        {
                            var mapping = property.FindTypeMapping();
                            var columnType = property.GetColumnType();
                        }
                        catch (Exception ex)
                        {
                            // Wrap the error inside a custom Exception with the property name!
                            throw new Exception($"[FAIL_TARGET] Entity: {entity.Name}, Property: {property.Name}, Type: {property.ClrType.FullName}", ex);
                        }
                    }
                }
            }
        }
    }
}
