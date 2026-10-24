using Dapper;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Queries;

namespace Sergin.MeterMinder.IntegrationTests.All.Lists;

/// <summary>
/// The translation of a list query's Term, Filtering and Sorting into SQL, without a database: only declared
/// expressions reach the text, every value is a parameter, LIKE wildcards in a search are literal, and anything
/// the field set does not declare is refused.
/// </summary>
public sealed class ListQuerySqlTests
{
    private static readonly ListFieldSet Fields = ListFieldSet.Create(
        ListField.SortOnly("name"),
        ListField.FilterOnly("ownerId", ListFieldKind.Identifier, FilteringType.equals),
        ListField.FilterOnly("createdAtUtc", ListFieldKind.Instant, FilteringType.between, FilteringType.betweenInclusive),
        ListField.FilterOnly("code", ListFieldKind.Text, FilteringType.contains, FilteringType.startsWith));

    private static readonly ListQueryColumns Columns = new(
        Fields,
        new Dictionary<string, string>
        {
            ["name"] = "t.name",
            ["ownerId"] = "t.owner_id",
            ["createdAtUtc"] = "t.created_at_utc",
            ["code"] = "t.code",
        },
        searchColumns: ["t.name", "t.code"],
        tieBreaker: "t.id");

    [Fact]
    public void NoCriteria_OrdersByTheTieBreakerOnly()
    {
        ListQuerySqlParts parts = ListQuerySql.Apply(new TestListQuery(Paggination.Default), Columns, new DynamicParameters());

        Assert.Equal(string.Empty, parts.Where);
        Assert.Equal("ORDER BY t.id", parts.OrderBy);
    }

    [Fact]
    public void Term_SearchesEveryColumn_WithWildcardsEscaped()
    {
        DynamicParameters parameters = new();

        ListQuerySqlParts parts = ListQuerySql.Apply(new TestListQuery(Paggination.Default, new Term(" 50%_off\\ ")), Columns, parameters);

        Assert.Equal(" AND (t.name ILIKE @LqTerm OR t.code ILIKE @LqTerm)", parts.Where);
        Assert.Equal("%50\\%\\_off\\\\%", parameters.Get<string>(ListQuerySql.TermParameter));
    }

    [Fact]
    public void Sorting_PutsTheRequestedSortBeforeTheTieBreaker()
    {
        ListQuerySqlParts parts = ListQuerySql.Apply(
            new TestListQuery(Paggination.Default, sorting: Sorting.Create("""[{"id":"name","desc":true}]""")), Columns, new DynamicParameters());

        Assert.Equal("ORDER BY t.name DESC, t.id", parts.OrderBy);
    }

    [Fact]
    public void Filters_BecomeParameterisedPredicates()
    {
        var owner = Guid.CreateVersion7();
        DynamicParameters parameters = new();
        var filtering = Filtering.Create(
            $$"""
            [
              {"id":"ownerId","value":"{{owner}}","mode":"equals"},
              {"id":"createdAtUtc","value":["2026-10-01T00:00:00Z",null],"mode":"between"},
              {"id":"code","value":"MTR","mode":"startsWith"}
            ]
            """);

        ListQuerySqlParts parts = ListQuerySql.Apply(new TestListQuery(Paggination.Default, filtering: filtering), Columns, parameters);

        Assert.Equal(" AND t.owner_id = @Lq0 AND (t.created_at_utc >= @Lq1) AND t.code ILIKE @Lq2", parts.Where);
        Assert.Equal(owner, parameters.Get<Guid>("Lq0"));
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), parameters.Get<DateTime>("Lq1"));
        Assert.Equal(DateTimeKind.Utc, parameters.Get<DateTime>("Lq1").Kind);
        Assert.Equal("MTR%", parameters.Get<string>("Lq2"));
    }

    [Fact]
    public void Between_IsHalfOpen_AndBetweenInclusiveIsClosed()
    {
        static string Range(string mode) => ListQuerySql.Apply(
            new TestListQuery(Paggination.Default, filtering: Filtering.Create(
                $$"""[{"id":"createdAtUtc","value":["2026-10-01T00:00:00Z","2026-10-02T00:00:00Z"],"mode":"{{mode}}"}]""")),
            Columns,
            new DynamicParameters()).Where;

        Assert.Equal(" AND (t.created_at_utc >= @Lq0 AND t.created_at_utc < @Lq1)", Range("between"));
        Assert.Equal(" AND (t.created_at_utc >= @Lq0 AND t.created_at_utc <= @Lq1)", Range("betweenInclusive"));
    }

    [Theory]
    [InlineData("""[{"id":"secret","value":"x","mode":"equals"}]""")]
    [InlineData("""[{"id":"ownerId","value":"x","mode":"contains"}]""")]
    [InlineData("""[{"id":"ownerId","value":"not-a-guid","mode":"equals"}]""")]
    [InlineData("""[{"id":"createdAtUtc","value":[null,null],"mode":"between"}]""")]
    public void AnUndeclaredOrMalformedFilter_IsRefused(string filtering) =>
        Assert.Throws<ArgumentException>(() => ListQuerySql.Apply(
            new TestListQuery(Paggination.Default, filtering: Filtering.Create(filtering)), Columns, new DynamicParameters()));

    [Fact]
    public void ASortOnAFieldThatIsNotSortable_IsRefused() =>
        Assert.Throws<ArgumentException>(() => ListQuerySql.Apply(
            new TestListQuery(Paggination.Default, sorting: Sorting.Create("""[{"id":"ownerId","desc":false}]""")), Columns, new DynamicParameters()));

    [Fact]
    public void AColumnMapMissingAField_IsRefusedWhenBuilt() =>
        Assert.Throws<ArgumentException>(() => new ListQueryColumns(
            Fields, new Dictionary<string, string> { ["name"] = "t.name" }, [], "t.id"));

    private sealed record TestListQuery : ListQuery<string>
    {
        public TestListQuery(Paggination paggination, Term? term = null, Filtering? filtering = null, Sorting? sorting = null)
            : base(paggination, term, filtering, sorting)
        {
        }
    }
}
