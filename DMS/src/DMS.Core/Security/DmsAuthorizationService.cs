using DMS.Core.Transactions;

namespace DMS.Core.Security;

/// <summary>
/// Společné vyhodnocování oprávnění DMS.
/// Neřeší stav modulu v SYS13; ten zůstává runtime pravidlem desktopového shellu.
/// </summary>
public sealed class DmsAuthorizationService
{
    public DmsAuthorizationResult EvaluateTransaction(
        DmsUserContext user,
        TransactionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(definition);

        var required = NormalizeRoles(definition.Roles);

        if (!definition.IsActive)
        {
            return new DmsAuthorizationResult
            {
                Allowed = false,
                ReasonCode = "TRANSACTION_INACTIVE",
                RequiredRoles = required
            };
        }

        if (required.Count == 0)
        {
            return new DmsAuthorizationResult
            {
                Allowed = true,
                IsPublic = true,
                ReasonCode = "NO_ROLE_REQUIRED"
            };
        }

        var userRoles = NormalizeRoles(user.Roles);
        var matching = required
            .Where(role => userRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (matching.Count > 0)
        {
            return new DmsAuthorizationResult
            {
                Allowed = true,
                ReasonCode = "ROLE_MATCH",
                RequiredRoles = required,
                MatchingRoles = matching
            };
        }

        return new DmsAuthorizationResult
        {
            Allowed = false,
            ReasonCode = "ROLE_REQUIRED",
            RequiredRoles = required,
            MissingRoles = required
        };
    }

    public DmsAuthorizationResult EvaluateRoles(
        IEnumerable<string> userRoles,
        IEnumerable<string> requiredRoles)
    {
        var context = new DmsUserContext
        {
            Roles = NormalizeRoles(userRoles)
        };

        var definition = new TransactionDefinition
        {
            Code = "SECURITY_EVALUATION",
            IsActive = true,
            Roles = NormalizeRoles(requiredRoles)
        };

        return EvaluateTransaction(context, definition);
    }

    private static List<string> NormalizeRoles(IEnumerable<string>? roles) =>
        (roles ?? Array.Empty<string>())
        .Where(role => !string.IsNullOrWhiteSpace(role))
        .Select(role => role.Trim().ToUpperInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
