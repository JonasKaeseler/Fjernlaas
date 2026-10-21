using Fjernlaas.Web.Services;
using Xunit;

namespace Fjernlaas.Tests;

public class ReturnUrlTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("/", "/")]
    [InlineData("/organisationer", "/organisationer")]
    [InlineData("/organisationer?side=2", "/organisationer?side=2")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("http://evil.example/x", "/")]
    [InlineData("evil.example", "/")]
    [InlineData("/ok\r\nSet-Cookie: x=1", "/")]
    public void Sikker_accepterer_kun_lokale_stier(string? input, string forventet)
    {
        Assert.Equal(forventet, ReturnUrl.Sikker(input));
    }
}
