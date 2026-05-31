using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using SeedModel.Sts2;
using SeedUi.Commands;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private string _progressSaveLoadStatus = "progress.save：启动后自动检查";
    private string _progressSaveSummary = "未执行 progress.save 检查。";
    private string _progressSavePathText = "路径：—";
    private string _progressSaveEpochText = "已识别纪元：—";
    private string _progressSaveAncientRuleText = "当前古神规则：默认全解锁";
    private bool _isProgressSaveLoaded;
    private bool _hasProgressSavePath;
    private RelayCommand? _selectProgressSaveCommand;

    public string ProgressSaveLoadStatus
    {
        get => _progressSaveLoadStatus;
        private set => SetProperty(ref _progressSaveLoadStatus, value);
    }

    public string ProgressSaveSummary
    {
        get => _progressSaveSummary;
        private set => SetProperty(ref _progressSaveSummary, value);
    }

    public string ProgressSavePathText
    {
        get => _progressSavePathText;
        private set => SetProperty(ref _progressSavePathText, value);
    }

    public string ProgressSaveEpochText
    {
        get => _progressSaveEpochText;
        private set => SetProperty(ref _progressSaveEpochText, value);
    }

    public string ProgressSaveAncientRuleText
    {
        get => _progressSaveAncientRuleText;
        private set => SetProperty(ref _progressSaveAncientRuleText, value);
    }

    public bool IsProgressSaveLoaded
    {
        get => _isProgressSaveLoaded;
        private set => SetProperty(ref _isProgressSaveLoaded, value);
    }

    public bool HasProgressSavePath
    {
        get => _hasProgressSavePath;
        private set => SetProperty(ref _hasProgressSavePath, value);
    }

    public RelayCommand SelectProgressSaveCommand =>
        _selectProgressSaveCommand ??= new RelayCommand(SelectProgressSave);

    private void SelectProgressSave()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "progress.save|progress.save|所有文件 (*.*)|*.*",
            Title = "选择 progress.save",
            FileName = "progress.save"
        };

        if (dialog.ShowDialog() == true)
        {
            UseProgressSaveFile(dialog.FileName);
        }
    }

    public void UseProgressSaveFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "未选择 progress.save。";
            return;
        }

        _preferredProgressSavePath = path;
        RefreshAncientAvailabilityStatus("manual progress.save", shouldLog: true);
        StatusMessage = IsProgressSaveLoaded
            ? "已切换 progress.save。"
            : "progress.save 读取失败，已回退默认进度规则。";
    }

    private Sts2AncientAvailability ResolveEffectiveAncientAvailability(string scenario)
    {
        var resolved = RefreshAncientAvailabilityStatus(scenario, shouldLog: true);
        return resolved.Availability;
    }

    private UiAncientAvailabilityResolver.ResolvedAncientAvailabilityResult RefreshAncientAvailabilityStatus(
        string scenario,
        bool shouldLog)
    {
        var resolved = UiAncientAvailabilityResolver.Resolve(_preferredProgressSavePath);
        var ruleText = FormatAncientAvailability(resolved.Availability);

        IsProgressSaveLoaded = resolved.UsedProgressSave;
        HasProgressSavePath = !string.IsNullOrWhiteSpace(resolved.ProgressSavePath);
        ProgressSaveLoadStatus = resolved.UsedProgressSave
            ? "progress.save：已成功加载"
            : resolved.ProgressSavePath == null
                ? "progress.save：未找到，已回退默认规则"
                : "progress.save：读取失败，已回退默认规则";
        ProgressSaveSummary = resolved.Summary;
        ProgressSavePathText = resolved.ProgressSavePath == null
            ? "路径：—"
            : $"路径：{resolved.ProgressSavePath}";
        ProgressSaveEpochText = resolved.RevealedEpochIds.Count == 0
            ? "已识别纪元：—"
            : $"已识别纪元（{resolved.RevealedEpochIds.Count}）：{string.Join(", ", resolved.RevealedEpochIds)}";
        ProgressSaveAncientRuleText = $"当前古神规则：{ruleText}";

        if (shouldLog)
        {
            LogInfo($"[古神解锁] {scenario}: {resolved.Summary}");
            LogInfo($"[古神解锁] {scenario}: {ruleText}");
        }

        return resolved;
    }

    private static string FormatAncientAvailability(Sts2AncientAvailability availability)
    {
        var disabledShared = FormatAncientIds(availability.DisabledSharedAncientIds);
        var disabledActs = availability.DisabledActAncientIds.Count == 0
            ? "无"
            : string.Join(
                "; ",
                availability.DisabledActAncientIds
                    .OrderBy(entry => entry.Key)
                    .Select(entry => $"第{entry.Key}幕=[{FormatAncientIds(entry.Value)}]"));

        return $"共享古神禁用=[{disabledShared}]，分幕古神禁用={disabledActs}";
    }

    private static string FormatAncientIds(IReadOnlyList<string> ancientIds)
    {
        return ancientIds.Count == 0 ? "无" : string.Join(", ", ancientIds);
    }
}
