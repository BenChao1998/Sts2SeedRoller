using System;
using System.Collections.Generic;
using System.Linq;
using SeedModel.Neow;

namespace SeedUi.ViewModels;

internal sealed class OptionDisplayViewModel
{
    public OptionDisplayViewModel(NeowOptionResult result)
    {
        RelicId = result.RelicId;
        Pool = result.Pool;
        Kind = result.Kind.ToString();
        Title = result.Title ?? result.RelicId;
        Description = result.Description ?? string.Empty;
        Note = result.Note ?? string.Empty;
        Details = result.Details.Select(FormatDetail).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
    }

    public string RelicId { get; }

    public string Pool { get; }

    public string Kind { get; }

    public string Title { get; }

    public string Description { get; }

    public string Note { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public IReadOnlyList<string> Details { get; }

    private static string FormatDetail(RewardDetail detail)
    {
        var label = string.IsNullOrWhiteSpace(detail.Label) ? string.Empty : $"{detail.Label}: ";
        var modelSegment = string.IsNullOrWhiteSpace(detail.ModelId) ? string.Empty : $" [{detail.ModelId}]";
        var value = string.IsNullOrWhiteSpace(detail.Value) && detail.Amount.HasValue
            ? detail.Amount.Value.ToString()
            : detail.Value;
        return $"{label}{value}{modelSegment}";
    }
}
