namespace Bicep.Extension.IncidentIo.Handlers;

public class ScheduleHandler : NamedResourceHandler<Schedule>
{
    protected override string ApiPath => "v3/schedules";
    protected override string ItemKey => "schedule";
    protected override string CollectionKey => "schedules";
    protected override string? RequestWrapperKey => "schedule";
    protected override int? PageSize => 250;

    // The update payload has no timezone: rotations are anchored to it.
    protected override IReadOnlyCollection<string> CreateOnlyFields => ["timezone"];
}
