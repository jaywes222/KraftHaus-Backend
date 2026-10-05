using System.Globalization;
using Common.Application.Validation;
using FluentValidation;

namespace Common.Application.Pagination;

public class PaginationRequestValidator<T> : CustomValidator<T>
    where T : PaginationRequest
{
    private const int PageNumberGreaterThanOrEqualTo = 1;
    private const int PageSizeInclusiveMin = 1;
    private const int PageSizeInclusiveMax = 200;

    public PaginationRequestValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(PageNumberGreaterThanOrEqualTo)
            .WithMessage($"Page number must be greater than or equal to {PageNumberGreaterThanOrEqualTo}.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(PageSizeInclusiveMin, PageSizeInclusiveMax)
            .WithMessage($"Page size must be between {PageSizeInclusiveMin} and {PageSizeInclusiveMax}.");
    }
}
