using System.Threading;
using System.Threading.Tasks;

namespace DMS.Integration.Mes.Reporting.Automation;

public interface IMes06AutomationReportProvider
{
    bool CanHandle(string reportCode);

    Task<Mes06AutomationResult> GenerateAsync(
        Mes06AutomationRequest request,
        CancellationToken cancellationToken = default);
}
