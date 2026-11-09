using CommunityToolkit.Mvvm.ComponentModel;

namespace ProtoBench.Desktop.Features.Parsers;

public sealed record ParserPaneLayout(bool MessagesBeside, double MessageWidth, bool DetailsBeside, double DetailWidth);

/// <summary>Responsive layout and shared typography for all result templates; never scales text below 12 DIPs.</summary>
public sealed class ParserResultPresentation : ObservableObject
{
    public static double DetailHeight(double availableHeight, double headerHeight) =>
        double.IsFinite(availableHeight) && double.IsFinite(headerHeight)
            ? Math.Clamp(availableHeight - Math.Max(0, headerHeight) - 24, 320, 1000)
            : 600;

    // List length never participates in the shared result row's height.
    public static double MessagePaneHeight(double availableHeight, bool beside, double inspectorHeight) =>
        beside ? Math.Clamp(double.IsFinite(inspectorHeight) ? inspectorHeight : 600, 320, 1400)
            : Math.Clamp(double.IsFinite(availableHeight) ? availableHeight * 0.4 : 300, 200, 360);

    private double bodyFontSize = 14, smallFontSize = 12, headingFontSize = 18;
    private double cardGap = 3, cardPadding = 8, fieldGap = 3, fieldPadding = 8, panelGap = 8;
    private bool compactCards;
    private bool showDecodedDescriptions = true;
    private double resultsGap = 12;
    public double BodyFontSize { get => bodyFontSize; private set => SetProperty(ref bodyFontSize, value); }
    public double SmallFontSize { get => smallFontSize; private set => SetProperty(ref smallFontSize, value); }
    public double HeadingFontSize { get => headingFontSize; private set => SetProperty(ref headingFontSize, value); }
    public double CardGap { get => cardGap; private set => SetProperty(ref cardGap, value); }
    public double CardPadding { get => cardPadding; private set => SetProperty(ref cardPadding, value); }
    public double FieldGap { get => fieldGap; private set => SetProperty(ref fieldGap, value); }
    public double FieldPadding { get => fieldPadding; private set => SetProperty(ref fieldPadding, value); }
    public double PanelGap { get => panelGap; private set => SetProperty(ref panelGap, value); }
    public bool CompactCards { get => compactCards; private set => SetProperty(ref compactCards, value); }
    public bool ShowDecodedDescriptions { get => showDecodedDescriptions; private set => SetProperty(ref showDecodedDescriptions, value); }
    public double ResultsGap { get => resultsGap; private set => SetProperty(ref resultsGap, value); }

    public ParserPaneLayout Arrange(double browseWidth, double fieldWidth, bool autoFit, bool showingDetails,
        double? preferredDetailWidth = null, int specificationColumns = 0, double availableHeight = double.PositiveInfinity,
        int decodedRowCount = 0)
    {
        browseWidth = double.IsFinite(browseWidth) ? Math.Max(0, browseWidth) : 0;
        fieldWidth = double.IsFinite(fieldWidth) ? Math.Max(0, fieldWidth) : 0;
        var beside = browseWidth >= 720;
        var messageWidth = autoFit ? Math.Clamp(Math.Round(browseWidth * 0.22), 220, 340) : 240;
        var inspectorWidth = beside ? browseWidth - messageWidth - 36 : browseWidth - 24;
        // Content density is local to the detail table. Shared typography and spacing depend
        // only on the window, so opening or closing details cannot rescale the message list.
        var crowdedDetails = showingDetails && decodedRowCount > 0 && decodedRowCount * 64d + 280 > availableHeight;
        var tightDetails = crowdedDetails && decodedRowCount * 48d + 280 > availableHeight;
        var compact = autoFit && double.IsFinite(availableHeight) && availableHeight < 760;
        var tight = compact && availableHeight < 620;
        var fontCeiling = compact ? tight ? 12 : 14 : 16;
        BodyFontSize = autoFit ? Math.Clamp(Math.Round(inspectorWidth / 70), 12, fontCeiling) : 14;
        SmallFontSize = autoFit ? Math.Clamp(BodyFontSize - 2, 12, 14) : 12;
        HeadingFontSize = BodyFontSize + 4;
        CompactCards = compact;
        ShowDecodedDescriptions = !autoFit || !tightDetails;
        ResultsGap = compact ? 2 : 12;
        CardGap = tight ? 0 : compact ? 1 : 3;
        CardPadding = tight ? 1 : compact ? 3 : 8;
        FieldGap = tight ? 0 : compact ? 1 : 3;
        FieldPadding = tight ? 0 : compact ? 2 : 8;
        PanelGap = tight ? 2 : compact ? 4 : 8;

        // Long specification lines use the local horizontal scrollbar; they must not
        // change pane placement when the user selects another field or detail tab.
        double minimumDetail = autoFit ? 320 : 220;
        double reservedListWidth = autoFit ? 200 : 168;
        const double resizeHandleWidth = 12;
        var detailsBeside = showingDetails && fieldWidth >= (autoFit
            ? minimumDetail + reservedListWidth + resizeHandleWidth : 420);
        double detailWidth = 0;
        if (detailsBeside)
        {
            // Large monitors should give the decoded table more room instead of stopping at 900 DIPs.
            var desired = autoFit ? fieldWidth * 0.64 : Math.Min(480, fieldWidth * 0.68);
            detailWidth = Math.Clamp(preferredDetailWidth ?? desired, minimumDetail, fieldWidth - reservedListWidth - resizeHandleWidth);
        }
        return new(beside, messageWidth, detailsBeside, detailWidth);
    }
}
