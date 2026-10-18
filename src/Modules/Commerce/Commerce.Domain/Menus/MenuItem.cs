using Commerce.Domain.Common;
using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;

namespace Commerce.Domain.Menus;

public enum MenuCategory
{
    Food = 1,
    Drink = 2,

    /// <summary>A bookable experience (boat ride, horse ride): one item per time slot, its stock is the slot's places.</summary>
    Activity = 3
}

/// <summary>An optional extra on a menu item ("Extra avocado", +KES 50). Ids are stable so orders can name them.</summary>
public sealed record MenuAddOn(Guid Id, string Name, decimal Price);

/// <param name="Id">Null for a new add-on; an existing id keeps its identity.</param>
public sealed record MenuAddOnSpec(Guid? Id, string? Name, decimal Price);

public enum MenuItemStatus
{
    Active = 1,

    /// <summary>Taken off the menu for good (kept for order history).</summary>
    Retired = 2
}

/// <param name="LiveStock">Portions for live ordering on the day. Null: no limit; 0: not sold live.</param>
/// <param name="ReservationAllocation">Portions that can be pre-ordered (reserved) before the day. Null or 0: none.</param>
/// <param name="AgeRestricted">Needs an 18+ confirmation at checkout. Defaults to true for drinks.</param>
public sealed record MenuItemSpec(
    string? Name,
    string? Description,
    MenuCategory Category,
    decimal Price,
    int? LiveStock,
    int? ReservationAllocation,
    bool? AgeRestricted,
    int SortOrder,
    IReadOnlyList<MenuAddOnSpec>? AddOns = null,
    DateTimeOffset? StartsAt = null,
    int? DurationMinutes = null);

/// <summary>
///     One thing a Kraft sells at one event (UF-017 menu items): price, how many portions go to live orders and to
///     reservations. Counts of what is held and sold live in the stock ledger, not here, so a price edit never races
///     a checkout. Kitchen staff can switch an item off for new orders when it runs out (<see cref="Available" />).
/// </summary>
public sealed class MenuItem : AggregateRoot<MenuItemId>
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 300;
    public const decimal MaxPrice = 100_000m;
    public const int MaxStock = 100_000;
    public const int MaxAddOns = 12;
    public const int AddOnNameMaxLength = 40;
    public const int MaxDurationMinutes = 24 * 60;
    public const int ImagePathMaxLength = 200;

    private MenuItem(MenuItemId id)
        : base(id)
    {
    }

    public Guid KraftId { get; private set; }

    public Guid EventId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public MenuCategory Category { get; private set; }

    /// <summary>KES, whole shillings (M-Pesa).</summary>
    public decimal Price { get; private set; }

    public int? LiveStock { get; private set; }

    public int? ReservationAllocation { get; private set; }

    public bool AgeRestricted { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>False while the kitchen has it switched off (ran out, equipment down). Orders already paid keep it.</summary>
    public bool Available { get; private set; }

    public MenuItemStatus Status { get; private set; }

    /// <summary>Optional extras the customer can tick (stored as JSON).</summary>
    public IReadOnlyList<MenuAddOn> AddOns { get; private set; } = [];

    /// <summary>Activities: when the slot starts.</summary>
    public DateTimeOffset? StartsAt { get; private set; }

    /// <summary>Activities: how long the slot lasts.</summary>
    public int? DurationMinutes { get; private set; }

    /// <summary>Photo of the dish or activity (storage path from ids only).</summary>
    public string? ImagePath { get; private set; }

    /// <summary>Bumped when the photo changes, for cache-busting URLs.</summary>
    public int ImageVersion { get; private set; }

    public bool IsActivity => Category == MenuCategory.Activity;

    public bool SoldLive => LiveStock is null or > 0;

    public bool Reservable => ReservationAllocation is > 0;

    public bool IsOrderable => Status == MenuItemStatus.Active && Available;

    public static Result<MenuItem> Create(MenuItemId id, Guid kraftId, Guid eventId, MenuItemSpec spec)
    {
        var item = new MenuItem(id) { KraftId = kraftId, EventId = eventId, Status = MenuItemStatus.Active, Available = true };

        var applied = item.Apply(spec);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        item.RaiseEvent(new MenuItemAdded(id, kraftId, eventId, item.Price));

        return item;
    }

    public Result Update(MenuItemSpec spec)
    {
        if (Status == MenuItemStatus.Retired)
        {
            return Retired;
        }

        var oldPrice = Price;
        var applied = Apply(spec);
        if (applied.IsFailure)
        {
            return applied;
        }

        RaiseEvent(new MenuItemUpdated(Id, oldPrice, Price));

        return Result.Success;
    }

    public Result SetAvailable(bool available)
    {
        if (Status == MenuItemStatus.Retired)
        {
            return Retired;
        }

        if (Available == available)
        {
            return Result.Success;
        }

        Available = available;
        RaiseEvent(new MenuItemAvailabilityChanged(Id, available));

        return Result.Success;
    }

    public Result Retire()
    {
        if (Status == MenuItemStatus.Retired)
        {
            return Retired;
        }

        Status = MenuItemStatus.Retired;
        Available = false;
        RaiseEvent(new MenuItemRetired(Id));

        return Result.Success;
    }

    /// <summary>Records a new photo and returns the previous path so the caller can delete the old file.</summary>
    public Result<string?> ReplaceImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Status == MenuItemStatus.Retired)
        {
            return Retired;
        }

        var previous = ImagePath;
        ImagePath = path;
        ImageVersion++;
        RaiseEvent(new MenuItemImageChanged(Id));

        return previous;
    }

    /// <summary>The add-ons chosen for one line, or an error naming an unknown one.</summary>
    public Result<IReadOnlyList<MenuAddOn>> ChooseAddOns(IReadOnlyCollection<Guid>? addOnIds)
    {
        if (addOnIds is null || addOnIds.Count == 0)
        {
            return Result<IReadOnlyList<MenuAddOn>>.Success([]);
        }

        var chosen = AddOns.Where(x => addOnIds.Contains(x.Id)).ToList();
        if (chosen.Count != addOnIds.Distinct().Count())
        {
            return CommerceErrors.Invalid("CheckoutInvalid", [$"Items: An add-on chosen for {Name} is no longer offered."]);
        }

        return Result<IReadOnlyList<MenuAddOn>>.Success(chosen);
    }

    private static Error Retired => CommerceErrors.Conflict("MenuItemRetired", "This item has been taken off the menu.");

    private Result Apply(MenuItemSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var errors = new List<string>();
        var name = spec.Name?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(spec.Description) ? null : spec.Description.Trim();

        if (name.Length is < 2 or > NameMaxLength)
        {
            errors.Add($"Name: Use 2-{NameMaxLength} characters.");
        }

        if (description is { Length: > DescriptionMaxLength })
        {
            errors.Add($"Description: Keep it under {DescriptionMaxLength} characters.");
        }

        if (!Enum.IsDefined(spec.Category))
        {
            errors.Add("Category: Choose Food, Drink or Activity.");
        }

        var addOns = new List<MenuAddOn>();
        foreach (var (addOn, index) in (spec.AddOns ?? []).Select((x, i) => (x, i)))
        {
            var addOnName = addOn.Name?.Trim() ?? string.Empty;
            if (addOnName.Length is < 1 or > AddOnNameMaxLength ||
                addOn.Price is < 0m or > MaxPrice || decimal.Round(addOn.Price, 0) != addOn.Price)
            {
                errors.Add($"AddOns[{index}]: Give a name of 1-{AddOnNameMaxLength} characters and a whole-shilling price (0 for free).");
                continue;
            }

            var keep = addOn.Id is { } existing && AddOns.Any(x => x.Id == existing) ? existing : Guid.CreateVersion7();
            addOns.Add(new MenuAddOn(keep, addOnName, addOn.Price));
        }

        if (addOns.Count > MaxAddOns)
        {
            errors.Add($"AddOns: At most {MaxAddOns} add-ons.");
        }

        if (addOns.Select(x => x.Name.ToUpperInvariant()).Distinct().Count() != addOns.Count)
        {
            errors.Add("AddOns: Each add-on needs its own name.");
        }

        var activity = spec.Category == MenuCategory.Activity;
        if (activity && spec.StartsAt is null)
        {
            errors.Add("StartsAt: Give the slot's start time.");
        }

        if (spec.DurationMinutes is < 5 or > MaxDurationMinutes)
        {
            errors.Add($"DurationMinutes: Use 5-{MaxDurationMinutes} minutes.");
        }

        if (spec.Price is < 1m or > MaxPrice || decimal.Round(spec.Price, 0) != spec.Price)
        {
            errors.Add($"Price: Use whole shillings between 1 and {MaxPrice:0} (M-Pesa takes no cents).");
        }

        if (spec.LiveStock is < 0 or > MaxStock)
        {
            errors.Add($"LiveStock: Use 0-{MaxStock} portions, or leave it empty for no limit.");
        }

        if (spec.ReservationAllocation is < 0 or > MaxStock)
        {
            errors.Add($"ReservationAllocation: Use 0-{MaxStock} portions.");
        }

        if (spec.LiveStock == 0 && spec.ReservationAllocation is null or 0)
        {
            errors.Add("LiveStock: An item must be sold live, reservable, or both.");
        }

        if (spec.SortOrder is < 0 or > 10_000)
        {
            errors.Add("SortOrder: Use 0-10000.");
        }

        if (errors.Count > 0)
        {
            return CommerceErrors.Invalid("MenuItemInvalid", errors);
        }

        Name = name;
        Description = description;
        Category = spec.Category;
        Price = spec.Price;
        LiveStock = spec.LiveStock;
        ReservationAllocation = spec.ReservationAllocation is 0 ? null : spec.ReservationAllocation;
        AgeRestricted = spec.AgeRestricted ?? spec.Category == MenuCategory.Drink;
        SortOrder = spec.SortOrder;
        AddOns = addOns;
        StartsAt = activity ? spec.StartsAt : null;
        DurationMinutes = activity ? spec.DurationMinutes : null;

        return Result.Success;
    }
}
