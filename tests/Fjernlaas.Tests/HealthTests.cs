using System.Net;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Fjernlaas.Tests;

public class HealthTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
