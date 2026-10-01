using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DMS.Core.Sap
{
    public sealed class SapScreenBomResolver
    {
        private const string Plant = "2000";
        private const string RequiredAlternative = "01";

        private static readonly TimeSpan CacheTtl =
            TimeSpan.FromHours(1);

        private readonly JsonSapBomRepository _repository;
        private readonly object _sync = new();

        private IReadOnlyList<SapBom> _boms =
            Array.Empty<SapBom>();

        private DateTime _loadedAtUtc =
            DateTime.MinValue;

        public SapScreenBomResolver(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException(
                    "DMS root directory is required.",
                    nameof(rootDirectory));
            }

            var storagePaths =
                new SapStoragePaths(rootDirectory);

            _repository =
                new JsonSapBomRepository(
                    storagePaths.SapBomSnapshotsFilePath);
        }

        /// <summary>
        /// Returns every text item (T) from plant 2000 / BOM alternative 01.
        /// The order follows the BOM item position.
        /// </summary>
        public Task<IReadOnlyList<string>> ResolveScreenTextsAsync(
            string sapNumber,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(sapNumber))
            {
                return Task.FromResult<IReadOnlyList<string>>(
                    Array.Empty<string>());
            }

            EnsureLoaded();

            var materialKey =
                NormalizeMaterialNumber(sapNumber);

            var candidates =
                _boms
                    .Where(b =>
                        string.Equals(
                            NormalizeMaterialNumber(b.MaterialNumber),
                            materialKey,
                            StringComparison.OrdinalIgnoreCase))
                    .Where(b =>
                        string.Equals(
                            b.Plant?.Trim(),
                            Plant,
                            StringComparison.OrdinalIgnoreCase))
                    .Where(b =>
                        string.Equals(
                            NormalizeAlternative(b.Alternative),
                            RequiredAlternative,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(b =>
                        string.Equals(
                            b.BomUsage?.Trim(),
                            "1",
                            StringComparison.OrdinalIgnoreCase)
                            ? 0
                            : 1)
                    .ThenByDescending(b => b.ImportedAt)
                    .ToList();

            // One current snapshot/usage is enough. We take all T items from it.
            foreach (var bom in candidates)
            {
                var texts =
                    bom.Items
                        .Where(IsScreenTextItem)
                        .OrderBy(i => ParsePosition(i.Position))
                        .Select(i => i.ItemText?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                if (texts.Length > 0)
                {
                    return Task.FromResult<IReadOnlyList<string>>(texts);
                }
            }

            return Task.FromResult<IReadOnlyList<string>>(
                Array.Empty<string>());
        }

        /// <summary>
        /// Compatibility helper for older callers.
        /// </summary>
        public async Task<string?> ResolveScreenTextAsync(
            string sapNumber,
            CancellationToken cancellationToken = default)
        {
            var texts =
                await ResolveScreenTextsAsync(
                    sapNumber,
                    cancellationToken);

            return texts.FirstOrDefault();
        }

        public void ClearCache()
        {
            lock (_sync)
            {
                _boms = Array.Empty<SapBom>();
                _loadedAtUtc = DateTime.MinValue;
            }
        }

        private void EnsureLoaded()
        {
            if (_boms.Count > 0 &&
                DateTime.UtcNow - _loadedAtUtc < CacheTtl)
            {
                return;
            }

            lock (_sync)
            {
                if (_boms.Count > 0 &&
                    DateTime.UtcNow - _loadedAtUtc < CacheTtl)
                {
                    return;
                }

                _boms = _repository.LoadAll();
                _loadedAtUtc = DateTime.UtcNow;
            }
        }

        private static bool IsScreenTextItem(
            SapBomItem item)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.ItemText))
            {
                return false;
            }

            var category =
                item.ItemCategory?.Trim()
                ?? string.Empty;

            return string.Equals(
                       category,
                       "T",
                       StringComparison.OrdinalIgnoreCase)
                   ||
                   category.Contains(
                       "TEXT",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeMaterialNumber(
            string? value)
        {
            var text =
                value?.Trim()
                ?? string.Empty;

            if (text.Length == 0)
            {
                return string.Empty;
            }

            var trimmed =
                text.TrimStart('0');

            return trimmed.Length == 0
                ? "0"
                : trimmed;
        }

        private static string NormalizeAlternative(
            string? value)
        {
            var text =
                value?.Trim()
                ?? string.Empty;

            return int.TryParse(text, out var n)
                ? n.ToString("00")
                : text;
        }

        private static int ParsePosition(
            string? value) =>
            int.TryParse(value, out var n)
                ? n
                : int.MaxValue;
    }
}