using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private IReadOnlyList<EventPoolCatalogActViewModel> _eventPoolCatalogActs = Array.Empty<EventPoolCatalogActViewModel>();
    private IReadOnlyList<SeedAnalysisDisplayItemViewModel> _eventPoolCatalogSharedEvents = Array.Empty<SeedAnalysisDisplayItemViewModel>();

    public IReadOnlyList<EventPoolCatalogActViewModel> EventPoolCatalogActs => _eventPoolCatalogActs;

    public IReadOnlyList<SeedAnalysisDisplayItemViewModel> EventPoolCatalogSharedEvents => _eventPoolCatalogSharedEvents;

    public bool HasEventPoolCatalogActs => _eventPoolCatalogActs.Count > 0;

    public bool HasEventPoolCatalogSharedEvents => _eventPoolCatalogSharedEvents.Count > 0;

    public string EventPoolCatalogSharedEventCountText => $"事件数：{_eventPoolCatalogSharedEvents.Count}";

    private void InitializeEventPoolCatalog()
    {
        RefreshEventPoolCatalog();
    }

    private void RefreshEventPoolCatalog()
    {
        try
        {
            using var stream = OpenActsDataStream(SelectedGameVersion.Id);
            if (stream == null)
            {
                _eventPoolCatalogSharedEvents = Array.Empty<SeedAnalysisDisplayItemViewModel>();
                _eventPoolCatalogActs = Array.Empty<EventPoolCatalogActViewModel>();
                RaiseEventPoolCatalogSharedEventPropertiesChanged();
                RaisePropertyChanged(nameof(EventPoolCatalogActs));
                RaisePropertyChanged(nameof(HasEventPoolCatalogActs));
                return;
            }

            var model = JsonSerializer.Deserialize<Sts2ActsFileModel>(stream);
            var actNameLookup = LoadActNameLookup(SelectedGameVersion.Id);

            _eventPoolCatalogSharedEvents = (model?.SharedEvents ?? [])
                .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
                .Select(eventId => CreateSeedAnalysisEventDisplayItem(eventId))
                .ToList();

            _eventPoolCatalogActs = (model?.Acts ?? [])
                .Where(act => act.Number is >= 1 and <= 3)
                .Select(act => new EventPoolCatalogActViewModel(
                    act.Number,
                    GetActBranchDisplayName(act, actNameLookup),
                    FormatAncientSummary(act.Ancients),
                    (act.Events ?? [])
                        .Where(eventId => !string.IsNullOrWhiteSpace(eventId))
                        .Select(eventId => CreateSeedAnalysisEventDisplayItem(eventId))
                        .ToList()))
                .OrderBy(item => item.ActNumber)
                .ThenBy(item => item.BranchName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            RaiseEventPoolCatalogSharedEventPropertiesChanged();
            RaisePropertyChanged(nameof(EventPoolCatalogActs));
            RaisePropertyChanged(nameof(HasEventPoolCatalogActs));
        }
        catch (Exception ex)
        {
            LogWarn($"加载事件池信息失败：{ex.Message}");
            _eventPoolCatalogSharedEvents = Array.Empty<SeedAnalysisDisplayItemViewModel>();
            _eventPoolCatalogActs = Array.Empty<EventPoolCatalogActViewModel>();
            RaiseEventPoolCatalogSharedEventPropertiesChanged();
            RaisePropertyChanged(nameof(EventPoolCatalogActs));
            RaisePropertyChanged(nameof(HasEventPoolCatalogActs));
        }
    }

    private void RaiseEventPoolCatalogSharedEventPropertiesChanged()
    {
        RaisePropertyChanged(nameof(EventPoolCatalogSharedEvents));
        RaisePropertyChanged(nameof(HasEventPoolCatalogSharedEvents));
        RaisePropertyChanged(nameof(EventPoolCatalogSharedEventCountText));
    }

    private static string FormatAncientSummary(IReadOnlyList<string>? ancients)
    {
        if (ancients == null || ancients.Count == 0)
        {
            return "无古神开场事件";
        }

        var displayValues = ancients
            .Where(ancientId => !string.IsNullOrWhiteSpace(ancientId))
            .Select(ancientId => AncientDisplayCatalog.GetDisplayText(ancientId, ancientId))
            .ToList();

        return displayValues.Count == 0
            ? "无古神开场事件"
            : $"古神开场：{string.Join(" / ", displayValues)}";
    }

    internal sealed class EventPoolCatalogActViewModel
    {
        public EventPoolCatalogActViewModel(
            int actNumber,
            string branchName,
            string ancientSummary,
            IReadOnlyList<SeedAnalysisDisplayItemViewModel> events)
        {
            ActNumber = actNumber;
            BranchName = branchName;
            AncientSummary = ancientSummary;
            Events = events;
        }

        public int ActNumber { get; }

        public string BranchName { get; }

        public string AncientSummary { get; }

        public IReadOnlyList<SeedAnalysisDisplayItemViewModel> Events { get; }

        public string Title => $"第 {ActNumber} 幕 - {BranchName}";

        public string EventCountText => $"事件数：{Events.Count}";

        public bool HasEvents => Events.Count > 0;
    }
}
