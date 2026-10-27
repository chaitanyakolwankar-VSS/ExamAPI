using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;

namespace ExamAPI.Tests;

/// <summary>The Swagger page builds: two request classes with the same name (in different controllers) break it with a 500.</summary>
public sealed class SwaggerDocumentTests
{
    [Fact]
    public void The_api_description_builds()
    {
        using var factory = new AccessAuditFactory();
        factory.CreateClient();

        var document = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.NotEmpty(document.Paths);
    }
}
