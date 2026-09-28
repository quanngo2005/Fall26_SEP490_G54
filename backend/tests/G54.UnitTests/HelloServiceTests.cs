using G54.BLL.Services;

namespace G54.UnitTests;

public sealed class HelloServiceTests
{
    [Fact]
    public void GetMessage_ReturnsHelloWorld()
    {
        var service = new HelloService();
        var message = service.GetMessage();
        Assert.Equal("Hello World", message);
    }
}
