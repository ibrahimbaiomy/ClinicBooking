using ClinicBooking.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace ClinicBooking.Api.Filters;

/// <summary>
/// Runs the FluentValidation validator of every action argument (D9). It is a global action
/// filter: <c>IEndpointFilter</c> exists only for minimal APIs, and controllers were chosen (D4).
/// Failures become <see cref="InvalidRequestException"/> with camelCase field names. A message
/// that is not an error key is replaced, so an English default can never reach a client (rule 4).
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    public const string FailedKey = "error.validation.failed";
    public const string InvalidKey = "error.validation.invalid";

    private readonly IServiceProvider _services;
    private readonly ILogger<ValidationFilter> _logger;

    public ValidationFilter(IServiceProvider services, ILogger<ValidationFilter> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (_services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument),
                context.HttpContext.RequestAborted);

            foreach (var failure in result.Errors)
            {
                var field = ToCamelCase(failure.PropertyName);
                var message = failure.ErrorMessage;
                if (!message.StartsWith("error.", StringComparison.Ordinal))
                {
                    _logger.LogWarning("Validator message for {Field} is not an error key", field);
                    message = InvalidKey;
                }

                if (!errors.TryGetValue(field, out var messages))
                {
                    errors[field] = messages = [];
                }

                if (!messages.Contains(message))
                {
                    messages.Add(message);
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidRequestException(
                FailedKey,
                errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()));
        }

        await next();
    }

    // "Address.Street" -> "address.street"; indexers such as "[0]" are kept.
    private static string ToCamelCase(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(part =>
            part.Length > 0 && char.IsUpper(part[0]) ? char.ToLowerInvariant(part[0]) + part[1..] : part));
}
