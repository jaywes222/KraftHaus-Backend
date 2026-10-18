namespace Commerce.Domain.Common;

/// <summary>Permissions the Commerce endpoints require; must match Identity's catalog (an API test keeps them in step).</summary>
public static class CommercePermission
{
    /// <summary>List Krafts (no commission figures).</summary>
    public const string ViewKrafts = "events:view";

    /// <summary>Create and archive Krafts, sign them up for events and set their commission: Tenant Admin.</summary>
    public const string ManageKrafts = "events:manage";

    /// <summary>A Kraft's own profile, brands, Krafters and menu: Kraft Owner (its Kraft) and Tenant Admin.</summary>
    public const string ManageKraft = "kraft:manage";

    /// <summary>
    ///     Commission, fees and Kraft revenue. Rides on till approval (Tenant Admin, Finance, Kraft Owner): Support and
    ///     Kraft Staff never see fee fields (Spec 01 §3).
    /// </summary>
    public const string ViewMoney = "till:approve";

    /// <summary>The kitchen board of a Kraft (UF-015).</summary>
    public const string KitchenView = "kitchen:view";

    /// <summary>Move orders along, mark items unavailable, hand over (UF-014, UF-015).</summary>
    public const string KitchenHandle = "kitchen:handle";

    /// <summary>Close an order's open money issue after a manual refund (Finance, Tenant Admin).</summary>
    public const string ResolveOrders = "payments:manage";

    public static IReadOnlyList<string> All => [ViewKrafts, ManageKrafts, ManageKraft, ViewMoney, KitchenView, KitchenHandle, ResolveOrders];
}
