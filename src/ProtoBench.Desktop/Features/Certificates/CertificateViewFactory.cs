using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Utilities.Certificates;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Certificates;

public sealed class CertificateViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    public string ToolId => "certificate-inspector";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (CertificateWorkspace)workspace;
        var required = new RequiredInputs(vm);
        var feedback = Ui.Text("");
        void Copy(string text)
        {
            if (text.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data); feedback.Text = "Copied"; }
            catch { feedback.Text = "Clipboard unavailable. Select and copy the value manually."; }
        }
        FrameworkElement Result(string header, string property, Func<string> value, double height = 150)
        {
            var box = new RawTextViewer { Header = header, MinHeight = 55, MaxHeight = height, TextWrapping = TextWrapping.Wrap };
            box.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(property), Mode = BindingMode.OneWay });
            return ToolLayout.Stack(box, Ui.Button("Copy " + header, () => Copy(value())));
        }
        var input = Ui.Input(vm, nameof(vm.Input), "PEM certificate", true);
        input.MaxLength = CertificateInspector.MaximumInputBytes;
        required.Require(input, "Paste a PEM certificate, or use Open certificate.");
        var tabs = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false, TabWidthMode = TabViewWidthMode.SizeToContent };
        var summary = Ui.Text("");
        summary.IsTextSelectionEnabled = true;
        tabs.TabItems.Add(new TabViewItem { Header = "Certificate", IsClosable = false,
            Content = ToolLayout.Stack(summary, new Expander { Header = "All certificate details", Content = Result("Certificate details", nameof(vm.Details), () => vm.Details, 520),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }) });
        var format = new ComboBox { Header = "Number format", ItemsSource = new[] { "Hex", "Decimal" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var hex = ToolLayout.Stack(Result("Modulus (hex)", nameof(vm.ModulusHex), () => vm.ModulusHex), Result("Exponent (hex)", nameof(vm.ExponentHex), () => vm.ExponentHex));
        var dec = ToolLayout.Stack(Result("Modulus (decimal)", nameof(vm.ModulusDecimal), () => vm.ModulusDecimal), Result("Exponent (decimal)", nameof(vm.ExponentDecimal), () => vm.ExponentDecimal));
        dec.Visibility = Visibility.Collapsed;
        format.SelectionChanged += (_, _) => { hex.Visibility = format.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; dec.Visibility = format.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; };
        tabs.TabItems.Add(new TabViewItem { Header = "RSA components", IsClosable = false,
            Content = ToolLayout.Stack(Ui.Text("Unsigned big-endian modulus (n) and public exponent (e). Available for RSA certificates."),
                format, hex, dec) });
        tabs.TabItems.Add(new TabViewItem { Header = "Public key", IsClosable = false,
            Content = ToolLayout.Stack(Result("Public key PEM", nameof(vm.PublicKeyPem), () => vm.PublicKeyPem, 320),
                Ui.AsyncButton("Export public key…", async () =>
                {
                    if (vm.PublicKeyPem.Length == 0) { feedback.Text = "Inspect a certificate first."; return; }
                    var path = await dialogs.SaveAsync("public-key", ".pem", "PEM public key");
                    if (path is not null) await vm.SavePublicKeyAsync(path);
                }, ex => feedback.Text = ex.Message)) });
        tabs.SelectedIndex = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(vm.Input) or nameof(vm.Details)) feedback.Text = "";
            if (e.PropertyName == nameof(vm.Details))
                summary.Text = string.Join(Environment.NewLine, vm.Details.Split('\n').Where(line =>
                    line.StartsWith("Subject:") || line.StartsWith("Issuer:") || line.StartsWith("Not before") ||
                    line.StartsWith("Not after") || line.StartsWith("Date status:") || line.StartsWith("SHA-256 fingerprint:")));
        };
        return ToolLayout.Page(vm.Title, "Inspect X.509 certificates and extract RSA public key components locally.",
            ToolLayout.Workspace(vm,
                ToolLayout.Section("Input", input,
                    ToolLayout.Columns(150, Ui.Primary(Ui.AsyncButton("Inspect PEM", () => required.RunAsync(vm.InspectAsync))), Ui.AsyncButton("Open certificate…", async () =>
                    {
                        var path = await dialogs.OpenAsync();
                        if (path is not null) await vm.LoadAsync(path);
                    }, ex => feedback.Text = ex.Message)),
                    Ui.Button("Clear inputs", () => { vm.Clear(); required.Clear(); }), Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status)))),
                ToolLayout.Section("Inspection", Ui.Text("Trust not evaluated. Date status alone does not establish trust."), tabs), () => vm.Details.Length > 0, nameof(vm.Details)), feedback,
            ToolLayout.Help(Ui.Text("Paste one PEM CERTIFICATE block or open a PEM/DER .cer, .crt or .pem file (maximum 1 MiB). Shows subject, issuer, serial number, UTC validity dates, algorithms, fingerprints and extensions including SANs and key usage. RSA n/e are available in unsigned hex and decimal. Public keys export as SubjectPublicKeyInfo PEM (PUBLIC KEY). Inputs stay local and are not saved. Certificate chains, signatures, hostname matching and revocation are not validated. Bundles, PFX/PKCS#12, CSRs and private keys are not supported.")));
    }
}
