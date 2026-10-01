using BenchmarkDotNet.Attributes;
using DMS.Core.Quality;
using DMS.Core.Sap;
using Microsoft.VSDiagnostics;

namespace BenchmarkSuite1;
[CPUUsageDiagnoser]
public class QualityTaskOverviewBench
{
    private QualityTaskOverviewService _service = null!;
    [GlobalSetup]
    public void Setup()
    {
        var materials = Enumerable.Range(0, 10_000).Select(index => new SapMaterial { MaterialNumber = (index + 1).ToString(), OldMaterialNumber = $"OLD-{index + 1}", MaterialStatus = "Active" }).ToList();
        var printVersions = Enumerable.Range(0, 1_000).Select(index => new QualityPrintVersion { SapMaterialNumber = (index % 10_000 + 1).ToString(), FullPrintVersionNumber = $"PV-{index}", Tasks = { new QualityTask { Number = index, Text = "Task" } } }).ToList();
        _service = new QualityTaskOverviewService(materials, printVersions);
    }

    [Benchmark]
    public void BuildRows()
    {
        var _ = _service.BuildRows();
    }
}