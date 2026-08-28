using DMS.Desktop.Theming;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using DMS.Core.Domain.Organization;
using DMS.Core.Domain.People;
using DMS.Core.Domain.Units;
using DMS.Core.Framework.MasterData;
using DMS.Core.Security;
using DMS.Core.Workflow;
using DMS.Desktop.Behaviors;
using DMS.Desktop.Configuration.Modules;
using DMS.Desktop.Configuration.Roles;
using DMS.Desktop.UI;
using System.Windows.Controls.Primitives;

namespace DMS.Desktop.Services.Framework;

public sealed class DmsReleaseHealthService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly DmsReleaseHealthContext _context;
    private readonly List<DmsReleaseCheckResult> _results = new();

    public DmsReleaseHealthService(DmsReleaseHealthContext context) => _context = context;

    public DmsReleaseHealthReport Run()
    {
        _results.Clear();
        CheckRuntime();
        CheckFrameworkSet();
        CheckPaths();
        CheckJsonFiles();
        CheckTransactionsAndModules();
        CheckSecurity();
        CheckLocalization();
        CheckUi();
        CheckLogging();
        CheckWorkflowAndChecklistFiles();
        CheckMasterData();
        CheckPerformance();
        CheckExtensions();
        return BuildReport();
    }

    private DmsReleaseHealthReport BuildReport()
    {
        var critical=_results.Count(x=>x.Severity=="CRITICAL");
        var errors=_results.Count(x=>x.Severity=="ERROR");
        var warnings=_results.Count(x=>x.Severity=="WARNING");
        var ok=_results.Count(x=>x.Severity=="OK");
        var info=_results.Count(x=>x.Severity=="INFO");
        var rqi=Math.Clamp(100d-critical*25d-errors*5d-warnings*0.5d,0d,100d);
        var allowed=critical==0 && errors==0;
        var verdict=!allowed?"NOT READY":warnings>0?"READY WITH WARNINGS":"READY";
        var assembly=Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version=assembly.GetName().Version?.ToString() ?? "unknown";
        var informational=assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;
        return new DmsReleaseHealthReport
        {
            GeneratedAt=DateTime.Now, Verdict=verdict, ReleaseQualityIndex=rqi, BuildAllowed=allowed,
            Environment=_context.Environment, Version=version, InformationalVersion=informational,
            CriticalCount=critical, ErrorCount=errors, WarningCount=warnings, OkCount=ok, InfoCount=info,
            Results=_results.OrderBy(x=>Order(x.Severity)).ThenBy(x=>x.FrameworkCode).ThenBy(x=>x.Category).ThenBy(x=>x.Name).ToList()
        };
    }

    private static int Order(string severity)=>severity switch {"CRITICAL"=>0,"ERROR"=>1,"WARNING"=>2,"OK"=>3,_=>4};

    private void CheckRuntime()
    {
        var assembly=Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        Add("OK","FW03","Runtime","BUILD","Application build",$"Version={assembly.GetName().Version}; Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}",Environment.ProcessPath ?? string.Empty,"FW03");
        Add(string.Equals(_context.Environment,"PROD",StringComparison.OrdinalIgnoreCase)?"OK":"WARNING","FW03","Runtime","ENV","Release environment",string.Equals(_context.Environment,"PROD",StringComparison.OrdinalIgnoreCase)?"Runtime is configured as PROD.":$"Runtime is '{_context.Environment}'. Confirm before production deployment.",_context.ConfigurationRoot,"FW03");
        Add("INFO","FW03","Integration","MODES","Integration modes",$"Configuration={_context.ConfigurationMode}; SAP={_context.SapMode}; MES={_context.MesMode}; Database={_context.DatabaseMode}",_context.ConfigurationRoot,"FW03");
    }

    private void CheckFrameworkSet()
    {
        var map=_context.RuntimeTransactions.ToDictionary(x=>x.Code,StringComparer.OrdinalIgnoreCase);
        string[] required={"FW01","FW02","FW03","FW04","FW05","FW06","FW07","FW08","FW09","FW11"};
        var missing=required.Where(code=>!map.TryGetValue(code,out var d)||!d.IsActive).ToList();
        AddList(missing.Count==0?"OK":"CRITICAL","FW11","Framework","FRAMEWORK_SET","Required framework transactions",missing,missing.Count==0?"FW01-FW09 and FW11 are registered and active.":"Missing or inactive: {0}","SYS11");
    }

    private void CheckPaths()
    {
        CheckDirectory("FW03","Configuration root",_context.ConfigurationRoot,true,true,"SYS01");
        CheckDirectory("FW03","Data root",_context.DataRoot,true,true,"SYS01");
        CheckDirectory("FW03","Documents root",_context.DocumentsRoot,true,true,"SYS01");
        CheckDirectory("FW05","Logs root",_context.LogsRoot,true,true,"LOG03");
        CheckDirectory("FW02","Branding root",_context.BrandingRoot,false,false,"CLSET");
        if(string.IsNullOrWhiteSpace(_context.ArticlesDataPath)||!File.Exists(_context.ArticlesDataPath))
            Add("ERROR","FW03","Data","ARTICLES","Articles cache","Configured article cache is missing.",_context.ArticlesDataPath,"SAP00");
        else
        {
            var f=new FileInfo(_context.ArticlesDataPath);
            Add(f.Length>0?"OK":"ERROR","FW03","Data","ARTICLES","Articles cache",$"Available; {f.Length/1024d/1024d:0.00} MB; modified {f.LastWriteTime:yyyy-MM-dd HH:mm:ss}.",f.FullName,"SAP00");
        }
    }

    private void CheckJsonFiles()
    {
        var files=new (string Name,bool Required,string Fix)[]{
            ("transactions.json",true,"SYS11"),("dms-modules.json",true,"SYS13"),("dms-roles.json",true,"SYS12"),("users.json",true,"USR01"),
            ("dms-system-settings.json",false,"SYS01"),("mes-integration.json",false,"MES00"),("mes-plc-bindings.json",false,"MES00"),("mes-communication-settings.json",false,"MES00")};
        foreach(var item in files) CheckJson("FW04","Configuration",item.Name,Path.Combine(_context.ConfigurationRoot,item.Name),item.Required,item.Fix);
        CheckJson("FW01","Localization","localization.index.json",Path.Combine(_context.ConfigurationRoot,"Localization","localization.index.json"),true,"SYS01");
        ScanJsonDirectory(Path.Combine(_context.DataRoot,"Data","Checklists"),"FW07","Checklists","CHL00");
        ScanJsonDirectory(Path.Combine(_context.DataRoot,"Data","MasterData"),"FW09","Master data","SYS01");
    }

    private void CheckTransactionsAndModules()
    {
        var tx=_context.RuntimeTransactions.ToList();
        var duplicate=tx.Where(x=>!string.IsNullOrWhiteSpace(x.Code)).GroupBy(x=>x.Code,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToList();
        AddList(duplicate.Count==0?"OK":"CRITICAL","FW04","Transactions","TX_DUP","Duplicate transaction codes",duplicate,duplicate.Count==0?"No duplicate codes.":"Duplicate code(s): {0}","SYS11");
        var handlers=_context.RegisteredHandlerKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingHandlers=tx.Where(x=>x.IsActive&&(string.IsNullOrWhiteSpace(x.HandlerKey)||!handlers.Contains(x.HandlerKey))).Select(x=>$"{x.Code}->{x.HandlerKey}").ToList();
        AddList(missingHandlers.Count==0?"OK":"CRITICAL","FW04","Transactions","HANDLERS","Registered handlers",missingHandlers,missingHandlers.Count==0?$"{handlers.Count} handler key(s); all active transactions dispatchable.":"Missing handler(s): {0}","SYS11");
        var modules=LoadList<DmsModuleDefinition>(Path.Combine(_context.ConfigurationRoot,"dms-modules.json"));
        if(modules is null) return;
        DmsModuleDefinition? FindModule(string? rawModule)
        {
            if(string.IsNullOrWhiteSpace(rawModule)) return null;
            var value=rawModule.Trim();

            return modules.FirstOrDefault(module =>
                string.Equals(module.Code?.Trim(),value,StringComparison.OrdinalIgnoreCase) ||
                string.Equals(module.Name?.Trim(),value,StringComparison.OrdinalIgnoreCase));
        }

        var unknown=tx
            .Where(x=>x.IsActive&&!string.IsNullOrWhiteSpace(x.Module)&&FindModule(x.Module) is null)
            .Select(x=>$"{x.Code}->{x.Module}")
            .ToList();

        AddList(
            unknown.Count==0?"OK":"ERROR",
            "FW04",
            "Transactions",
            "MODULE_REFS",
            "Transaction module references",
            unknown,
            unknown.Count==0
                ?"All active transactions reference known modules by code or configured name."
                :"Unknown reference(s): {0}",
            "SYS13");

        var inactive=tx
            .Where(x=>x.IsActive)
            .Select(x=>(Transaction:x,Module:FindModule(x.Module)))
            .Where(x=>x.Module is not null&&!x.Module.IsActive)
            .Select(x=>$"{x.Transaction.Code}->{x.Transaction.Module}")
            .ToList();

        AddList(
            inactive.Count==0?"OK":"WARNING",
            "FW04",
            "Transactions",
            "INACTIVE_MODULE",
            "Transactions in inactive modules",
            inactive,
            inactive.Count==0?"None.":"Review: {0}",
            "SYS13");
    }

    private void CheckSecurity()
    {
        var roles=LoadList<DmsRoleDefinition>(Path.Combine(_context.ConfigurationRoot,"dms-roles.json"));
        var users=LoadList<DmsUser>(Path.Combine(_context.ConfigurationRoot,"users.json"));
        if(roles is null||users is null) return;
        var duplicates=roles.Where(x=>!string.IsNullOrWhiteSpace(x.Code)).GroupBy(x=>x.Code,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToList();
        AddList(duplicates.Count==0?"OK":"ERROR","FW06","Security","ROLE_DUP","Duplicate role codes",duplicates,duplicates.Count==0?"No duplicate role codes.":"Duplicate: {0}","SYS12");
        var roleMap=roles.Where(x=>!string.IsNullOrWhiteSpace(x.Code)).GroupBy(x=>x.Code,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
        var adminRole=roleMap.TryGetValue("DMS_ADMIN",out var ar)&&ar.IsActive;
        Add(adminRole?"OK":"CRITICAL","FW06","Security","ADMIN_ROLE","DMS_ADMIN role",adminRole?"Present and active.":"Missing or inactive; administrative lockout is possible.",Path.Combine(_context.ConfigurationRoot,"dms-roles.json"),"SYS12");
        var duplicateUsers=users.Where(x=>!string.IsNullOrWhiteSpace(x.WindowsLogin)).GroupBy(x=>x.WindowsLogin,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToList();
        AddList(duplicateUsers.Count==0?"OK":"ERROR","FW06","Security","USER_DUP","Duplicate Windows logins",duplicateUsers,duplicateUsers.Count==0?"Windows logins are unique.":"Duplicate: {0}","USR01");
        var admins=users.Where(x=>x.IsActive&&x.Roles.Contains("DMS_ADMIN",StringComparer.OrdinalIgnoreCase)).Select(x=>x.WindowsLogin).Where(x=>!string.IsNullOrWhiteSpace(x)).ToList();
        Add(admins.Count>0?"OK":"CRITICAL","FW06","Security","ACTIVE_ADMIN","Active DMS administrator",admins.Count>0?$"{admins.Count} active administrator(s): {string.Join(", ",admins)}":"No active user has DMS_ADMIN.",Path.Combine(_context.ConfigurationRoot,"users.json"),"USR01");
        var unknownUserRoles=users.Where(x=>x.IsActive).SelectMany(u=>u.Roles.Select(r=>(u.WindowsLogin,Role:r))).Where(x=>!roleMap.ContainsKey(x.Role)).Select(x=>$"{x.WindowsLogin}->{x.Role}").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AddList(unknownUserRoles.Count==0?"OK":"ERROR","FW06","Security","USER_ROLE_REFS","User role references",unknownUserRoles,unknownUserRoles.Count==0?"All active-user role references are valid.":"Unknown: {0}","USR01");
        var unknownTxRoles=_context.RuntimeTransactions.Where(x=>x.IsActive).SelectMany(t=>t.Roles.Select(r=>(t.Code,Role:r))).Where(x=>!roleMap.ContainsKey(x.Role)).Select(x=>$"{x.Code}->{x.Role}").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AddList(unknownTxRoles.Count==0?"OK":"ERROR","FW06","Security","TX_ROLE_REFS","Transaction role references",unknownTxRoles,unknownTxRoles.Count==0?"All transaction role references are valid.":"Unknown: {0}","SYS11");
    }

    private void CheckLocalization()
    {
        var root=Path.Combine(_context.ConfigurationRoot,"Localization");
        var indexPath=Path.Combine(root,"localization.index.json");
        if(!File.Exists(indexPath)) return;
        LocalizationIndex? index;
        try { index=JsonSerializer.Deserialize<LocalizationIndex>(ReadText(indexPath),JsonOptions); }
        catch(Exception ex){ Add("CRITICAL","FW01","Localization","INDEX_PARSE","Localization index",ex.Message,indexPath,"SYS01"); return; }
        var cultures=(index?.SupportedCultures??new()).Select(x=>x.Culture).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(cultures.Count==0){ Add("CRITICAL","FW01","Localization","CULTURES","Supported cultures","No supported cultures are configured.",indexPath,"SYS01"); return; }
        var def=!string.IsNullOrWhiteSpace(index?.DefaultCulture)?index!.DefaultCulture:cultures[0];
        var dicts=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var culture in cultures)
        {
            var path=Path.Combine(root,$"{culture}.json");
            try{ dicts[culture]=JsonSerializer.Deserialize<Dictionary<string,string>>(ReadText(path),JsonOptions)??new(); Add("OK","FW01","Localization","DICT","Localization "+culture,$"{dicts[culture].Count} key(s).",path,"FW01"); }
            catch(Exception ex){ Add("ERROR","FW01","Localization","DICT","Localization "+culture,ex.Message,path,"SYS01"); }
        }
        if(!dicts.TryGetValue(def,out var reference)){ Add("CRITICAL","FW01","Localization","DEFAULT","Default culture",$"Default culture '{def}' is not loadable.",root,"SYS01"); return; }
        foreach(var (culture,dictionary) in dicts)
        {
            var missing=reference.Keys.Except(dictionary.Keys,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToList();
            var extra=dictionary.Keys.Except(reference.Keys,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToList();
            var broken=dictionary.Where(x=>LooksCorrupted(x.Value)).Select(x=>x.Key).OrderBy(x=>x).ToList();
            AddList(missing.Count==0?"OK":"ERROR","FW01","Localization","MISSING_"+culture,"Missing keys — "+culture,missing,missing.Count==0?$"No keys missing vs {def}.":"Missing: {0}","FW01");
            AddList(extra.Count==0?"OK":"WARNING","FW01","Localization","EXTRA_"+culture,"Extra keys — "+culture,extra,extra.Count==0?$"No extra keys vs {def}.":"Extra: {0}","FW01");
            AddList(broken.Count==0?"OK":"ERROR","FW01","Localization","ENCODING_"+culture,"Encoding — "+culture,broken,broken.Count==0?"No mojibake markers detected.":"Potentially damaged keys: {0}","FW01");
        }
        var modules=LoadList<DmsModuleDefinition>(Path.Combine(_context.ConfigurationRoot,"dms-modules.json"))??new();
        foreach(var (culture,dictionary) in dicts)
        {
            var missingTx=_context.RuntimeTransactions.Where(x=>x.IsActive).Select(x=>$"Transaction.{x.Code}.Name").Where(k=>!dictionary.ContainsKey(k)).ToList();
            var missingModules=modules.Where(x=>x.IsActive).Select(x=>$"Module.{x.Code}.Name").Where(k=>!dictionary.ContainsKey(k)).ToList();
            AddList(missingTx.Count==0?"OK":"ERROR","FW01","Localization","TX_NAMES_"+culture,"Transaction names — "+culture,missingTx,missingTx.Count==0?"All active transactions have localized names.":"Missing: {0}","SYS01");
            AddList(missingModules.Count==0?"OK":"ERROR","FW01","Localization","MODULE_NAMES_"+culture,"Module names — "+culture,missingModules,missingModules.Count==0?"All active modules have localized names.":"Missing: {0}","SYS01");
        }
    }

    private void CheckUi()
    {
        string[] keys={"DmsBackgroundBrush","DmsPanelBrush","DmsForegroundBrush","DmsMutedForegroundBrush","DmsBorderBrush","DmsAccentBrush","DmsErrorBrush","DmsWarningBrush"};
        var missing=keys.Where(k=>Application.Current?.TryFindResource(k) is null).ToList();
        AddList(missing.Count==0?"OK":"ERROR","FW02","UI","BRUSHES","Shared DMS resources",missing,missing.Count==0?"Core brushes are loaded.":"Missing: {0}","FW02");
        var types=new[]{typeof(Button),typeof(TextBox),typeof(ComboBox),typeof(DataGrid),typeof(DataGridCell),typeof(DataGridColumnHeader)};
        var styles=types.Where(t=>Application.Current?.TryFindResource(t) is not Style).Select(t=>t.Name).ToList();
        AddList(styles.Count==0?"OK":"ERROR","FW02","UI","STYLES","Implicit control styles",styles,styles.Count==0?"Required implicit styles are loaded.":"Missing: {0}","FW02");
        Add("OK","FW02","UI","DIALOGS","DMS dialogs",$"{typeof(DmsConfirmDialog).Name}, {typeof(DmsTextPromptDialog).Name}",string.Empty,"FW02");
        Add("OK","FW02","UI","GRID_TOOLTIP","DataGrid clipped-text ToolTip",typeof(DmsDataGridCellToolTip).FullName ?? "available",string.Empty,"FW02");
    }

    private void CheckLogging()
    {
        var today=Path.Combine(_context.LogsRoot,$"dms-{DateTime.Now:yyyy-MM-dd}.log");
        if(File.Exists(today)){ var f=new FileInfo(today); Add("OK","FW05","Logging","TODAY_LOG","Current application log",$"{f.Length/1024d:0.0} KB; modified {f.LastWriteTime:yyyy-MM-dd HH:mm:ss}.",today,"LOG03"); }
        else Add("WARNING","FW05","Logging","TODAY_LOG","Current application log","Today's log does not exist yet.",today,"LOG03");
    }

    private void CheckWorkflowAndChecklistFiles()
    {
        var workflow=DmsWorkflowCatalog.CreateChecklistDefault();
        var states=workflow.States.Select(x=>x.Code).ToList();
        var set=states.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicate=states.GroupBy(x=>x,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToList();
        AddList(duplicate.Count==0?"OK":"ERROR","FW07","Workflow","STATE_DUP","Workflow state codes",duplicate,duplicate.Count==0?"State codes are unique.":"Duplicate: {0}","FW07");
        var broken=workflow.Transitions.Where(x=>!set.Contains(x.FromState)||!set.Contains(x.ToState)).Select(x=>$"{x.Code}:{x.FromState}->{x.ToState}").ToList();
        AddList(broken.Count==0?"OK":"ERROR","FW07","Workflow","TRANSITIONS","Workflow transition endpoints",broken,broken.Count==0?"Every transition references known states.":"Broken: {0}","FW07");
        Add(set.Contains(workflow.InitialState)?"OK":"ERROR","FW07","Workflow","INITIAL","Initial workflow state",set.Contains(workflow.InitialState)?$"'{workflow.InitialState}' exists.":$"'{workflow.InitialState}' does not exist.",string.Empty,"FW07");
    }

    private void CheckMasterData()
    {
        var root=Path.Combine(_context.DataRoot,"Data","MasterData");
        var org=LoadList<DmsOrganizationUnit>(Path.Combine(root,"organization-units.json"));
        var people=LoadList<DmsPerson>(Path.Combine(root,"people.json"));
        var dims=LoadList<UnitDimension>(Path.Combine(root,"unit-dimensions.json"));
        var units=LoadList<UnitDefinition>(Path.Combine(root,"units.json"));
        var users=LoadList<DmsUser>(Path.Combine(_context.ConfigurationRoot,"users.json"));
        if(org is null||people is null||dims is null||units is null||users is null){ Add("WARNING","FW09","Master data","INSPECT","Cross-entity master-data integrity","One or more master-data files are unavailable; FW09 cross-check skipped.",root,"FW09"); return; }
        try
        {
            var links=users.Select(x=>new DmsUserPersonLink(x.WindowsLogin,x.PersonId,x.IsActive)).ToList();
            var health=new DmsMasterDataInspector().Inspect(org,people,dims,units,links);
            foreach(var row in health) Add(row.Severity,"FW09","Master data","MASTER_"+Code(row.Area+"_"+row.Check),row.Check,row.Details,root,row.Area=="Users"?"USR01":"SYS01");
        }
        catch(Exception ex)
        {
            Add("ERROR","FW09","Master data","INSPECT_EXCEPTION","Cross-entity master-data integrity","Inspector could not complete: "+ex.Message,root,"FW09");
        }
    }

    private void CheckPerformance()
    {
        var snapshots=_context.Performance.GetSnapshots();
        if(snapshots.Count==0) Add("INFO","FW08","Performance","SAMPLES","Runtime performance history","No FW08 samples are available in this process. Run FW08 during smoke testing.",string.Empty,"FW08");
        else
        {
            var last=snapshots[^1];
            Add(last.WorkingSetMb<2048?"OK":"WARNING","FW08","Performance","MEMORY","Current working set",$"{last.WorkingSetMb:0.0} MB.",string.Empty,"FW08");
            Add(last.UiDelayMs<500?"OK":"WARNING","FW08","Performance","UI_DELAY","Current UI delay",$"{last.UiDelayMs:0.0} ms.",string.Empty,"FW08");
        }
        var summary=_context.Performance.GetTransactionSummary();
        var slow=summary.Where(x=>x.P95Ms>=3000).Select(x=>$"{x.TransactionCode}:{x.P95Ms:0}ms").ToList();
        AddList(slow.Count==0?"OK":"WARNING","FW08","Performance","SLOW","Slow transactions (P95 ≥ 3000 ms)",slow,slow.Count==0?"No measured transaction exceeds the threshold.":"Slow: {0}","FW08");
        var failures=summary.Where(x=>x.Failures>0).Select(x=>$"{x.TransactionCode}:{x.Failures}").ToList();
        AddList(failures.Count==0?"OK":"WARNING","FW08","Performance","FAILURES","Transaction failures in FW08 history",failures,failures.Count==0?"No failed transaction in current history.":"Failures: {0}","FW08");
    }

    private void CheckExtensions()=>Add("INFO","FW10","Extensions","STATUS","Extension framework","FW10 remains reserved for extension/plugin inventory and does not block this release.",string.Empty,"FW10");

    private void CheckDirectory(string fw,string name,string path,bool required,bool write,string fix)
    {
        if(string.IsNullOrWhiteSpace(path)||!Directory.Exists(path)){ Add(required?"ERROR":"WARNING",fw,"Paths",Code(name),name,string.IsNullOrWhiteSpace(path)?"Path is empty.":"Directory does not exist.",path,fix); return; }
        if(!write){ Add("OK",fw,"Paths",Code(name),name,"Directory exists.",path,fix); return; }
        var probe=Path.Combine(path,$".dms-fw11-{Guid.NewGuid():N}.tmp");
        try{ File.WriteAllText(probe,"DMS FW11 write test"); File.Delete(probe); Add("OK",fw,"Paths",Code(name),name,"Directory exists and is writable.",path,fix); }
        catch(Exception ex){ Add("ERROR",fw,"Paths",Code(name),name,"Directory is not writable: "+ex.Message,path,fix); }
        finally{ try{ if(File.Exists(probe)) File.Delete(probe);}catch{} }
    }

    private void CheckJson(string fw,string category,string name,string path,bool required,string fix)
    {
        if(!File.Exists(path)){ Add(required?"ERROR":"WARNING",fw,category,Code(name),name,required?"Required JSON is missing.":"Optional JSON is missing.",path,fix); return; }
        try{ using var doc=JsonDocument.Parse(ReadText(path)); Add("OK",fw,category,Code(name),name,$"Valid JSON; root={doc.RootElement.ValueKind}; modified {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss}.",path,fix); }
        catch(Exception ex){ Add(required?"ERROR":"WARNING",fw,category,Code(name),name,"Invalid JSON: "+ex.Message,path,fix); }
    }

    private void ScanJsonDirectory(string path,string fw,string category,string fix)
    {
        if(!Directory.Exists(path)){ Add("WARNING",fw,category,"DIRECTORY","JSON directory","Directory not found; subsystem may not be initialized yet.",path,fix); return; }
        try
        {
            var files=Directory.EnumerateFiles(path,"*.json",SearchOption.AllDirectories).ToList();
            var bad=new List<string>();
            foreach(var file in files){ try{ using var doc=JsonDocument.Parse(ReadText(file)); } catch{ bad.Add(Path.GetRelativePath(path,file)); } }
            AddList(bad.Count==0?"OK":"ERROR",fw,category,"JSON_SCAN","Subsystem JSON files",bad,bad.Count==0?$"{files.Count} JSON file(s) parsed successfully.":"Invalid file(s): {0}",fix);
        }
        catch(Exception ex)
        {
            Add("ERROR",fw,category,"JSON_SCAN","Subsystem JSON files","Directory scan failed: "+ex.Message,path,fix);
        }
    }

    private static List<T>? LoadList<T>(string path)
    {
        try{ return File.Exists(path)?JsonSerializer.Deserialize<List<T>>(ReadText(path),JsonOptions):null; }
        catch{ return null; }
    }

    private static string ReadText(string path){ using var reader=new StreamReader(path,detectEncodingFromByteOrderMarks:true); return reader.ReadToEnd(); }
    private static bool LooksCorrupted(string value)
    {
        if(string.IsNullOrWhiteSpace(value)) return false;
        string[] markers={"Ã„","Ã–","Ãœ","Ã¤","Ã¶","Ã¼","ÃŸ","ÄŤ","Ä›","Ä™","Äľ","Äĺ","ÄŹ","Å™","Å¡","Å¾","Åˆ","Å¯","Å¥","â€","â€“","â€”","â€¦","â€ž","â€œ","ï»¿","\uFFFD"};
        return markers.Any(x=>value.Contains(x,StringComparison.Ordinal));
    }
    private void Add(string severity,string fw,string category,string check,string name,string details,string source,string fix)=>_results.Add(new DmsReleaseCheckResult{Severity=Normalize(severity),FrameworkCode=fw,Category=category,CheckCode=check,Name=name,Details=details,Source=source,FixTransaction=fix});
    private void AddList(string severity,string fw,string category,string check,string name,IReadOnlyCollection<string> values,string format,string fix)=>Add(severity,fw,category,check,name,values.Count==0?format:string.Format(format,Sample(values)),string.Empty,fix);
    private static string Sample(IReadOnlyCollection<string> values){ var a=values.Take(25).ToList(); var s=string.Join(", ",a); if(values.Count>a.Count)s+=$", … (+{values.Count-a.Count})"; return s; }
    private static string Normalize(string severity)=>severity.Trim().ToUpperInvariant() switch{"CRITICAL"=>"CRITICAL","ERROR"=>"ERROR","WARNING"=>"WARNING","WARN"=>"WARNING","INFO"=>"INFO",_=>"OK"};
    private static string Code(string value)=>new(value.Select(ch=>char.IsLetterOrDigit(ch)?char.ToUpperInvariant(ch):'_').ToArray());

    private sealed class LocalizationIndex { public string DefaultCulture {get;set;}="en-US"; public List<SupportedCulture> SupportedCultures {get;set;}=new(); }
    private sealed class SupportedCulture { public string Culture {get;set;}=string.Empty; }
}
