using Sergin.SharedKernel.Application.Localizations;

namespace Sergin.MeterMinder.Hosts.All.Components;

/// <summary>
/// The host's own texts: the product copy on its home slot. Registered as a text source of its own in Program.cs, under
/// the host's segment <c>meter-minder</c>, since no module owns the product description.
/// </summary>
public static class MeterMinderTexts
{
    public static readonly TextCategory Category = TextCategory.Create("MeterMinder", "Home");

    public static readonly TextDefinition Description = Category.Text(
        "Description",
        "A head-end system for smart electricity, gas, and water meters. It talks to metering devices in the field, "
        + "collects the readings they report, and keeps a record of the devices themselves and the people allowed to "
        + "work with them.");
}
