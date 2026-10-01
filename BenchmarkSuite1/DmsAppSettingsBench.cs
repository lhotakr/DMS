using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;
using System;
using System.IO;
using DMS.Desktop.Configuration;

namespace DMS.Benchmarks
{
    [CPUUsageDiagnoser]
    public class DmsAppSettingsBench
    {
        private string _configPath;
        [GlobalSetup]
        public void Setup()
        {
            var baseDir = AppContext.BaseDirectory;
            var configDir = Path.Combine(baseDir, "Config");
            Directory.CreateDirectory(configDir);
            _configPath = Path.Combine(configDir, "appsettings.json");
            // Write a representative JSON payload. Adjust if DmsAppSettings expects specific properties.
            File.WriteAllText(_configPath, "{\"Example\": \"value\"}");
        }

        [Benchmark]
        public void LoadSettings()
        {
            var svc = new DmsAppSettingsService();
            var _ = svc.Load();
        }
    }
}