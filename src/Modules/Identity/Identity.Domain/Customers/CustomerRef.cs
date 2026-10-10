using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Common.Domain.Tenancy;
using Identity.Domain.Common;
using Identity.Domain.Events;
using Identity.Domain.Users;

namespace Identity.Domain.Customers;

public sealed class CustomerRef(CustomerRefId id)
    : AggregateRoot<CustomerRefId>(id)
{
    private CustomerRef()
        : this(new CustomerRefId(
            DefaultIdType.CreateVersion7()))
    {
    }

    public IdentityScope IdentityScope { get; private set; }

    public string ExternalIdentityId { get; private set; } =
        string.Empty;

    public PhoneNumber? Phone { get; private set; }

    public string DisplayName { get; private set; } =
        string.Empty;

    public UserStatus Status { get; private set; }

    public static Result<CustomerRef> CreateFrom(
        CustomerRefId id,
        UserSnapshot snapshot)
    {
        if (snapshot.Phone is null)
        {
            return new Error
            {
                Key = "Identity.CustomerRefPhoneRequired",
                Description =
                    "A customer reference requires a phone number."
            };
        }

        var phoneResult =
            PhoneNumber.Create(snapshot.Phone);

        if (phoneResult.IsFailure)
        {
            return phoneResult.Error!;
        }

        var customer = new CustomerRef(id)
        {
            IdentityScope = snapshot.Scope,
            ExternalIdentityId =
                snapshot.UserId.ToString(),
            Phone = phoneResult.Value,
            DisplayName = snapshot.DisplayName ?? string.Empty,
            Status = snapshot.Status
        };

        customer.RaiseEvent(
            new CustomerRefCreated(
                customer.Id,
                customer.IdentityScope,
                customer.ExternalIdentityId));

        return customer;
    }

    public Result Refresh(UserSnapshot snapshot)
    {
        if (snapshot.Phone is null)
        {
            return new Error
            {
                Key = "Identity.CustomerRefPhoneRequired",
                Description =
                    "A customer reference requires a phone number."
            };
        }

        var phoneResult =
            PhoneNumber.Create(snapshot.Phone);

        if (phoneResult.IsFailure)
        {
            return phoneResult.Error!;
        }

        var changed =
            IdentityScope != snapshot.Scope ||
            ExternalIdentityId !=
                snapshot.UserId.ToString() ||
            Phone != phoneResult.Value ||
            DisplayName != snapshot.DisplayName ||
            Status != snapshot.Status;

        if (!changed)
        {
            return Result.Success;
        }

        IdentityScope = snapshot.Scope;
        ExternalIdentityId =
            snapshot.UserId.ToString();
        Phone = phoneResult.Value;
        DisplayName = snapshot.DisplayName?? string.Empty;
        Status = snapshot.Status;

        RaiseEvent(
            new CustomerRefRefreshed(
                Id,
                IdentityScope,
                ExternalIdentityId));

        return Result.Success;
    }
}
