using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

public sealed partial class ChildProbeCreatePage
{
    [SupplyParameterFromQuery(Name = "parentId")]
    public Guid? ParentId { get; set; }
}
