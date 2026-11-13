using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates;

/// <summary>
/// Puts the view probe's notes into a test host beside the view probe (call after <see cref="ViewProbeTestHost.AddTo"/>,
/// whose registry factory reads every registered view source and includes the notes' features): their list query, texts
/// and view configuration. Nothing sends the list query: the notes' list body is a slot that loads nothing.
/// </summary>
internal static class ViewProbeNoteTestHost
{
    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton(ModuleConfigurationSource.FromTypes(
            typeof(ViewProbeNoteAggregateFeatureConfiguration),
            typeof(ListViewProbeNotesQuery)));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(ViewProbeNoteTexts)));
        services.AddSingleton(AggregateViewSource.FromTypes("tst", typeof(ViewProbeNoteViewConfiguration)));
    }
}
