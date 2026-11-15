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
        var tabs = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false, TabWidthMode = TabViewWidthMode.SizeToContent };
        tabs.TabItems.Add(new TabViewItem { Header = "Certificate", IsClosable = false,
            Content = Result("Certificate details", nameof(vm.Details), () => vm.Details, 520) });
        tabs.TabItems.Add(new TabViewItem { Header = "RSA components", IsClosable = false,
            Content = ToolLayout.Stack(Ui.Text("Unsigned big-endian modulus (n) and public exponent (e). Available for RSA certificates."),
                Result("Modulus (hex)", nameof(vm.ModulusHex), () => vm.ModulusHex),
                Result("Modulus (decimal)", nameof(vm.ModulusDecimal), () => vm.ModulusDecimal),
                Result("Exponent (hex)", nameof(vm.ExponentHex), () => vm.ExponentHex),
                Result("Exponent (decimal)", nameof(vm.ExponentDecimal), () => vm.ExponentDecimal)) });
        tabs.TabItems.Add(new TabViewItem { Header = "Public key", IsClosable = false,
            Content = ToolLayout.Stack(Result("Public key PEM", nameof(vm.PublicKeyPem), () => vm.PublicKeyPem, 320),
                Ui.AsyncButton("Export public key…", async () =>
                {
                    if (vm.PublicKeyPem.Length == 0) { feedback.Text = "Inspect a certificate first."; return; }
                    var path = await dialogs.SaveAsync("public-key", ".pem", "PEM public key");
                    if (path is not null) await vm.SavePublicKeyAsync(path);
                }, ex => feedback.Text = ex.Message)) });
        tabs.SelectedIndex = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(vm.Input) or nameof(vm.Details)) feedback.Text = ""; };
        return ToolLayout.Page(vm.Title, "Inspect X.509 certificates and extract RSA public key components locally.",
            ToolLayout.Columns(340,
                ToolLayout.Section("Input", input,
                    ToolLayout.Columns(150, Ui.AsyncButton("Inspect PEM", vm.InspectAsync), Ui.AsyncButton("Open certificate…", async () =>
                    {
                        var path = await dialogs.OpenAsync();
                        if (path is not null) await vm.LoadAsync(path);
                    }, ex => feedback.Text = ex.Message)),
                    Ui.Button("Clear inputs", vm.Clear), Ui.Row(Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status)))),
                ToolLayout.Section("Inspection", Ui.Text("Trust not evaluated. Date status alone does not establish trust."), tabs, feedback)),
            ToolLayout.Help(Ui.Text("Paste one PEM CERTIFICATE block or open a PEM/DER .cer, .crt or .pem file (maximum 1 MiB). Shows subject, issuer, serial number, UTC validity dates, algorithms, fingerprints and extensions including SANs and key usage. RSA n/e are available in unsigned hex and decimal. Public keys export as SubjectPublicKeyInfo PEM (PUBLIC KEY). Inputs stay local and are not saved. Certificate chains, signatures, hostname matching and revocation are not validated. Bundles, PFX/PKCS#12, CSRs and private keys are not supported.")));
    }
}
