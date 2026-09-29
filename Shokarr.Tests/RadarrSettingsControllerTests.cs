using System.Net;
using Microsoft.AspNetCore.Mvc;
using Shokarr.Config;
using Shokarr.Controllers;
using Shokarr.Controllers.Api;
using Shokarr.Services;
using Xunit;

namespace Shokarr.Tests;

public class RadarrSettingsControllerTests
{
    private static FakeSettingsSource Stored() => new()
    {
        Sonarr = new SonarrSettings { BaseUrl = "http://sonarr", ApiKey = "sk" },
        Radarr = new RadarrSettings { BaseUrl = "http://radarr", ApiKey = "secret", QualityProfileId = 2, RootFolderPath = "/movies" },
    };

    [Fact]
    public void GetSettings_MasksKey()
    {
        var controller = new RadarrSettingsController(Stored(), null!);

        var ok = Assert.IsType<OkObjectResult>(controller.GetSettings());
        var data = Assert.IsType<ShokarrBaseController.ApiResponse<RadarrSettings>>(ok.Value).Data!;

        Assert.Equal("http://radarr", data.BaseUrl);
        Assert.Equal("********", data.ApiKey);
    }

    [Fact]
    public void SaveSettings_BlankKeyPreserved_SonarrUntouched()
    {
        var source = Stored();
        var controller = new RadarrSettingsController(source, null!);

        controller.SaveSettings(new RadarrSettings { BaseUrl = "HTTP://RADARR" });

        Assert.Equal("HTTP://RADARR", source.Radarr.BaseUrl);
        Assert.Equal("secret", source.Radarr.ApiKey);
        Assert.Equal(2, source.Radarr.QualityProfileId);
        Assert.Equal("/movies", source.Radarr.RootFolderPath);
        Assert.Equal("sk", source.Sonarr.ApiKey);
    }

    [Fact]
    public void SaveSettings_NewUrlWithBlankKey_IsRefusedAndNothingSaved()
    {
        var source = Stored();
        var controller = new RadarrSettingsController(source, null!);

        var ok = Assert.IsType<OkObjectResult>(controller.SaveSettings(new RadarrSettings { BaseUrl = "http://elsewhere" }));

        Assert.Equal(ArrUrlRules.KeyRequiredMessage, Assert.IsType<ShokarrBaseController.ApiResponse<object>>(ok.Value).Message);
        Assert.Equal("http://radarr", source.Radarr.BaseUrl);
        Assert.Equal("secret", source.Radarr.ApiKey);
    }

    [Fact]
    public async Task TestConnection_NewUrlWithBlankKey_NeverContactsHost()
    {
        var handler = new StubHandler("{}");
        var controller = new RadarrSettingsController(Stored(), new RadarrClient(new HttpClient(handler)));

        var ok = Assert.IsType<OkObjectResult>(await controller.TestConnection(new RadarrSettings { BaseUrl = "http://attacker" }));

        Assert.Equal(ArrUrlRules.KeyRequiredMessage, Assert.IsType<ShokarrBaseController.ApiResponse<object>>(ok.Value).Message);
        Assert.Equal(0, handler.Calls);
    }

    private class StubHandler(string json) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    [Fact]
    public async Task GetSavedQualityProfile_ResolvesNameFromRadarr()
    {
        var controller = new RadarrSettingsController(Stored(), new RadarrClient(new HttpClient(new StubHandler("""[{"id":1,"name":"Any"},{"id":2,"name":"HD-1080p"}]"""))));

        var ok = Assert.IsType<OkObjectResult>(await controller.GetSavedQualityProfile());
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(ok.Value);

        Assert.True(response.Success);
        var profile = Assert.IsType<ArrQualityProfileResource>(response.Data);
        Assert.Equal((2, "HD-1080p"), (profile.Id, profile.Name));
    }

    [Fact]
    public async Task GetSavedQualityProfile_ProfileGoneFromRadarr_ReportsFailure()
    {
        var controller = new RadarrSettingsController(Stored(), new RadarrClient(new HttpClient(new StubHandler("""[{"id":9,"name":"Other"}]"""))));

        var ok = Assert.IsType<OkObjectResult>(await controller.GetSavedQualityProfile());
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(ok.Value);

        Assert.False(response.Success);
    }
}
