using Microsoft.AspNetCore.Components;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>Edits a collection-typed field, which no generated input can: the three parameters a field component receives.</summary>
public sealed partial class ChildProbeTagsInput
{
    /// <summary>The last instance rendered, so a test can set a value through it.</summary>
    public static ChildProbeTagsInput? Current { get; private set; }

    [Parameter]
    public object? Value { get; set; }

    [Parameter]
    public EventCallback<object?> ValueChanged { get; set; }

    [Parameter]
    public FormFieldView Field { get; set; } = default!;

    private int Count => Value is IReadOnlyCollection<string> tags ? tags.Count : 0;

    /// <summary>What the input does when the user changes the tags.</summary>
    public Task SetAsync(IReadOnlyCollection<string> tags) => ValueChanged.InvokeAsync(tags);

    protected override void OnInitialized() => Current = this;
}
