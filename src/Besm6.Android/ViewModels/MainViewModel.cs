using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Besm6.Android.Mvvm;
using Besm6.Android.Services;
using Besm6.Runtime.Configuration;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace Besm6.Android.ViewModels;

/// <summary>
/// Screen state and commands for the local machine. The MAUI view binds only to
/// this model; all machine callbacks are marshalled to the UI thread here.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private static readonly string[] DevicePlotter =
    [
        DeviceChannels.Watanabe,
        DeviceChannels.Calcomp,
        DeviceChannels.Tektronix,
    ];

    private static readonly string[] FontFamilies = ["monospace", "sans-serif", "serif"];
    private static readonly int[] MemoryWordCounts = [16, 32, 64, 128];

    private readonly AssetInstaller _installer;
    private readonly SettingsStore _settingsStore;
    private readonly ThemeService _theme;
    private readonly MachineRunService _runService;
    private readonly IUiDispatcher _dispatcher;
    private readonly DeviceTextBuffer _deviceBuffer = new();
    private readonly CodeSearchService _search;
    private readonly Stopwatch _runClock = new();
    private readonly string _examplesDirectory;

    private static readonly string[] PlotterTypes = ["Watanabe", "Calcomp", "Tektronix"];

    private ExampleEntry? _selectedExample;
    private string _code = string.Empty;
    private string _status = "Подготовка…";
    private string _inputText = string.Empty;
    private string _inputPrompt = string.Empty;
    private string _deviceInput = string.Empty;
    private string _deviceText = string.Empty;
    private string _deviceTitle = string.Empty;
    private string _registersText = "Снимок регистров ещё не получен.";
    private string _popupTitle = string.Empty;
    private string _popupError = string.Empty;
    private bool _isInitialized;
    private bool _isBusy;
    private bool _isRunning;
    private bool _isWaitingInput;
    private bool _isPaused;
    private bool _isSettingsOpen;
    private bool _isRegistersOpen;
    private bool _isDeviceOpen;
    private bool _isTerminalDevice;
    private bool _isPlotterDevice;
    private bool _isSearchOpen;
    private bool _isMemoryOpen;
    private string _activeTab = "code";
    private string _terminalText = string.Empty;
    private string _printerText = string.Empty;
    private string _punchText = string.Empty;
    private string _searchQuery = string.Empty;
    private string _searchStatus = string.Empty;
    private SearchHit? _selectedSearchHit;
    private readonly ObservableCollection<SearchHit> _searchResults = new();
    private string _memoryAddressText = "1000";
    private string _memoryText = "Снимок памяти ещё не получен.";
    private string _plotterText = string.Empty;
    private int _plotterTypeIndex;
    private RunSettings _settings = RunSettings.Default;
    private RegisterSnapshot? _snapshot;
    private RunOutcome? _lastOutcome;
    private string[] _activeChannels = [];
    private int _speedIndex;
    private int _modelIndex;
    private string _instructionLimitText = string.Empty;
    private string _wallClockSecondsText = string.Empty;
    private bool _guestClock;
    private bool _statistics;
    private bool _debugMode;
    private bool _useWallClock = true;
    private bool _loopDetect;
    private bool _hangDetect;
    private bool _forexAsFortran;
    private int _fontFamilyIndex;
    private double _outputFontSize = 10.5;
    private double _editorFontSize = 11;
    private int _memoryWordsIndex = 1;
    private int _numberBaseIndex;

    public MainViewModel(
        AssetInstaller installer,
        SettingsStore settingsStore,
        ThemeService theme,
        MachineRunService runService,
        IUiDispatcher dispatcher)
    {
        _installer = installer;
        _settingsStore = settingsStore;
        _theme = theme;
        _runService = runService;
        _dispatcher = dispatcher;
        _examplesDirectory = installer.ExamplesDirectory;
        _search = new CodeSearchService(_examplesDirectory);

        RunCommand = new RelayCommand(() => _ = RunAsync(), () => CanRun);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        OpenRegistersCommand = new RelayCommand(() => SelectTab("registers"));
        OpenDeviceCommand = new RelayCommand<string>(OpenDevice);
        SelectTabCommand = new RelayCommand<string>(SelectTab);
        OpenSearchCommand = new RelayCommand(() => SelectTab("search"));
        SearchFilesCommand = new RelayCommand(() => _ = SearchAsync());
        OpenMemoryCommand = new RelayCommand(OpenMemory);
        InspectMemoryCommand = new RelayCommand(InspectMemory);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        PauseCommand = new RelayCommand(Pause, () => IsDebugMode && IsRunning && !IsPaused);
        StepCommand = new RelayCommand(Step, () => IsDebugMode && IsPaused);
        ResumeCommand = new RelayCommand(Resume, () => IsDebugMode && IsPaused);
        ClosePopupCommand = new RelayCommand(ClosePopup);
        ApplySettingsCommand = new RelayCommand(ApplySettings);
        SendInputCommand = new RelayCommand(SendInput);
        SendDeviceInputCommand = new RelayCommand(SendDeviceInput);

        _runService.OutputAppended += OnOutputAppended;
        _runService.PhaseChanged += OnPhaseChanged;
        _runService.RegistersChanged += OnRegistersChanged;
        _runService.MemoryChanged += OnMemoryChanged;
        _runService.InputRequested += OnInputRequested;
        _runService.Completed += OnCompleted;
    }

    public ObservableCollection<ExampleEntry> Examples { get; } = new();
    public IReadOnlyList<string> SpeedOptions { get; } =
    [
        "Максимальная",
        "Как на БЭСМ-6 (original)",
        "Виртуальная",
    ];
    public IReadOnlyList<string> ModelOptions { get; } =
    [
        "Дубна",
        "Дубна + БРЗ/БРС",
        "Supervisor (функциональный)",
        "Supervisor (аппаратный)",
    ];
    public IReadOnlyList<string> FontFamilyOptions { get; } =
    [
        "Моноширинный",
        "Системный",
        "С засечками",
    ];
    public IReadOnlyList<string> MemoryWordsOptions { get; } = ["16", "32", "64", "128"];
    public IReadOnlyList<string> NumberBaseOptions { get; } =
    [
        "Восьмеричная",
        "Десятичная",
        "Шестнадцатеричная",
    ];

    public RelayCommand RunCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenRegistersCommand { get; }
    public RelayCommand<string> OpenDeviceCommand { get; }
    public RelayCommand<string> SelectTabCommand { get; }
    public RelayCommand OpenSearchCommand { get; }
    public RelayCommand SearchFilesCommand { get; }
    public RelayCommand OpenMemoryCommand { get; }
    public RelayCommand InspectMemoryCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand StepCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ClosePopupCommand { get; }
    public RelayCommand ApplySettingsCommand { get; }
    public RelayCommand SendInputCommand { get; }
    public RelayCommand SendDeviceInputCommand { get; }

    public string JobPath => Path.Combine(_installer.WorkDirectory, "job.dub");

    public ExampleEntry? SelectedExample
    {
        get => _selectedExample;
        set
        {
            if (!SetProperty(ref _selectedExample, value) || value is null) return;
            LoadExample(value);
        }
    }

    public string Code
    {
        get => _code;
        set
        {
            if (!SetProperty(ref _code, value)) return;
            RunCommand.RaiseCanExecuteChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (!SetProperty(ref _status, value)) return;
            RaiseStatusColor();
        }
    }

    public string ThemeLabel => _theme.Label;

    public Color StatusColor => LastOutcome is { Success: false, Cancelled: false }
        ? Color.FromArgb("#C24A3A")
        : IsWaitingInput
            ? Color.FromArgb("#C08A2E")
            : IsRunning
                ? Color.FromArgb("#3E8A5A")
                : Color.FromArgb("#8C7C70");

    public string InputText
    {
        get => _inputText;
        set => SetProperty(ref _inputText, value);
    }

    public string InputPrompt
    {
        get => _inputPrompt;
        private set => SetProperty(ref _inputPrompt, value);
    }

    public string DeviceInput
    {
        get => _deviceInput;
        set => SetProperty(ref _deviceInput, value);
    }

    public string DeviceText
    {
        get => _deviceText;
        private set => SetProperty(ref _deviceText, value);
    }

    public string DeviceTitle
    {
        get => _deviceTitle;
        private set => SetProperty(ref _deviceTitle, value);
    }

    public ObservableCollection<SearchHit> SearchResults => _searchResults;

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public string SearchStatus
    {
        get => _searchStatus;
        private set => SetProperty(ref _searchStatus, value);
    }

    public SearchHit? SelectedSearchHit
    {
        get => _selectedSearchHit;
        set
        {
            if (!SetProperty(ref _selectedSearchHit, value) || value is null) return;
            OpenSearchHit(value);
            _dispatcher.Post(() =>
            {
                _selectedSearchHit = null;
                OnPropertyChanged(nameof(SelectedSearchHit));
            });
        }
    }

    public string MemoryAddressText
    {
        get => _memoryAddressText;
        set => SetProperty(ref _memoryAddressText, value);
    }

    public string MemoryText
    {
        get => _memoryText;
        private set => SetProperty(ref _memoryText, value);
    }

    public string PlotterText
    {
        get => _plotterText;
        private set => SetProperty(ref _plotterText, value);
    }

    public IReadOnlyList<string> PlotterTypeOptions => PlotterTypes;

    public int PlotterTypeIndex
    {
        get => _plotterTypeIndex;
        set
        {
            if (!SetProperty(ref _plotterTypeIndex, value)) return;
            OnPropertyChanged(nameof(PlotterKind));
            PlotterText = _deviceBuffer.Get(PlotterKind);
        }
    }

    public string PlotterKind => PlotterTypes[Math.Clamp(PlotterTypeIndex, 0, PlotterTypes.Length - 1)]
        .ToLowerInvariant();

    public string RegistersText
    {
        get => _registersText;
        private set => SetProperty(ref _registersText, value);
    }

    public string PopupTitle
    {
        get => _popupTitle;
        private set => SetProperty(ref _popupTitle, value);
    }

    public string PopupError
    {
        get => _popupError;
        private set
        {
            if (!SetProperty(ref _popupError, value)) return;
            OnPropertyChanged(nameof(HasPopupError));
        }
    }

    public bool HasPopupError => !string.IsNullOrEmpty(PopupError);

    public bool IsInitialized
    {
        get => _isInitialized;
        private set
        {
            if (SetProperty(ref _isInitialized, value)) RaiseRunState();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) RaiseRunState();
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            StopCommand.RaiseCanExecuteChanged();
            RaiseRunState();
            RaiseStatusColor();
        }
    }

    public bool IsWaitingInput
    {
        get => _isWaitingInput;
        private set
        {
            if (SetProperty(ref _isWaitingInput, value)) RaiseStatusColor();
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (!SetProperty(ref _isPaused, value)) return;
            RaiseDebugCommands();
        }
    }

    public bool IsDebugMode => Settings.DebugMode;

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        private set
        {
            if (SetProperty(ref _isSettingsOpen, value)) OnPropertyChanged(nameof(IsPopupOpen));
        }
    }

    public bool IsRegistersOpen
    {
        get => _isRegistersOpen;
        private set
        {
            if (SetProperty(ref _isRegistersOpen, value)) OnPropertyChanged(nameof(IsPopupOpen));
        }
    }

    public bool IsDeviceOpen
    {
        get => _isDeviceOpen;
        private set
        {
            if (SetProperty(ref _isDeviceOpen, value)) OnPropertyChanged(nameof(IsPopupOpen));
        }
    }

    public bool IsSearchOpen
    {
        get => _isSearchOpen;
        private set
        {
            if (SetProperty(ref _isSearchOpen, value)) OnPropertyChanged(nameof(IsPopupOpen));
        }
    }

    public bool IsMemoryOpen
    {
        get => _isMemoryOpen;
        private set
        {
            if (SetProperty(ref _isMemoryOpen, value)) OnPropertyChanged(nameof(IsPopupOpen));
        }
    }

    public bool IsPopupOpen => IsSettingsOpen;

    public string ActiveTab => _activeTab;
    public bool IsCodeTab => _activeTab == "code";
    public bool IsSearchTab => _activeTab == "search";
    public bool IsRegistersTab => _activeTab == "registers";
    public bool IsLampTab => _activeTab == "lamps";
    public bool IsMemoryTab => _activeTab == "memory";
    public bool IsTerminalTab => _activeTab == "terminal";
    public bool IsPrinterTab => _activeTab == "print";
    public bool IsPunchTab => _activeTab == "punch";
    public bool IsPlotterTab => _activeTab == "plotter";

    public string TerminalText
    {
        get => _terminalText;
        private set => SetProperty(ref _terminalText, value);
    }

    public string PrinterText
    {
        get => _printerText;
        private set => SetProperty(ref _printerText, value);
    }

    public string PunchText
    {
        get => _punchText;
        private set => SetProperty(ref _punchText, value);
    }

    public bool IsTerminalDevice
    {
        get => _isTerminalDevice;
        private set => SetProperty(ref _isTerminalDevice, value);
    }

    public bool IsPlotterDevice
    {
        get => _isPlotterDevice;
        private set
        {
            if (!SetProperty(ref _isPlotterDevice, value)) return;
            OnPropertyChanged(nameof(IsRawDevice));
        }
    }

    public bool IsRawDevice => !IsPlotterDevice;

    public bool CanRun => IsInitialized && !IsBusy && !IsRunning;

    public RunSettings Settings
    {
        get => _settings;
        private set
        {
            if (!SetProperty(ref _settings, value)) return;
            OnPropertyChanged(nameof(IsDebugMode));
            RaiseDebugCommands();
        }
    }

    public bool DebugMode
    {
        get => _debugMode;
        set => SetProperty(ref _debugMode, value);
    }

    public RegisterSnapshot? Snapshot
    {
        get => _snapshot;
        private set => SetProperty(ref _snapshot, value);
    }

    public RunOutcome? LastOutcome
    {
        get => _lastOutcome;
        private set
        {
            if (SetProperty(ref _lastOutcome, value)) RaiseStatusColor();
        }
    }

    public int SpeedIndex
    {
        get => _speedIndex;
        set => SetProperty(ref _speedIndex, value);
    }

    public int ModelIndex
    {
        get => _modelIndex;
        set
        {
            if (!SetProperty(ref _modelIndex, value)) return;
            if (value == (int)MachineModel.SupervisorHardware) DebugMode = false;
            OnPropertyChanged(nameof(IsDebugEnabled));
        }
    }

    public bool IsDebugEnabled => ModelIndex != (int)MachineModel.SupervisorHardware;

    public bool UseWallClock
    {
        get => _useWallClock;
        set => SetProperty(ref _useWallClock, value);
    }

    public bool LoopDetect
    {
        get => _loopDetect;
        set => SetProperty(ref _loopDetect, value);
    }

    public bool HangDetect
    {
        get => _hangDetect;
        set => SetProperty(ref _hangDetect, value);
    }

    public bool ForexAsFortran
    {
        get => _forexAsFortran;
        set => SetProperty(ref _forexAsFortran, value);
    }

    public int FontFamilyIndex
    {
        get => _fontFamilyIndex;
        set
        {
            if (!SetProperty(ref _fontFamilyIndex, value)) return;
            OnPropertyChanged(nameof(OutputFontFamily));
        }
    }

    public string OutputFontFamily =>
        FontFamilies[Math.Clamp(FontFamilyIndex, 0, FontFamilies.Length - 1)];

    public double OutputFontSize
    {
        get => _outputFontSize;
        set => SetProperty(ref _outputFontSize, value);
    }

    public double EditorFontSize
    {
        get => _editorFontSize;
        set => SetProperty(ref _editorFontSize, value);
    }

    public int MemoryWordsIndex
    {
        get => _memoryWordsIndex;
        set => SetProperty(ref _memoryWordsIndex, value);
    }

    public int MemoryWords => MemoryWordCounts[Math.Clamp(MemoryWordsIndex, 0, MemoryWordCounts.Length - 1)];

    public int NumberBaseIndex
    {
        get => _numberBaseIndex;
        set => SetProperty(ref _numberBaseIndex, value);
    }

    public NumberBase NumberBase => (NumberBase)Math.Clamp(NumberBaseIndex, 0, 2);

    public string InstructionLimitText
    {
        get => _instructionLimitText;
        set => SetProperty(ref _instructionLimitText, value);
    }

    public string WallClockSecondsText
    {
        get => _wallClockSecondsText;
        set => SetProperty(ref _wallClockSecondsText, value);
    }

    public bool GuestClock
    {
        get => _guestClock;
        set => SetProperty(ref _guestClock, value);
    }

    public bool Statistics
    {
        get => _statistics;
        set => SetProperty(ref _statistics, value);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await Task.Run(() => _installer.EnsureInstalled());
            var catalog = new ExampleCatalog(_examplesDirectory);
            var entries = await Task.Run(catalog.Load);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Examples.Clear();
                foreach (var entry in entries) Examples.Add(entry);
                Settings = _settingsStore.Load();
                ApplyDisplay(_settingsStore.LoadDisplay());
                IsInitialized = true;
                Status = entries.Count == 0
                    ? "Примеры не найдены."
                    : $"Готово. Примеров: {entries.Count}.";
                SelectedExample = ExampleCatalog.FindPreferred(entries);
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => Status = "Ошибка подготовки: " + ex.Message);
        }
    }

    private void ApplyDisplay(DisplaySettings display)
    {
        int fontIndex = Array.FindIndex(FontFamilies,
            value => string.Equals(value, display.OutputFontFamily, StringComparison.Ordinal));
        FontFamilyIndex = fontIndex < 0 ? 0 : fontIndex;
        OutputFontSize = display.OutputFontSize;
        EditorFontSize = display.EditorFontSize;
        int wordsIndex = Array.IndexOf(MemoryWordCounts, display.MemoryWords);
        MemoryWordsIndex = wordsIndex < 0 ? 1 : wordsIndex;
        NumberBaseIndex = (int)display.NumberBase;
    }

    private void LoadExample(ExampleEntry entry)
    {
        try
        {
            Code = File.ReadAllText(entry.FullPath, Encoding.UTF8);
            Status = "Загружен пример " + entry.RelativePath + ".";
        }
        catch (Exception ex)
        {
            Status = "Не удалось открыть пример: " + ex.Message;
        }
    }

    private async Task RunAsync()
    {
        if (!CanRun) return;
        if (!Settings.TryValidate(out string? settingsError))
        {
            Status = settingsError ?? "Некорректные настройки.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Code))
        {
            Status = "Текст задания пуст.";
            return;
        }

        IsBusy = true;
        IsRunning = true;
        IsWaitingInput = false;
        _runClock.Restart();
        LastOutcome = null;
        InputText = string.Empty;
        InputPrompt = string.Empty;
        DeviceInput = string.Empty;
        _deviceBuffer.Clear();
        TerminalText = string.Empty;
        PrinterText = string.Empty;
        PunchText = string.Empty;
        PlotterText = string.Empty;
        Status = "Загрузка задания…";

        try
        {
            string code = Code;
            await Task.Run(() => File.WriteAllText(JobPath, code, new UTF8Encoding(false)));
            await _runService.Start(JobPath, Settings);
        }
        catch (Exception ex)
        {
            Status = "Ошибка запуска: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsRunning = _runService.IsRunning;
            RunCommand.RaiseCanExecuteChanged();
        }
    }

    private void Stop()
    {
        _runService.RequestStop();
        IsPaused = false;
        Status = "Остановка…";
    }

    private void Pause()
    {
        _runService.Pause();
        Status = "Пауза…";
    }

    private void Step()
    {
        _runService.Step();
        IsPaused = false;
        Status = "Шаг…";
    }

    private void Resume()
    {
        _runService.Resume();
        IsPaused = false;
        Status = "Выполнение…";
    }

    private void SendInput()
    {
        if (_runService.SubmitInput(InputText))
        {
            InputText = string.Empty;
            InputPrompt = string.Empty;
            IsWaitingInput = false;
            Status = "Выполнение…";
        }
    }

    private void SendDeviceInput()
    {
        if (_runService.SubmitInput(DeviceInput))
        {
            DeviceInput = string.Empty;
            InputPrompt = string.Empty;
            IsWaitingInput = false;
            Status = "Выполнение…";
        }
    }

    private void OpenSettings()
    {
        PopupError = string.Empty;
        SpeedIndex = Settings.Speed switch
        {
            ExecutionSpeed.Original => 1,
            ExecutionSpeed.Virtual => 2,
            _ => 0,
        };
        ModelIndex = (int)Settings.Model;
        InstructionLimitText = Settings.InstructionLimit.ToString(CultureInfo.InvariantCulture);
        WallClockSecondsText = (Settings.WallClockLimitMs / 1000.0)
            .ToString("0.###", CultureInfo.InvariantCulture);
        GuestClock = Settings.GuestClock;
        Statistics = Settings.CollectStatistics;
        DebugMode = Settings.DebugMode;
        UseWallClock = Settings.UseWallClock;
        LoopDetect = Settings.LoopDetect;
        HangDetect = Settings.HangDetect;
        ForexAsFortran = Settings.ForexAsFortran;
        PopupTitle = "Настройки запуска";
        IsRegistersOpen = false;
        IsDeviceOpen = false;
        IsSearchOpen = false;
        IsMemoryOpen = false;
        IsSettingsOpen = true;
    }

    private void ApplySettings()
    {
        if (!long.TryParse(InstructionLimitText, NumberStyles.None, CultureInfo.InvariantCulture,
                out long instructionLimit))
        {
            PopupError = "Лимит команд должен быть целым неотрицательным числом.";
            return;
        }

        string secondsText = (WallClockSecondsText ?? string.Empty).Trim().Replace(',', '.');
        if (!double.TryParse(secondsText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out double seconds) || seconds < 0 || seconds > 3600)
        {
            PopupError = "Лимит времени должен быть от 0 до 3600 секунд.";
            return;
        }

        var speed = SpeedIndex switch
        {
            1 => ExecutionSpeed.Original,
            2 => ExecutionSpeed.Virtual,
            _ => ExecutionSpeed.Max,
        };
        var model = (MachineModel)Math.Clamp(ModelIndex, 0, ModelOptions.Count - 1);
        long wallClockMs = (long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero);
        bool debug = model == MachineModel.SupervisorHardware ? false : DebugMode;
        var candidate = new RunSettings(speed, instructionLimit, wallClockMs, model,
            GuestClock, Statistics, debug, UseWallClock, LoopDetect, HangDetect, ForexAsFortran);
        if (!candidate.TryValidate(out string? error))
        {
            PopupError = error ?? "Некорректные настройки.";
            return;
        }

        var display = new DisplaySettings(OutputFontFamily, OutputFontSize, EditorFontSize,
            MemoryWords, NumberBase);
        if (!display.TryValidate(out string? displayError))
        {
            PopupError = displayError ?? "Некорректные настройки отображения.";
            return;
        }

        Settings = candidate;
        _settingsStore.Save(candidate);
        _settingsStore.SaveDisplay(display);
        ApplyDisplay(display);
        PopupError = string.Empty;
        ClosePopup();
        Status = "Настройки сохранены.";
    }

    private void SelectTab(string? tab)
    {
        string value = tab is "code" or "search" or "registers" or "lamps" or "memory"
            or "terminal" or "print" or "punch" or "plotter" ? tab : "code";
        if (_activeTab != value)
        {
            _activeTab = value;
            OnPropertyChanged(nameof(ActiveTab));
            NotifyTabFlags();
        }

        switch (value)
        {
            case "registers":
                RegistersText = RegisterFormatter.Format(Snapshot);
                break;
            case "memory":
                if (Snapshot is { } snapshot)
                {
                    int address = snapshot.K == 0 ? 512 : (int)snapshot.K;
                    MemoryAddressText = MemoryAddress(address);
                }
                InspectMemory();
                break;
            case "terminal":
                TerminalText = _deviceBuffer.Get(DeviceChannels.Terminal);
                break;
            case "print":
                PrinterText = _deviceBuffer.Get(DeviceChannels.Printer);
                break;
            case "punch":
                PunchText = _deviceBuffer.Get(DeviceChannels.Punch);
                break;
            case "plotter":
                int index = Array.FindIndex(DevicePlotter, channel => _deviceBuffer.Get(channel).Length > 0);
                PlotterTypeIndex = index < 0 ? 0 : index;
                PlotterText = _deviceBuffer.Get(PlotterKind);
                break;
            case "search":
                SearchStatus = SearchResults.Count == 0
                    ? "Поиск по имени файла и строкам кода."
                    : $"Найдено: {SearchResults.Count}.";
                break;
        }
    }

    private void NotifyTabFlags()
    {
        OnPropertyChanged(nameof(IsCodeTab));
        OnPropertyChanged(nameof(IsSearchTab));
        OnPropertyChanged(nameof(IsRegistersTab));
        OnPropertyChanged(nameof(IsLampTab));
        OnPropertyChanged(nameof(IsMemoryTab));
        OnPropertyChanged(nameof(IsTerminalTab));
        OnPropertyChanged(nameof(IsPrinterTab));
        OnPropertyChanged(nameof(IsPunchTab));
        OnPropertyChanged(nameof(IsPlotterTab));
    }

    private async Task SearchAsync()
    {
        string query = SearchQuery.Trim();
        if (query.Length == 0)
        {
            SearchResults.Clear();
            SearchStatus = "Введите запрос.";
            return;
        }

        SearchStatus = "Поиск…";
        var hits = await Task.Run(() => _search.Search(query));
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            SearchResults.Clear();
            foreach (var hit in hits) SearchResults.Add(hit);
            SearchStatus = hits.Count == 0 ? "Ничего не найдено." : $"Найдено: {hits.Count}.";
        });
    }

    private void OpenSearchHit(SearchHit hit)
    {
        var entry = Examples.FirstOrDefault(item =>
            string.Equals(item.RelativePath, hit.RelativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            SelectedExample = entry;
        }
        else
        {
            Code = File.ReadAllText(hit.FullPath, Encoding.UTF8);
        }
        Status = "Открыт " + hit.RelativePath + ".";
        SelectTab("code");
    }

    private void OpenMemory() => SelectTab("memory");

    private void InspectMemory()
    {
        if (!TryParseAddress(MemoryAddressText, out int address))
        {
            Status = "Некорректный адрес памяти.";
            return;
        }
        _runService.RequestMemorySnapshot(address, MemoryWords);
    }

    private string MemoryAddress(int address) => NumberBase switch
    {
        NumberBase.Decimal => address.ToString("D5", CultureInfo.InvariantCulture),
        NumberBase.Hexadecimal => address.ToString("X4", CultureInfo.InvariantCulture),
        _ => Convert.ToString(address, 8).PadLeft(5, '0'),
    };

    private bool TryParseAddress(string? text, out int address)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            address = 512;
            return true;
        }

        int radix = NumberBase switch
        {
            NumberBase.Decimal => 10,
            NumberBase.Hexadecimal => 16,
            _ => 8,
        };
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            radix = 16;
            text = text[2..];
        }
        else if (text.StartsWith("0d", StringComparison.OrdinalIgnoreCase))
        {
            radix = 10;
            text = text[2..];
        }
        try
        {
            address = Convert.ToInt32(text, radix);
            return address is >= 0 and < 32768;
        }
        catch (FormatException)
        {
            address = 0;
            return false;
        }
        catch (OverflowException)
        {
            address = 0;
            return false;
        }
    }

    private void OpenDevice(string? channel) => SelectTab(channel);

    private void ClosePopup()
    {
        IsSettingsOpen = false;
        IsSearchOpen = false;
        PopupError = string.Empty;
    }

    private string BuildActiveText()
    {
        if (_activeChannels.Length == 0) return string.Empty;
        if (_activeChannels.Length == 1) return _deviceBuffer.Get(_activeChannels[0]);
        var text = new StringBuilder();
        foreach (string channel in _activeChannels)
        {
            if (text.Length > 0) text.Append("\n\n");
            text.Append(ChannelTitle(channel)).Append('\n');
            text.Append(_deviceBuffer.Get(channel));
        }
        return text.ToString();
    }

    private static string ChannelTitle(string channel) => channel switch
    {
        DeviceChannels.Watanabe => "WATANABE",
        DeviceChannels.Calcomp => "CALCOMP",
        DeviceChannels.Tektronix => "TEKTRONIX",
        _ => channel.ToUpperInvariant(),
    };

    private void OnOutputAppended(string channel, string text)
    {
        bool trimmed = _deviceBuffer.Append(channel, text);
        _dispatcher.Post(() =>
        {
            switch (channel)
            {
                case DeviceChannels.Terminal:
                    TerminalText = trimmed ? _deviceBuffer.Get(channel) : TerminalText + text;
                    break;
                case DeviceChannels.Printer:
                    PrinterText = trimmed ? _deviceBuffer.Get(channel) : PrinterText + text;
                    break;
                case DeviceChannels.Punch:
                    PunchText = trimmed ? _deviceBuffer.Get(channel) : PunchText + text;
                    break;
            }
            if (channel == PlotterKind)
                PlotterText = trimmed ? _deviceBuffer.Get(channel) : PlotterText + text;
        });
    }

    private void OnPhaseChanged(RunPhase phase)
    {
        _dispatcher.Post(() =>
        {
            switch (phase)
            {
                case RunPhase.Preparing:
                    Status = "Подготовка машины…";
                    IsWaitingInput = false;
                    break;
                case RunPhase.Running:
                    Status = "Выполнение…";
                    IsWaitingInput = false;
                    IsPaused = false;
                    break;
                case RunPhase.Paused:
                    IsPaused = true;
                    Status = "Пауза. Регистры и память доступны.";
                    break;
                case RunPhase.WaitingInput:
                    IsWaitingInput = true;
                    Status = "Ожидание ввода.";
                    break;
                case RunPhase.Stopping:
                    IsWaitingInput = false;
                    IsPaused = false;
                    Status = "Остановка…";
                    break;
                case RunPhase.Completed:
                case RunPhase.Failed:
                    IsWaitingInput = false;
                    IsPaused = false;
                    break;
            }
        });
    }

    private void OnInputRequested(string prompt)
    {
        _dispatcher.Post(() =>
        {
            InputPrompt = string.IsNullOrWhiteSpace(prompt) ? "Введите строку:" : prompt.Trim();
            IsWaitingInput = true;
            Status = "Ожидание ввода.";
            SelectTab("terminal");
        });
    }

    private void OnRegistersChanged(RegisterSnapshot snapshot)
    {
        _dispatcher.Post(() =>
        {
            Snapshot = snapshot;
            RegistersText = RegisterFormatter.Format(snapshot);
            if (IsRunning)
                Status = $"Выполнение · {_runClock.Elapsed.TotalSeconds:0} с · {snapshot.Instructions:N0} команд";
        });
    }

    private void OnMemoryChanged(MemorySnapshot snapshot)
    {
        _dispatcher.Post(() =>
        {
            MemoryText = RegisterFormatter.FormatMemory(snapshot, NumberBase);
            MemoryAddressText = MemoryAddress(snapshot.Address);
        });
    }

    private void OnCompleted(RunOutcome outcome)
    {
        _dispatcher.Post(() =>
        {
            _runClock.Stop();
            LastOutcome = outcome;
            IsRunning = false;
            IsWaitingInput = false;
            IsPaused = false;
            Status = outcome.Cancelled
                ? "Остановлено пользователем."
                : outcome.Success
                    ? $"Завершено. Команд: {outcome.Instructions:N0}."
                    : outcome.LimitExceeded
                        ? $"Достигнут лимит. Команд: {outcome.Instructions:N0}."
                        : "Ошибка: " + (outcome.Error ?? "неизвестная причина");
            RaiseRunState();
        });
    }

    private void ToggleTheme()
    {
        _theme.Cycle();
        OnPropertyChanged(nameof(ThemeLabel));
    }

    private void RaiseStatusColor() => OnPropertyChanged(nameof(StatusColor));

    private void RaiseDebugCommands()
    {
        PauseCommand.RaiseCanExecuteChanged();
        StepCommand.RaiseCanExecuteChanged();
        ResumeCommand.RaiseCanExecuteChanged();
    }

    private void RaiseRunState()
    {
        OnPropertyChanged(nameof(CanRun));
        RunCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        RaiseDebugCommands();
    }
}
