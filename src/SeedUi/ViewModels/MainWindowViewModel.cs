using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Input;
using SeedModel.Events;
using SeedModel.Neow;
using SeedModel.Run;
using SeedModel.Seeds;
using SeedModel.Sts2;
using SeedUi.Commands;

namespace SeedUi.ViewModels;

internal sealed partial class MainWindowViewModel : ObservableObject
{
    private const int MaxRollCount = 200_000;
    private const int MaxSeedStep = 10_000;
    private const int MaxLogEntries = 500;
    private const int ProgressReportMinimum = 1_000;
    private const int DefaultPartitionSize = 4_096;
    private const string EmbeddedDatasetResource = "SeedUi.Data.Neow.options.json";
    private const string EmbeddedActsResource = "SeedUi.Data.sts2.acts.json";
    private const string EmbeddedAncientOptionsResource = "SeedUi.Data.ancients.options.json";
    private const string EmbeddedAncientOptionsZhResource = "SeedUi.Data.ancients.options.zhs.json";
    private static readonly char[] TermSeparators = [',', ';', ' ', '|', '/', '\n', '\r', '\t'];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _configFilePath = Path.Combine(AppContext.BaseDirectory, "config.json");
    private readonly AsyncRelayCommand _rollCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly AsyncRelayCommand _loadDatasetCommand;
    private readonly AsyncRelayCommand _loadConfigCommand;
    private readonly AsyncRelayCommand _saveConfigCommand;
    private readonly RelayCommand _clearResultsCommand;
    private readonly AsyncRelayCommand _exportResultsCommand;
    private readonly RelayCommand _copyResultCommand;
    private readonly RelayCommand _addAct2OptionFilterCommand;
    private readonly RelayCommand _removeAct2OptionFilterCommand;
    private readonly RelayCommand _addAct3OptionFilterCommand;
    private readonly RelayCommand _removeAct3OptionFilterCommand;

    private NeowOptionDataset? _dataset;
    private SeedEventMetadata _selectedEvent;
    private CancellationTokenSource? _rollCancellation;
    private bool _isRolling;

    public bool IsRolling
    {
        get => _isRolling;
        private set => SetProperty(ref _isRolling, value);
    }
    private string _datasetSummary = "尚未加载数据";
    private string _relicCatalogFilter = string.Empty;
    private string _cardCatalogFilter = string.Empty;
    private string _potionCatalogFilter = string.Empty;
    private string _filterRelicTerms = string.Empty;
    private CatalogItem? _selectedRelicCatalogItem;
    private CatalogItem? _selectedCardCatalogItem;
    private CatalogItem? _selectedPotionCatalogItem;
    private string _seedText = new string('0', SeedFormatter.DefaultLength);
    private string _rollCount = "100";
    private string _seedStep = "1";
    private bool _hasResult;
    private CharacterId _selectedCharacter = CharacterId.Ironclad;
    private int _selectedAscensionLevel;
    private SeedRollMode _selectedSeedMode = SeedRollMode.Random;
    private int _scannedSeeds = 1;
    private int _hitSeeds;
    private int _hitOptions;
    private int _progressMaximum = 1;
    private string _statusMessage = "等待操作…";
    private string _rollProgressText = string.Empty;
    private IReadOnlyList<CatalogItem> _relicCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _cardCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _potionCatalog = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredRelics = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredCards = Array.Empty<CatalogItem>();
    private IReadOnlyList<CatalogItem> _filteredPotions = Array.Empty<CatalogItem>();
    private bool _includeAct2;
    private bool _includeAct3;
    private bool _isAncientPreviewAvailable;
    private readonly Sts2RunPreviewer? _ancientPreviewer;
    private readonly IReadOnlyList<AncientDisplayCatalog.AncientDisplayOption> _act2FilterOptions =
        AncientDisplayCatalog.AllowedForAct2;

    private readonly IReadOnlyList<AncientDisplayCatalog.AncientDisplayOption> _act3FilterOptions =
        AncientDisplayCatalog.AllowedForAct3;
    private string _act2AncientFilter = string.Empty;
    private string _act3AncientFilter = string.Empty;
    private IReadOnlyList<AncientDisplayCatalog.AncientRelicDisplayOption> _act2RelicOptions =
        Array.Empty<AncientDisplayCatalog.AncientRelicDisplayOption>();
    private IReadOnlyList<AncientDisplayCatalog.AncientRelicDisplayOption> _act3RelicOptions =
        Array.Empty<AncientDisplayCatalog.AncientRelicDisplayOption>();
    private AncientDisplayCatalog.AncientRelicDisplayOption? _selectedAct2RelicOption;
    private AncientDisplayCatalog.AncientRelicDisplayOption? _selectedAct3RelicOption;
    private string _ancientFilterSummary = "第二幕：任意 | 第三幕：任意";
    private RollResultViewModel? _selectedResult;
    private int _selectedTabIndex;

    public ObservableCollection<FilterChipViewModel> Act2OptionFilterChips { get; } = new();

    public ObservableCollection<FilterChipViewModel> Act3OptionFilterChips { get; } = new();

    public MainWindowViewModel()
    {
        EventOptions = SeedEventRegistry.All;
        _selectedEvent = EventOptions.First();

        CharacterOptions = CreateCharacterOptions();
        AscensionOptions = Enumerable.Range(0, 11).ToList();
        SeedModeOptions =
        [
            new SeedModeOption(SeedRollMode.Random, "随机模式"),
            new SeedModeOption(SeedRollMode.Sequential, "顺序递增"),
            new SeedModeOption(SeedRollMode.RandomUntilHit, "命中即停")
        ];
        Results = new ObservableCollection<RollResultViewModel>();
        Logs = new ObservableCollection<LogEntryViewModel>();
        RelicFilterChips = new ObservableCollection<FilterChipViewModel>();
        CardFilterChips = new ObservableCollection<FilterChipViewModel>();
        PotionFilterChips = new ObservableCollection<FilterChipViewModel>();
        Act2OptionFilterChips.CollectionChanged += OnAncientOptionChipsChanged;
        Act3OptionFilterChips.CollectionChanged += OnAncientOptionChipsChanged;

        Results.CollectionChanged += OnResultsChanged;

        _rollCommand = new AsyncRelayCommand(RollAsync, CanRoll);
        _cancelCommand = new RelayCommand(CancelRoll, () => _isRolling);
        _loadDatasetCommand = new AsyncRelayCommand(LoadDatasetAsync);
        _loadConfigCommand = new AsyncRelayCommand(LoadConfigAsync);
        _saveConfigCommand = new AsyncRelayCommand(SaveConfigAsync);
        _clearResultsCommand = new RelayCommand(ClearResults);
        _exportResultsCommand = new AsyncRelayCommand(ExportResultsAsync, () => Results.Count > 0);
        _copyResultCommand = new RelayCommand(CopySeedToClipboard, parameter => parameter is RollResultViewModel);

        AddRelicFilterCommand = new RelayCommand(AddRelicFilter);
        RemoveRelicFilterCommand = new RelayCommand(RemoveRelicFilter);
        AddCardFilterCommand = new RelayCommand(AddCardFilter);
        RemoveCardFilterCommand = new RelayCommand(RemoveCardFilter);
        AddPotionFilterCommand = new RelayCommand(AddPotionFilter);
        RemovePotionFilterCommand = new RelayCommand(RemovePotionFilter);
        _addAct2OptionFilterCommand = new RelayCommand(AddAct2OptionFilter);
        _removeAct2OptionFilterCommand = new RelayCommand(RemoveAct2OptionFilter);
        _addAct3OptionFilterCommand = new RelayCommand(AddAct3OptionFilter);
        _removeAct3OptionFilterCommand = new RelayCommand(RemoveAct3OptionFilter);
        ClearLogsCommand = new RelayCommand(ClearLogs);

        _ancientPreviewer = InitializeAncientPreviewer();
        UpdateAct2RelicOptions();
        UpdateAct3RelicOptions();
        UpdateAncientFilterSummary();
    }
    public bool TryAutoLoadOnStartup => true;

    private Sts2RunPreviewer? InitializeAncientPreviewer()
    {
        // Try loading from files first
        var optionPath = AncientDisplayCatalog.ResolveOptionDataPath();
        var actDataPath = Path.Combine(AppContext.BaseDirectory, "data", "sts2", "acts.json");

        if (File.Exists(optionPath) && File.Exists(actDataPath))
        {
            try
            {
                var previewer = Sts2RunPreviewer.CreateFromDataFiles(optionPath, actDataPath);
                IsAncientPreviewAvailable = true;
                return previewer;
            }
            catch (Exception ex)
            {
                LogWarn($"从文件加载第二/第三幕古神数据失败，尝试嵌入资源：{ex.Message}");
            }
        }

        // Fallback: try embedded resources (for single-file deployments)
        try
        {
            var previewer = TryCreateFromEmbeddedResources();
            if (previewer != null)
            {
                IsAncientPreviewAvailable = true;
                return previewer;
            }
        }
        catch (Exception ex)
        {
            LogWarn($"从嵌入资源加载第二/第三幕古神数据失败：{ex.Message}");
        }

        return DisableAncientPreview("未找到完整的第二/第三幕古神数据（缺少 options.json 或 acts.json），相关功能已禁用。");
    }

    private Sts2RunPreviewer? TryCreateFromEmbeddedResources()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        // Find the best ancient options resource (prefer zh)
        string? ancientResource = null;
        foreach (var name in resourceNames)
        {
            if (!name.Contains("SeedUi.Data.ancients."))
                continue;
            if (!name.EndsWith("options.json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (ancientResource == null ||
                name.EndsWith(EmbeddedAncientOptionsZhResource, StringComparison.OrdinalIgnoreCase))
            {
                ancientResource = name;
            }
        }

        if (ancientResource == null)
            return null;

        var actsResource = resourceNames.FirstOrDefault(n =>
            n.Contains("SeedUi.Data.sts2.") && n.EndsWith(".acts.json", StringComparison.OrdinalIgnoreCase));

        if (actsResource == null)
            return null;

        using var ancientStream = assembly.GetManifestResourceStream(ancientResource)
            ?? throw new FileNotFoundException("Ancient resource not found.");
        using var actsStream = assembly.GetManifestResourceStream(actsResource)
            ?? throw new FileNotFoundException("Acts resource not found.");

        LogInfo("从嵌入资源加载第二/第三幕古神数据（单文件模式）。");
        return Sts2RunPreviewer.Create(ancientStream, actsStream);
    }

    private Sts2RunPreviewer? DisableAncientPreview(string message)
    {
        IsAncientPreviewAvailable = false;
        IncludeAct2 = false;
        IncludeAct3 = false;
        LogWarn(message);
        return null;
    }

    public IReadOnlyList<SeedEventMetadata> EventOptions { get; }

    public SeedEventMetadata SelectedEvent
    {
        get => _selectedEvent;
        set => SetSelectedEvent(value);
    }

    public bool IsSelectedEventImplemented => SelectedEvent?.IsImplemented ?? false;

    public string SelectedEventStatusText
    {
        get
        {
            if (SelectedEvent == null)
            {
                return string.Empty;
            }

            var description = SelectedEvent.Description;
            var status = SelectedEvent.IsImplemented
                ? $"{SelectedEvent.DisplayName} 功能已启用。"
                : $"{SelectedEvent.DisplayName} 尚未实装，仅提供配置入口。";

            if (string.IsNullOrWhiteSpace(description))
            {
                return status;
            }

            return $"{description} {status}";
        }
    }

    public string SelectedEventDataHint =>
        SelectedEvent?.Type == SeedEventType.Act1Neow
            ? "Neow 数据使用内置的 data/neow/options.json，启动应用或切换事件时会自动加载；替换该文件后重新启动或切换事件即可生效。"
            : $"{SelectedEvent?.DisplayName ?? "该事件"} 当前仅提供配置入口，Roll 功能尚未实现。";

    public IReadOnlyList<CharacterOption> CharacterOptions { get; }

    public IReadOnlyList<int> AscensionOptions { get; }

    public IReadOnlyList<SeedModeOption> SeedModeOptions { get; }

    public IReadOnlyList<AncientDisplayCatalog.AncientDisplayOption> Act2FilterOptions => _act2FilterOptions;

    public IReadOnlyList<AncientDisplayCatalog.AncientDisplayOption> Act3FilterOptions => _act3FilterOptions;

    public IReadOnlyList<AncientDisplayCatalog.AncientRelicDisplayOption> Act2RelicOptions => _act2RelicOptions;

    public IReadOnlyList<AncientDisplayCatalog.AncientRelicDisplayOption> Act3RelicOptions => _act3RelicOptions;

    public ObservableCollection<RollResultViewModel> Results { get; }

    public RollResultViewModel? SelectedResult
    {
        get => _selectedResult;
        set => SetProperty(ref _selectedResult, value);
    }

    public ObservableCollection<LogEntryViewModel> Logs { get; }

    public ObservableCollection<FilterChipViewModel> RelicFilterChips { get; }

    public ObservableCollection<FilterChipViewModel> CardFilterChips { get; }

    public ObservableCollection<FilterChipViewModel> PotionFilterChips { get; }

    public IEnumerable<CatalogItem> RelicCatalogView => _filteredRelics;

    public IEnumerable<CatalogItem> CardCatalogView => _filteredCards;

    public IEnumerable<CatalogItem> PotionCatalogView => _filteredPotions;

    public ICommand LoadDatasetCommand => _loadDatasetCommand;

    public ICommand RollCommand => _rollCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand ClearResultsCommand => _clearResultsCommand;

    public ICommand ExportResultsCommand => _exportResultsCommand;

    public ICommand LoadConfigCommand => _loadConfigCommand;

    public ICommand SaveConfigCommand => _saveConfigCommand;

    public ICommand ClearLogsCommand { get; }


    public ICommand CopyResultCommand => _copyResultCommand;

    public ICommand AddRelicFilterCommand { get; }

    public ICommand RemoveRelicFilterCommand { get; }

    public ICommand AddCardFilterCommand { get; }

    public ICommand RemoveCardFilterCommand { get; }

    public ICommand AddPotionFilterCommand { get; }

    public ICommand RemovePotionFilterCommand { get; }

    public ICommand AddAct2OptionFilterCommand => _addAct2OptionFilterCommand;

    public ICommand RemoveAct2OptionFilterCommand => _removeAct2OptionFilterCommand;

    public ICommand AddAct3OptionFilterCommand => _addAct3OptionFilterCommand;

    public ICommand RemoveAct3OptionFilterCommand => _removeAct3OptionFilterCommand;

    public string DatasetSummary
    {
        get => _datasetSummary;
        private set => SetProperty(ref _datasetSummary, value);
    }

    public string RelicCatalogFilter
    {
        get => _relicCatalogFilter;
        set
        {
            if (SetProperty(ref _relicCatalogFilter, value ?? string.Empty))
            {
                ApplyRelicFilter();
            }
        }
    }

    public string CardCatalogFilter
    {
        get => _cardCatalogFilter;
        set
        {
            if (SetProperty(ref _cardCatalogFilter, value ?? string.Empty))
            {
                ApplyCardFilter();
            }
        }
    }

    public string PotionCatalogFilter
    {
        get => _potionCatalogFilter;
        set
        {
            if (SetProperty(ref _potionCatalogFilter, value ?? string.Empty))
            {
                ApplyPotionFilter();
            }
        }
    }

    public CatalogItem? SelectedRelicCatalogItem
    {
        get => _selectedRelicCatalogItem;
        set => SetProperty(ref _selectedRelicCatalogItem, value);
    }

    public CatalogItem? SelectedCardCatalogItem
    {
        get => _selectedCardCatalogItem;
        set => SetProperty(ref _selectedCardCatalogItem, value);
    }

    public CatalogItem? SelectedPotionCatalogItem
    {
        get => _selectedPotionCatalogItem;
        set => SetProperty(ref _selectedPotionCatalogItem, value);
    }

    public string FilterRelicTerms
    {
        get => _filterRelicTerms;
        set => SetProperty(ref _filterRelicTerms, value ?? string.Empty);
    }

    public string SeedText
    {
        get => _seedText;
        set
        {
            var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
            SetProperty(ref _seedText, normalized);
        }
    }

    public string RollCount
    {
        get => _rollCount;
        set => SetProperty(ref _rollCount, value ?? string.Empty);
    }

    public string SeedStep
    {
        get => _seedStep;
        set => SetProperty(ref _seedStep, value ?? string.Empty);
    }

    public bool HasResult
    {
        get => _hasResult;
        private set
        {
            if (SetProperty(ref _hasResult, value))
            {
                _exportResultsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public CharacterId SelectedCharacter
    {
        get => _selectedCharacter;
        set => SetProperty(ref _selectedCharacter, value);
    }

    public int SelectedAscensionLevel
    {
        get => _selectedAscensionLevel;
        set => SetProperty(ref _selectedAscensionLevel, Math.Clamp(value, 0, AscensionOptions[^1]));
    }

    public SeedRollMode SelectedSeedMode
    {
        get => _selectedSeedMode;
        set
        {
            if (SetProperty(ref _selectedSeedMode, value))
            {
                RaisePropertyChanged(nameof(IsIncrementalMode));
            }
        }
    }

    public bool IsIncrementalMode => SelectedSeedMode == SeedRollMode.Sequential;

    public bool IncludeAct2
    {
        get => _includeAct2;
        set
        {
            if (SetProperty(ref _includeAct2, value))
            {
                if (value && string.IsNullOrWhiteSpace(Act2AncientFilter))
                {
                    Act2AncientFilter = _act2FilterOptions.FirstOrDefault()?.Id ?? string.Empty;
                }
                RaisePropertyChanged(nameof(AncientPreviewStatusText));
                UpdateAncientFilterSummary();
            }
        }
    }

    public bool IncludeAct3
    {
        get => _includeAct3;
        set
        {
            if (SetProperty(ref _includeAct3, value))
            {
                if (value && string.IsNullOrWhiteSpace(Act3AncientFilter))
                {
                    Act3AncientFilter = _act3FilterOptions.FirstOrDefault()?.Id ?? string.Empty;
                }
                RaisePropertyChanged(nameof(AncientPreviewStatusText));
                UpdateAncientFilterSummary();
            }
        }
    }

    public string Act2AncientFilter
    {
        get => _act2AncientFilter;
        set
        {
            if (SetProperty(ref _act2AncientFilter, NormalizeAncientInput(value)))
            {
                UpdateAct2RelicOptions();
                UpdateAncientFilterSummary();
            }
        }
    }

    public string Act3AncientFilter
    {
        get => _act3AncientFilter;
        set
        {
            if (SetProperty(ref _act3AncientFilter, NormalizeAncientInput(value)))
            {
                UpdateAct3RelicOptions();
                UpdateAncientFilterSummary();
            }
        }
    }

    public AncientDisplayCatalog.AncientRelicDisplayOption? SelectedAct2RelicOption
    {
        get => _selectedAct2RelicOption;
        set => SetProperty(ref _selectedAct2RelicOption, value);
    }

    public AncientDisplayCatalog.AncientRelicDisplayOption? SelectedAct3RelicOption
    {
        get => _selectedAct3RelicOption;
        set => SetProperty(ref _selectedAct3RelicOption, value);
    }

    public bool IsAncientPreviewAvailable
    {
        get => _isAncientPreviewAvailable;
        private set
        {
            if (SetProperty(ref _isAncientPreviewAvailable, value))
            {
                RaisePropertyChanged(nameof(IsAncientPreviewUnavailable));
                RaisePropertyChanged(nameof(AncientPreviewStatusText));
            }
        }
    }

    public bool IsAncientPreviewUnavailable => !IsAncientPreviewAvailable;

    public string AncientPreviewStatusText
    {
        get
        {
            if (!IsAncientPreviewAvailable)
            {
                return "古神预览不可用：缺少第二/第三幕数据文件。";
            }

            if (IncludeAct2 && IncludeAct3)
            {
                return "古神预览启用：显示第二幕与第三幕古神。";
            }

            if (IncludeAct2)
            {
                return "古神预览启用：仅显示第二幕古神。";
            }

            if (IncludeAct3)
            {
                return "古神预览启用：仅显示第三幕古神。";
            }

            return "古神预览已关闭，仅展示第一幕。";
        }
    }

    public string AncientFilterSummary
    {
        get => _ancientFilterSummary;
        private set => SetProperty(ref _ancientFilterSummary, value);
    }

    public int ScannedSeeds
    {
        get => _scannedSeeds;
        private set => SetProperty(ref _scannedSeeds, value);
    }

    public int HitSeeds
    {
        get => _hitSeeds;
        private set => SetProperty(ref _hitSeeds, value);
    }

    public int HitOptions
    {
        get => _hitOptions;
        private set => SetProperty(ref _hitOptions, value);
    }

    public int ProgressMaximum
    {
        get => _progressMaximum;
        private set => SetProperty(ref _progressMaximum, Math.Max(1, value));
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string RollProgressText
    {
        get => _rollProgressText;
        private set => SetProperty(ref _rollProgressText, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, Math.Max(0, value));
    }

    /// <summary>Raised when the View should navigate to a specific tab page (0=Config, 1=Result, 2=Logs).</summary>
    public event Action<int>? NavigationRequested;

    private void RaiseNavigation(int pageIndex)
    {
        NavigationRequested?.Invoke(pageIndex);
    }

    private static IReadOnlyList<CharacterOption> CreateCharacterOptions()
    {
        var list = new List<CharacterOption>
        {
            new(CharacterId.Ironclad, "铁甲战士"),
            new(CharacterId.Silent, "静默猎手"),
            new(CharacterId.Defect, "故障机器人"),
            new(CharacterId.Necrobinder, "亡灵契约师"),
            new(CharacterId.Regent, "储君")
        };
        return list;
    }

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _exportResultsCommand.RaiseCanExecuteChanged();
    }

    private bool CanRoll() =>
        !_isRolling &&
        _dataset != null;

    private async Task LoadDatasetAsync()
    {
        if (!IsSelectedEventImplemented)
        {
            StatusMessage = $"事件 {SelectedEvent.DisplayName} 尚未实现，无法加载数据。";
            LogWarn(StatusMessage);
            return;
        }

        if (SelectedEvent.Type != SeedEventType.Act1Neow)
        {
            StatusMessage = $"当前版本仅支持 Neow 事件，{SelectedEvent.DisplayName} 的数据加载暂不可用。";
            LogWarn(StatusMessage);
            return;
        }

        try
        {
            var dataPath = ResolveDefaultDataPath(SelectedEvent);
            StatusMessage = "正在加载第一幕数据…";
            var dataset = await Task.Run(() => LoadDatasetInternal(dataPath));
            _dataset = dataset;
            DatasetSummary = $"已加载 {dataset.Options.Count} 条遗物、{dataset.Cards.Count} 张卡牌、{dataset.Potions.Count} 瓶药水";
            BuildCatalogs(dataset);
            var sourceLabel = File.Exists(dataPath) ? dataPath : "内置数据";
            StatusMessage = $"已从 {sourceLabel} 加载 {SelectedEvent.DisplayName} 数据。";
            LogInfo(StatusMessage);
        }
        catch (Exception ex)
        {
            _dataset = null;
            DatasetSummary = "数据加载失败。";
            StatusMessage = $"加载数据失败：{ex.Message}";
            LogError(StatusMessage);
        }
        finally
        {
            _rollCommand.RaiseCanExecuteChanged();
        }
    }

    private static NeowOptionDataset LoadDatasetInternal(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            return NeowOptionDataLoader.Load(stream);
        }

        var assembly = Assembly.GetExecutingAssembly();
        using var embedded = assembly.GetManifestResourceStream(EmbeddedDatasetResource)
            ?? throw new FileNotFoundException("找不到内置数据文件。");
        return NeowOptionDataLoader.Load(embedded);
    }

    private void BuildCatalogs(NeowOptionDataset dataset)
    {
        _relicCatalog = dataset.Options
            .Select(option =>
            {
                var title = string.IsNullOrWhiteSpace(option.Title) ? option.RelicId : option.Title!;
                var display = $"{title} ({option.RelicId})";
                return new CatalogItem(option.RelicId, display, BuildSearchKey(display, option.RelicId, option.Description, option.Note));
            })
            .OrderBy(item => item.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cardCatalog = dataset.Cards
            .Select(card =>
            {
                var display = $"{card.Name} ({card.Id})";
                return new CatalogItem(card.Id, display, BuildSearchKey(display, card.Id));
            })
            .OrderBy(item => item.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _potionCatalog = dataset.Potions
            .Select(potion =>
            {
                var display = $"{potion.Name} ({potion.Id})";
                return new CatalogItem(potion.Id, display, BuildSearchKey(display, potion.Id));
            })
            .OrderBy(item => item.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ApplyRelicFilter();
        ApplyCardFilter();
        ApplyPotionFilter();

        SelectedRelicCatalogItem = null;
        SelectedCardCatalogItem = null;
        SelectedPotionCatalogItem = null;
    }

    private static string BuildSearchKey(params string?[] fragments)
    {
        var text = string.Join(" ", fragments.Where(f => !string.IsNullOrWhiteSpace(f)));
        return text.ToUpperInvariant();
    }

    private void ApplyRelicFilter()
    {
        _filteredRelics = FilterCatalog(_relicCatalog, _relicCatalogFilter);
        RaisePropertyChanged(nameof(RelicCatalogView));
    }

    private void ApplyCardFilter()
    {
        _filteredCards = FilterCatalog(_cardCatalog, _cardCatalogFilter);
        RaisePropertyChanged(nameof(CardCatalogView));
    }

    private void ApplyPotionFilter()
    {
        _filteredPotions = FilterCatalog(_potionCatalog, _potionCatalogFilter);
        RaisePropertyChanged(nameof(PotionCatalogView));
    }

    private static IReadOnlyList<CatalogItem> FilterCatalog(IReadOnlyList<CatalogItem> source, string filter)
    {
        if (source.Count == 0)
        {
            return Array.Empty<CatalogItem>();
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            return source;
        }

        var terms = filter
            .Split(TermSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToUpperInvariant())
            .ToArray();

        if (terms.Length == 0)
        {
            return source;
        }

        return source
            .Where(item => terms.All(term => item.SearchText.Contains(term, StringComparison.Ordinal)))
            .ToList();
    }

    private void AddRelicFilter()
    {
        if (_dataset == null)
        {
            LogWarn("请先加载数据再添加遗物筛选。");
            return;
        }

        if (SelectedRelicCatalogItem is null)
        {
            LogWarn("请选择要添加的遗物。");
            return;
        }

        if (RelicFilterChips.Any(chip => string.Equals(chip.Value, SelectedRelicCatalogItem.Value, StringComparison.OrdinalIgnoreCase)))
        {
            LogWarn("该遗物已在筛选条件中。");
            return;
        }

        RelicFilterChips.Add(FilterChipViewModel.FromCatalog(SelectedRelicCatalogItem));
    }

    private void RemoveRelicFilter(object? parameter)
    {
        RemoveChipById(RelicFilterChips, parameter as string);
    }

    private void AddCardFilter()
    {
        if (_dataset == null)
        {
            LogWarn("请先加载数据再添加卡牌筛选。");
            return;
        }

        if (SelectedCardCatalogItem is null)
        {
            LogWarn("请选择要添加的卡牌。");
            return;
        }

        CardFilterChips.Add(FilterChipViewModel.FromCatalog(SelectedCardCatalogItem));
    }

    private void RemoveCardFilter(object? parameter)
    {
        RemoveChipById(CardFilterChips, parameter as string);
    }

    private void AddPotionFilter()
    {
        if (_dataset == null)
        {
            LogWarn("请先加载数据再添加药水筛选。");
            return;
        }

        if (SelectedPotionCatalogItem is null)
        {
            LogWarn("请选择要添加的药水。");
            return;
        }

        PotionFilterChips.Add(FilterChipViewModel.FromCatalog(SelectedPotionCatalogItem));
    }

    private void RemovePotionFilter(object? parameter)
    {
        RemoveChipById(PotionFilterChips, parameter as string);
    }

    private void AddAct2OptionFilter()
    {
        TryAddAncientOptionChip(Act2OptionFilterChips, SelectedAct2RelicOption);
    }

    private void RemoveAct2OptionFilter(object? parameter)
    {
        RemoveChipById(Act2OptionFilterChips, parameter as string);
    }

    private void AddAct3OptionFilter()
    {
        TryAddAncientOptionChip(Act3OptionFilterChips, SelectedAct3RelicOption);
    }

    private void RemoveAct3OptionFilter(object? parameter)
    {
        RemoveChipById(Act3OptionFilterChips, parameter as string);
    }

    private static void RemoveChipById(ObservableCollection<FilterChipViewModel> chips, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var target = chips.FirstOrDefault(chip => string.Equals(chip.Id, id, StringComparison.Ordinal));
        if (target != null)
        {
            chips.Remove(target);
        }
    }

    private void ClearLogs()
    {
        Logs.Clear();
    }

    private void ClearResults()
    {
        Results.Clear();
        SelectedResult = null;
        ResetCounters();
        UpdateResultAvailability();
    }

    private void ResetCounters()
    {
        ScannedSeeds = 0;
        HitSeeds = 0;
        HitOptions = 0;
        ProgressMaximum = 1;
        RollProgressText = string.Empty;
    }

    private void SetSelectedEvent(SeedEventMetadata? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        if (SetProperty(ref _selectedEvent, metadata, nameof(SelectedEvent)))
        {
            ResetDatasetForEventChange();
            _ = LoadDatasetAsync();
            return;
        }

        RaiseSelectedEventComputedProperties();
    }

    private void ResetDatasetForEventChange()
    {
        _dataset = null;
        DatasetSummary = "尚未加载数据";
        _rollCommand?.RaiseCanExecuteChanged();
        _relicCatalog = Array.Empty<CatalogItem>();
        _cardCatalog = Array.Empty<CatalogItem>();
        _potionCatalog = Array.Empty<CatalogItem>();
        ApplyRelicFilter();
        ApplyCardFilter();
        ApplyPotionFilter();
        SelectedRelicCatalogItem = null;
        SelectedCardCatalogItem = null;
        SelectedPotionCatalogItem = null;
        RaiseSelectedEventComputedProperties();
    }

    private void RaiseSelectedEventComputedProperties()
    {
        RaisePropertyChanged(nameof(IsSelectedEventImplemented));
        RaisePropertyChanged(nameof(SelectedEventStatusText));
        RaisePropertyChanged(nameof(SelectedEventDataHint));
    }

    private static string ResolveDefaultDataPath(SeedEventMetadata metadata)
    {
        if (Path.IsPathRooted(metadata.DefaultDataPath))
        {
            return metadata.DefaultDataPath;
        }

        var normalized = metadata.DefaultDataPath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(AppContext.BaseDirectory, normalized);
    }

    private async Task RollAsync()
    {
        if (_dataset == null)
        {
            StatusMessage = "请先加载数据。";
            LogError(StatusMessage);
            return;
        }

        if (!IsSelectedEventImplemented)
        {
            StatusMessage = $"事件 {SelectedEvent.DisplayName} 尚未实现。";
            LogWarn(StatusMessage);
            return;
        }

        if (SelectedEvent.Type != SeedEventType.Act1Neow)
        {
            StatusMessage = $"{SelectedEvent.DisplayName} 的 Roll 功能尚未完成。";
            LogWarn(StatusMessage);
            return;
        }

        if (!TryBuildRollConfig(out var config, out var error))
        {
            StatusMessage = error;
            LogError(error);
            return;
        }

        var runFilter = BuildRunFilter();

        Results.Clear();
        SelectedResult = null;
        ResetCounters();
        ProgressMaximum = config.StopOnFirstMatch ? 1 : config.RollCount;

        IsRolling = true;
        _rollCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();

        _rollCancellation = new CancellationTokenSource();
        var token = _rollCancellation.Token;
        var progress = new Progress<RollProgress>(p =>
        {
            if (config.StopOnFirstMatch && p.Scanned >= ProgressMaximum)
            {
                ProgressMaximum = p.Scanned + 1;
            }

            ScannedSeeds = p.Scanned;
            HitSeeds = p.HitSeeds;
            HitOptions = p.HitOptions;
            RollProgressText = $"已扫描 {p.Scanned:N0}，命中 {p.HitSeeds} 个种子、{p.HitOptions} 个选项";
        });

        var quantityText = config.StopOnFirstMatch ? "命中即停" : config.RollCount.ToString(CultureInfo.InvariantCulture);
        StatusMessage = $"正在 Roll（事件：{SelectedEvent.DisplayName}，模式：{GetModeDisplayName(config.Mode)}，数量：{quantityText}）…";
        LogInfo($"开始 Roll：事件={SelectedEvent.DisplayName}，模式={GetModeDisplayName(config.Mode)}，数量={quantityText}。");

        try
        {
            var runResult = await Task.Run(() => ExecuteRolls(_dataset!, config, runFilter, progress, token), token);
            foreach (var result in runResult.Results)
            {
                Results.Add(result);
            }
            SelectedResult = Results.FirstOrDefault();

            var rangeText = $"（首个种子：{runResult.FirstSeed}，最后种子：{runResult.LastSeed}）";
            StatusMessage = runResult.SummaryMessage;
            LogInfo($"{runResult.SummaryMessage} {rangeText}");

            if (!runResult.WasCancelled)
            {
                await SaveResultsAsync(runResult);
            }

            if (Results.Count > 0)
            {
                SelectedTabIndex = 1;
                RaiseNavigation(1);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消当前 Roll。";
            LogInfo(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Roll 失败：{ex.Message}";
            LogError(StatusMessage);
        }
        finally
        {
            _rollCancellation = null;
            IsRolling = false;
            _rollCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
        }
    }

    private static int NormalizeRollCount(int requested, SeedRollMode mode) =>
        Math.Max(1, requested);

    private static List<SeedWorkItem> CreateWorkItems(RollConfig config, CancellationToken token, out bool wasCancelled)
    {
        var items = new List<SeedWorkItem>(config.RollCount);
        wasCancelled = false;
        for (var i = 0; i < config.RollCount; i++)
        {
            if (token.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            var (seedText, seedValue) = config.ResolveSeed(i);
            items.Add(new SeedWorkItem(i, seedText, seedValue));
        }

        return items;
    }

    private RollExecutionResult ExecuteRolls(
        NeowOptionDataset dataset,
        RollConfig config,
        SeedRunFilter filter,
        IProgress<RollProgress> progress,
        CancellationToken token)
    {
        var includeAct2 = IncludeAct2 && IsAncientPreviewAvailable;
        var includeAct3 = IncludeAct3 && IsAncientPreviewAvailable;
        var requireAct2Match = filter.AncientFilter.HasAct2Criteria;
        var requireAct3Match = filter.AncientFilter.HasAct3Criteria;

        return config.StopOnFirstMatch
            ? ExecuteUntilHit(
                dataset,
                config,
                filter,
                includeAct2,
                includeAct3,
                requireAct2Match,
                requireAct3Match,
                progress,
                token)
            : ExecuteFixedCount(
                dataset,
                config,
                filter,
                includeAct2,
                includeAct3,
                requireAct2Match,
                requireAct3Match,
                progress,
                token);
    }

    private RollExecutionResult ExecuteFixedCount(
        NeowOptionDataset dataset,
        RollConfig config,
        SeedRunFilter filter,
        bool includeAct2,
        bool includeAct3,
        bool requireAct2Match,
        bool requireAct3Match,
        IProgress<RollProgress> progress,
        CancellationToken token)
    {
        var workItems = CreateWorkItems(config, token, out var creationCancelled);
        var hits = new ConcurrentBag<RollHit>();
        var totalTarget = workItems.Count;
        var state = new RollAggregationState
        {
            CancellationFlag = (creationCancelled || token.IsCancellationRequested) ? 1 : 0
        };

        if (state.CancellationFlag == 0)
        {
            ProcessBatch(
                workItems,
                dataset,
                filter,
                config,
                includeAct2,
                includeAct3,
                requireAct2Match,
                requireAct3Match,
                stopOnFirstMatch: false,
                totalTarget,
                hits,
                progress,
                token,
                state);
        }

        var orderedHits = hits.OrderBy(static h => h.Index).ToList();
        var finalResults = orderedHits.Select(static h => h.Result).ToList();
        state.TotalHitSeeds = finalResults.Count;
        var wasCancelled = state.CancellationFlag == 1;
        var summary = BuildSummary(wasCancelled, config.StopOnFirstMatch, state.TotalScanned, finalResults.Count, state.TotalHitOptions);
        var (firstSeed, lastSeed) = ResolveSeedRange(workItems, state.MinIndex, state.MaxIndex, config.InitialSeed);

        ReportProgress(progress, state.TotalScanned, state.TotalHitSeeds, state.TotalHitOptions);

        return new RollExecutionResult(
            finalResults,
            state.TotalScanned,
            summary,
            firstSeed,
            lastSeed,
            config.Mode,
            state.TotalHitOptions,
            wasCancelled);
    }

    private RollExecutionResult ExecuteUntilHit(
        NeowOptionDataset dataset,
        RollConfig config,
        SeedRunFilter filter,
        bool includeAct2,
        bool includeAct3,
        bool requireAct2Match,
        bool requireAct3Match,
        IProgress<RollProgress> progress,
        CancellationToken token)
    {
        var hits = new ConcurrentBag<RollHit>();
        var state = new RollAggregationState
        {
            CancellationFlag = token.IsCancellationRequested ? 1 : 0
        };
        var nextIndex = 0;
        string? firstSeed = null;
        string? lastSeed = null;

        while (Volatile.Read(ref state.CancellationFlag) == 0)
        {
            var batch = new List<SeedWorkItem>(DefaultPartitionSize);
            for (var i = 0; i < DefaultPartitionSize; i++)
            {
                if (token.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref state.CancellationFlag, 1);
                    break;
                }

                var (seedText, seedValue) = config.ResolveSeed(nextIndex);
                batch.Add(new SeedWorkItem(nextIndex, seedText, seedValue));
                nextIndex++;
                firstSeed ??= seedText;
                lastSeed = seedText;
            }

            if (batch.Count == 0)
            {
                break;
            }

            var hitFound = ProcessBatch(
                batch,
                dataset,
                filter,
                config,
                includeAct2,
                includeAct3,
                requireAct2Match,
                requireAct3Match,
                stopOnFirstMatch: true,
                totalTarget: int.MaxValue,
                hits,
                progress,
                token,
                state);

            if (hitFound)
            {
                break;
            }
        }

        var orderedHits = hits.OrderBy(static h => h.Index).ToList();
        var finalResults = orderedHits.Select(static h => h.Result).ToList();
        state.TotalHitSeeds = finalResults.Count;
        var wasCancelled = Volatile.Read(ref state.CancellationFlag) == 1 && finalResults.Count == 0;
        var summary = BuildSummary(wasCancelled, config.StopOnFirstMatch, state.TotalScanned, finalResults.Count, state.TotalHitOptions);
        var firstSeedText = firstSeed ?? config.InitialSeed;
        var lastSeedText = lastSeed ?? firstSeedText;

        ReportProgress(progress, state.TotalScanned, state.TotalHitSeeds, state.TotalHitOptions);

        return new RollExecutionResult(
            finalResults,
            state.TotalScanned,
            summary,
            firstSeedText,
            lastSeedText,
            config.Mode,
            state.TotalHitOptions,
            wasCancelled);
    }

    private bool ProcessBatch(
        IList<SeedWorkItem> workItems,
        NeowOptionDataset dataset,
        SeedRunFilter filter,
        RollConfig config,
        bool includeAct2,
        bool includeAct3,
        bool requireAct2Match,
        bool requireAct3Match,
        bool stopOnFirstMatch,
        int totalTarget,
        ConcurrentBag<RollHit> hits,
        IProgress<RollProgress> progress,
        CancellationToken token,
        RollAggregationState state)
    {
        if (workItems.Count == 0 || Volatile.Read(ref state.CancellationFlag) == 1)
        {
            return false;
        }

        var hitFlag = 0;
        var partitioner = Partitioner.Create(workItems, loadBalance: true);

        Parallel.ForEach(
            partitioner,
            () => new RollWorkerState(
                dataset,
                _ancientPreviewer,
                filter,
                config.Character,
                config.CharacterName,
                includeAct2,
                includeAct3,
                SelectedAscensionLevel,
                requireAct2Match,
                requireAct3Match),
            (workItem, loopState, workerState) =>
            {
                if (Volatile.Read(ref state.CancellationFlag) == 1)
                {
                    loopState.Stop();
                    return workerState;
                }

                if (token.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref state.CancellationFlag, 1);
                    loopState.Stop();
                    return workerState;
                }

                if (stopOnFirstMatch && Volatile.Read(ref hitFlag) == 1)
                {
                    loopState.Stop();
                    return workerState;
                }

                var hit = workerState.Process(workItem);
                var scanned = Interlocked.Increment(ref state.TotalScanned);
                UpdateMinIndex(ref state.MinIndex, workItem.Index);
                UpdateMaxIndex(ref state.MaxIndex, workItem.Index);

                if (hit != null)
                {
                    hits.Add(hit);
                    Interlocked.Increment(ref state.TotalHitSeeds);
                    Interlocked.Add(ref state.TotalHitOptions, hit.OptionCount);

                    if (stopOnFirstMatch)
                    {
                        Interlocked.Exchange(ref hitFlag, 1);
                        loopState.Stop();
                    }
                }

                if (!stopOnFirstMatch)
                {
                    if (scanned == totalTarget || scanned % ProgressReportMinimum == 0)
                    {
                        ReportProgress(progress, scanned, Volatile.Read(ref state.TotalHitSeeds), Volatile.Read(ref state.TotalHitOptions));
                    }
                }
                else
                {
                    if (hit != null || scanned % ProgressReportMinimum == 0)
                    {
                        ReportProgress(progress, scanned, Volatile.Read(ref state.TotalHitSeeds), Volatile.Read(ref state.TotalHitOptions));
                    }
                }

                return workerState;
            },
            _ => { });

        return Volatile.Read(ref hitFlag) == 1;
    }

    private static (string FirstSeed, string LastSeed) ResolveSeedRange(
        IList<SeedWorkItem> workItems,
        int minIndex,
        int maxIndex,
        string fallbackSeed)
    {
        if (workItems.Count == 0 || minIndex == int.MaxValue)
        {
            return (fallbackSeed, fallbackSeed);
        }

        var firstSeed = fallbackSeed;
        var lastSeed = fallbackSeed;

        if (minIndex >= 0 && minIndex < workItems.Count)
        {
            firstSeed = workItems[minIndex].SeedText;
        }

        if (maxIndex >= 0 && maxIndex < workItems.Count)
        {
            lastSeed = workItems[maxIndex].SeedText;
        }
        else
        {
            lastSeed = firstSeed;
        }

        return (firstSeed, lastSeed);
    }

    private static string BuildSummary(bool wasCancelled, bool stopOnFirstMatch, int totalScanned, int hitSeeds, int hitOptions)
    {
        if (wasCancelled)
        {
            return $"Roll 已取消：扫描 {totalScanned}，命中种子 {hitSeeds}，第一幕命中 {hitOptions}。";
        }

        if (stopOnFirstMatch)
        {
            return $"命中即停：扫描 {totalScanned}，命中种子 {hitSeeds}，第一幕命中 {hitOptions}。";
        }

        return $"Roll 完成：扫描 {totalScanned}，命中种子 {hitSeeds}，第一幕命中 {hitOptions}。";
    }

    private static void ReportProgress(IProgress<RollProgress> progress, int scanned, int hitSeeds, int hitOptions)
    {
        progress.Report(new RollProgress(scanned, hitSeeds, hitOptions));
    }

    private static void UpdateMinIndex(ref int current, int candidate)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref current);
            if (candidate >= snapshot)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref current, candidate, snapshot) == snapshot)
            {
                return;
            }
        }
    }

    private static void UpdateMaxIndex(ref int current, int candidate)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref current);
            if (candidate <= snapshot)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref current, candidate, snapshot) == snapshot)
            {
                return;
            }
        }
    }

    private bool TryBuildRollConfig(out RollConfig config, out string error)
    {
        config = default!;
        error = string.Empty;

        if (!int.TryParse(RollCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rollCount) || rollCount <= 0)
        {
            error = "Roll 数量必须是正整数。";
            return false;
        }

        rollCount = NormalizeRollCount(rollCount, SelectedSeedMode);

        if (!int.TryParse(SeedStep, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seedStep) || seedStep <= 0)
        {
            error = "种子步长必须是正整数。";
            return false;
        }

        seedStep = Math.Clamp(seedStep, 1, MaxSeedStep);

        if (SelectedSeedMode == SeedRollMode.Sequential)
        {
            if (!SeedFormatter.TryNormalize(SeedText, out var normalized, out var normalizeError))
            {
                error = $"种子格式错误：{normalizeError}";
                return false;
            }

            SeedText = normalized;
            var totalAdvance = (long)(rollCount - 1) * seedStep;
            if (totalAdvance > int.MaxValue)
            {
                error = "种子步长与数量的组合过大。";
                return false;
            }

            config = new RollConfig(SeedRollMode.Sequential, normalized, seedStep, rollCount, SelectedCharacter, GetCharacterDisplayName(SelectedCharacter));
            return true;
        }

        var initialSeed = SeedFormatter.GenerateRandomSeed();
        config = new RollConfig(SelectedSeedMode, initialSeed, seedStep, rollCount, SelectedCharacter, GetCharacterDisplayName(SelectedCharacter));
        return true;
    }

    private static string GetCharacterDisplayName(CharacterId id)
    {
        return id switch
        {
            CharacterId.Ironclad => "铁甲战士",
            CharacterId.Silent => "静默猎手",
            CharacterId.Defect => "故障机器人",
            CharacterId.Necrobinder => "亡灵契约师",
            CharacterId.Regent => "储君",
            _ => id.ToString()
        };
    }

    private static string GetModeDisplayName(SeedRollMode mode) =>
        mode switch
        {
            SeedRollMode.Sequential => "顺序递增",
            SeedRollMode.RandomUntilHit => "命中即停",
            _ => "随机模式"
        };

    private SeedRunFilter BuildRunFilter()
    {
        var relicTerms = SplitTerms(FilterRelicTerms);
        var relicIds = RelicFilterChips.Select(chip => chip.Value);
        var cardIds = CardFilterChips.Select(chip => chip.Value);
        var potionIds = PotionFilterChips.Select(chip => chip.Value);
        var neowFilter = NeowOptionFilter.Create(null, relicTerms, relicIds, cardIds, potionIds);

        var includeAct2 = IncludeAct2 && IsAncientPreviewAvailable;
        var includeAct3 = IncludeAct3 && IsAncientPreviewAvailable;
        IReadOnlyList<string> act2OptionIds = includeAct2
            ? Act2OptionFilterChips.Select(chip => chip.Value).ToList()
            : Array.Empty<string>();
        IReadOnlyList<string> act3OptionIds = includeAct3
            ? Act3OptionFilterChips.Select(chip => chip.Value).ToList()
            : Array.Empty<string>();
        var ancientFilter = new Sts2AncientFilter
        {
            Act2AncientId = includeAct2 ? NormalizeAncientFilterValue(Act2AncientFilter) : null,
            Act3AncientId = includeAct3 ? NormalizeAncientFilterValue(Act3AncientFilter) : null,
            Act2OptionIds = act2OptionIds,
            Act3OptionIds = act3OptionIds
        };

        return new SeedRunFilter
        {
            NeowFilter = neowFilter,
            AncientFilter = ancientFilter
        };
    }

    private static IEnumerable<string> SplitTerms(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(TermSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 0);
    }

    private void UpdateAct2RelicOptions()
    {
        _act2RelicOptions = AncientDisplayCatalog.GetRelicOptions(Act2AncientFilter);
        RaisePropertyChanged(nameof(Act2RelicOptions));
        var next = SelectRelicOption(_selectedAct2RelicOption, _act2RelicOptions);
        if (!ReferenceEquals(next, _selectedAct2RelicOption))
        {
            SelectedAct2RelicOption = next;
        }
    }

    private void UpdateAct3RelicOptions()
    {
        _act3RelicOptions = AncientDisplayCatalog.GetRelicOptions(Act3AncientFilter);
        RaisePropertyChanged(nameof(Act3RelicOptions));
        var next = SelectRelicOption(_selectedAct3RelicOption, _act3RelicOptions);
        if (!ReferenceEquals(next, _selectedAct3RelicOption))
        {
            SelectedAct3RelicOption = next;
        }
    }

    private static AncientDisplayCatalog.AncientRelicDisplayOption? SelectRelicOption(
        AncientDisplayCatalog.AncientRelicDisplayOption? current,
        IReadOnlyList<AncientDisplayCatalog.AncientRelicDisplayOption> options)
    {
        if (options.Count == 0)
        {
            return null;
        }

        if (current != null)
        {
            var matched = options.FirstOrDefault(option =>
                string.Equals(option.Id, current.Id, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }
        }

        return options.FirstOrDefault();
    }

    private void UpdateAncientFilterSummary()
    {
        string Format(string label, bool enabled, string filterValue, ObservableCollection<FilterChipViewModel> optionChips)
        {
            if (!enabled)
            {
                return $"{label}：关闭";
            }

            var displayName = AncientDisplayCatalog.GetDisplayText(filterValue, filterValue);
            var options = optionChips.Count > 0
                ? string.Join(" / ", optionChips.Select(chip => chip.Label))
                : "任意遗物";
            return $"{label}：{displayName}（遗物：{options}）";
        }

        AncientFilterSummary = string.Join(" | ", new[]
        {
            Format("第二幕", IncludeAct2, Act2AncientFilter, Act2OptionFilterChips),
            Format("第三幕", IncludeAct3, Act3AncientFilter, Act3OptionFilterChips)
        });
    }

    private void OnAncientOptionChipsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateAncientFilterSummary();

    private static string NormalizeAncientInput(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string? NormalizeAncientFilterValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeOptionInput(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static bool TryAddAncientOptionChip(
        ObservableCollection<FilterChipViewModel> target,
        AncientDisplayCatalog.AncientRelicDisplayOption? option)
    {
        if (option == null)
        {
            return false;
        }

        var normalized = NormalizeOptionInput(option.Id);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (target.Any(chip => string.Equals(chip.Value, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var label = string.IsNullOrWhiteSpace(option.DisplayText) ? normalized : option.DisplayText;
        target.Add(new FilterChipViewModel(normalized, label));
        return true;
    }

    private async Task SaveResultsAsync(RollExecutionResult runResult, string? filePath = null)
    {
        var exportModel = BuildExportModel(runResult.TotalScanned);

        var actualPath = filePath;
        if (string.IsNullOrWhiteSpace(actualPath))
        {
            var resultsDir = Path.Combine(AppContext.BaseDirectory, "results");
            Directory.CreateDirectory(resultsDir);
            actualPath = Path.Combine(resultsDir, $"results_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        }

        var directory = Path.GetDirectoryName(actualPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(actualPath);
        await JsonSerializer.SerializeAsync(stream, exportModel, JsonOptions);
        HasResult = true;
        StatusMessage = $"{StatusMessage} 结果已保存。";
        LogInfo($"结果 JSON 已写入：{actualPath}");
    }

    private ExportFileModel BuildExportModel(int totalScanned)
    {
        var seeds = Results
            .Select(result =>
            {
                var act1Options = result.RawOptions
                    .Select(option => new OptionExportRecord
                    {
                        Kind = option.Kind.ToString(),
                        RelicId = option.RelicId,
                        Title = option.Title,
                        Description = option.Description,
                        Note = option.Note,
                        Details = option.Details.Select(detail => new DetailExportRecord
                        {
                            Type = detail.Type.ToString(),
                            Label = detail.Label,
                            Value = detail.Value,
                            ModelId = detail.ModelId,
                            Amount = detail.Amount
                        }).ToList()
                    })
                    .ToList();

                var ancientActs = result.AncientActs
                    .Select(act => new AncientActExportRecord
                    {
                        Act = act.ActNumber,
                        AncientId = act.AncientId,
                        AncientName = act.AncientName,
                        Options = act.Options.Select(option => new AncientOptionExportRecord
                        {
                            OptionId = option.OptionId,
                            Title = option.Title,
                            Description = option.Description,
                            Note = option.Note
                        }).ToList()
                    })
                    .ToList();

                return new SeedExportRecord
                {
                    Seed = result.SeedString,
                    Character = result.CharacterName,
                    Ascension = result.AscensionLevel,
                    Options = act1Options,
                    AncientActs = ancientActs
                };
            })
            .ToList();

        return new ExportFileModel
        {
            GeneratedAt = DateTimeOffset.Now,
            TotalSeeds = totalScanned,
            MatchedSeeds = seeds.Count,
            MatchedOptions = seeds.Sum(seed => seed.Options.Count),
            Seeds = seeds
        };
    }

    private async Task ExportResultsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件 (*.json)|*.json|所有文件|*.*",
            Title = "导出结果",
            FileName = $"roll_results_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var runResult = new RollExecutionResult(
            Results.ToList(),
            ScannedSeeds,
            "导出完成。",
            Results.FirstOrDefault()?.SeedString ?? SeedText,
            Results.LastOrDefault()?.SeedString ?? SeedText,
            SelectedSeedMode,
            HitOptions,
            WasCancelled: false);

        await SaveResultsAsync(runResult, dialog.FileName);
    }

    public void ResetConfig()
    {
        SelectedCharacter = CharacterId.Ironclad;
        SelectedAscensionLevel = 0;
        SelectedSeedMode = SeedRollMode.Random;
        SeedText = new string('0', SeedFormatter.DefaultLength);
        RollCount = "100";
        SeedStep = "1";
        FilterRelicTerms = string.Empty;
        SelectedRelicCatalogItem = null;
        SelectedCardCatalogItem = null;
        SelectedPotionCatalogItem = null;
        RelicFilterChips.Clear();
        CardFilterChips.Clear();
        PotionFilterChips.Clear();
        IncludeAct2 = false;
        IncludeAct3 = false;
        Act2AncientFilter = string.Empty;
        Act3AncientFilter = string.Empty;
        Act2OptionFilterChips.Clear();
        Act3OptionFilterChips.Clear();
        SelectedAct2RelicOption = null;
        SelectedAct3RelicOption = null;
        StatusMessage = "配置已重置为默认值。";
        LogInfo(StatusMessage);
    }

    private async Task LoadConfigAsync()
    {
        await LoadConfigFromFileAsync(_configFilePath);
    }

    public async Task LoadConfigFromFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                StatusMessage = $"未找到配置文件：{filePath}";
                LogWarn(StatusMessage);
                return;
            }

            await using var stream = File.OpenRead(filePath);
            var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream, JsonOptions);
            if (config == null)
            {
                throw new InvalidDataException("配置文件为空。");
            }

            ApplyConfig(config);
            StatusMessage = $"已加载配置：{Path.GetFileName(filePath)}";
            LogInfo(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载配置失败：{ex.Message}";
            LogError(StatusMessage);
        }
    }

    private void ApplyConfig(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.EventId) &&
            SeedEventRegistry.TryGetById(config.EventId, out var eventMetadata))
        {
            SetSelectedEvent(eventMetadata);
        }

        if (!string.IsNullOrWhiteSpace(config.Seed))
        {
            SeedText = config.Seed!;
        }

        if (!string.IsNullOrWhiteSpace(config.SeedMode) &&
            Enum.TryParse(config.SeedMode, ignoreCase: true, out SeedRollMode mode))
        {
            SelectedSeedMode = mode;
        }

        if (config.StopOnFirstMatch)
        {
            SelectedSeedMode = SeedRollMode.RandomUntilHit;
        }
        var normalizedRollCount = NormalizeRollCount(config.RollCount, SelectedSeedMode);
        RollCount = normalizedRollCount.ToString(CultureInfo.InvariantCulture);
        SeedStep = Math.Clamp(config.SeedStep, 1, MaxSeedStep).ToString(CultureInfo.InvariantCulture);
        FilterRelicTerms = string.Join(", ", config.RelicTerms != null ? config.RelicTerms : Array.Empty<string>());

        if (!string.IsNullOrWhiteSpace(config.Character) &&
            Enum.TryParse(config.Character, ignoreCase: true, out CharacterId character))
        {
        SelectedCharacter = character;
    }

    SelectedAscensionLevel = Math.Clamp(config.Ascension, 0, AscensionOptions[^1]);
        Act2AncientFilter = config.Act2AncientId ?? string.Empty;
        Act3AncientFilter = config.Act3AncientId ?? string.Empty;
        ResetValueChips(Act2OptionFilterChips, config.Act2OptionIds);
        ResetValueChips(Act3OptionFilterChips, config.Act3OptionIds);
        IncludeAct2 = config.IncludeAct2;
        IncludeAct3 = config.IncludeAct3;

        ResetChips(RelicFilterChips, config.RelicIds, _relicCatalog);
        ResetChips(CardFilterChips, config.CardIds, _cardCatalog);
        ResetChips(PotionFilterChips, config.PotionIds, _potionCatalog);
    }

    private static void ResetChips(
        ObservableCollection<FilterChipViewModel> target,
        IEnumerable<string>? values,
        IReadOnlyList<CatalogItem> catalog)
    {
        target.Clear();
        if (values == null)
        {
            return;
        }

        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var label = catalog.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase))?.Display ?? value;
            target.Add(new FilterChipViewModel(value, label));
        }
    }

    private static void ResetValueChips(
        ObservableCollection<FilterChipViewModel> target,
        IEnumerable<string>? values)
    {
        target.Clear();
        if (values == null)
        {
            return;
        }

        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            var normalized = NormalizeOptionInput(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var metadata = AncientDisplayCatalog.TryGetRelicOption(normalized);
            var label = metadata?.DisplayText ?? normalized;
            target.Add(new FilterChipViewModel(normalized, label));
        }
    }

    private async Task SaveConfigAsync()
    {
        await SaveConfigToFileAsync(_configFilePath);
    }

    public async Task SaveConfigToFileAsync(string filePath)
    {
        try
        {
            var model = new AppConfig
            {
                EventId = SelectedEvent.Id,
                Seed = SeedText,
                SeedMode = SelectedSeedMode.ToString(),
                RollCount = ParseIntOrDefault(RollCount, 100),
                SeedStep = ParseIntOrDefault(SeedStep, 1),
                Character = SelectedCharacter.ToString(),
                Ascension = SelectedAscensionLevel,
                IncludeAct2 = IncludeAct2,
                IncludeAct3 = IncludeAct3,
                Act2AncientId = Act2AncientFilter,
                Act3AncientId = Act3AncientFilter,
                Act2OptionIds = Act2OptionFilterChips.Select(chip => chip.Value).ToList(),
                Act3OptionIds = Act3OptionFilterChips.Select(chip => chip.Value).ToList(),
                RelicIds = RelicFilterChips.Select(chip => chip.Value).ToList(),
                RelicTerms = SplitTerms(FilterRelicTerms).ToList(),
                CardIds = CardFilterChips.Select(chip => chip.Value).ToList(),
                PotionIds = PotionFilterChips.Select(chip => chip.Value).ToList(),
                StopOnFirstMatch = SelectedSeedMode == SeedRollMode.RandomUntilHit
            };

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, model, JsonOptions);
            StatusMessage = $"已保存配置：{Path.GetFileName(filePath)}";
            LogInfo(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存配置失败：{ex.Message}";
            LogError(StatusMessage);
        }
    }

    private static int ParseIntOrDefault(string text, int fallback)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private void CancelRoll()
    {
        if (!_isRolling)
        {
            return;
        }

        _rollCancellation?.Cancel();
    }

    private void UpdateResultAvailability()
    {
        HasResult = Results.Count > 0;
    }

    private void CopySeedToClipboard(object? parameter)
    {
        if (parameter is not RollResultViewModel result)
        {
            return;
        }

        try
        {
            Clipboard.SetText(result.SeedString);
            StatusMessage = $"已复制种子 {result.SeedString}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"复制失败：{ex.Message}";
            LogError(StatusMessage);
        }
    }

    private void LogInfo(string message) => AddLog("信息", message);

    private void LogWarn(string message) => AddLog("警告", message);

    private void LogError(string message) => AddLog("错误", message);

    private void AddLog(string level, string message)
    {
        Logs.Add(new LogEntryViewModel(level, message));
        if (Logs.Count > MaxLogEntries)
        {
            Logs.RemoveAt(0);
        }
    }

    internal sealed record CatalogItem(string Value, string Display, string SearchText);

    internal sealed record CharacterOption(CharacterId Id, string DisplayName);

    internal sealed record SeedModeOption(SeedRollMode Value, string DisplayName);

    internal sealed record RollProgress(int Scanned, int HitSeeds, int HitOptions);

    private sealed record SeedWorkItem(int Index, string SeedText, uint SeedValue);

    private sealed record RollHit(int Index, RollResultViewModel Result, int OptionCount);

    private sealed class RollAggregationState
    {
        public int TotalScanned;
        public int TotalHitSeeds;
        public int TotalHitOptions;
        public int MinIndex = int.MaxValue;
        public int MaxIndex = -1;
        public int CancellationFlag;
    }

    internal sealed record RollExecutionResult(
        List<RollResultViewModel> Results,
        int TotalScanned,
        string SummaryMessage,
        string FirstSeed,
        string LastSeed,
        SeedRollMode Mode,
        int HitAct1Options,
        bool WasCancelled);

    private sealed class RollConfig
    {
        public RollConfig(SeedRollMode mode, string initialSeed, int seedStep, int rollCount, CharacterId character, string characterName)
        {
            Mode = mode;
            InitialSeed = initialSeed;
            SeedStep = seedStep;
            RollCount = rollCount;
            Character = character;
            CharacterName = characterName;
        }

        public SeedRollMode Mode { get; }

        public string InitialSeed { get; }

        public int SeedStep { get; }

        public int RollCount { get; }

        public CharacterId Character { get; }

        public string CharacterName { get; }

        public bool StopOnFirstMatch => Mode == SeedRollMode.RandomUntilHit;

        public (string SeedText, uint SeedValue) ResolveSeed(int index)
        {
            if (Mode == SeedRollMode.Sequential)
            {
                var delta = index == 0 ? 0 : checked(index * SeedStep);
                var seed = delta == 0 ? InitialSeed : SeedFormatter.Advance(InitialSeed, delta);
                return (seed, SeedFormatter.ToUIntSeed(seed));
            }

            return CreateRandomSeed();
        }

        private static (string SeedText, uint SeedValue) CreateRandomSeed()
        {
            var randomSeed = SeedFormatter.GenerateRandomSeed();
            return (randomSeed, SeedFormatter.ToUIntSeed(randomSeed));
        }
    }

    private sealed class RollWorkerState
    {
        private readonly SeedRunEvaluator _evaluator;
        private readonly SeedRunFilter _filter;
        private readonly CharacterId _character;
        private readonly string _characterName;
        private readonly bool _includeAct2;
        private readonly bool _includeAct3;
        private readonly int _ascensionLevel;
        private readonly bool _requireAct2Match;
        private readonly bool _requireAct3Match;

        public RollWorkerState(
            NeowOptionDataset dataset,
            Sts2RunPreviewer? previewer,
            SeedRunFilter filter,
            CharacterId character,
            string characterName,
            bool includeAct2,
            bool includeAct3,
            int ascensionLevel,
            bool requireAct2Match,
            bool requireAct3Match)
        {
            _evaluator = new SeedRunEvaluator(dataset, previewer);
            _filter = filter;
            _character = character;
            _characterName = characterName;
            _includeAct2 = includeAct2;
            _includeAct3 = includeAct3;
            _ascensionLevel = ascensionLevel;
            _requireAct2Match = requireAct2Match;
            _requireAct3Match = requireAct3Match;
        }

        public RollHit? Process(SeedWorkItem workItem)
        {
            var runContext = new SeedRunEvaluationContext
            {
                RunSeed = workItem.SeedValue,
                SeedText = workItem.SeedText,
                Character = _character,
                PlayerCount = 1,
                ScrollBoxesEligible = true,
                AscensionLevel = _ascensionLevel,
                IncludeAct2 = _includeAct2,
                IncludeAct3 = _includeAct3
            };

            var match = _evaluator.Evaluate(runContext, _filter);
            if (!match.IsFinalMatch)
            {
                return null;
            }

            var displayNeow = _filter.NeowFilter.HasCriteria ? match.NeowMatches : match.NeowOptions;

            var viewModel = new RollResultViewModel(
                workItem.SeedText,
                workItem.SeedValue,
                _character,
                _characterName,
                displayNeow,
                match.Sts2Preview,
                _requireAct2Match,
                _requireAct3Match,
                _ascensionLevel);

            return new RollHit(workItem.Index, viewModel, displayNeow.Count);
        }
    }

    internal sealed class LogEntryViewModel
    {
        public LogEntryViewModel(string level, string message)
        {
            Level = level;
            Message = message;
            Timestamp = DateTimeOffset.Now;
            TimestampText = Timestamp.ToString("HH:mm:ss");
        }

        public DateTimeOffset Timestamp { get; }

        public string TimestampText { get; }

        public string Level { get; }

        public string Message { get; }
    }

    internal sealed class FilterChipViewModel
    {
        public FilterChipViewModel(string value, string label)
        {
            Id = Guid.NewGuid().ToString("N");
            Value = value;
            Label = label;
        }

        public string Id { get; }

        public string Value { get; }

        public string Label { get; }

        public static FilterChipViewModel FromCatalog(CatalogItem item) => new(item.Value, item.Display);
    }

    private sealed class ExportFileModel
    {
        public DateTimeOffset GeneratedAt { get; init; }

        public int TotalSeeds { get; init; }

        public int MatchedSeeds { get; init; }

        public int MatchedOptions { get; init; }

        public List<SeedExportRecord> Seeds { get; init; } = new();
    }

    private sealed class SeedExportRecord
    {
        public string Seed { get; init; } = string.Empty;

        public string Character { get; init; } = string.Empty;

        public int Ascension { get; init; }

        public List<OptionExportRecord> Options { get; init; } = new();

        public List<AncientActExportRecord> AncientActs { get; init; } = new();
    }

    private sealed class OptionExportRecord
    {
        public string Kind { get; init; } = string.Empty;

        public string RelicId { get; init; } = string.Empty;

        public string? Title { get; init; }

        public string? Description { get; init; }

        public string? Note { get; init; }

        public List<DetailExportRecord> Details { get; init; } = new();
    }

    private sealed class DetailExportRecord
    {
        public string Type { get; init; } = string.Empty;

        public string Label { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;

        public string? ModelId { get; init; }

        public int? Amount { get; init; }
    }

    private sealed class AncientActExportRecord
    {
        public int Act { get; init; }

        public string? AncientId { get; init; }

        public string? AncientName { get; init; }

        public List<AncientOptionExportRecord> Options { get; init; } = new();
    }

    private sealed class AncientOptionExportRecord
    {
        public string OptionId { get; init; } = string.Empty;

        public string? Title { get; init; }

        public string? Description { get; init; }

        public string? Note { get; init; }
    }

    internal sealed class AppConfig
    {
        public string? EventId { get; init; }

        public string? Seed { get; init; }

        public string? SeedMode { get; init; }

        public int RollCount { get; init; } = 100;

        public int SeedStep { get; init; } = 1;

        public string? Character { get; init; }

        public int Ascension { get; init; }

        public bool IncludeAct2 { get; init; }

        public bool IncludeAct3 { get; init; }

        public string? Act2AncientId { get; init; }

        public string? Act3AncientId { get; init; }

        public List<string>? Act2OptionIds { get; init; }

        public List<string>? Act3OptionIds { get; init; }

        public List<string>? RelicIds { get; init; }

        public List<string>? RelicTerms { get; init; }

        public List<string>? CardIds { get; init; }

        public List<string>? PotionIds { get; init; }

        public bool StopOnFirstMatch { get; init; }
    }
}

internal enum SeedRollMode
{
    Random,
    Sequential,
    RandomUntilHit
}
