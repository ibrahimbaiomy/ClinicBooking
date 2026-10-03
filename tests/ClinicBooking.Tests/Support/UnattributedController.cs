using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicBooking.Tests.Support;

/// <summary>A controller with no authorization attributes at all: the fallback policy must protect it.</summary>
[ApiController]
[Route("test/unattributed")]
public sealed class UnattributedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

/// <summary>Body with a validator whose message is an English sentence, to prove it never reaches a client.</summary>
[ApiController]
[AllowAnonymous]
[Route("test/validated")]
public sealed class ValidatedController : ControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] ValidatedBody body) => Ok();

    public sealed record ValidatedBody(string? Name, string? Other);

    public sealed class ValidatedBodyValidator : AbstractValidator<ValidatedBody>
    {
        public ValidatedBodyValidator()
        {
            RuleFor(b => b.Name).NotEmpty().WithMessage("Name must not be empty, please.");
            RuleFor(b => b.Other).NotEmpty().WithMessage("error.test.other_required");
        }
    }
}
