using System.Text.Json;

namespace Bicep.Extension.IncidentIo.Handlers;

/// <summary>
/// Read-only lookup of a user by email (Terraform's incident_user data source). Use with 'existing'.
/// </summary>
public class UserHandler : IncidentIoResourceHandlerBase<User, UserIdentifiers>
{
    protected override string ApiPath => "v2/users";
    protected override string ItemKey => "user";
    protected override string CollectionKey => "users";
    protected override int? PageSize => 250;

    protected override string? GetListQuery(UserIdentifiers identifiers) => $"email={Uri.EscapeDataString(identifiers.Email)}";

    protected override bool IsMatch(JsonElement item, UserIdentifiers identifiers) => StringEquals(item, "email", identifiers.Email);

    protected override UserIdentifiers GetIdentifiers(User properties) => new() { Email = properties.Email };

    protected override Task<ResourceResponse> CreateOrUpdate(ResourceRequest request, CancellationToken cancellationToken)
        => throw new ResourceErrorException("ReadOnlyResource",
            $"User '{request.Properties.Email}' can't be created or changed by Bicep. Declare it with 'existing' to look up its ID: resource u 'User' existing = {{ email: '...' }}");
}
