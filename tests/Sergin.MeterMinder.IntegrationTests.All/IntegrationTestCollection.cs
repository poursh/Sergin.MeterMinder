using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All;

[CollectionDefinition(nameof(IntegrationTestCollection))]
public sealed class IntegrationTestCollection : ICollectionFixture<SerginWebApiFactory<Program>>;
