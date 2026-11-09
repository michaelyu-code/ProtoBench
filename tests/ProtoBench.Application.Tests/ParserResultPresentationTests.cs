using ProtoBench.Desktop.Features.Parsers;

namespace ProtoBench.Application.Tests;

public sealed class ParserResultPresentationTests
{
    [Theory]
    [InlineData(1000, true, 980, 980)]
    [InlineData(560, true, 520, 520)]
    [InlineData(560, false, 520, 224)]
    [InlineData(2000, false, 1200, 360)]
    [InlineData(100, false, 400, 200)]
    [InlineData(double.PositiveInfinity, true, double.PositiveInfinity, 600)]
    public void MessageListHasABoundedViewportAndMatchesTheInspectorWhenBeside(double available, bool beside,
        double inspector, double expected)
    {
        Assert.Equal(expected, ParserResultPresentation.MessagePaneHeight(available, beside, inspector));
    }

    [Theory]
    [InlineData(1100, 180, 896)]
    [InlineData(560, 180, 356)]
    [InlineData(200, 180, 320)]
    [InlineData(2000, 180, 1000)]
    [InlineData(double.PositiveInfinity, 180, 600)]
    public void DetailViewportReservesHeaderSpaceAndStaysUsable(double height, double header, double expected)
    {
        Assert.Equal(expected, ParserResultPresentation.DetailHeight(height, header));
    }

    [Fact]
    public void NarrowWindowsStackDetailsAndKeepEveryTextRoleReadable()
    {
        var presentation = new ParserResultPresentation();
        var layout = presentation.Arrange(600, 480, true, true);
        Assert.False(layout.MessagesBeside);
        Assert.False(layout.DetailsBeside);
        Assert.Equal(12, presentation.BodyFontSize);
        Assert.Equal(12, presentation.SmallFontSize);
        Assert.Equal(16, presentation.HeadingFontSize);
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
        Assert.Equal(12, presentation.BodyFontSize);
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
}
