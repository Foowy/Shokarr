using Microsoft.AspNetCore.Authorization;
using ShokoArr.Controllers;
using Xunit;

namespace ShokoArr.Tests;

public class AuthorizationTests
{
    [Fact]
    public void ApiControllers_RequireAdmin_AndNothingOptsOut()
    {
        var baseAuth = typeof(ShokoArrBaseController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("admin", baseAuth.Policy);

        var controllers = typeof(ShokoArrBaseController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(ShokoArrBaseController)))
            .ToList();
        Assert.NotEmpty(controllers);
        foreach (var controller in controllers)
        {
            Assert.False(controller.IsDefined(typeof(AllowAnonymousAttribute), true), controller.Name);
            Assert.All(controller.GetMethods(), m => Assert.False(m.IsDefined(typeof(AllowAnonymousAttribute), true), $"{controller.Name}.{m.Name}"));
        }
    }
}
