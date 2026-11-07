using System.Globalization;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Presentation.Blazor.Localizations;

namespace Sergin.MeterMinder.IntegrationTests.All.Localization;

/// <summary>
/// What the app bar's language menu lists. The menu renders its items only while it is open, so the list is checked
/// here rather than in the prerendered page: the default language first, then the published ones, then the drafts the
/// caller may preview, marked as drafts.
/// </summary>
public sealed class LanguageChoicesTests
{
    private const string DraftSuffix = "draft";

    [Fact]
    public void DefaultLanguage_IsFirst_NamedInItself()
    {
        IReadOnlyList<LanguageChoice> choices = LanguageChoices.Build("en", [], canPreview: false, DraftSuffix);

        LanguageChoice only = Assert.Single(choices);
        Assert.Equal("en", only.Culture);
        Assert.Equal(CultureInfo.GetCultureInfo("en").NativeName, only.Label);
    }

    [Fact]
    public void Published_IsListed_Draft_IsNot()
    {
        IReadOnlyList<LanguageChoice> choices = LanguageChoices.Build(
            "en",
            [new LanguageInfo("de", "Deutsch", false, true), new LanguageInfo("fr", "Francais", false, false)],
            canPreview: false,
            DraftSuffix);

        Assert.Equal(["en", "de"], choices.Select(choice => choice.Culture));
        Assert.Equal("Deutsch", choices[1].Label);
    }

    [Fact]
    public void Draft_IsListed_WhenPreviewAllowed_MarkedAsDraft()
    {
        IReadOnlyList<LanguageChoice> choices = LanguageChoices.Build(
            "en",
            [new LanguageInfo("fr", "Francais", false, false)],
            canPreview: true,
            DraftSuffix);

        Assert.Equal("Francais (draft)", choices[1].Label);
    }

    [Fact]
    public void DefaultLanguage_InTheStore_IsNotListedTwice()
    {
        IReadOnlyList<LanguageChoice> choices = LanguageChoices.Build(
            "en",
            [new LanguageInfo("EN", "English", false, true)],
            canPreview: false,
            DraftSuffix);

        Assert.Single(choices);
    }
}
