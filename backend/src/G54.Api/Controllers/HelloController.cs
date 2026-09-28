using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HelloController(IHelloService helloService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HelloResponse>(StatusCodes.Status200OK)]
    public ActionResult<HelloResponse> Get() => Ok(new HelloResponse(helloService.GetMessage()));
}

public sealed record HelloResponse(string Message);
