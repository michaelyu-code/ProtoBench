using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProtoBench.Application.Workspaces;
using ProtoBench.Infrastructure.Configuration;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;

namespace ProtoBench.Desktop.Features.Payments;

public sealed class Iso8583BuilderField(Iso8583FieldDefinition definition) : ObservableObject
{
    private string value = "", digitCount = "";
    private int mode;
    public Iso8583FieldDefinition Definition { get; } = definition;
    public string Label => $"Field {Definition.Id} · {Definition.Name}";
    public string Hint => $"{Definition.Class.Split('.').Last()} · XML length {Definition.Length}";
    public string Value { get => value; set => SetProperty(ref this.value, value); }
    public string DigitCount { get => digitCount; set => SetProperty(ref digitCount, value); }
    public int Mode
    {
        get => mode;
        set { if (SetProperty(ref mode, value)) OnPropertyChanged(nameof(ShowDigitCount)); }
    }
    public bool ShowDigitCount => Mode == 1 && Iso8583Builder.NeedsHexDigitCount(Definition);
}

public sealed record Iso8583BuilderChoice(int Id, string Label);

public sealed class Iso8583BuilderWorkspace : ToolWorkspace
{
    private int profileIndex, selectedFieldId = -1, ebcdicCodePage, revision;
    private string headerHex = "", output = "", asciiPreview = "", summary = "", error = "";
    private bool canCopyAscii;
    private readonly HashSet<Iso8583BuilderField> observed = [];
    public override string ToolId => "iso8583-builder";
    public override string Title => "ISO 8583 Message Builder";
    public ObservableCollection<Iso8583Profile> Profiles { get; }
    public ObservableCollection<Iso8583BuilderField> Fields { get; } = [];
    public IReadOnlyList<Iso8583Field> PreviewFields { get; private set; } = [];
    public Iso8583Profile? SelectedProfile => ProfileIndex >= 0 && ProfileIndex < Profiles.Count ? Profiles[ProfileIndex] : null;
    public IReadOnlyList<Iso8583FieldDefinition> AvailableFields => SelectedProfile is { } p
        ? p.Fields.Values.Where(f => f.Id > 1 && f.Id <= p.MaxValidField && !(f.Id == 65 && p.Fields[1].Length == 24) && !Fields.Any(r => r.Definition.Id == f.Id)).OrderBy(f => f.Id).ToArray() : [];
    public IReadOnlyList<Iso8583BuilderChoice> FieldChoices => AvailableFields.Select(f => new Iso8583BuilderChoice(f.Id, $"Field {f.Id} · {f.Name}")).ToArray();
    public int HeaderLength => SelectedProfile?.HeaderLength ?? 0;
    public int ProfileIndex { get => profileIndex; set { if (SetProperty(ref profileIndex, value)) ResetFields(); } }
    public int SelectedFieldId { get => selectedFieldId; set => SetProperty(ref selectedFieldId, value); }
    public int EbcdicCodePage { get => ebcdicCodePage; set { if (SetProperty(ref ebcdicCodePage, value)) Invalidate(); } }
    public string HeaderHex { get => headerHex; set { if (SetProperty(ref headerHex, value)) Invalidate(); } }
    public string Output { get => output; private set => SetProperty(ref output, value); }
    public string AsciiPreview { get => asciiPreview; private set => SetProperty(ref asciiPreview, value); }
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public bool CanCopyAscii { get => canCopyAscii; private set => SetProperty(ref canCopyAscii, value); }
    public IAsyncRelayCommand BuildCommand { get; }
    public IRelayCommand ClearCommand { get; }

    public Iso8583BuilderWorkspace(DefinitionLoadResult<Iso8583Profile> profiles)
    {
        Profiles = new(profiles.Definitions);
        Fields.CollectionChanged += FieldsChanged;
        BuildCommand = new AsyncRelayCommand(() => RunAsync(async token =>
        {
            Invalidate();
            var version = revision;
            try
            {
                var profile = SelectedProfile ?? throw new FormatException("Select or load a jPOS XML profile.");
                var fields = Fields.Select(row =>
                {
                    int? digits = string.IsNullOrWhiteSpace(row.DigitCount) || !row.ShowDigitCount ? null
                        : int.TryParse(row.DigitCount, out var n) && n >= 0 ? n : throw new FormatException($"Field {row.Definition.Id}: BCD digit count must be a non-negative integer.");
                    return new Iso8583BuildField(row.Definition.Id, row.Value, row.Mode == 1 ? Iso8583ValueInput.Hex : Iso8583ValueInput.Text, digits);
                }).ToArray();
                var encoding = EbcdicCodePage == 1 ? Iso8583EbcdicEncoding.Cp500 : Iso8583EbcdicEncoding.Jpos1047;
                var header = HeaderHex;
                var result = await Task.Run(() => Iso8583Builder.Build(profile, fields, encoding, header, token), token);
                token.ThrowIfCancellationRequested();
                if (version != revision) return;
                PreviewFields = result.Parsed.Fields;
                AsciiPreview = result.AsciiPreview;
                CanCopyAscii = result.IsPrintableAscii;
                Summary = $"{result.Bytes.Length:N0} bytes · {fields.Length - 1} data fields · Bitmap and length prefixes generated · Verified by parser";
                Output = result.Hex;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            { if (version == revision) Error = ex.Message; }
        }));
        ClearCommand = new RelayCommand(ResetFields);
        ResetFields();
    }

    public void AddField()
    {
        if (AvailableFields.FirstOrDefault(f => f.Id == SelectedFieldId) is { } field) Fields.Add(new(field));
    }
    public void RemoveField(Iso8583BuilderField field)
    {
        if (field.Definition.Id != 0) Fields.Remove(field);
    }
    public void LoadProfile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 1048576) throw new InvalidDataException("Definition exceeds 1 MiB.");
            var profile = Iso8583Profile.FromXml(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            Profiles.Add(profile);
            if (ProfileIndex == Profiles.Count - 1) ResetFields(); else ProfileIndex = Profiles.Count - 1;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or System.Xml.XmlException or IOException or UnauthorizedAccessException) { Error = ex.Message; }
    }
    private void ResetFields()
    {
        Fields.Clear();
        HeaderHex = "";
        if (SelectedProfile is { } profile) Fields.Add(new(profile.Fields[0]) { Value = profile.Fields[0].Length == 4 ? "0200" : "" });
        OnPropertyChanged(nameof(HeaderLength));
        OnPropertyChanged(nameof(SelectedProfile));
        Invalidate();
        if (SelectedProfile is null) Error = "Load a jPOS XML profile to begin, or install a plugin providing one.";
    }
    private void FieldsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        foreach (var old in observed.Where(r => !Fields.Contains(r)).ToArray()) { old.PropertyChanged -= FieldChanged; observed.Remove(old); }
        foreach (var row in Fields) if (observed.Add(row)) row.PropertyChanged += FieldChanged;
        OnPropertyChanged(nameof(AvailableFields));
        OnPropertyChanged(nameof(FieldChoices));
        SelectedFieldId = AvailableFields.FirstOrDefault()?.Id ?? -1;
        Invalidate();
    }
    private void FieldChanged(object? sender, PropertyChangedEventArgs args) => Invalidate();
    private void Invalidate()
    {
        revision++;
        PreviewFields = [];
        Output = AsciiPreview = Summary = Error = "";
        CanCopyAscii = false;
    }
    public override void Dispose()
    {
        base.Dispose();
        Fields.Clear();
        Fields.CollectionChanged -= FieldsChanged;
        HeaderHex = "";
        Invalidate();
    }
}
