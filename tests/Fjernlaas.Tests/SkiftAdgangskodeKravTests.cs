using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Fjernlaas.Tests;

public class SkiftAdgangskodeKravTests
{
    [Theory]
    [InlineData("/skift-adgangskode", true)]
    [InlineData("/Skift-Adgangskode", true)]
    [InlineData("/logout", true)]
    [InlineData("/_framework/blazor.web.js", true)]
    [InlineData("/app.css", true)]
    [InlineData("/favicon.ico", true)]
    [InlineData("/", false)]
    [InlineData("/organisationer", false)]
    [InlineData("/brugere", false)]
    [InlineData("/login", false)]
    public void Tilladt_slipper_kun_skift_logout_og_filer_igennem(string sti, bool forventet)
    {
        Assert.Equal(forventet, SkiftAdgangskodeKrav.Tilladt(new PathString(sti)));
    }
}
