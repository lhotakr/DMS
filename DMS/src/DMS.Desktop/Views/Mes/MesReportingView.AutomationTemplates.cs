using DMS.Core.Scheduling;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace DMS.Desktop.Views.Mes;

public partial class MesReportingView
{
    private string _automationTemplatesPath = string.Empty;

    private void InitializeAutomationTemplateUi()
    {
        BtnPublishAutomation.Content =
            T(
                "MES06.Automation.Publish",
                "Save for automation");

        BtnPublishAutomation.IsEnabled =
            !string.IsNullOrWhiteSpace(
                _automationTemplatesPath);
    }

    private async void BtnPublishAutomation_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(
                _automationTemplatesPath))
        {
            TxtStatus.Text =
                T(
                    "MES06.Automation.NotConfigured",
                    "Automation template storage is not configured.");
            return;
        }

        var preset =
            FindPreset(
                _activePresetName)
            ?? LstReportPresets.SelectedItem
                as Mes06ReportPreset;

        if (preset is null)
        {
            TxtStatus.Text =
                T(
                    "MES06.Automation.NoPreset",
                    "Save or select a preset first.");
            return;
        }

        // Publish the CURRENT filter state under the active preset name.
        // This means the user can adjust MES06 and republish without any
        // additional configuration dialog.
        var current =
            CaptureCurrentPreset(
                preset.Name);

        var repository =
            new ScheduledReportTemplateRepository(
                _automationTemplatesPath);

        try
        {
            var file =
                await repository.LoadAsync();

            var existing =
                file.Templates.FirstOrDefault(x =>
                    string.Equals(
                        x.Source,
                        "MES06",
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    string.Equals(
                        x.Name,
                        current.Name,
                        StringComparison.CurrentCultureIgnoreCase));

            var created =
                existing is null;

            var template =
                existing
                ?? new ScheduledReportTemplate
                {
                    Id =
                        Guid.NewGuid()
                            .ToString("N"),
                    Source =
                        "MES06"
                };

            template.Name =
                current.Name;

            template.ReportCode =
                current.ReportCode;

            var definition =
                _definitions.FirstOrDefault(x =>
                    string.Equals(
                        x.Code,
                        current.ReportCode,
                        StringComparison.OrdinalIgnoreCase));

            template.ExecutorCode =
                "MES06";

            template.QuickPeriodCode =
                current.QuickPeriodCode;

            template.From =
                current.From;

            template.To =
                current.To;

            template.TimeFrom =
                current.TimeFrom;

            template.TimeTo =
                current.TimeTo;

            template.SelectAllWorkcenters =
                current.SelectAllWorkcenters;

            template.WorkcenterCodes =
                current.WorkcenterCodes
                    .ToList();

            template.ShiftCode =
                current.ShiftCode;

            template.Article =
                current.Article;

            template.Order =
                current.Order;

            template.Operation =
                current.Operation;

            template.UpdatedAtUtc =
                DateTime.UtcNow;

            template.UpdatedBy =
                _user;

            if (created)
            {
                file.Templates.Add(
                    template);
            }

            await repository.SaveAsync(
                file);

            _logger.AdminAction(
                "MES06",
                created
                    ? "AUDIT_CREATE"
                    : "AUDIT",
                _user,
                $"AutomationTemplateId={template.Id}; Name={template.Name}; Report={template.ReportCode}");

            TxtStatus.Text =
                string.Format(
                    T(
                        "MES06.Automation.Published",
                        "Automation template '{0}' was saved."),
                    template.Name);
        }
        catch (Exception ex)
        {
            _logger.Error(
                "MES06 automation template publish failed.",
                ex);

            TxtStatus.Text =
                T(
                    "MES06.Automation.PublishFailed",
                    "Automation template could not be saved.");
        }
    }
}
