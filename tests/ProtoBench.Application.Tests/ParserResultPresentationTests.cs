using ProtoBench.Desktop.Features.Parsers;

namespace ProtoBench.Application.Tests;

public sealed class ParserResultPresentationTests
{
    [Fact]
    public void NarrowWindowsStackDetailsAndKeepEveryTextRoleReadable()
    {
        var presentation = new ParserResultPresentation();
        var layout = presentation.Arrange(600, 480, true, true);
        Assert.False(layout.MessagesBeside);
        Assert.False(layout.DetailsBeside);
        Assert.Equal(14, presentation.BodyFontSize);
        Assert.Equal(12, presentation.SmallFontSize);
        Assert.Equal(18, presentation.HeadingFontSize);
    }

    [Fact]
    public void WideWindowsUseMoreSpaceAndEnlargeTextWithinReadableBounds()
    {
        var presentation = new ParserResultPresentation();
        var layout = presentation.Arrange(2400, 2000, true, true);
        Assert.True(layout.MessagesBeside);
        Assert.True(layout.DetailsBeside);
        Assert.Equal(340, layout.MessageWidth);
        Assert.Equal(1280, layout.DetailWidth);
        Assert.Equal(16, presentation.BodyFontSize);
        Assert.Equal(14, presentation.SmallFontSize);
    }

    [Fact]
    public void LargeDecodedFieldsKeepWindowScaleAndUseLocalDescriptionDensity()
    {
        var presentation = new ParserResultPresentation();
        var layout = presentation.Arrange(2400, 2000, true, true, availableHeight: 1100, decodedRowCount: 25);
        Assert.Equal(1280, layout.DetailWidth);
        Assert.Equal(16, presentation.BodyFontSize);
        Assert.Equal(8, presentation.FieldPadding);
        Assert.False(presentation.ShowDecodedDescriptions);
        presentation.Arrange(2400, 2000, true, true, availableHeight: 1100, decodedRowCount: 3);
        Assert.Equal(16, presentation.BodyFontSize);
        Assert.Equal(8, presentation.FieldPadding);
        Assert.True(presentation.ShowDecodedDescriptions);
        presentation.Arrange(2400, 2000, false, true, availableHeight: 1100, decodedRowCount: 25);
        Assert.Equal(14, presentation.BodyFontSize);
        Assert.Equal(8, presentation.FieldPadding);
        Assert.True(presentation.ShowDecodedDescriptions);
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(700)]
    [InlineData(560)]
    public void OpeningAndClosingLongDetailsPreservesSharedScale(double height)
    {
        var presentation = new ParserResultPresentation();
        object Scale() => new { presentation.BodyFontSize, presentation.SmallFontSize,
            presentation.HeadingFontSize, presentation.CardGap, presentation.CardPadding,
            presentation.FieldGap, presentation.FieldPadding, presentation.PanelGap,
            presentation.CompactCards, presentation.ResultsGap };
        presentation.Arrange(2400, 2000, true, false, availableHeight: height);
        var initial = Scale();
        presentation.Arrange(2400, 2000, true, true, availableHeight: height, decodedRowCount: 25);
        Assert.Equal(initial, Scale());
        presentation.Arrange(2400, 2000, true, false, availableHeight: height);
        Assert.Equal(initial, Scale());
    }

    [Fact]
    public void SpecificationLengthDoesNotChangePanePlacementOrWidth()
    {
        var presentation = new ParserResultPresentation();
        Assert.True(presentation.Arrange(1600, 1200, true, true, specificationColumns: 60).DetailsBeside);
        var shortSpec = presentation.Arrange(1600, 1200, true, true, specificationColumns: 60);
        var longSpec = presentation.Arrange(1600, 1200, true, true, specificationColumns: 150);
        Assert.Equal(shortSpec, longSpec);
        Assert.True(presentation.Arrange(1000, 600, true, true).DetailsBeside);
        Assert.True(presentation.Arrange(1000, 600, false, true).DetailsBeside);
    }

    [Fact]
    public void DisablingAutoFitRestoresStandardSizesAndManualWidthsStayBounded()
    {
        var presentation = new ParserResultPresentation();
        presentation.Arrange(2400, 2000, true, true);
        var standard = presentation.Arrange(1600, 1200, false, true);
        Assert.Equal(240, standard.MessageWidth);
        Assert.Equal(480, standard.DetailWidth);
        Assert.Equal(14, presentation.BodyFontSize);
        Assert.Equal(12, presentation.SmallFontSize);
        var dragged = presentation.Arrange(1600, 1000, true, true, preferredDetailWidth: 10000);
        Assert.Equal(788, dragged.DetailWidth);
        Assert.False(presentation.Arrange(double.NaN, 0, true, true).DetailsBeside);
    }

    [Theory]
    [InlineData(900, 600)]
    [InlineData(1200, 850)]
    [InlineData(1600, 1200)]
    [InlineData(2100, 1700)]
    [InlineData(820, 532)]
    public void LaptopSizedLogicalViewportsKeepDetailsBesideFields(double browseWidth, double fieldWidth)
    {
        var presentation = new ParserResultPresentation();
        var layout = presentation.Arrange(browseWidth, fieldWidth, true, true, specificationColumns: 200,
            availableHeight: 560);
        Assert.True(layout.DetailsBeside);
        Assert.True(layout.DetailWidth >= 320);
        Assert.True(fieldWidth - layout.DetailWidth - 12 >= 200);
        Assert.False(presentation.Arrange(browseWidth, 531, true, true).DetailsBeside);
    }

    [Fact]
    public void ShortWindowsUseCompactSpacingWithoutMakingTextUnreadable()
    {
        var presentation = new ParserResultPresentation();
        presentation.Arrange(2000, 1600, true, false, availableHeight: 560);
        Assert.Equal(14, presentation.BodyFontSize);
        Assert.Equal(12, presentation.SmallFontSize);
        Assert.Equal(0, presentation.FieldPadding);
        Assert.Equal(1, presentation.CardPadding);
        Assert.Equal(0, presentation.CardGap);
        Assert.True(presentation.CompactCards);
        Assert.Equal(2, presentation.ResultsGap);
        presentation.Arrange(2000, 1600, true, false, availableHeight: 1000);
        Assert.Equal(16, presentation.BodyFontSize);
        Assert.Equal(8, presentation.FieldPadding);
        Assert.False(presentation.CompactCards);
        Assert.Equal(12, presentation.ResultsGap);
        presentation.Arrange(2000, 1600, false, false, availableHeight: 560);
        Assert.Equal(14, presentation.BodyFontSize);
        Assert.Equal(8, presentation.FieldPadding);
    }

    [Theory]
    [InlineData(320, 0)]
    [InlineData(600, 1)]
    [InlineData(1200, 2)]
    [InlineData(2400, 3)]
    public void FrozenColumnsLeaveRoomToBrowseTheRemainingTable(double viewportWidth, int expected)
    {
        Assert.Equal(expected, ParserResultPresentation.FrozenColumnCount([180, 220, 240], 3, viewportWidth));
    }

    [Fact]
    public void ShortWindowsKeepBodyTextReadableAcrossWidths()
    {
        var presentation = new ParserResultPresentation();
        foreach (var width in new[] { 360d, 600, 900, 1600, 2400 })
        {
            presentation.Arrange(width, width - 48, true, true, availableHeight: 200);
            Assert.InRange(presentation.BodyFontSize, 14, 16);
            Assert.InRange(presentation.SmallFontSize, 12, 14);
        }
    }
}
