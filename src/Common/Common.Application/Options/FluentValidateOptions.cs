using FluentValidation;
using Microsoft.Extensions.Options;

namespace Common.Application.Options;

public sealed class FluentValidateOptions<T>(IValidator<T> validator) : IValidateOptions<T>
    where T : class
{
    private readonly IValidator<T> _validator = validator;

    public ValidateOptionsResult Validate(
        string? name,
        T options)
    {
        var result = _validator.Validate(options);

        if (result.IsValid)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = result.Errors
            .Select(x => $"{x.PropertyName}: {x.ErrorMessage}")
            .ToArray();

        return ValidateOptionsResult.Fail(failures);
    }
}
