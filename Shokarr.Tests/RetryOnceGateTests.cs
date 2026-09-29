using Shokarr.Services;
using Xunit;

namespace Shokarr.Tests;

public class RetryOnceGateTests
{
    [Fact]
    public void Run_FailureIsRetried_SuccessRunsOnce()
    {
        var gate = new RetryOnceGate();
        var calls = 0;
        void Action()
        {
            if (++calls == 1)
                throw new InvalidOperationException();
        }

        Assert.Throws<InvalidOperationException>(() => gate.Run(Action));
        gate.Run(Action);
        gate.Run(Action);

        Assert.Equal(2, calls);
    }
}
