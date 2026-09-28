using System.Net;
using Microsoft.AspNetCore.Mvc;
using ShokoArr.Config;
using ShokoArr.Controllers;
using ShokoArr.Controllers.Api;
using ShokoArr.Services;
using Xunit;

namespace ShokoArr.Tests;

public class SettingsControllerTests
{
    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static FakeSettingsSource Stored() => new()
    {
        Sonarr = new SonarrSettings { BaseUrl = "http://sonarr", ApiKey = "secret", QualityProfileId = 4, RootFolderPath = "/tv", NotificationWebhookUrl = "http://hook", ScanIntervalHours = 6 },
    };

    [Fact]
    public void GetSettings_MasksSecretsAndReturnsBaseUrl()
    {
        var controller = new SettingsController(Stored(), null!);

        var ok = Assert.IsType<OkObjectResult>(controller.GetSettings());
        var data = Assert.IsType<ShokoArrBaseController.ApiResponse<SonarrSettings>>(ok.Value).Data!;

        Assert.Equal("http://sonarr", data.BaseUrl);
        Assert.Equal("********", data.ApiKey);
        Assert.Equal("********", data.NotificationWebhookUrl);
        Assert.Equal(4, data.QualityProfileId);
        Assert.Equal(6, data.ScanIntervalHours);
        Assert.True(data.CountSonarrHeldAsMissing);
    }

    [Fact]
    public void SaveSettings_BlankFieldsKeepStoredValues()
    {
        var source = Stored();
        var controller = new SettingsController(source, null!);

        controller.SaveSettings(new SonarrSettings { BaseUrl = "http://sonarr/", ScanIntervalHours = 12 });

        Assert.Equal("http://sonarr/", source.Sonarr.BaseUrl);
        Assert.Equal(12, source.Sonarr.ScanIntervalHours);
        Assert.Equal("secret", source.Sonarr.ApiKey);
        Assert.Equal(4, source.Sonarr.QualityProfileId);
        Assert.Equal("/tv", source.Sonarr.RootFolderPath);
        Assert.Equal("http://hook", source.Sonarr.NotificationWebhookUrl);
    }

    [Fact]
    public void SaveSettings_NewKeyReplacesStored()
    {
        var source = Stored();
        var controller = new SettingsController(source, null!);

        controller.SaveSettings(new SonarrSettings { BaseUrl = "http://sonarr", ApiKey = "fresh", QualityProfileId = 9 });

        Assert.Equal("fresh", source.Sonarr.ApiKey);
        Assert.Equal(9, source.Sonarr.QualityProfileId);
    }

    [Fact]
    public async Task TestConnection_BlankKeyUsesStoredKey()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var controller = new SettingsController(Stored(), new SonarrClient(new HttpClient(handler)));

        var ok = Assert.IsType<OkObjectResult>(await controller.TestConnection(new SonarrSettings { BaseUrl = "http://sonarr" }));

        Assert.True(Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value).Success);
        Assert.Equal("secret", handler.LastRequest!.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public void SaveSettings_NewUrlWithBlankKey_IsRefusedAndNothingSaved()
    {
        var source = Stored();
        var controller = new SettingsController(source, null!);

        var ok = Assert.IsType<OkObjectResult>(controller.SaveSettings(new SonarrSettings { BaseUrl = "http://elsewhere" }));

        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value);
        Assert.False(response.Success);
        Assert.Equal(ArrUrlRules.KeyRequiredMessage, response.Message);
        Assert.Equal("http://sonarr", source.Sonarr.BaseUrl);
        Assert.Equal("secret", source.Sonarr.ApiKey);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("sonarr:8989")]
    public void SaveSettings_NonHttpUrl_IsRefused(string url)
    {
        var source = Stored();
        var controller = new SettingsController(source, null!);

        var ok = Assert.IsType<OkObjectResult>(controller.SaveSettings(new SonarrSettings { BaseUrl = url, ApiKey = "k" }));

        Assert.Equal(ArrUrlRules.InvalidUrlMessage, Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value).Message);
        Assert.Equal("http://sonarr", source.Sonarr.BaseUrl);
    }

    [Fact]
    public void SaveSettings_NonHttpWebhook_IsRefused()
    {
        var source = Stored();
        var controller = new SettingsController(source, null!);

        var ok = Assert.IsType<OkObjectResult>(controller.SaveSettings(new SonarrSettings { BaseUrl = "http://sonarr", NotificationWebhookUrl = "javascript:x" }));

        Assert.False(Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value).Success);
        Assert.Equal("http://hook", source.Sonarr.NotificationWebhookUrl);
    }

    [Fact]
    public async Task TestConnection_NewUrlWithBlankKey_NeverSendsStoredKey()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var controller = new SettingsController(Stored(), new SonarrClient(new HttpClient(handler)));

        var ok = Assert.IsType<OkObjectResult>(await controller.TestConnection(new SonarrSettings { BaseUrl = "http://attacker" }));

        Assert.Equal(ArrUrlRules.KeyRequiredMessage, Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value).Message);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SonarrOptions_NewUrlWithBlankKey_NeverSendsStoredKey()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        var controller = new SettingsController(Stored(), new SonarrClient(new HttpClient(handler)));

        var ok = Assert.IsType<OkObjectResult>(await controller.GetSonarrOptions(new SonarrSettings { BaseUrl = "http://attacker" }));

        Assert.False(Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value).Success);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public void SaveSettings_CountSonarrHeldAsMissingFalse_RoundTrips()
    {
        var source = Stored();
        var controller = new SettingsController(source, null!);

        controller.SaveSettings(new SonarrSettings { BaseUrl = "http://sonarr", CountSonarrHeldAsMissing = false });

        Assert.False(source.Sonarr.CountSonarrHeldAsMissing);
        var ok = Assert.IsType<OkObjectResult>(controller.GetSettings());
        Assert.False(Assert.IsType<ShokoArrBaseController.ApiResponse<SonarrSettings>>(ok.Value).Data!.CountSonarrHeldAsMissing);
    }
}
