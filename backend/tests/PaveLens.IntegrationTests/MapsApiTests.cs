using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace PaveLens.IntegrationTests;

public class MapsApiTests
{
    private const string FakeKey = "test-maps-key-not-real";

    private sealed class MapsFactory : PaveLensApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("GoogleMaps:BrowserKey", FakeKey);
        }
    }

    [Fact]
    public async Task GivesTheBrowserKeyOnlyToTheAppsOwnPages()
    {
        await using var factory = new MapsFactory();
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        // A script or another tool: no Fetch Metadata, no Referer.
        using var bare = await client.GetAsync("/api/v1/maps/config", ct);
        Assert.Equal(HttpStatusCode.OK, bare.StatusCode);
        Assert.Contains("no-store", bare.Headers.CacheControl!.ToString());
        Assert.DoesNotContain(FakeKey, await bare.Content.ReadAsStringAsync(ct));

        // Another website embedding the API.
        using var crossSite = new HttpRequestMessage(HttpMethod.Get, "/api/v1/maps/config");
        crossSite.Headers.Add("Sec-Fetch-Site", "cross-site");
        crossSite.Headers.Referrer = new Uri("https://evil.example/");
        using var cross = await client.SendAsync(crossSite, ct);
        Assert.DoesNotContain(FakeKey, await cross.Content.ReadAsStringAsync(ct));

        // Same-origin metadata but a foreign Referer.
        using var spoofed = new HttpRequestMessage(HttpMethod.Get, "/api/v1/maps/config");
        spoofed.Headers.Add("Sec-Fetch-Site", "same-origin");
        spoofed.Headers.Referrer = new Uri("https://evil.example/page");
        using var spoof = await client.SendAsync(spoofed, ct);
        Assert.DoesNotContain(FakeKey, await spoof.Content.ReadAsStringAsync(ct));

        // The web app itself.
        using var own = new HttpRequestMessage(HttpMethod.Get, "/api/v1/maps/config");
        own.Headers.Add("Sec-Fetch-Site", "same-origin");
        own.Headers.Referrer = new Uri("http://localhost/");
        using var ok = await client.SendAsync(own, ct);
        var config = await ok.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(config.GetProperty("googleAvailable").GetBoolean());
        Assert.Equal(FakeKey, config.GetProperty("browserKey").GetString());
        Assert.Equal("osm", config.GetProperty("mapProvider").GetString()); // OpenStreetMap stays the default
        Assert.False(config.GetProperty("googleRouting").GetBoolean()); // no server key: no Google calls
        Assert.Contains("openstreetmap", config.GetProperty("tileUrl").GetString());
    }

    [Fact]
    public async Task RejectsAnEmptyCrewRoute()
    {
        await using var factory = new MapsFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/maps/crew-route", new { inspectionIds = Array.Empty<Guid>() }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
