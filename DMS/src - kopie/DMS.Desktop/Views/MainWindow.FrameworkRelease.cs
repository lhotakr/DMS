using DMS.Desktop.Performance;
using DMS.Desktop.Services.Framework;
using DMS.Desktop.Views.Framework;

namespace DMS.Desktop.Views;

public partial class MainWindow
{
    private void RenderFrameworkReleaseHealth()
    {
        WorkspacePanel.Children.Clear();
        DmsReleaseHealthReport RunHealthCheck()
        {
            var context=new DmsReleaseHealthContext
            {
                Environment=_appSettings.Environment, ConfigurationMode=_appSettings.ConfigurationMode,
                ConfigurationRoot=_appSettings.ConfigurationRootPath, DataRoot=GetDmsDataRootPath(),
                DocumentsRoot=_appSettings.DocumentsRootPath, LogsRoot=_appSettings.LogsRootPath,
                BrandingRoot=_appSettings.BrandingRootPath, ArticlesDataPath=_appSettings.ArticlesDataPath,
                SapMode=_appSettings.SapMode, MesMode=_appSettings.MesMode, DatabaseMode=_appSettings.DatabaseMode,
                RuntimeTransactions=_transactionDispatcher.GetDefinitions(), RegisteredHandlerKeys=_transactionDispatcher.GetRegisteredHandlerKeys(),
                Performance=DmsPerformanceService.Current
            };
            return new DmsReleaseHealthService(context).Run();
        }
        WorkspacePanel.Children.Add(new FrameworkReleaseHealthView(RunHealthCheck,_appSettings.LogsRootPath,key=>T(key),ExecuteTransaction,(action,details)=>_logger.AdminAction("FW11",action,_currentUser.DisplayName,details)));
        ResetWorkspaceScroll();
    }
}
