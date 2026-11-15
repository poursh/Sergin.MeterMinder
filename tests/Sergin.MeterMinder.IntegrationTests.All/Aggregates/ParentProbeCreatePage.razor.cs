using Microsoft.AspNetCore.Components;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

public sealed partial class ParentProbeCreatePage
{
    [SupplyParameterFromQuery(Name = "parentId")]
    public Guid? ParentId { get; set; }
}
