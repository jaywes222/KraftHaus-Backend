using Common.Domain.ResultMonad;
using Events.Domain.Common;

namespace Events.Domain.Venues;

/// <summary>Validation shared by venue entrances and the entrances an event keeps.</summary>
public static class EntranceRules
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 300;
    public const int MaxEntrances = 20;

    public static Result<(string Name, string? Description)> Validate(string? name, string? description)
    {
        var validName = Text.Required(name, "EntranceName", NameMaxLength);
        if (validName.IsFailure)
        {
            return validName.Error!;
        }

        var validDescription = Text.Optional(description, "EntranceDescription", DescriptionMaxLength);
        if (validDescription.IsFailure)
        {
            return validDescription.Error!;
        }

        return (validName.Value!, validDescription.Value);
    }
}
