using ClinicBooking.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Tests.Support;

[ApiController]
[Route("test")]
public sealed class ThrowingController : ControllerBase
{
    public const string SecretMessage = "secret internal detail";

    [HttpGet("throw/{kind}")]
    public IActionResult Throw(string kind)
    {
        throw kind switch
        {
            "invalid" => new InvalidRequestException(
                "error.test.invalid",
                new Dictionary<string, string[]> { ["name"] = ["error.test.name_required"] }),
            "not-found" => new NotFoundException("error.test.not_found"),
            "conflict" => new ConflictException("error.test.conflict"),
            "rule" => new BusinessRuleException("error.test.rule"),
            _ => new InvalidOperationException(SecretMessage)
        };
    }

    [HttpPost("body")]
    public IActionResult Body([FromBody] BodyDto dto) => Ok();

    public sealed record BodyDto(int Number);
}
