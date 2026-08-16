using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.WebUi.All;

[CollectionDefinition(nameof(WebUiIntegrationTestCollection))]
public sealed class WebUiIntegrationTestCollection : ICollectionFixture<SerginWebApiFactory<Program>>;
