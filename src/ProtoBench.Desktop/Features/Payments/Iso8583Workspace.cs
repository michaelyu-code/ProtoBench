using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using ProtoBench.Application.Workspaces;
using ProtoBench.Infrastructure.Configuration;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;

namespace ProtoBench.Desktop.Features.Payments;

public sealed record Iso8583FieldDisplay(Iso8583Field Field, string SourceValue, string SourceField, string LengthPrefix, string SourcePosition)
{
    public string Heading => $"{(Field.Number < 0 ? "Header" : $"Field {Field.Number}")} · {Field.Description}{(Field.Complete ? "" : " · INCOMPLETE")}";
}

public sealed class Iso8583Workspace : ToolWorkspace
{
    private string input = "", notice = "", output = "";
    private int profileIndex, inputMode, revision, ebcdicCodePage;
    public override string ToolId => "iso8583-parser";
    public override string Title => "ISO 8583 Parser";
    public ObservableCollection<Iso8583Profile> Profiles { get; }
    public ObservableCollection<Iso8583Field> Fields { get; } = [];
    public ObservableCollection<Iso8583FieldDisplay> DisplayFields { get; } = [];
    public string RemainingHex { get; private set; } = "";
    public string Input { get => input; set { if (SetProperty(ref input, value)) Invalidate(); } }
    public int ProfileIndex { get => profileIndex; set { if (SetProperty(ref profileIndex, value)) Invalidate(); } }
    public int InputMode { get => inputMode; set { if (SetProperty(ref inputMode, value)) Invalidate(); } }
    public int EbcdicCodePage { get => ebcdicCodePage; set { if (SetProperty(ref ebcdicCodePage, value)) Invalidate(); } }
    public string Notice { get => notice; private set => SetProperty(ref notice, value); }
    public string Output { get => output; private set => SetProperty(ref output, value); }
    public IAsyncRelayCommand ParseCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public Iso8583Workspace(DefinitionLoadResult<Iso8583Profile> profiles)
    {
        Profiles = new(profiles.Definitions);
        Notice = Profiles.Count == 0 ? "Load a jPOS XML packager to begin, or install a plugin providing one." : "";
        ParseCommand = new AsyncRelayCommand(() => RunAsync(async token =>
        {
            Invalidate();
            var version = revision;
            if (ProfileIndex < 0 || ProfileIndex >= Profiles.Count) { Notice = "Select or load a jPOS XML packager."; return; }
            var profile = Profiles[ProfileIndex];
            var text = Input;
            var mode = InputMode;
            var codePage = EbcdicCodePage == 1 ? Iso8583EbcdicEncoding.Cp500 : Iso8583EbcdicEncoding.Jpos1047;
            try
            {
                var result = await Task.Run(() =>
                {
                    if (mode == 0 && text.Any(c => c > 127)) throw new FormatException("Text input accepts ASCII only. Use hex for binary data.");
                    var bytes = mode == 0 ? Encoding.ASCII.GetBytes(text) : Convert.FromHexString(string.Concat(text.Where(c => !char.IsWhiteSpace(c))));
                    return Iso8583Parser.Parse(profile, bytes, codePage);
                }, token);
                token.ThrowIfCancellationRequested();
                if (version != revision) return;
                foreach (var field in result.Fields) Fields.Add(field);
                // Map byte ranges back to the exact pasted characters, including hex case and internal whitespace.
                var hexPositions = mode == 1 ? text.Select((c, i) => (c, i)).Where(p => !char.IsWhiteSpace(p.c)).Select(p => p.i).ToArray() : [];
                string Source(int offset, int length)
                {
                    if (length == 0) return "";
                    return mode == 0 ? text.Substring(offset, length)
                        : text[hexPositions[offset * 2]..(hexPositions[(offset + length) * 2 - 1] + 1)];
                }
                foreach (var field in result.Fields)
                {
                    var sourceValue = Source(field.ValueOffset, field.ValueByteLength);
                    var sourceField = Source(field.Offset, field.RawValue.Length / 2);
                    var prefix = Source(field.Offset, field.PrefixByteLength);
                    DisplayFields.Add(new(field, sourceValue, sourceField, prefix,
                        $"Value bytes: {field.ValueOffset}–{field.ValueOffset + field.ValueByteLength} (end exclusive) · {field.ValueByteLength} bytes · Field starts at byte {field.Offset}"));
                }
                RemainingHex = result.RemainingHex;
                Notice = result.IsComplete ? "Complete message parsed." : string.Join("\n", result.Warnings);
                Output = Notice + "\n\n" + string.Join("\n\n", DisplayFields.Select(d =>
                    $"{d.Heading}\nValue: {d.Field.Value}\nSource value: {d.SourceValue}"
                    + (d.Field.PrefixByteLength > 0 ? $"\nLength prefix: {d.LengthPrefix}\nSource field (prefix + value): {d.SourceField}" : "")
                    + $"\nRaw hex (prefix + value): {d.Field.RawValue}\n{d.SourcePosition}"))
                    + (result.RemainingHex.Length > 0 ? "\n\nUnparsed bytes (hex): " + result.RemainingHex : "");
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { if (version == revision) Notice = ex.Message; }
        }));
        ClearCommand = new RelayCommand(() => { Input = ""; Invalidate(); });
    }
    public void LoadProfile(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 1048576) throw new InvalidDataException("Definition exceeds 1 MiB.");
            var profile = Iso8583Profile.FromXml(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
            Profiles.Add(profile);
            ProfileIndex = Profiles.Count - 1;
            Invalidate();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or System.Xml.XmlException or IOException or UnauthorizedAccessException) { Notice = ex.Message; }
    }
    private void Invalidate() { revision++; Fields.Clear(); DisplayFields.Clear(); RemainingHex = ""; Output = Notice = ""; }
    public override void Dispose() { base.Dispose(); Input = ""; Invalidate(); }
}
