using System.Windows;
using System.Windows.Controls;

namespace DMS.Desktop.Views.Framework;

public partial class FrameworkHubView : UserControl
{
    private readonly Action<string> _executeTransaction;

    public FrameworkHubView(
        string frameworkCode,
        Action<string> executeTransaction,
        Func<string, string> translate)
    {
        InitializeComponent();
        _executeTransaction = executeTransaction;

        TitleText.Text = Translate(translate, $"Framework.{frameworkCode}.Title", frameworkCode);
        DescriptionText.Text = Translate(translate, $"Framework.{frameworkCode}.Description", string.Empty);
        LinksTitleText.Text = Translate(translate, "Framework.RelatedTransactions", "Related administration transactions");
        LinksItems.ItemsSource = CreateLinks(frameworkCode, translate);
    }

    private static IReadOnlyList<FrameworkLinkItem> CreateLinks(string code, Func<string, string> translate)
    {
        string T(string key, string fallback) => Translate(translate, key, fallback);

        return code.ToUpperInvariant() switch
        {
            "FW01" => new[]
            {
                Link("SYS01", T("Transaction.SYS01.Name", "DMS system settings"), T("Framework.Link.Localization", "Localization dictionaries")),
                Link("CLSET", T("Transaction.CLSET.Name", "Client settings"), T("Framework.Link.ClientLanguage", "Client language and culture"))
            },
            "FW02" => new[]
            {
                Link("CLSET", T("Transaction.CLSET.Name", "Client settings"), T("Framework.Link.UiSettings", "Theme and row highlighting")),
                Link("SYS01", T("Transaction.SYS01.Name", "DMS system settings"), T("Framework.Link.SystemUi", "System branding and UI defaults"))
            },
            "FW03" => new[]
            {
                Link("SYS11", T("Transaction.SYS11.Name", "Transaction management"), T("Framework.Link.Transactions", "Runtime transactions")),
                Link("SYS13", T("Transaction.SYS13.Name", "Module management"), T("Framework.Link.Modules", "Runtime modules")),
                Link("SYS01", T("Transaction.SYS01.Name", "DMS system settings"), T("Framework.Link.SystemConfig", "System configuration"))
            },
            "FW05" => new[]
            {
                Link("LOG03", T("Transaction.LOG03.Name", "Application log"), T("Framework.Link.Log", "Runtime and audit log")),
                Link("SYS03", T("Transaction.SYS03.Name", "DMS system overview"), T("Framework.Link.SystemOverview", "Runtime system overview"))
            },
            "FW06" => new[]
            {
                Link("USR01", T("Transaction.USR01.Name", "User management"), T("Framework.Link.Users", "Users and person links")),
                Link("SYS12", T("Transaction.SYS12.Name", "Role management"), T("Framework.Link.Roles", "Roles and permissions")),
                Link("SYS11", T("Transaction.SYS11.Name", "Transaction management"), T("Framework.Link.TransactionRoles", "Transaction role assignments"))
            },
            "FW07" => new[]
            {
                Link("CHL05", T("Transaction.CHL05.Name", "Checklist overview"), T("Framework.Link.ChecklistWorkflow", "Checklist lifecycle overview")),
                Link("CHL06", T("Transaction.CHL06.Name", "Checklist review"), T("Framework.Link.ChecklistApproval", "Pending reviews and approvals"))
            },
            "FW08" => new[]
            {
                Link("CHL00", T("Transaction.CHL00.Name", "Checklist definitions"), T("Framework.Link.FormDefinitions", "Dynamic form definitions")),
                Link("CHLSET", T("Transaction.CHLSET.Name", "Checklist settings"), T("Framework.Link.FormCatalogs", "Catalogs and shared checklist settings"))
            },
            "FW09" => new[]
            {
                Link("SYS01", T("Transaction.SYS01.Name", "DMS system settings"), T("Framework.Link.MasterData", "Organization, people and units")),
                Link("USR01", T("Transaction.USR01.Name", "User management"), T("Framework.Link.PersonLinks", "User-to-person links"))
            },
            _ => Array.Empty<FrameworkLinkItem>()
        };
    }

    private static FrameworkLinkItem Link(string transactionCode, string caption, string description) =>
        new(transactionCode, $"{transactionCode}  {caption}", description);

    private static string Translate(Func<string, string> translate, string key, string fallback)
    {
        var value = translate(key);
        return string.IsNullOrWhiteSpace(value) || value.StartsWith("[[", StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void OpenTransaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string transactionCode } && !string.IsNullOrWhiteSpace(transactionCode))
        {
            _executeTransaction(transactionCode);
        }
    }

    private sealed record FrameworkLinkItem(string TransactionCode, string Caption, string Description);
}
