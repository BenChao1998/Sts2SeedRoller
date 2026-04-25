using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SeedModel.Neow;
using SeedModel.Run;
using SeedModel.Seeds;
using SeedUi.Commands;
using SeedUi.Storage;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel
{
    private const int ArchiveEvaluationBatchSize = DefaultPartitionSize;
    private const int ViewerResultLimit = 200;
    private const string RandomSeedAlphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private AsyncRelayCommand _startArchiveScanCommand = null!;
    private AsyncRelayCommand _continueArchiveScanCommand = null!;
    private RelayCommand _cancelArchiveScanCommand = null!;
    private RelayCommand _refreshArchiveStateCommand = null!;
    private AsyncRelayCommand _runViewerSearchCommand = null!;
    private RelayCommand _openViewerSelectedRunCommand = null!;
    private RelayCommand _addViewerAct1RelicFilterCommand = null!;
    private RelayCommand _removeViewerAct1RelicFilterCommand = null!;
    private RelayCommand _addViewerAct1CardFilterCommand = null!;
    private RelayCommand _removeViewerAct1CardFilterCommand = null!;
    private RelayCommand _addViewerAct1PotionFilterCommand = null!;
    private RelayCommand _removeViewerAct1PotionFilterCommand = null!;
    private RelayCommand _addViewerAct2OptionFilterCommand = null!;
    private RelayCommand _removeViewerAct2OptionFilterCommand = null!;
    private RelayCommand _addViewerAct3OptionFilterCommand = null!;
    private RelayCommand _removeViewerAct3OptionFilterCommand = null!;
    private readonly SeedRunFilter _archivePassThroughFilter = new()
    {
        NeowFilter = NeowOptionFilter.Create(null, null, null, null, null)
    };

    private SeedArchiveDatabase? _seedArchiveDatabase;
    private CancellationTokenSource? _archiveScanCancellation;
    private bool _isArchiveScanning;
    private string _archiveDatabasePath = "Not initialized";
    private string _archiveDatabaseSummary = "Seed archive database is not initialized.";
    private string _archiveJobStatus = "No archive job has started yet.";
    private string _archiveSeedText = new string('0', SeedFormatter.DefaultLength);
    private string _archiveScanCount = "1000";
    private string _archiveSeedStep = "1";
    private SeedArchiveMode _selectedArchiveMode = SeedArchiveMode.Sequential;
    private CharacterId _archiveCharacter = CharacterId.Ironclad;
    private int _archiveAscension;
    private int _archiveScannedSeeds;
    private int _archiveStoredSeeds;
    private int _archiveSkippedSeeds;
    private int _archiveProgressMaximum = 1;
    private string _archiveProgressText = string.Empty;
    private SeedArchiveJobItemViewModel? _selectedArchiveJob;
    private string _viewerSeedFrom = string.Empty;
    private string _viewerSeedTo = string.Empty;
    private string _viewerAct1RelicId = string.Empty;
    private string _viewerAct1CardId = string.Empty;
    private string _viewerAct1PotionId = string.Empty;
    private string _viewerAct2AncientId = string.Empty;
    private string _viewerAct2OptionId = string.Empty;
    private string _viewerAct3AncientId = string.Empty;
    private string _viewerAct3OptionId = string.Empty;
    private string _viewerAct1RelicCatalogFilter = string.Empty;
    private string _viewerAct1CardCatalogFilter = string.Empty;
    private string _viewerAct1PotionCatalogFilter = string.Empty;
    private string _viewerAct2AncientCatalogFilter = string.Empty;
    private string _viewerAct2OptionCatalogFilter = string.Empty;
    private string _viewerAct3AncientCatalogFilter = string.Empty;
    private string _viewerAct3OptionCatalogFilter = string.Empty;
    private IReadOnlyList<ViewerFilterOption> _viewerAct1RelicOptionCatalog = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct1CardOptionCatalog = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct1PotionOptionCatalog = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct2AncientOptionCatalog = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct2OptionOptionCatalog = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct3AncientOptionCatalog = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct3OptionOptionCatalog = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct1RelicOptions = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct1CardOptions = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct1PotionOptions = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct2AncientOptions = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct2OptionOptions = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct3AncientOptions = CreateEmptyViewerFilterOptions();
    private IReadOnlyList<ViewerFilterOption> _viewerAct3OptionOptions = CreateEmptyViewerFilterOptions();
    private CharacterOption? _selectedViewerCharacter;
    private int? _viewerAscension;
    private string _viewerSummaryText = "No query has been run yet.";
    private SeedArchiveRunSummaryItemViewModel? _selectedViewerRunSummary;
    private RollResultViewModel? _selectedViewerRun;
    private bool _isViewerSearching;

    public ObservableCollection<SeedArchiveJobItemViewModel> ArchiveJobs { get; } = new();

    public ObservableCollection<SeedArchiveRunSummaryItemViewModel> ViewerRuns { get; } = new();

    public ObservableCollection<FilterChipViewModel> ViewerAct1RelicFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> ViewerAct1CardFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> ViewerAct1PotionFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> ViewerAct2OptionFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> ViewerAct3OptionFilterChips { get; } = new();

    public IReadOnlyList<ViewerFilterOption> ViewerAct1RelicOptions => _viewerAct1RelicOptions;

    public IReadOnlyList<ViewerFilterOption> ViewerAct1CardOptions => _viewerAct1CardOptions;

    public IReadOnlyList<ViewerFilterOption> ViewerAct1PotionOptions => _viewerAct1PotionOptions;

    public IReadOnlyList<ViewerFilterOption> ViewerAct2AncientOptions => _viewerAct2AncientOptions;

    public IReadOnlyList<ViewerFilterOption> ViewerAct2OptionOptions => _viewerAct2OptionOptions;

    public IReadOnlyList<ViewerFilterOption> ViewerAct3AncientOptions => _viewerAct3AncientOptions;

    public IReadOnlyList<ViewerFilterOption> ViewerAct3OptionOptions => _viewerAct3OptionOptions;

    public string ViewerAct1RelicCatalogFilter
    {
        get => _viewerAct1RelicCatalogFilter;
        set
        {
            if (SetProperty(ref _viewerAct1RelicCatalogFilter, value ?? string.Empty))
            {
                ApplyViewerAct1RelicFilter();
            }
        }
    }

    public string ViewerAct1CardCatalogFilter
    {
        get => _viewerAct1CardCatalogFilter;
        set
        {
            if (SetProperty(ref _viewerAct1CardCatalogFilter, value ?? string.Empty))
            {
                ApplyViewerAct1CardFilter();
            }
        }
    }

    public string ViewerAct1PotionCatalogFilter
    {
        get => _viewerAct1PotionCatalogFilter;
        set
        {
            if (SetProperty(ref _viewerAct1PotionCatalogFilter, value ?? string.Empty))
            {
                ApplyViewerAct1PotionFilter();
            }
        }
    }

    public string ViewerAct2AncientCatalogFilter
    {
        get => _viewerAct2AncientCatalogFilter;
        set
        {
            if (SetProperty(ref _viewerAct2AncientCatalogFilter, value ?? string.Empty))
            {
                ApplyViewerAct2AncientFilter();
            }
        }
    }

    public string ViewerAct2OptionCatalogFilter
    {
        get => _viewerAct2OptionCatalogFilter;
        set
        {
            if (SetProperty(ref _viewerAct2OptionCatalogFilter, value ?? string.Empty))
            {
                ApplyViewerAct2OptionFilter();
            }
        }
    }

    public string ViewerAct3AncientCatalogFilter
    {
        get => _viewerAct3AncientCatalogFilter;
        set
        {
            if (SetProperty(ref _viewerAct3AncientCatalogFilter, value ?? string.Empty))
            {
                ApplyViewerAct3AncientFilter();
            }
        }
    }

    public string ViewerAct3OptionCatalogFilter
    {
        get => _viewerAct3OptionCatalogFilter;
        set
        {
            if (SetProperty(ref _viewerAct3OptionCatalogFilter, value ?? string.Empty))
            {
                ApplyViewerAct3OptionFilter();
            }
        }
    }

    public IReadOnlyList<SeedArchiveModeOption> ArchiveModeOptions { get; } =
    [
        new(SeedArchiveMode.Sequential, "顺序递增"),
        new(SeedArchiveMode.Random, "随机铺种")
    ];

    private void InitializeSeedArchive()
    {
        _startArchiveScanCommand = new AsyncRelayCommand(StartArchiveScanAsync, CanStartArchiveScan);
        _continueArchiveScanCommand = new AsyncRelayCommand(ContinueArchiveScanAsync, CanContinueArchiveScan);
        _cancelArchiveScanCommand = new RelayCommand(CancelArchiveScan, () => IsArchiveScanning);
        _refreshArchiveStateCommand = new RelayCommand(RefreshArchiveState);
        _runViewerSearchCommand = new AsyncRelayCommand(RunViewerSearchAsync, () => !IsViewerSearching);
        _openViewerSelectedRunCommand = new RelayCommand(OpenSelectedViewerRun, () => SelectedViewerRunSummary != null);
        _addViewerAct1RelicFilterCommand = new RelayCommand(AddViewerAct1RelicFilter);
        _removeViewerAct1RelicFilterCommand = new RelayCommand(RemoveViewerAct1RelicFilter);
        _addViewerAct1CardFilterCommand = new RelayCommand(AddViewerAct1CardFilter);
        _removeViewerAct1CardFilterCommand = new RelayCommand(RemoveViewerAct1CardFilter);
        _addViewerAct1PotionFilterCommand = new RelayCommand(AddViewerAct1PotionFilter);
        _removeViewerAct1PotionFilterCommand = new RelayCommand(RemoveViewerAct1PotionFilter);
        _addViewerAct2OptionFilterCommand = new RelayCommand(AddViewerAct2OptionFilter);
        _removeViewerAct2OptionFilterCommand = new RelayCommand(RemoveViewerAct2OptionFilter);
        _addViewerAct3OptionFilterCommand = new RelayCommand(AddViewerAct3OptionFilter);
        _removeViewerAct3OptionFilterCommand = new RelayCommand(RemoveViewerAct3OptionFilter);
        RefreshViewerFilterOptions();

        try
        {
            EnsureSeedArchiveDatabase();
            RefreshArchiveState();
            _ = RunViewerSearchAsync();
        }
        catch (Exception ex)
        {
            ArchiveDatabaseSummary = $"铺种数据库初始化失败：{ex.Message}";
            LogError(ArchiveDatabaseSummary);
        }

        RefreshArchiveCommandStates();
    }

    private void RefreshArchiveCommandStates()
    {
        _startArchiveScanCommand?.RaiseCanExecuteChanged();
        _continueArchiveScanCommand?.RaiseCanExecuteChanged();
        _cancelArchiveScanCommand?.RaiseCanExecuteChanged();
        _runViewerSearchCommand?.RaiseCanExecuteChanged();
        _openViewerSelectedRunCommand?.RaiseCanExecuteChanged();
    }

    public bool IsArchiveScanning
    {
        get => _isArchiveScanning;
        private set
        {
            if (SetProperty(ref _isArchiveScanning, value))
            {
                _startArchiveScanCommand.RaiseCanExecuteChanged();
                _continueArchiveScanCommand.RaiseCanExecuteChanged();
                _cancelArchiveScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsViewerSearching
    {
        get => _isViewerSearching;
        private set
        {
            if (SetProperty(ref _isViewerSearching, value))
            {
                _runViewerSearchCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand StartArchiveScanCommand => _startArchiveScanCommand;

    public ICommand ContinueArchiveScanCommand => _continueArchiveScanCommand;

    public ICommand CancelArchiveScanCommand => _cancelArchiveScanCommand;

    public ICommand RefreshArchiveStateCommand => _refreshArchiveStateCommand;

    public ICommand RunViewerSearchCommand => _runViewerSearchCommand;

    public ICommand OpenViewerSelectedRunCommand => _openViewerSelectedRunCommand;

    public ICommand AddViewerAct1RelicFilterCommand => _addViewerAct1RelicFilterCommand;

    public ICommand RemoveViewerAct1RelicFilterCommand => _removeViewerAct1RelicFilterCommand;

    public ICommand AddViewerAct1CardFilterCommand => _addViewerAct1CardFilterCommand;

    public ICommand RemoveViewerAct1CardFilterCommand => _removeViewerAct1CardFilterCommand;

    public ICommand AddViewerAct1PotionFilterCommand => _addViewerAct1PotionFilterCommand;

    public ICommand RemoveViewerAct1PotionFilterCommand => _removeViewerAct1PotionFilterCommand;

    public ICommand AddViewerAct2OptionFilterCommand => _addViewerAct2OptionFilterCommand;

    public ICommand RemoveViewerAct2OptionFilterCommand => _removeViewerAct2OptionFilterCommand;

    public ICommand AddViewerAct3OptionFilterCommand => _addViewerAct3OptionFilterCommand;

    public ICommand RemoveViewerAct3OptionFilterCommand => _removeViewerAct3OptionFilterCommand;

    public string ArchiveDatabasePath
    {
        get => _archiveDatabasePath;
        private set => SetProperty(ref _archiveDatabasePath, value);
    }

    public string ArchiveDatabaseSummary
    {
        get => _archiveDatabaseSummary;
        private set => SetProperty(ref _archiveDatabaseSummary, value);
    }

    public string ArchiveJobStatus
    {
        get => _archiveJobStatus;
        private set => SetProperty(ref _archiveJobStatus, value);
    }

    public string ArchiveSeedText
    {
        get => _archiveSeedText;
        set => SetProperty(ref _archiveSeedText, value?.Trim().ToUpperInvariant() ?? string.Empty);
    }

    public string ArchiveScanCount
    {
        get => _archiveScanCount;
        set
        {
            if (SetProperty(ref _archiveScanCount, value ?? string.Empty))
            {
                _startArchiveScanCommand.RaiseCanExecuteChanged();
                _continueArchiveScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ArchiveSeedStep
    {
        get => _archiveSeedStep;
        set
        {
            if (SetProperty(ref _archiveSeedStep, value ?? string.Empty))
            {
                _startArchiveScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public SeedArchiveMode SelectedArchiveMode
    {
        get => _selectedArchiveMode;
        set => SetProperty(ref _selectedArchiveMode, value);
    }

    public CharacterId ArchiveCharacter
    {
        get => _archiveCharacter;
        set => SetProperty(ref _archiveCharacter, value);
    }

    public int ArchiveAscension
    {
        get => _archiveAscension;
        set => SetProperty(ref _archiveAscension, Math.Clamp(value, 0, AscensionOptions[^1]));
    }

    public int ArchiveScannedSeeds
    {
        get => _archiveScannedSeeds;
        set => SetProperty(ref _archiveScannedSeeds, value);
    }

    public int ArchiveStoredSeeds
    {
        get => _archiveStoredSeeds;
        set => SetProperty(ref _archiveStoredSeeds, value);
    }

    public int ArchiveSkippedSeeds
    {
        get => _archiveSkippedSeeds;
        set => SetProperty(ref _archiveSkippedSeeds, value);
    }

    public int ArchiveProgressMaximum
    {
        get => _archiveProgressMaximum;
        set => SetProperty(ref _archiveProgressMaximum, Math.Max(1, value));
    }

    public string ArchiveProgressText
    {
        get => _archiveProgressText;
        private set => SetProperty(ref _archiveProgressText, value);
    }

    public SeedArchiveJobItemViewModel? SelectedArchiveJob
    {
        get => _selectedArchiveJob;
        set
        {
            if (SetProperty(ref _selectedArchiveJob, value))
            {
                _continueArchiveScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public CharacterOption? SelectedViewerCharacter
    {
        get => _selectedViewerCharacter;
        set => SetProperty(ref _selectedViewerCharacter, value);
    }

    public int? ViewerAscension
    {
        get => _viewerAscension;
        set => SetProperty(ref _viewerAscension, value);
    }

    public string ViewerSeedFrom
    {
        get => _viewerSeedFrom;
        set => SetProperty(ref _viewerSeedFrom, value?.Trim().ToUpperInvariant() ?? string.Empty);
    }

    public string ViewerSeedTo
    {
        get => _viewerSeedTo;
        set => SetProperty(ref _viewerSeedTo, value?.Trim().ToUpperInvariant() ?? string.Empty);
    }

    public string ViewerAct1RelicId
    {
        get => _viewerAct1RelicId;
        set => SetProperty(ref _viewerAct1RelicId, NormalizeViewerFilterValue(value));
    }

    public string ViewerAct1CardId
    {
        get => _viewerAct1CardId;
        set => SetProperty(ref _viewerAct1CardId, NormalizeViewerFilterValue(value));
    }

    public string ViewerAct1PotionId
    {
        get => _viewerAct1PotionId;
        set => SetProperty(ref _viewerAct1PotionId, NormalizeViewerFilterValue(value));
    }

    public string ViewerAct2AncientId
    {
        get => _viewerAct2AncientId;
        set
        {
            if (SetProperty(ref _viewerAct2AncientId, NormalizeViewerFilterValue(value)))
            {
                RefreshViewerAct2OptionOptions(clearInvalidSelection: true);
            }
        }
    }

    public string ViewerAct2OptionId
    {
        get => _viewerAct2OptionId;
        set => SetProperty(ref _viewerAct2OptionId, NormalizeViewerFilterValue(value));
    }

    public string ViewerAct3AncientId
    {
        get => _viewerAct3AncientId;
        set
        {
            if (SetProperty(ref _viewerAct3AncientId, NormalizeViewerFilterValue(value)))
            {
                RefreshViewerAct3OptionOptions(clearInvalidSelection: true);
            }
        }
    }

    public string ViewerAct3OptionId
    {
        get => _viewerAct3OptionId;
        set => SetProperty(ref _viewerAct3OptionId, NormalizeViewerFilterValue(value));
    }

    public string ViewerSummaryText
    {
        get => _viewerSummaryText;
        private set => SetProperty(ref _viewerSummaryText, value);
    }

    public SeedArchiveRunSummaryItemViewModel? SelectedViewerRunSummary
    {
        get => _selectedViewerRunSummary;
        set
        {
            if (SetProperty(ref _selectedViewerRunSummary, value))
            {
                _openViewerSelectedRunCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RollResultViewModel? SelectedViewerRun
    {
        get => _selectedViewerRun;
        private set => SetProperty(ref _selectedViewerRun, value);
    }

    private bool CanStartArchiveScan() =>
        !IsArchiveScanning &&
        _dataset != null &&
        _ancientPreviewer != null &&
        TryParsePositiveInt(ArchiveScanCount, out _) &&
        (SelectedArchiveMode == SeedArchiveMode.Random || SeedFormatter.TryNormalize(ArchiveSeedText, out _, out _)) &&
        TryParsePositiveInt(ArchiveSeedStep, out _);

    private bool CanContinueArchiveScan() =>
        !IsArchiveScanning &&
        _dataset != null &&
        _ancientPreviewer != null &&
        SelectedArchiveJob != null &&
        TryParsePositiveInt(ArchiveScanCount, out _);

    internal void RefreshViewerFilterOptions()
    {
        _viewerAct1RelicOptionCatalog = BuildViewerCatalogOptions(
            StaticRelicCatalog,
            ViewerAct1RelicId,
            GetRelicDisplayName);
        ApplyViewerAct1RelicFilter();

        _viewerAct1CardOptionCatalog = BuildViewerCatalogOptions(
            StaticCardCatalog,
            ViewerAct1CardId,
            GetCardDisplayName);
        ApplyViewerAct1CardFilter();

        _viewerAct1PotionOptionCatalog = BuildViewerCatalogOptions(
            StaticPotionCatalog,
            ViewerAct1PotionId,
            GetPotionDisplayName);
        ApplyViewerAct1PotionFilter();

        _viewerAct2AncientOptionCatalog = BuildViewerAncientOptions(
            AncientDisplayCatalog.AllowedForAct2,
            ViewerAct2AncientId);
        ApplyViewerAct2AncientFilter();

        _viewerAct3AncientOptionCatalog = BuildViewerAncientOptions(
            AncientDisplayCatalog.AllowedForAct3,
            ViewerAct3AncientId);
        ApplyViewerAct3AncientFilter();

        RefreshViewerAct2OptionOptions(clearInvalidSelection: false);
        RefreshViewerAct3OptionOptions(clearInvalidSelection: false);
    }

    private void AddViewerAct1RelicFilter()
    {
        if (TryAddViewerFilterChip(ViewerAct1RelicFilterChips, ViewerAct1RelicId, ViewerAct1RelicOptions))
        {
            ViewerAct1RelicId = string.Empty;
        }
    }

    private void RemoveViewerAct1RelicFilter(object? parameter)
    {
        RemoveChipById(ViewerAct1RelicFilterChips, parameter as string);
    }

    private void AddViewerAct1CardFilter()
    {
        if (TryAddViewerFilterChip(ViewerAct1CardFilterChips, ViewerAct1CardId, ViewerAct1CardOptions))
        {
            ViewerAct1CardId = string.Empty;
        }
    }

    private void RemoveViewerAct1CardFilter(object? parameter)
    {
        RemoveChipById(ViewerAct1CardFilterChips, parameter as string);
    }

    private void AddViewerAct1PotionFilter()
    {
        if (TryAddViewerFilterChip(ViewerAct1PotionFilterChips, ViewerAct1PotionId, ViewerAct1PotionOptions))
        {
            ViewerAct1PotionId = string.Empty;
        }
    }

    private void RemoveViewerAct1PotionFilter(object? parameter)
    {
        RemoveChipById(ViewerAct1PotionFilterChips, parameter as string);
    }

    private void AddViewerAct2OptionFilter()
    {
        if (TryAddViewerFilterChip(ViewerAct2OptionFilterChips, ViewerAct2OptionId, ViewerAct2OptionOptions))
        {
            ViewerAct2OptionId = string.Empty;
        }
    }

    private void RemoveViewerAct2OptionFilter(object? parameter)
    {
        RemoveChipById(ViewerAct2OptionFilterChips, parameter as string);
    }

    private void AddViewerAct3OptionFilter()
    {
        if (TryAddViewerFilterChip(ViewerAct3OptionFilterChips, ViewerAct3OptionId, ViewerAct3OptionOptions))
        {
            ViewerAct3OptionId = string.Empty;
        }
    }

    private void RemoveViewerAct3OptionFilter(object? parameter)
    {
        RemoveChipById(ViewerAct3OptionFilterChips, parameter as string);
    }

    private void EnsureSeedArchiveDatabase()
    {
        if (_seedArchiveDatabase != null)
        {
            return;
        }

        var versionInfo = BuildSeedArchiveVersionInfo();
        var databasePath = SeedArchiveDatabase.BuildDatabasePath(AppContext.BaseDirectory, versionInfo);
        _seedArchiveDatabase = new SeedArchiveDatabase(databasePath, versionInfo);
        _seedArchiveDatabase.EnsureCreated();
    }

    private SeedArchiveVersionInfo BuildSeedArchiveVersionInfo()
    {
        var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var components = new[]
        {
            BuildDataVersionComponent("neow", ResolveDefaultDataPath(SelectedEvent)),
            BuildDataVersionComponent("acts", UiDataPathResolver.ResolveVersionedDataFilePath(SelectedGameVersion.Id, "sts2", "acts.json")),
            BuildDataVersionComponent("ancients", AncientDisplayCatalog.ResolveOptionDataPath(SelectedGameVersion.Id))
        };

        return new SeedArchiveVersionInfo(
            SeedArchiveDatabase.CurrentSchemaVersion,
            string.Join("|", components),
            appVersion);
    }

    private static string BuildDataVersionComponent(string label, string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            return $"{label}:embedded";
        }

        var fileInfo = new System.IO.FileInfo(filePath);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1}:{2}",
            label,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc.Ticks);
    }

    private void RefreshArchiveState()
    {
        if (_seedArchiveDatabase == null)
        {
            return;
        }

        var summary = _seedArchiveDatabase.GetSummary();
        ArchiveDatabasePath = summary.DatabasePath;
        ArchiveDatabaseSummary = $"数据库：{summary.RunCount:N0} 条种子，{summary.JobCount:N0} 个任务，Schema {summary.VersionInfo.SchemaVersion}";

        ArchiveJobs.Clear();
        foreach (var job in _seedArchiveDatabase.GetRecentJobs(20))
        {
            ArchiveJobs.Add(new SeedArchiveJobItemViewModel(job));
        }

        SelectedArchiveJob ??= ArchiveJobs.FirstOrDefault();
    }

    private async Task StartArchiveScanAsync()
    {
        if (_seedArchiveDatabase == null)
        {
            EnsureSeedArchiveDatabase();
        }

        if (_seedArchiveDatabase == null)
        {
            return;
        }

        if (!TryBuildArchiveJobCreateRequest(out var request, out var seedCount, out var error))
        {
            StatusMessage = error;
            LogError(error);
            return;
        }

        var job = _seedArchiveDatabase.CreateJob(request);
        RefreshArchiveState();
        await ExecuteArchiveJobAsync(job, seedCount);
    }

    private async Task ContinueArchiveScanAsync()
    {
        if (_seedArchiveDatabase == null || SelectedArchiveJob == null)
        {
            return;
        }

        var job = _seedArchiveDatabase.GetJob(SelectedArchiveJob.JobId);
        if (job == null)
        {
            StatusMessage = "未找到选中的铺种任务。";
            LogWarn(StatusMessage);
            return;
        }

        if (!TryParsePositiveInt(ArchiveScanCount, out var seedCount))
        {
            StatusMessage = "请输入有效的铺种数量。";
            LogError(StatusMessage);
            return;
        }

        await ExecuteArchiveJobAsync(job, seedCount);
    }

    private void CancelArchiveScan()
    {
        _archiveScanCancellation?.Cancel();
    }

    private async Task ExecuteArchiveJobAsync(SeedArchiveScanJob job, int targetCount)
    {
        if (_dataset == null || _ancientPreviewer == null || _seedArchiveDatabase == null)
        {
            StatusMessage = "铺种前需要先加载第一幕数据和第二/第三幕预览数据。";
            LogError(StatusMessage);
            return;
        }

        _archiveScanCancellation = new CancellationTokenSource();
        var token = _archiveScanCancellation.Token;
        IsArchiveScanning = true;
        ArchiveScannedSeeds = 0;
        ArchiveStoredSeeds = 0;
        ArchiveSkippedSeeds = 0;
        ArchiveProgressMaximum = targetCount;
        ArchiveProgressText = string.Empty;

        StatusMessage = $"开始铺种：{job.Character} A{job.Ascension}，模式={GetArchiveModeDisplayName(job.Mode)}，数量={targetCount:N0}";
        ArchiveJobStatus = $"正在铺种任务 {job.JobId[..8]}...";
        LogInfo(StatusMessage);

        try
        {
            job = _seedArchiveDatabase.UpdateJobStatus(job.JobId, SeedArchiveJobStatus.Running);
            var remaining = targetCount;
            var currentJob = job;

            while (remaining > 0 && !token.IsCancellationRequested)
            {
                var batchSize = Math.Min(ArchiveEvaluationBatchSize, remaining);
                var workItems = BuildArchiveWorkItems(currentJob, batchSize);
                var evaluatedRuns = await Task.Run(
                    () => EvaluateArchiveBatch(currentJob, workItems, token),
                    token);

                if (token.IsCancellationRequested)
                {
                    break;
                }

                var lastSeedText = workItems.LastOrDefault()?.SeedText;
                var nextIndex = currentJob.NextIndex + workItems.Count;
                var markCompleted = remaining == batchSize;
                var writeResult = _seedArchiveDatabase.SaveBatch(
                    currentJob.JobId,
                    evaluatedRuns.Select(item => item.Run).ToList(),
                    nextIndex,
                    lastSeedText,
                    markCompleted);

                currentJob = writeResult.UpdatedJob;
                ArchiveScannedSeeds += workItems.Count;
                ArchiveStoredSeeds += writeResult.InsertedRuns;
                ArchiveSkippedSeeds += writeResult.SkippedRuns;
                ArchiveProgressText = $"已铺 {ArchiveScannedSeeds:N0}，入库 {ArchiveStoredSeeds:N0}，跳过重复 {ArchiveSkippedSeeds:N0}";
                remaining -= batchSize;
            }

            if (token.IsCancellationRequested)
            {
                _seedArchiveDatabase.UpdateJobStatus(job.JobId, SeedArchiveJobStatus.Paused);
                ArchiveJobStatus = "铺种已取消，任务可继续。";
                StatusMessage = ArchiveJobStatus;
                LogInfo(StatusMessage);
            }
            else
            {
                ArchiveJobStatus = $"铺种完成：入库 {ArchiveStoredSeeds:N0} 条，跳过 {ArchiveSkippedSeeds:N0} 条重复。";
                StatusMessage = ArchiveJobStatus;
                LogInfo(StatusMessage);
            }

            RefreshArchiveState();
            await RunViewerSearchAsync();
        }
        catch (OperationCanceledException)
        {
            _seedArchiveDatabase.UpdateJobStatus(job.JobId, SeedArchiveJobStatus.Paused);
            ArchiveJobStatus = "铺种已取消，任务可继续。";
            StatusMessage = ArchiveJobStatus;
            LogInfo(StatusMessage);
            RefreshArchiveState();
        }
        catch (Exception ex)
        {
            _seedArchiveDatabase.UpdateJobStatus(job.JobId, SeedArchiveJobStatus.Failed);
            StatusMessage = $"铺种失败：{ex.Message}";
            ArchiveJobStatus = StatusMessage;
            LogError(StatusMessage);
            RefreshArchiveState();
        }
        finally
        {
            _archiveScanCancellation = null;
            IsArchiveScanning = false;
        }
    }

    private List<SeedWorkItem> BuildArchiveWorkItems(SeedArchiveScanJob job, int count)
    {
        var items = new List<SeedWorkItem>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var absoluteIndex = job.NextIndex + offset;
            var seedText = ResolveArchiveSeed(job, absoluteIndex);
            items.Add(new SeedWorkItem(absoluteIndex, seedText, SeedFormatter.ToUIntSeed(seedText)));
        }

        return items;
    }

    private IReadOnlyList<EvaluatedArchiveRun> EvaluateArchiveBatch(
        SeedArchiveScanJob job,
        List<SeedWorkItem> workItems,
        CancellationToken token)
    {
        var results = new ConcurrentBag<EvaluatedArchiveRun>();
        var partitioner = Partitioner.Create(workItems, loadBalance: true);
        var character = ParseArchiveCharacter(job.Character);
        var ancientAvailability = ResolveEffectiveAncientAvailability("archive");

        Parallel.ForEach(
            partitioner,
            () => new SeedRunEvaluator(_dataset!, _ancientPreviewer),
            (workItem, loopState, evaluator) =>
            {
                if (token.IsCancellationRequested)
                {
                    loopState.Stop();
                    return evaluator;
                }

                var context = new SeedRunEvaluationContext
                {
                    RunSeed = workItem.SeedValue,
                    SeedText = workItem.SeedText,
                    Character = character,
                    UnlockedCharacters = GetConfiguredUnlockedCharacters(),
                    PlayerCount = 1,
                    ScrollBoxesEligible = true,
                    AscensionLevel = job.Ascension,
                    AncientAvailability = ancientAvailability,
                    IncludeAct2 = true,
                    IncludeAct3 = true
                };

                var match = evaluator.Evaluate(context, _archivePassThroughFilter);
                if (match.Sts2Preview == null)
                {
                    throw new InvalidOperationException("第二/第三幕预览数据未生成。");
                }

                results.Add(new EvaluatedArchiveRun(
                    workItem.Index,
                    new SeedArchiveStoredRun
                    {
                        JobId = job.JobId,
                        SeedText = workItem.SeedText,
                        SeedValue = workItem.SeedValue,
                        SeedOrderValue = SeedArchiveDatabase.SeedOrderHelper.ToOrderValue(workItem.SeedText),
                        Character = job.Character,
                        Ascension = job.Ascension,
                        Act1Options = match.NeowOptions.ToList(),
                        Sts2Preview = match.Sts2Preview
                    }));

                return evaluator;
            },
            _ => { });

        return results.OrderBy(item => item.Index).ToList();
    }

    private bool TryBuildArchiveJobCreateRequest(
        out SeedArchiveJobCreateRequest request,
        out int seedCount,
        out string error)
    {
        request = default!;
        seedCount = 0;
        error = string.Empty;

        if (!TryParsePositiveInt(ArchiveScanCount, out seedCount))
        {
            error = "请输入有效的铺种数量。";
            return false;
        }

        if (!TryParsePositiveInt(ArchiveSeedStep, out var seedStep))
        {
            error = "请输入有效的铺种步长。";
            return false;
        }

        var startSeed = new string('0', SeedFormatter.DefaultLength);
        if (SelectedArchiveMode == SeedArchiveMode.Sequential &&
            !SeedFormatter.TryNormalize(ArchiveSeedText, out startSeed, out error))
        {
            return false;
        }

        request = new SeedArchiveJobCreateRequest
        {
            Mode = SelectedArchiveMode,
            Character = ArchiveCharacter.ToString(),
            Ascension = ArchiveAscension,
            StartSeedText = startSeed,
            SeedStep = seedStep,
            SequenceToken = SelectedArchiveMode == SeedArchiveMode.Random ? Guid.NewGuid().ToString("N") : startSeed,
            RequestedCount = seedCount
        };
        return true;
    }

    private static bool TryParsePositiveInt(string text, out int value)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private void ApplyViewerAct1RelicFilter()
    {
        _viewerAct1RelicOptions = FilterViewerOptions(_viewerAct1RelicOptionCatalog, ViewerAct1RelicCatalogFilter);
        RaisePropertyChanged(nameof(ViewerAct1RelicOptions));
    }

    private void ApplyViewerAct1CardFilter()
    {
        _viewerAct1CardOptions = FilterViewerOptions(_viewerAct1CardOptionCatalog, ViewerAct1CardCatalogFilter);
        RaisePropertyChanged(nameof(ViewerAct1CardOptions));
    }

    private void ApplyViewerAct1PotionFilter()
    {
        _viewerAct1PotionOptions = FilterViewerOptions(_viewerAct1PotionOptionCatalog, ViewerAct1PotionCatalogFilter);
        RaisePropertyChanged(nameof(ViewerAct1PotionOptions));
    }

    private void ApplyViewerAct2AncientFilter()
    {
        _viewerAct2AncientOptions = FilterViewerOptions(_viewerAct2AncientOptionCatalog, ViewerAct2AncientCatalogFilter);
        RaisePropertyChanged(nameof(ViewerAct2AncientOptions));
    }

    private void ApplyViewerAct2OptionFilter()
    {
        _viewerAct2OptionOptions = FilterViewerOptions(_viewerAct2OptionOptionCatalog, ViewerAct2OptionCatalogFilter);
        RaisePropertyChanged(nameof(ViewerAct2OptionOptions));
    }

    private void ApplyViewerAct3AncientFilter()
    {
        _viewerAct3AncientOptions = FilterViewerOptions(_viewerAct3AncientOptionCatalog, ViewerAct3AncientCatalogFilter);
        RaisePropertyChanged(nameof(ViewerAct3AncientOptions));
    }

    private void ApplyViewerAct3OptionFilter()
    {
        _viewerAct3OptionOptions = FilterViewerOptions(_viewerAct3OptionOptionCatalog, ViewerAct3OptionCatalogFilter);
        RaisePropertyChanged(nameof(ViewerAct3OptionOptions));
    }

    private void RefreshViewerAct2OptionOptions(bool clearInvalidSelection)
    {
        var options = GetViewerAct2RelicOptions();
        if (clearInvalidSelection &&
            !string.IsNullOrWhiteSpace(ViewerAct2AncientId) &&
            !string.IsNullOrWhiteSpace(ViewerAct2OptionId) &&
            !options.Any(option => string.Equals(option.Id, ViewerAct2OptionId, StringComparison.OrdinalIgnoreCase)))
        {
            ViewerAct2OptionId = string.Empty;
        }

        _viewerAct2OptionOptionCatalog = BuildViewerRelicOptions(
            options,
            ViewerAct2OptionId,
            optionId => AncientDisplayCatalog.TryGetRelicOption(optionId)?.DisplayText ?? optionId);
        ApplyViewerAct2OptionFilter();
    }

    private void RefreshViewerAct3OptionOptions(bool clearInvalidSelection)
    {
        var options = GetViewerAct3RelicOptions();
        if (clearInvalidSelection &&
            !string.IsNullOrWhiteSpace(ViewerAct3AncientId) &&
            !string.IsNullOrWhiteSpace(ViewerAct3OptionId) &&
            !options.Any(option => string.Equals(option.Id, ViewerAct3OptionId, StringComparison.OrdinalIgnoreCase)))
        {
            ViewerAct3OptionId = string.Empty;
        }

        _viewerAct3OptionOptionCatalog = BuildViewerRelicOptions(
            options,
            ViewerAct3OptionId,
            optionId => AncientDisplayCatalog.TryGetRelicOption(optionId)?.DisplayText ?? optionId);
        ApplyViewerAct3OptionFilter();
    }

    private bool TryAddViewerFilterChip(
        ObservableCollection<FilterChipViewModel> target,
        string? value,
        IReadOnlyList<ViewerFilterOption> options)
    {
        var normalizedValue = NormalizeViewerFilterValue(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            LogWarn("请先从下拉框中选择要添加的筛选项。");
            return false;
        }

        if (target.Any(chip => string.Equals(chip.Value, normalizedValue, StringComparison.OrdinalIgnoreCase)))
        {
            LogWarn("该筛选项已添加。");
            return false;
        }

        var label = options.FirstOrDefault(option => string.Equals(option.Value, normalizedValue, StringComparison.OrdinalIgnoreCase))?.Display
            ?? normalizedValue;
        target.Add(new FilterChipViewModel(normalizedValue, label));
        return true;
    }

    private string ResolveArchiveSeed(SeedArchiveScanJob job, int index)
    {
        if (job.Mode == SeedArchiveMode.Sequential)
        {
            var delta = checked(index * job.SeedStep);
            return SeedFormatter.Advance(job.StartSeedText, delta);
        }

        return GenerateIndexedRandomSeed(job.SequenceToken, index);
    }

    private static string GenerateIndexedRandomSeed(string sequenceToken, int index)
    {
        var payload = Encoding.UTF8.GetBytes($"{sequenceToken}:{index}");
        var hash = SHA256.HashData(payload);
        Span<char> chars = stackalloc char[SeedFormatter.DefaultLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = RandomSeedAlphabet[hash[i] % RandomSeedAlphabet.Length];
        }

        return new string(chars);
    }

    private void RunViewerSearchLegacy()
    {
        if (_seedArchiveDatabase == null)
        {
            return;
        }

        var criteria = new SeedArchiveSearchCriteria
        {
            Character = SelectedViewerCharacter?.Id.ToString(),
            Ascension = ViewerAscension,
            SeedTextFrom = NormalizeOptionalSeed(ViewerSeedFrom),
            SeedTextTo = NormalizeOptionalSeed(ViewerSeedTo),
            Act1RelicIds = ViewerAct1RelicFilterChips.Select(chip => chip.Value).ToList(),
            Act1CardIds = ViewerAct1CardFilterChips.Select(chip => chip.Value).ToList(),
            Act1PotionIds = ViewerAct1PotionFilterChips.Select(chip => chip.Value).ToList(),
            Act2AncientId = EmptyToNull(ViewerAct2AncientId),
            Act2OptionIds = ViewerAct2OptionFilterChips.Select(chip => chip.Value).ToList(),
            Act3AncientId = EmptyToNull(ViewerAct3AncientId),
            Act3OptionIds = ViewerAct3OptionFilterChips.Select(chip => chip.Value).ToList()
        };

        ViewerRuns.Clear();
        foreach (var row in _seedArchiveDatabase.SearchRuns(criteria, ViewerResultLimit))
        {
            ViewerRuns.Add(new SeedArchiveRunSummaryItemViewModel(row));
        }

        ViewerSummaryText = $"查询到 {ViewerRuns.Count:N0} 条结果。";
        SelectedViewerRunSummary = ViewerRuns.FirstOrDefault();
        SelectedViewerRun = null;
    }

    private async Task RunViewerSearchAsync()
    {
        if (_seedArchiveDatabase == null)
        {
            return;
        }

        var criteria = new SeedArchiveSearchCriteria
        {
            Character = SelectedViewerCharacter?.Id.ToString(),
            Ascension = ViewerAscension,
            SeedTextFrom = NormalizeOptionalSeed(ViewerSeedFrom),
            SeedTextTo = NormalizeOptionalSeed(ViewerSeedTo),
            Act1RelicIds = ViewerAct1RelicFilterChips.Select(chip => chip.Value).ToList(),
            Act1CardIds = ViewerAct1CardFilterChips.Select(chip => chip.Value).ToList(),
            Act1PotionIds = ViewerAct1PotionFilterChips.Select(chip => chip.Value).ToList(),
            Act2AncientId = EmptyToNull(ViewerAct2AncientId),
            Act2OptionIds = ViewerAct2OptionFilterChips.Select(chip => chip.Value).ToList(),
            Act3AncientId = EmptyToNull(ViewerAct3AncientId),
            Act3OptionIds = ViewerAct3OptionFilterChips.Select(chip => chip.Value).ToList()
        };

        IsViewerSearching = true;
        ViewerSummaryText = "正在查询...";
        StatusMessage = "正在查询铺种结果...";

        try
        {
            var database = _seedArchiveDatabase;
            var rows = await Task.Run(() => database.SearchRuns(criteria, ViewerResultLimit));

            ViewerRuns.Clear();
            foreach (var row in rows)
            {
                ViewerRuns.Add(new SeedArchiveRunSummaryItemViewModel(row));
            }

            ViewerSummaryText = $"查询到 {ViewerRuns.Count:N0} 条结果。";
            SelectedViewerRunSummary = ViewerRuns.FirstOrDefault();
            SelectedViewerRun = null;
            StatusMessage = ViewerSummaryText;
        }
        catch (Exception ex)
        {
            ViewerSummaryText = $"查询失败：{ex.Message}";
            StatusMessage = ViewerSummaryText;
            LogError(StatusMessage);
        }
        finally
        {
            IsViewerSearching = false;
        }
    }

    private void OpenSelectedViewerRun()
    {
        if (_seedArchiveDatabase == null || SelectedViewerRunSummary == null)
        {
            return;
        }

        var run = _seedArchiveDatabase.LoadRun(SelectedViewerRunSummary.RunId);
        if (run == null)
        {
            StatusMessage = "未能读取选中的铺种记录。";
            LogWarn(StatusMessage);
            return;
        }

        var character = ParseArchiveCharacter(run.Character);
        var characterName = CharacterOptions.FirstOrDefault(item => item.Id == character)?.DisplayName ?? run.Character;
        SelectedViewerRun = new RollResultViewModel(
            run.SeedText,
            run.SeedValue,
            character,
            characterName,
            run.Act1Options,
            poolAnalysis: null,
            relicVisibilityAnalysis: null,
            poolFilter: null,
            run.Sts2Preview,
            requiresAct2: false,
            requiresAct3: false,
            run.Ascension);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeViewerFilterValue(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string? NormalizeOptionalSeed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return SeedFormatter.TryNormalize(value, out var normalized, out _)
            ? normalized
            : null;
    }

    private static CharacterId ParseArchiveCharacter(string value)
    {
        _ = CharacterIdExtensions.TryParse(value, out var character);
        return character;
    }

    private static string GetArchiveModeDisplayName(SeedArchiveMode mode) =>
        mode switch
        {
            SeedArchiveMode.Sequential => "顺序递增",
            SeedArchiveMode.Random => "随机铺种",
            _ => mode.ToString()
        };

    private IReadOnlyList<AncientDisplayCatalog.AncientRelicDisplayOption> GetViewerAct2RelicOptions()
    {
        var source = string.IsNullOrWhiteSpace(ViewerAct2AncientId)
            ? AncientDisplayCatalog.AllowedForAct2.SelectMany(option => option.RelicOptions)
            : AncientDisplayCatalog.GetRelicOptions(ViewerAct2AncientId);
        return DistinctViewerRelicOptions(source);
    }

    private IReadOnlyList<AncientDisplayCatalog.AncientRelicDisplayOption> GetViewerAct3RelicOptions()
    {
        var source = string.IsNullOrWhiteSpace(ViewerAct3AncientId)
            ? AncientDisplayCatalog.AllowedForAct3.SelectMany(option => option.RelicOptions)
            : AncientDisplayCatalog.GetRelicOptions(ViewerAct3AncientId);
        return DistinctViewerRelicOptions(source);
    }

    private static IReadOnlyList<AncientDisplayCatalog.AncientRelicDisplayOption> DistinctViewerRelicOptions(
        IEnumerable<AncientDisplayCatalog.AncientRelicDisplayOption> source)
    {
        var result = new List<AncientDisplayCatalog.AncientRelicDisplayOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in source)
        {
            if (string.IsNullOrWhiteSpace(option.Id) || !seen.Add(option.Id))
            {
                continue;
            }

            result.Add(option);
        }

        return result;
    }

    private static IReadOnlyList<ViewerFilterOption> BuildViewerCatalogOptions(
        IReadOnlyList<CatalogItem> catalog,
        string? selectedValue,
        Func<string, string> fallbackDisplayFactory)
    {
        return BuildViewerOptions(
            catalog.Select(item => new ViewerFilterOption(item.Value, item.Display)),
            selectedValue,
            fallbackDisplayFactory);
    }

    private static IReadOnlyList<ViewerFilterOption> BuildViewerAncientOptions(
        IReadOnlyList<AncientDisplayCatalog.AncientDisplayOption> catalog,
        string? selectedValue)
    {
        return BuildViewerOptions(
            catalog.Select(item => new ViewerFilterOption(item.Id, item.DisplayText)),
            selectedValue,
            ancientId => AncientDisplayCatalog.GetDisplayText(ancientId, ancientId));
    }

    private static IReadOnlyList<ViewerFilterOption> BuildViewerRelicOptions(
        IReadOnlyList<AncientDisplayCatalog.AncientRelicDisplayOption> catalog,
        string? selectedValue,
        Func<string, string> fallbackDisplayFactory)
    {
        return BuildViewerOptions(
            catalog.Select(item => new ViewerFilterOption(item.Id, item.DisplayText)),
            selectedValue,
            fallbackDisplayFactory);
    }

    private static IReadOnlyList<ViewerFilterOption> BuildViewerOptions(
        IEnumerable<ViewerFilterOption> options,
        string? selectedValue,
        Func<string, string> fallbackDisplayFactory)
    {
        var result = new List<ViewerFilterOption> { ViewerFilterOption.Any };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { string.Empty };

        foreach (var option in options)
        {
            var normalizedValue = NormalizeViewerFilterValue(option.Value);
            if (!seen.Add(normalizedValue))
            {
                continue;
            }

            result.Add(option with { Value = normalizedValue });
        }

        var normalizedSelectedValue = NormalizeViewerFilterValue(selectedValue);
        if (!string.IsNullOrEmpty(normalizedSelectedValue) && seen.Add(normalizedSelectedValue))
        {
            var display = fallbackDisplayFactory(normalizedSelectedValue);
            result.Insert(1, new ViewerFilterOption(normalizedSelectedValue, display));
        }

        return result;
    }

    private static IReadOnlyList<ViewerFilterOption> CreateEmptyViewerFilterOptions() =>
        new[] { ViewerFilterOption.Any };

    private static IReadOnlyList<ViewerFilterOption> FilterViewerOptions(
        IReadOnlyList<ViewerFilterOption> source,
        string filter)
    {
        if (source.Count == 0)
        {
            return Array.Empty<ViewerFilterOption>();
        }

        var anyOption = source.FirstOrDefault(option => string.IsNullOrEmpty(option.Value)) ?? ViewerFilterOption.Any;
        if (string.IsNullOrWhiteSpace(filter))
        {
            return source;
        }

        var terms = filter
            .Split(TermSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.ToUpperInvariant())
            .ToArray();
        if (terms.Length == 0)
        {
            return source;
        }

        var matches = source
            .Where(option => !string.IsNullOrEmpty(option.Value))
            .Where(option => terms.All(term => BuildSearchKey(option.Display, option.Value).Contains(term, StringComparison.Ordinal)))
            .ToList();

        matches.Insert(0, anyOption);
        return matches;
    }

    internal sealed record SeedArchiveModeOption(SeedArchiveMode Value, string DisplayName);

    internal sealed record ViewerFilterOption(string Value, string Display)
    {
        public static ViewerFilterOption Any { get; } = new(string.Empty, "任意");
    }

    internal sealed class SeedArchiveJobItemViewModel
    {
        public SeedArchiveJobItemViewModel(SeedArchiveScanJob job)
        {
            JobId = job.JobId;
            Mode = GetArchiveModeDisplayName(job.Mode);
            Status = job.Status.ToString();
            Character = job.Character;
            Ascension = job.Ascension;
            Progress = $"{job.StoredCount:N0} 入库 / {job.SkippedCount:N0} 跳过 / 下次从 #{job.NextIndex:N0}";
            LastSeedText = job.LastSeedText ?? "—";
            CreatedAt = job.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public string JobId { get; }

        public string Mode { get; }

        public string Status { get; }

        public string Character { get; }

        public int Ascension { get; }

        public string Progress { get; }

        public string LastSeedText { get; }

        public string CreatedAt { get; }
    }

    internal sealed class SeedArchiveRunSummaryItemViewModel
    {
        public SeedArchiveRunSummaryItemViewModel(SeedArchiveRunSummary row)
        {
            RunId = row.RunId;
            JobId = row.JobId;
            SeedText = row.SeedText;
            Character = row.Character;
            Ascension = row.Ascension;
            Act2Ancient = row.Act2AncientId ?? "—";
            Act3Ancient = row.Act3AncientId ?? "—";
            CreatedAt = row.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public long RunId { get; }

        public string JobId { get; }

        public string SeedText { get; }

        public string Character { get; }

        public int Ascension { get; }

        public string Act2Ancient { get; }

        public string Act3Ancient { get; }

        public string CreatedAt { get; }
    }

    private sealed record EvaluatedArchiveRun(int Index, SeedArchiveStoredRun Run);
}
