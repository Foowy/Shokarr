using Microsoft.AspNetCore.Authorization;
using Shokarr.Controllers;
using Xunit;

namespace Shokarr.Tests;

public class AuthorizationTests
{
    [Fact]
    public void ApiControllers_RequireAdmin_AndNothingOptsOut()
    {
        var baseAuth = typeof(ShokarrBaseController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("admin", baseAuth.Policy);

        var controllers = typeof(ShokarrBaseController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(ShokarrBaseController)))
            .ToList();
        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        {
            Assert.False(controller.IsDefined(typeof(AllowAnonymousAttribute), true), controller.Name);
            Assert.All(controller.GetMethods(), m => Assert.False(m.IsDefined(typeof(AllowAnonymousAttribute), true), $"{controller.Name}.{m.Name}"));
        }
    }
}
