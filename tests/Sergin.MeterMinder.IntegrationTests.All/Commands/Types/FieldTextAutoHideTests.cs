using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Hosts.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

/// <summary>
/// The field-text guard treats the target ids a record's target interface declares (<c>Id</c>, <c>RootId</c>,
/// <c>ExpectedCount</c>) as not shown, so a configuration no longer marks them <c>NotShown</c>. A parent id stays shown,
/// and so does a property called <c>Id</c> on an untyped record.
/// </summary>
public sealed class FieldTextAutoHideTests
{
    [Fact]
    public void TargetIds_NeedNoFieldText() =>
        Assert.Null(Record.Exception(() => Check(typeof(AutoHideChildCommand), typeof(AutoHideChildConfiguration))));

    [Fact]
    public void ParentId_StaysShown()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            Check(typeof(AutoHideUnderParentCommand), typeof(AutoHideUnderParentConfiguration)));

        Assert.Contains($"{typeof(AutoHideUnderParentCommand).FullName}.{nameof(AutoHideUnderParentCommand.ParentId)}", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IdOnAnUntypedRecord_StaysShown()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            Check(typeof(AutoHideUntypedCommand), typeof(AutoHideUntypedConfiguration)));

        Assert.Contains($"{typeof(AutoHideUntypedCommand).FullName}.{nameof(AutoHideUntypedCommand.Id)}", refused.Message, StringComparison.Ordinal);
    }

    private static void Check(Type requestType, Type configurationType) =>
        FieldTextGuard.Check(
            [requestType],
            CommandConfigurationRegistry.FromConfigurationTypes([configurationType]),
            TextCatalog.FromSources([TextSource.FromTypes("DeviceManagement", TextOrigin.Application, typeof(AutoHideTexts))]));
}
