using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using MqttPulse.App.Infrastructure;
using MqttPulse.App.Models;
using MqttPulse.App.Services;
using MqttPulse.Core;

namespace MqttPulse.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxMessagesPerUiTick = 5_000;
    private const int MaxPendingMessages = 10_000;
    private const long MaxPendingPayloadCharacters = 16_000_000;
    private const int HistoryDisplayLimit = 100;
    private static readonly long UiDrainBudgetTicks = Stopwatch.Frequency / 100;
    private static readonly long ValueRefreshMinTicks = Stopwatch.Frequency / 10;
    private static readonly long HistoryRefreshMinTicks = Stopwatch.Frequency / 2;
    private static readonly long TopicVisualRefreshMinTicks = Stopwatch.Frequency / 2;
    private const int SearchResultLimit = 500;
    private const int MinPeriodCheckSeconds = 1;
    private const int MaxPeriodCheckSeconds = 3_600;
    private const int PeriodCheckHistoryLimit = 100;
    private readonly ConcurrentQueue<MqttMessageSnapshot> _pendingMessages = new();
    private readonly ConcurrentQueue<long> _periodCheckSamples = new();
    private readonly Dictionary<string, TopicViewModel> _rootTopicsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TopicViewModel> _leafTopicsByFullName = new(StringComparer.Ordinal);
    private readonly HashSet<TopicViewModel> _dirtyTopicNodes = new();
    private readonly HashSet<string> _profileFolderPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProfileStore _profileStore;
    private readonly MqttClientService _mqttClient = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _flushTimer;
    private CancellationTokenSource? _periodCheckCancellation;
    private ProfileTreeNodeViewModel? _selectedProfileNode;
    private TopicViewModel? _brokerTopicRoot;
    private BrokerProfile? _selectedProfile;
    private BrokerProfile? _connectedProfile;
    private TopicViewModel? _selectedTopic;
    private HistoryItemViewModel? _selectedHistoryItem;
    private PeriodCheckHistoryItemViewModel? _selectedPeriodCheckHistoryItem;
    private string _searchText = string.Empty;
    private string _selectedFolderPath = string.Empty;
    private string _selectedFolderName = string.Empty;
    private string _selectedProfileFolderPath = string.Empty;
    private bool _isRefreshingFolderOptions;
    private string _valuePayloadText = string.Empty;
    private string _selectedPayloadText = string.Empty;
    private string _selectedTopicAveragePeriodText = "Avg -";
    private string _publishTopic = string.Empty;
    private string _publishPayload = "{\r\n  \"hello\": \"mqtt\"\r\n}";
    private int _publishQos;
    private bool _publishRetain;
    private bool _isConnected;
    private bool _isBusy;
    private bool _isConnectionManagerOpen;
    private bool _isPeriodCheckOpen;
    private bool _isPeriodCheckRunning;
    private bool _isJsonFormatterOpen;
    private bool _topicListsNeedRefresh;
    private bool _historyPaused;
    private bool _freezeDetail;
    private bool _followLatest = true;
    private string _statusMessage = "Ready";
    private string _periodCheckTopicText = string.Empty;
    private string _periodCheckDurationText = "10초";
    private string _periodCheckStatus = "토픽을 입력하거나 기존 토픽에서 선택하세요.";
    private string _periodCheckResultText = "아직 측정 결과가 없습니다.";
    private string _periodCheckTargetTopic = string.Empty;
    private string _jsonFormatterInput = string.Empty;
    private string _jsonFormatterOutput = string.Empty;
    private string _jsonFormatterStatus = "왼쪽에 JSON을 붙여 넣고 Format을 누르세요.";
    private long _receivedMessages;
    private int _pendingQueueCount;
    private long _pendingPayloadCharacters;
    private int _pendingCount;
    private long _lastValueRefreshTimestamp;
    private long _lastHistoryRefreshTimestamp;
    private long _lastTopicVisualRefreshTimestamp;
    private MqttMessageSnapshot? _pendingValueFormatMessage;
    private int _valueFormatWorkerRunning;
    private volatile bool _isDisposed;

    public MainViewModel(ProfileStore? profileStore = null)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _profileStore = profileStore ?? new ProfileStore();
        var library = _profileStore.LoadLibrary();
        Profiles = new ObservableCollection<BrokerProfile>(library.Profiles);
        foreach (var folderPath in library.FolderPaths.Select(ProfileTreeBuilder.NormalizeFolderPath).Where(x => x.Length > 0))
        {
            _profileFolderPaths.Add(folderPath);
        }

        if (Profiles.Count == 0)
        {
            Profiles.Add(BrokerProfile.CreateDefault());
        }

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && !IsConnected && SelectedProfile is not null);
        ConnectSelectedProfileCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && SelectedProfile is not null);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => !IsBusy && IsConnected);
        ToggleConnectionCommand = new AsyncRelayCommand(
            ToggleConnectionAsync,
            () => !IsBusy && (IsConnected || SelectedProfile is not null));
        PublishCommand = new AsyncRelayCommand(PublishAsync, () => IsConnected && !string.IsNullOrWhiteSpace(PublishTopic));
        FormatPublishJsonCommand = new RelayCommand(FormatPublishJson);
        CopyValueCommand = new RelayCommand(CopyValueToClipboard, () => !string.IsNullOrEmpty(ValuePayloadText));
        CopySelectedCommand = new RelayCommand(CopySelectedToClipboard, () => !string.IsNullOrEmpty(SelectedPayloadText));
        ToggleHistoryPauseCommand = new RelayCommand(ToggleHistoryPause);
        OpenConnectionManagerCommand = new AsyncRelayCommand(OpenConnectionManagerAsync, () => !IsBusy && !IsPeriodCheckRunning);
        CloseConnectionManagerCommand = new RelayCommand(CloseConnectionManager);
        OpenPeriodCheckCommand = new RelayCommand(OpenPeriodCheck, () => IsConnected && !IsPeriodCheckRunning);
        ClosePeriodCheckCommand = new RelayCommand(ClosePeriodCheck, () => !IsPeriodCheckRunning);
        StartPeriodCheckCommand = new RelayCommand(
            StartPeriodCheck,
            () => IsConnected
                  && !IsPeriodCheckRunning
                  && !string.IsNullOrWhiteSpace(PeriodCheckTopicText)
                  && TryGetPeriodCheckDuration(out _));
        StopPeriodCheckCommand = new RelayCommand(StopPeriodCheck, () => IsPeriodCheckRunning);
        OpenJsonFormatterCommand = new RelayCommand(OpenJsonFormatter);
        CloseJsonFormatterCommand = new RelayCommand(CloseJsonFormatter);
        FormatJsonFormatterCommand = new RelayCommand(() => FormatJsonFormatter(indented: true));
        CompactJsonFormatterCommand = new RelayCommand(() => FormatJsonFormatter(indented: false));
        ValidateJsonFormatterCommand = new RelayCommand(ValidateJsonFormatter);
        CopyJsonFormatterCommand = new RelayCommand(
            CopyJsonFormatter,
            () => !string.IsNullOrEmpty(JsonFormatterOutput));
        ClearJsonFormatterCommand = new RelayCommand(ClearJsonFormatter);
        AddProfileCommand = new RelayCommand(AddProfile);
        AddFolderCommand = new RelayCommand(AddFolder);
        RenameFolderCommand = new RelayCommand(RenameFolder, () => CanRenameSelectedFolder && !string.IsNullOrWhiteSpace(SelectedFolderName));
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => Profiles.Count > 1 && SelectedProfile is not null);
        DeleteFolderCommand = new RelayCommand(DeleteFolder, () => SelectedProfileNode?.IsFolder == true);
        SaveProfilesCommand = new RelayCommand(SaveProfiles);
        ClearTopicsCommand = new RelayCommand(ClearTopics);
        RebuildProfileTree();
        SelectedProfile = Profiles[0];

        _mqttClient.MessageReceived += OnMessageReceived;
        _mqttClient.StatusChanged += OnStatusChanged;

        _flushTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _flushTimer.Tick += (_, _) => DrainPendingMessages();
        _flushTimer.Start();
    }

    public ObservableCollection<BrokerProfile> Profiles { get; }

    public ObservableCollection<ProfileTreeNodeViewModel> ProfileTree { get; } = new();

    public ObservableCollection<string> FolderOptions { get; } = new();

    public ObservableCollection<TopicViewModel> RootTopics { get; } = new();

    public ObservableCollection<HistoryItemViewModel> SelectedTopicHistory { get; } = new();

    public ObservableCollection<string> PeriodCheckTopicSuggestions { get; } = new();

    public ObservableCollection<string> PublishTopicSuggestions { get; } = new();

    public ObservableCollection<PeriodCheckHistoryItemViewModel> PeriodCheckHistory { get; } = new();

    public ObservableCollection<JsonTreeNodeViewModel> JsonFormatterTreeRoots { get; } = new();

    public string AppVersionText { get; } = CreateAppVersionText();

    public IReadOnlyList<int> QosOptions { get; } = new[] { 0, 1, 2 };

    public IReadOnlyList<string> TransportOptions { get; } = new[] { "mqtt", "ws", "wss" };

    public IReadOnlyList<string> PeriodCheckDurationOptions { get; } = new[] { "10초", "30초", "1분" };

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand ConnectSelectedProfileCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

    public AsyncRelayCommand ToggleConnectionCommand { get; }

    public AsyncRelayCommand PublishCommand { get; }

    public RelayCommand FormatPublishJsonCommand { get; }

    public RelayCommand CopyValueCommand { get; }

    public RelayCommand CopySelectedCommand { get; }

    public RelayCommand ToggleHistoryPauseCommand { get; }

    public AsyncRelayCommand OpenConnectionManagerCommand { get; }

    public RelayCommand CloseConnectionManagerCommand { get; }

    public RelayCommand OpenPeriodCheckCommand { get; }

    public RelayCommand ClosePeriodCheckCommand { get; }

    public RelayCommand StartPeriodCheckCommand { get; }

    public RelayCommand StopPeriodCheckCommand { get; }

    public RelayCommand OpenJsonFormatterCommand { get; }

    public RelayCommand CloseJsonFormatterCommand { get; }

    public RelayCommand FormatJsonFormatterCommand { get; }

    public RelayCommand CompactJsonFormatterCommand { get; }

    public RelayCommand ValidateJsonFormatterCommand { get; }

    public RelayCommand CopyJsonFormatterCommand { get; }

    public RelayCommand ClearJsonFormatterCommand { get; }

    public RelayCommand AddProfileCommand { get; }

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand RenameFolderCommand { get; }

    public RelayCommand DeleteProfileCommand { get; }

    public RelayCommand DeleteFolderCommand { get; }

    public RelayCommand SaveProfilesCommand { get; }

    public RelayCommand ClearTopicsCommand { get; }

    public ProfileTreeNodeViewModel? SelectedProfileNode
    {
        get => _selectedProfileNode;
        set
        {
            if (ReferenceEquals(_selectedProfileNode, value))
            {
                return;
            }

            if (_selectedProfileNode is not null)
            {
                _selectedProfileNode.IsSelected = false;
            }

            _selectedProfileNode = value;
            OnPropertyChanged();

            if (value is not null)
            {
                value.IsSelected = true;
            }

            if (value is null)
            {
                SelectedFolderPath = string.Empty;
                SelectedFolderName = string.Empty;
                SelectedProfile = null;
                RefreshConnectionEditorState();
                RaiseCommandStates();
                return;
            }

            if (value.IsFolder)
            {
                SelectedFolderPath = value.FullPath;
                SelectedFolderName = GetFolderName(value.FullPath);
                SelectedProfile = null;
            }
            else
            {
                SelectedFolderPath = value.Profile?.FolderPath ?? string.Empty;
                SelectedFolderName = string.Empty;
                SelectedProfile = value.Profile;
            }

            RefreshConnectionEditorState();
            RaiseCommandStates();
        }
    }
    public BrokerProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                SelectedProfileFolderPath = value?.FolderPath ?? string.Empty;
                OnPropertyChanged(nameof(SelectedProfileCaption));
                RefreshConnectionEditorState();
                RaiseCommandStates();
            }
        }
    }

    public string SelectedProfileCaption => _connectedProfile is not { } profile
        ? "Not connected"
        : $"{profile.Name} ({profile.Transport}://{profile.Host}:{profile.Port})";

    public TopicViewModel? SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            if (SetProperty(ref _selectedTopic, value))
            {
                HistoryPaused = false;
                SelectedHistoryItem = null;
                PublishTopic = value?.FullTopic ?? string.Empty;
                _lastValueRefreshTimestamp = 0;
                _lastHistoryRefreshTimestamp = 0;
                RefreshSelectedTopic(keepCurrentSelection: false);
            }
        }
    }

    public HistoryItemViewModel? SelectedHistoryItem
    {
        get => _selectedHistoryItem;
        set
        {
            if (!SetProperty(ref _selectedHistoryItem, value))
            {
                return;
            }

            if (value is not null)
            {
                SelectedPayloadText = FormatPayloadForDetail(value.Message);
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyTopicFilter();
            }
        }
    }

    public string SelectedFolderPath
    {
        get => _selectedFolderPath;
        private set => SetProperty(ref _selectedFolderPath, value);
    }

    public string SelectedFolderName
    {
        get => _selectedFolderName;
        set
        {
            if (SetProperty(ref _selectedFolderName, value))
            {
                RenameFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedProfileFolderPath
    {
        get => _selectedProfileFolderPath;
        set
        {
            var normalized = ProfileTreeBuilder.NormalizeFolderPath(value);
            if (_isRefreshingFolderOptions
                && normalized.Length == 0
                && _selectedProfileFolderPath.Length > 0)
            {
                OnPropertyChanged();
                return;
            }

            if (!SetProperty(ref _selectedProfileFolderPath, normalized))
            {
                return;
            }

            if (SelectedProfile is not null)
            {
                SelectedProfile.FolderPath = normalized;
            }
        }
    }

    public bool CanRenameSelectedFolder => SelectedProfileNode?.IsFolder == true;

    public Visibility BrokerEditorVisibility => SelectedProfile is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility FolderEditorVisibility => SelectedProfileNode?.IsFolder == true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EmptyConnectionEditorVisibility => SelectedProfile is null && SelectedProfileNode?.IsFolder != true
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string SelectedFolderFullPath => SelectedProfileNode?.IsFolder == true
        ? SelectedProfileNode.FullPath
        : string.Empty;

    public string SelectedFolderParentPath
    {
        get
        {
            if (SelectedProfileNode?.IsFolder != true)
            {
                return string.Empty;
            }

            var parent = GetParentFolderPath(SelectedProfileNode.FullPath);
            return parent.Length == 0 ? "(root)" : parent;
        }
    }

    public string SelectedFolderSummaryText
    {
        get
        {
            if (SelectedProfileNode?.IsFolder != true)
            {
                return string.Empty;
            }

            var folderPath = ProfileTreeBuilder.NormalizeFolderPath(SelectedProfileNode.FullPath);
            var childPrefix = folderPath + "/";
            var brokerCount = Profiles.Count(profile =>
                ProfileTreeBuilder.NormalizeFolderPath(profile.FolderPath).Equals(folderPath, StringComparison.OrdinalIgnoreCase)
                || ProfileTreeBuilder.NormalizeFolderPath(profile.FolderPath).StartsWith(childPrefix, StringComparison.OrdinalIgnoreCase));
            var folderCount = _profileFolderPaths.Count(path =>
            {
                var normalized = ProfileTreeBuilder.NormalizeFolderPath(path);
                return !normalized.Equals(folderPath, StringComparison.OrdinalIgnoreCase)
                       && normalized.StartsWith(childPrefix, StringComparison.OrdinalIgnoreCase);
            });

            return $"{brokerCount} brokers, {folderCount} subfolders";
        }
    }

    public string ValuePayloadText
    {
        get => _valuePayloadText;
        private set
        {
            if (SetProperty(ref _valuePayloadText, value))
            {
                CopyValueCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedPayloadText
    {
        get => _selectedPayloadText;
        private set
        {
            if (SetProperty(ref _selectedPayloadText, value))
            {
                CopySelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedTopicAveragePeriodText
    {
        get => _selectedTopicAveragePeriodText;
        private set => SetProperty(ref _selectedTopicAveragePeriodText, value);
    }

    public string PublishTopic
    {
        get => _publishTopic;
        set
        {
            if (SetProperty(ref _publishTopic, value))
            {
                RefreshPublishTopicSuggestions();
                PublishCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PublishPayload
    {
        get => _publishPayload;
        set => SetProperty(ref _publishPayload, value);
    }

    public int PublishQos
    {
        get => _publishQos;
        set => SetProperty(ref _publishQos, value);
    }

    public bool PublishRetain
    {
        get => _publishRetain;
        set => SetProperty(ref _publishRetain, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(SelectedProfileCaption));
                OnPropertyChanged(nameof(ConnectionButtonText));
                RaiseCommandStates();
            }
        }
    }

    public string ConnectionButtonText => IsConnected ? "Disconnect" : "Connect";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsConnectionManagerOpen
    {
        get => _isConnectionManagerOpen;
        set
        {
            if (SetProperty(ref _isConnectionManagerOpen, value))
            {
                OnPropertyChanged(nameof(ConnectionManagerVisibility));
            }
        }
    }

    public Visibility ConnectionManagerVisibility => IsConnectionManagerOpen
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool IsPeriodCheckOpen
    {
        get => _isPeriodCheckOpen;
        set
        {
            if (SetProperty(ref _isPeriodCheckOpen, value))
            {
                OnPropertyChanged(nameof(PeriodCheckVisibility));
            }
        }
    }

    public Visibility PeriodCheckVisibility => IsPeriodCheckOpen
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool IsPeriodCheckRunning
    {
        get => _isPeriodCheckRunning;
        private set
        {
            if (SetProperty(ref _isPeriodCheckRunning, value))
            {
                OnPropertyChanged(nameof(PeriodCheckRunningText));
                OnPropertyChanged(nameof(IsPeriodCheckIdle));
                RaiseCommandStates();
            }
        }
    }

    public string PeriodCheckRunningText => IsPeriodCheckRunning ? "측정 중" : "대기";

    public bool IsPeriodCheckIdle => !IsPeriodCheckRunning;

    public string PeriodCheckTopicText
    {
        get => _periodCheckTopicText;
        set
        {
            if (SetProperty(ref _periodCheckTopicText, value))
            {
                RefreshPeriodCheckTopicSuggestions();
                StartPeriodCheckCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PeriodCheckDurationText
    {
        get => _periodCheckDurationText;
        set
        {
            if (SetProperty(ref _periodCheckDurationText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(PeriodCheckDurationSummaryText));
                StartPeriodCheckCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PeriodCheckDurationSummaryText => TryGetPeriodCheckDuration(out var seconds)
        ? $"{FormatDurationSeconds(seconds)} 측정"
        : "1초~60분 입력";

    public PeriodCheckHistoryItemViewModel? SelectedPeriodCheckHistoryItem
    {
        get => _selectedPeriodCheckHistoryItem;
        set
        {
            if (SetProperty(ref _selectedPeriodCheckHistoryItem, value)
                && value is not null
                && !IsPeriodCheckRunning)
            {
                PeriodCheckResultText = value.ResultText;
            }
        }
    }

    public string PeriodCheckStatus
    {
        get => _periodCheckStatus;
        private set => SetProperty(ref _periodCheckStatus, value);
    }

    public string PeriodCheckResultText
    {
        get => _periodCheckResultText;
        private set => SetProperty(ref _periodCheckResultText, value);
    }

    public bool IsJsonFormatterOpen
    {
        get => _isJsonFormatterOpen;
        private set
        {
            if (SetProperty(ref _isJsonFormatterOpen, value))
            {
                OnPropertyChanged(nameof(JsonFormatterVisibility));
            }
        }
    }

    public Visibility JsonFormatterVisibility => IsJsonFormatterOpen
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string JsonFormatterInput
    {
        get => _jsonFormatterInput;
        set => SetProperty(ref _jsonFormatterInput, value ?? string.Empty);
    }

    public string JsonFormatterOutput
    {
        get => _jsonFormatterOutput;
        private set
        {
            if (SetProperty(ref _jsonFormatterOutput, value))
            {
                CopyJsonFormatterCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string JsonFormatterStatus
    {
        get => _jsonFormatterStatus;
        private set => SetProperty(ref _jsonFormatterStatus, value);
    }

    public bool HistoryPaused
    {
        get => _historyPaused;
        set
        {
            if (SetProperty(ref _historyPaused, value))
            {
                OnPropertyChanged(nameof(HistoryPauseButtonText));
            }
        }
    }

    public string HistoryPauseButtonText => HistoryPaused ? "Resume" : "Pause";

    public bool FreezeDetail
    {
        get => _freezeDetail;
        set => SetProperty(ref _freezeDetail, value);
    }

    public bool FollowLatest
    {
        get => _followLatest;
        set => SetProperty(ref _followLatest, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public long ReceivedMessages
    {
        get => _receivedMessages;
        private set => SetProperty(ref _receivedMessages, value);
    }

    public int PendingCount
    {
        get => _pendingCount;
        private set => SetProperty(ref _pendingCount, value);
    }

    public int TopicCount => _leafTopicsByFullName.Count;

    public void Dispose()
    {
        _isDisposed = true;
        Interlocked.Exchange(ref _pendingValueFormatMessage, null);
        _periodCheckCancellation?.Cancel();
        _flushTimer.Stop();
        _mqttClient.Dispose();
    }

    private void RefreshConnectionEditorState()
    {
        OnPropertyChanged(nameof(CanRenameSelectedFolder));
        OnPropertyChanged(nameof(BrokerEditorVisibility));
        OnPropertyChanged(nameof(FolderEditorVisibility));
        OnPropertyChanged(nameof(EmptyConnectionEditorVisibility));
        OnPropertyChanged(nameof(SelectedFolderFullPath));
        OnPropertyChanged(nameof(SelectedFolderParentPath));
        OnPropertyChanged(nameof(SelectedFolderSummaryText));
        RenameFolderCommand.RaiseCanExecuteChanged();
        DeleteFolderCommand.RaiseCanExecuteChanged();
        DeleteProfileCommand.RaiseCanExecuteChanged();
    }

    public void MoveProfileNode(ProfileTreeNodeViewModel source, ProfileTreeNodeViewModel? target)
    {
        if (source.IsFolder)
        {
            MoveFolderNode(source, target);
            return;
        }

        if (source.Profile is null)
        {
            return;
        }

        var targetFolderPath = GetDropTargetFolderPath(target);
        var normalizedTarget = ProfileTreeBuilder.NormalizeFolderPath(targetFolderPath);
        if (ProfileTreeBuilder.NormalizeFolderPath(source.Profile.FolderPath).Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Broker is already in that folder.";
            return;
        }

        source.Profile.FolderPath = normalizedTarget;
        if (normalizedTarget.Length > 0)
        {
            _profileFolderPaths.Add(normalizedTarget);
        }

        SelectedProfile = source.Profile;
        SaveProfiles(rebuildTree: true);
        StatusMessage = normalizedTarget.Length == 0
            ? $"Broker moved to root: {source.Profile.Name}"
            : $"Broker moved to {normalizedTarget}: {source.Profile.Name}";
    }

    private void MoveFolderNode(ProfileTreeNodeViewModel source, ProfileTreeNodeViewModel? target)
    {
        var oldPath = ProfileTreeBuilder.NormalizeFolderPath(source.FullPath);
        var targetFolderPath = GetDropTargetFolderPath(target);
        var normalizedTarget = ProfileTreeBuilder.NormalizeFolderPath(targetFolderPath);
        if (oldPath.Length == 0)
        {
            return;
        }

        if (normalizedTarget.Equals(oldPath, StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Cannot move a folder into itself.";
            return;
        }

        var folderName = GetFolderName(oldPath);
        var newPath = normalizedTarget.Length == 0 ? folderName : $"{normalizedTarget}/{folderName}";
        newPath = ProfileTreeBuilder.NormalizeFolderPath(newPath);
        if (newPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "Folder is already in that location.";
            return;
        }

        if (FolderPathExistsOutside(oldPath, newPath))
        {
            StatusMessage = $"Folder already exists: {newPath}";
            return;
        }

        RenameFolderPath(oldPath, newPath);
        SaveProfiles(rebuildTree: true);
        SelectedProfileNode = FindFolderNode(ProfileTree, newPath);
        StatusMessage = $"Folder moved: {oldPath} -> {newPath}";
    }

    private static string GetDropTargetFolderPath(ProfileTreeNodeViewModel? target)
    {
        if (target is null)
        {
            return string.Empty;
        }

        if (target.IsFolder)
        {
            return target.FullPath;
        }

        return target.Profile?.FolderPath ?? string.Empty;
    }
    private async Task ConnectAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            CommitSelectedProfileFolderPath();
            var profileToConnect = SelectedProfile;
            SaveProfiles(rebuildTree: false);
            ClearTopics();
            StatusMessage = $"Connecting to {profileToConnect.Host}:{profileToConnect.Port}...";
            await _mqttClient.ConnectAsync(profileToConnect, CancellationToken.None);
            _connectedProfile = profileToConnect.Clone();
            IsConnected = true;
            OnPropertyChanged(nameof(SelectedProfileCaption));
            EnsureBrokerRoot();
            IsConnectionManagerOpen = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connect failed: {ex.Message}";
            _connectedProfile = null;
            IsConnected = false;
            OnPropertyChanged(nameof(SelectedProfileCaption));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ToggleConnectionAsync()
    {
        return IsConnected ? DisconnectAsync() : ConnectAsync();
    }

    private async Task DisconnectAsync()
    {
        try
        {
            IsBusy = true;
            await _mqttClient.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Disconnect failed: {ex.Message}";
        }
        finally
        {
            _connectedProfile = null;
            IsConnected = false;
            OnPropertyChanged(nameof(SelectedProfileCaption));
            IsBusy = false;
        }
    }

    private async Task PublishAsync()
    {
        try
        {
            await _mqttClient.PublishAsync(PublishTopic, PublishPayload, PublishQos, PublishRetain, CancellationToken.None);
            StatusMessage = $"Published: {PublishTopic}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Publish failed: {ex.Message}";
        }
    }

    private void FormatPublishJson()
    {
        var formatted = PayloadFormatter.Format(PublishPayload, previewLimit: 200);
        if (!formatted.IsJson)
        {
            StatusMessage = "Publish payload is not valid JSON";
            return;
        }

        PublishPayload = formatted.DisplayText;
        StatusMessage = "Publish JSON formatted";
    }

    private void CopyValueToClipboard()
    {
        if (string.IsNullOrEmpty(ValuePayloadText))
        {
            return;
        }

        Clipboard.SetText(ValuePayloadText);
        StatusMessage = "Value copied";
    }

    private void CopySelectedToClipboard()
    {
        if (string.IsNullOrEmpty(SelectedPayloadText))
        {
            return;
        }

        Clipboard.SetText(SelectedPayloadText);
        StatusMessage = "Selected copied";
    }

    private void ToggleHistoryPause()
    {
        HistoryPaused = !HistoryPaused;
        if (!HistoryPaused)
        {
            RefreshSelectedTopic(keepCurrentSelection: true);
        }
    }

    private void OpenJsonFormatter()
    {
        IsJsonFormatterOpen = true;
    }

    private void CloseJsonFormatter()
    {
        IsJsonFormatterOpen = false;
    }

    private void FormatJsonFormatter(bool indented)
    {
        if (JsonTextFormatter.TryFormat(
                JsonFormatterInput,
                indented,
                out var formatted,
                out var error))
        {
            JsonFormatterInput = formatted;
            JsonFormatterOutput = formatted;
            BuildJsonFormatterTree(formatted);
            JsonFormatterStatus = indented
                ? "JSON 형식을 정리했습니다."
                : "JSON을 한 줄로 압축했습니다.";
            return;
        }

        JsonFormatterOutput = string.Empty;
        JsonFormatterTreeRoots.Clear();
        JsonFormatterStatus = error;
    }

    private void ValidateJsonFormatter()
    {
        if (JsonTextFormatter.TryFormat(
                JsonFormatterInput,
                indented: false,
                out var compact,
                out var error))
        {
            JsonFormatterOutput = compact;
            BuildJsonFormatterTree(compact);
            JsonFormatterStatus = "유효한 JSON입니다.";
            return;
        }

        JsonFormatterOutput = string.Empty;
        JsonFormatterTreeRoots.Clear();
        JsonFormatterStatus = error;
    }

    private void CopyJsonFormatter()
    {
        if (string.IsNullOrEmpty(JsonFormatterOutput))
        {
            return;
        }

        Clipboard.SetText(JsonFormatterOutput);
        JsonFormatterStatus = "결과를 클립보드에 복사했습니다.";
    }

    private void ClearJsonFormatter()
    {
        JsonFormatterInput = string.Empty;
        JsonFormatterOutput = string.Empty;
        JsonFormatterTreeRoots.Clear();
        JsonFormatterStatus = "왼쪽에 JSON을 붙여 넣고 Format을 누르세요.";
    }

    private void BuildJsonFormatterTree(string json)
    {
        JsonFormatterTreeRoots.Clear();
        if (JsonStructureBuilder.TryBuild(json, out var root, out _)
            && root is not null)
        {
            JsonFormatterTreeRoots.Add(new JsonTreeNodeViewModel(root, isRoot: true));
        }
    }

    private Task OpenConnectionManagerAsync()
    {
        RebuildProfileTree();
        RestoreProfileTreeSelection(_connectedProfile?.Id ?? SelectedProfile?.Id, null);
        IsConnectionManagerOpen = true;
        return Task.CompletedTask;
    }

    private void CloseConnectionManager()
    {
        CommitSelectedProfileFolderPath();
        IsConnectionManagerOpen = false;
    }

    private void OpenPeriodCheck()
    {
        if (string.IsNullOrWhiteSpace(PeriodCheckTopicText) && SelectedTopic?.IsLeafTopic == true)
        {
            PeriodCheckTopicText = SelectedTopic.FullTopic;
        }

        RefreshPeriodCheckTopicSuggestions();
        PeriodCheckStatus = _leafTopicsByFullName.Count == 0
            ? "직접 토픽을 입력해 측정할 수 있습니다."
            : "토픽을 입력하거나 기존 토픽에서 선택하세요.";
        IsPeriodCheckOpen = true;
    }

    private void ClosePeriodCheck()
    {
        if (IsPeriodCheckRunning)
        {
            return;
        }

        IsPeriodCheckOpen = false;
    }

    private void StartPeriodCheck()
    {
        var topic = PeriodCheckTopicText.Trim();
        if (topic.Length == 0 || !TryGetPeriodCheckDuration(out var durationSeconds))
        {
            return;
        }

        PeriodCheckTopicText = topic;
        _ = StartPeriodCheckAsync(topic, durationSeconds);
    }

    private void StopPeriodCheck()
    {
        if (!IsPeriodCheckRunning)
        {
            return;
        }

        PeriodCheckStatus = "측정 중단 요청 중...";
        _periodCheckCancellation?.Cancel();
    }

    private void AddProfile()
    {
        var profile = BrokerProfile.CreateDefault();
        profile.Name = $"Broker {Profiles.Count + 1}";
        profile.FolderPath = GetTargetProfileFolderPath();
        Profiles.Add(profile);
        SelectedProfile = profile;
        SaveProfiles(rebuildTree: true);
    }

    private void AddFolder()
    {
        var parentPath = GetTargetProfileFolderPath();
        var folderPath = BuildUniqueFolderPath(parentPath);
        _profileFolderPaths.Add(folderPath);
        SelectedFolderPath = folderPath;
        SelectedFolderName = GetFolderName(folderPath);
        SaveProfiles(rebuildTree: false);
        RebuildProfileTree();
        SelectedProfileNode = FindFolderNode(ProfileTree, folderPath);
        StatusMessage = $"Folder added: {folderPath}";
    }

    private void RenameFolder()
    {
        if (SelectedProfileNode?.IsFolder != true)
        {
            return;
        }

        var oldPath = ProfileTreeBuilder.NormalizeFolderPath(SelectedProfileNode.FullPath);
        var newName = SelectedFolderName.Trim();
        if (newName.Length == 0)
        {
            StatusMessage = "Folder name cannot be empty.";
            return;
        }

        if (newName.Contains('/') || newName.Contains('\\'))
        {
            StatusMessage = "Folder name cannot contain path separators.";
            return;
        }

        var parentPath = GetParentFolderPath(oldPath);
        var newPath = parentPath.Length == 0 ? newName : $"{parentPath}/{newName}";
        newPath = ProfileTreeBuilder.NormalizeFolderPath(newPath);
        if (newPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
        {
            SelectedFolderName = GetFolderName(oldPath);
            return;
        }

        if (FolderPathExistsOutside(oldPath, newPath))
        {
            StatusMessage = $"Folder already exists: {newPath}";
            SelectedFolderName = GetFolderName(oldPath);
            return;
        }

        RenameFolderPath(oldPath, newPath);
        SaveProfiles(rebuildTree: true);
        SelectedFolderPath = newPath;
        SelectedFolderName = GetFolderName(newPath);
        SelectedProfileNode = FindFolderNode(ProfileTree, newPath);
        StatusMessage = $"Folder renamed: {oldPath} -> {newPath}";
    }

    private void DeleteFolder()
    {
        if (SelectedProfileNode?.IsFolder != true)
        {
            return;
        }

        var folderPath = SelectedProfileNode.FullPath;
        if (FolderHasChildren(folderPath))
        {
            StatusMessage = "Folder is not empty. Move profiles or subfolders first.";
            return;
        }

        _profileFolderPaths.Remove(folderPath);
        SelectedProfileNode = null;
        SaveProfiles(rebuildTree: true);
        StatusMessage = $"Folder removed: {folderPath}";
    }

    private void SaveProfiles()
    {
        SaveProfiles(rebuildTree: true);
    }

    private void SaveProfiles(bool rebuildTree)
    {
        var selectedProfileId = SelectedProfile?.Id;
        var selectedFolderPath = SelectedProfileNode?.IsFolder == true
            ? SelectedProfileNode.FullPath
            : null;

        CommitSelectedProfileFolderPath();

        foreach (var profile in Profiles)
        {
            profile.FolderPath = ProfileTreeBuilder.NormalizeFolderPath(profile.FolderPath);
            if (profile.FolderPath.Length > 0)
            {
                _profileFolderPaths.Add(profile.FolderPath);
            }
        }

        _profileStore.Save(Profiles, _profileFolderPaths);
        if (rebuildTree)
        {
            RebuildProfileTree();
            RestoreProfileTreeSelection(selectedProfileId, selectedFolderPath);
        }

        StatusMessage = "Profiles saved";
    }

    private void CommitSelectedProfileFolderPath()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var normalized = ProfileTreeBuilder.NormalizeFolderPath(SelectedProfileFolderPath);
        SelectedProfile.FolderPath = normalized;
        if (!SelectedProfileFolderPath.Equals(normalized, StringComparison.Ordinal))
        {
            _selectedProfileFolderPath = normalized;
            OnPropertyChanged(nameof(SelectedProfileFolderPath));
        }
    }

    private void RestoreProfileTreeSelection(string? selectedProfileId, string? selectedFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(selectedProfileId))
        {
            var profileNode = FindProfileNode(ProfileTree, selectedProfileId);
            if (profileNode is not null)
            {
                SelectedProfileNode = profileNode;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedFolderPath))
        {
            SelectedProfileNode = FindFolderNode(ProfileTree, selectedFolderPath);
        }
    }

    private void RebuildProfileTree()
    {
        ProfileTree.Clear();
        foreach (var node in ProfileTreeBuilder.Build(Profiles, _profileFolderPaths))
        {
            ProfileTree.Add(node);
        }

        RefreshFolderOptions();
    }

    private void RefreshFolderOptions()
    {
        var current = ProfileTreeBuilder.NormalizeFolderPath(SelectedProfile?.FolderPath ?? SelectedFolderPath);
        _isRefreshingFolderOptions = true;
        try
        {
            FolderOptions.Clear();
            FolderOptions.Add(string.Empty);

            foreach (var folderPath in _profileFolderPaths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (folderPath.Length > 0 && !FolderOptions.Contains(folderPath))
                {
                    FolderOptions.Add(folderPath);
                }
            }

            if (current.Length > 0 && !FolderOptions.Contains(current))
            {
                FolderOptions.Add(current);
            }
        }
        finally
        {
            _isRefreshingFolderOptions = false;
        }

        if (SelectedProfile is not null)
        {
            var normalized = ProfileTreeBuilder.NormalizeFolderPath(SelectedProfile.FolderPath);
            if (_selectedProfileFolderPath.Equals(normalized, StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(SelectedProfileFolderPath));
            }
            else
            {
                SelectedProfileFolderPath = normalized;
            }
        }
    }

    private string GetTargetProfileFolderPath()
    {
        if (SelectedProfileNode?.IsFolder == true)
        {
            return SelectedProfileNode.FullPath;
        }

        if (SelectedProfile is not null)
        {
            return ProfileTreeBuilder.NormalizeFolderPath(SelectedProfile.FolderPath);
        }

        return ProfileTreeBuilder.NormalizeFolderPath(SelectedFolderPath);
    }

    private void RenameFolderPath(string oldPath, string newPath)
    {
        var oldPrefix = oldPath + "/";
        var updatedFolders = _profileFolderPaths
            .Select(path => RemapFolderPath(path, oldPath, oldPrefix, newPath))
            .ToArray();

        _profileFolderPaths.Clear();
        foreach (var folderPath in updatedFolders.Where(x => x.Length > 0))
        {
            _profileFolderPaths.Add(folderPath);
        }

        foreach (var profile in Profiles)
        {
            profile.FolderPath = RemapFolderPath(
                ProfileTreeBuilder.NormalizeFolderPath(profile.FolderPath),
                oldPath,
                oldPrefix,
                newPath);
        }
    }

    private static string RemapFolderPath(string path, string oldPath, string oldPrefix, string newPath)
    {
        var normalized = ProfileTreeBuilder.NormalizeFolderPath(path);
        if (normalized.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
        {
            return newPath;
        }

        if (normalized.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return newPath + normalized[oldPath.Length..];
        }

        return normalized;
    }

    private bool FolderPathExistsOutside(string oldPath, string newPath)
    {
        var oldPrefix = oldPath + "/";
        return _profileFolderPaths.Any(path => IsConflictingFolderPath(path, oldPath, oldPrefix, newPath))
               || Profiles.Any(profile => IsConflictingFolderPath(profile.FolderPath, oldPath, oldPrefix, newPath));
    }

    private static bool IsConflictingFolderPath(string path, string oldPath, string oldPrefix, string newPath)
    {
        var normalized = ProfileTreeBuilder.NormalizeFolderPath(path);
        if (normalized.Length == 0
            || normalized.Equals(oldPath, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalized.Equals(newPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetParentFolderPath(string folderPath)
    {
        var normalized = ProfileTreeBuilder.NormalizeFolderPath(folderPath);
        var index = normalized.LastIndexOf('/');
        return index < 0 ? string.Empty : normalized[..index];
    }

    private static string GetFolderName(string folderPath)
    {
        var normalized = ProfileTreeBuilder.NormalizeFolderPath(folderPath);
        var index = normalized.LastIndexOf('/');
        return index < 0 ? normalized : normalized[(index + 1)..];
    }

    private static ProfileTreeNodeViewModel? FindFolderNode(IEnumerable<ProfileTreeNodeViewModel> nodes, string folderPath)
    {
        foreach (var node in nodes)
        {
            if (node.IsFolder && node.FullPath.Equals(folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var match = FindFolderNode(node.Children, folderPath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static ProfileTreeNodeViewModel? FindProfileNode(IEnumerable<ProfileTreeNodeViewModel> nodes, string profileId)
    {
        foreach (var node in nodes)
        {
            if (node.Profile?.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase) == true)
            {
                return node;
            }

            var match = FindProfileNode(node.Children, profileId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private string BuildUniqueFolderPath(string parentPath)
    {
        var baseName = "New folder";
        var candidate = parentPath.Length == 0 ? baseName : $"{parentPath}/{baseName}";
        var suffix = 2;

        while (_profileFolderPaths.Contains(candidate))
        {
            candidate = parentPath.Length == 0 ? $"{baseName} {suffix}" : $"{parentPath}/{baseName} {suffix}";
            suffix++;
        }

        return candidate;
    }

    private bool FolderHasChildren(string folderPath)
    {
        var normalized = ProfileTreeBuilder.NormalizeFolderPath(folderPath);
        var childPrefix = normalized + "/";

        return Profiles.Any(x =>
                ProfileTreeBuilder.NormalizeFolderPath(x.FolderPath).Equals(normalized, StringComparison.OrdinalIgnoreCase)
                || ProfileTreeBuilder.NormalizeFolderPath(x.FolderPath).StartsWith(childPrefix, StringComparison.OrdinalIgnoreCase))
            || _profileFolderPaths.Any(x =>
                !x.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                && x.StartsWith(childPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private void DeleteProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1)
        {
            return;
        }

        var index = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[Math.Clamp(index - 1, 0, Profiles.Count - 1)];
        SaveProfiles();
    }

    private void ClearTopics()
    {
        ClearPendingMessages();

        _rootTopicsByName.Clear();
        _leafTopicsByFullName.Clear();
        _dirtyTopicNodes.Clear();
        _topicListsNeedRefresh = false;
        _lastTopicVisualRefreshTimestamp = 0;
        PeriodCheckTopicSuggestions.Clear();
        PublishTopicSuggestions.Clear();
        PeriodCheckTopicText = string.Empty;
        _brokerTopicRoot = null;
        RootTopics.Clear();
        SelectedTopicHistory.Clear();
        SelectedTopic = null;
        ValuePayloadText = string.Empty;
        SelectedPayloadText = string.Empty;
        SelectedTopicAveragePeriodText = "Avg -";
        ReceivedMessages = 0;
        PendingCount = 0;
        OnPropertyChanged(nameof(TopicCount));
    }

    private void OnMessageReceived(MqttMessageSnapshot message)
    {
        if (IsPeriodCheckRunning)
        {
            if (message.Topic.Equals(_periodCheckTargetTopic, StringComparison.Ordinal))
            {
                _periodCheckSamples.Enqueue(message.ReceivedStopwatchTimestamp);
            }

            return;
        }

        _pendingMessages.Enqueue(message);
        Interlocked.Increment(ref _pendingQueueCount);
        Interlocked.Add(ref _pendingPayloadCharacters, message.PayloadLength);
        TrimPendingMessages();
    }

    private void OnStatusChanged(string status)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            StatusMessage = status;
            IsConnected = _mqttClient.IsConnected;
        });
    }

    private void DrainPendingMessages()
    {
        if (_pendingMessages.IsEmpty)
        {
            FlushTopicVisualChanges();
            return;
        }

        var selectedTopicWasTouched = false;
        var processed = 0;
        var drainStarted = Stopwatch.GetTimestamp();

        // Keep each UI batch short so counters and input stay responsive under sustained traffic.
        while (processed < MaxMessagesPerUiTick
               && (processed == 0 || Stopwatch.GetTimestamp() - drainStarted < UiDrainBudgetTicks)
               && _pendingMessages.TryDequeue(out var message))
        {
            Interlocked.Decrement(ref _pendingQueueCount);
            Interlocked.Add(ref _pendingPayloadCharacters, -message.PayloadLength);
            var leafTopic = IngestIntoTree(message, _dirtyTopicNodes, out var leafTopicWasNew);
            selectedTopicWasTouched |= ReferenceEquals(leafTopic, SelectedTopic);
            _topicListsNeedRefresh |= leafTopicWasNew;
            processed++;
        }

        FlushTopicVisualChanges();

        ReceivedMessages += processed;
        PendingCount = Math.Max(0, Volatile.Read(ref _pendingQueueCount));

        if (!FreezeDetail && !HistoryPaused && selectedTopicWasTouched)
        {
            if (ShouldRefreshValue())
            {
                QueueSelectedTopicValueRefresh();
            }

            if (ShouldRefreshHistory())
            {
                RefreshSelectedTopicHistory(keepCurrentSelection: true);
            }

        }
    }

    public TopicViewModel? FindLeafTopic(string fullTopic)
    {
        return _leafTopicsByFullName.GetValueOrDefault(fullTopic)
               ?? (SelectedTopic?.FullTopic.Equals(fullTopic, StringComparison.Ordinal) == true
                   ? SelectedTopic
                   : null);
    }

    private void FlushTopicVisualChanges(bool force = false)
    {
        if (_dirtyTopicNodes.Count == 0)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (!force && now - _lastTopicVisualRefreshTimestamp < TopicVisualRefreshMinTicks)
        {
            return;
        }

        foreach (var topic in _dirtyTopicNodes)
        {
            topic.NotifyRecordChanged();
        }

        _dirtyTopicNodes.Clear();
        _lastTopicVisualRefreshTimestamp = now;

        if (_topicListsNeedRefresh)
        {
            _topicListsNeedRefresh = false;
            OnPropertyChanged(nameof(TopicCount));
            ApplyTopicFilter();
            RefreshPublishTopicSuggestions();
            RefreshPeriodCheckTopicSuggestions();
        }
    }

    private bool ShouldRefreshValue()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _lastValueRefreshTimestamp < ValueRefreshMinTicks)
        {
            return false;
        }

        _lastValueRefreshTimestamp = now;
        return true;
    }

    private bool ShouldRefreshHistory()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _lastHistoryRefreshTimestamp < HistoryRefreshMinTicks)
        {
            return false;
        }

        _lastHistoryRefreshTimestamp = now;
        return true;
    }

    private TopicViewModel IngestIntoTree(
        MqttMessageSnapshot message,
        ISet<TopicViewModel> changedTopics,
        out bool leafTopicWasNew)
    {
        var activeProfile = _connectedProfile ?? SelectedProfile;
        var segments = message.Topic.Split('/', StringSplitOptions.None);
        var current = EnsureBrokerRoot();
        var path = string.Empty;
        leafTopicWasNew = !_leafTopicsByFullName.ContainsKey(message.Topic);

        current.Record(message, isLeaf: false, leafTopicWasNew, notify: false);
        changedTopics.Add(current);

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            path = path.Length == 0 ? segment : $"{path}/{segment}";
            var isLeaf = i == segments.Length - 1;

            current = current.GetOrCreateChild(segment, path, activeProfile?.MaxHistoryPerTopic ?? 300);
            current.Record(message, isLeaf, leafTopicWasNew, notify: false);
            changedTopics.Add(current);

            if (isLeaf)
            {
                _leafTopicsByFullName[path] = current;
            }
        }

        return current!;
    }

    private TopicViewModel EnsureBrokerRoot()
    {
        if (_brokerTopicRoot is not null)
        {
            return _brokerTopicRoot;
        }

        var activeProfile = _connectedProfile ?? SelectedProfile;
        var rootName = activeProfile is null
            ? "broker"
            : activeProfile.Host.Trim();
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = activeProfile?.Name ?? "broker";
        }

        _brokerTopicRoot = new TopicViewModel(rootName, string.Empty, activeProfile?.MaxHistoryPerTopic ?? 300);
        RootTopics.Add(_brokerTopicRoot);
        return _brokerTopicRoot;
    }

    private void ApplyTopicFilter()
    {
        foreach (var root in RootTopics)
        {
            root.ApplySearch(SearchText);
        }
    }

    private void RefreshPublishTopicSuggestions()
    {
        var query = PublishTopic.Trim();
        var matches = _leafTopicsByFullName.Keys
            .Where(x => query.Length == 0 || x.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => query.Length > 0 && x.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(SearchResultLimit)
            .ToArray();

        PublishTopicSuggestions.Clear();
        foreach (var topic in matches)
        {
            PublishTopicSuggestions.Add(topic);
        }
    }

    private async Task StartPeriodCheckAsync(string topic, int durationSeconds)
    {
        var profile = (_connectedProfile ?? SelectedProfile)?.Clone();
        if (profile is null || !IsConnected)
        {
            PeriodCheckResultText = "브로커에 연결된 상태에서만 주기 체크를 실행할 수 있습니다.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        _periodCheckCancellation = cancellation;
        _periodCheckTargetTopic = topic;
        IsPeriodCheckRunning = true;
        PeriodCheckResultText = string.Empty;
        PeriodCheckStatus = $"준비 중: {topic}";
        ClearPendingMessages();
        ClearPeriodCheckSamples();
        _flushTimer.Stop();
        var historySummary = "측정 실패";
        var finalStatus = "측정 실패";
        var measurementStartedAt = 0L;
        var hasMeasurementResult = false;
        var wasStopped = false;

        try
        {
            await _mqttClient.ReplaceSubscriptionAsync(topic, profile.SubscribeQos, cancellation.Token);
            ClearPeriodCheckSamples();
            measurementStartedAt = Stopwatch.GetTimestamp();

            for (var remaining = durationSeconds; remaining > 0; remaining--)
            {
                PeriodCheckStatus = $"측정 중: {topic} ({remaining}초 남음)";
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            }

            hasMeasurementResult = true;
            finalStatus = "측정 완료";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            hasMeasurementResult = true;
            wasStopped = true;
            finalStatus = "측정 중단";
        }
        catch (Exception ex)
        {
            PeriodCheckResultText = $"측정 실패: {ex.Message}";
        }
        finally
        {
            if (hasMeasurementResult)
            {
                var elapsedMilliseconds = measurementStartedAt == 0
                    ? 0
                    : Stopwatch.GetElapsedTime(measurementStartedAt).TotalMilliseconds;
                var samples = _periodCheckSamples.ToArray();
                var statistics = MessagePeriodStatistics.CalculateStopwatchTicks(samples, Stopwatch.Frequency);
                PeriodCheckResultText = BuildPeriodCheckResult(
                    topic,
                    statistics,
                    durationSeconds,
                    elapsedMilliseconds,
                    wasStopped);
                historySummary = BuildPeriodCheckSummary(statistics, wasStopped);
            }

            PeriodCheckStatus = $"{finalStatus}. 원래 구독으로 복원 중...";
            try
            {
                if (_mqttClient.IsConnected)
                {
                    await _mqttClient.RestoreProfileSubscriptionAsync(profile, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                PeriodCheckResultText = AppendResultLine(PeriodCheckResultText, $"원래 구독 복원 실패: {ex.Message}");
            }

            if (ReferenceEquals(_periodCheckCancellation, cancellation))
            {
                _periodCheckCancellation = null;
            }

            cancellation.Dispose();
            _periodCheckTargetTopic = string.Empty;
            IsPeriodCheckRunning = false;
            _flushTimer.Start();
            PendingCount = Math.Max(0, Volatile.Read(ref _pendingQueueCount));
            PeriodCheckStatus = finalStatus;
            AddPeriodCheckHistory(topic, historySummary, PeriodCheckResultText);
            RefreshPeriodCheckTopicSuggestions();
        }
    }

    private void RefreshPeriodCheckTopicSuggestions()
    {
        var query = PeriodCheckTopicText.Trim();
        var matches = _leafTopicsByFullName.Keys
            .Where(x => query.Length == 0 || x.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(SearchResultLimit)
            .ToArray();

        PeriodCheckTopicSuggestions.Clear();
        foreach (var topic in matches)
        {
            PeriodCheckTopicSuggestions.Add(topic);
        }
    }

    private void AddPeriodCheckHistory(string topic, string summary, string resultText)
    {
        var item = new PeriodCheckHistoryItemViewModel(DateTime.Now, topic, summary, resultText);
        PeriodCheckHistory.Insert(0, item);
        while (PeriodCheckHistory.Count > PeriodCheckHistoryLimit)
        {
            PeriodCheckHistory.RemoveAt(PeriodCheckHistory.Count - 1);
        }

        SelectedPeriodCheckHistoryItem = item;
    }

    private void RefreshSelectedTopic(bool keepCurrentSelection)
    {
        RefreshSelectedTopicHistory(keepCurrentSelection);
        RefreshSelectedTopicValue();
    }

    private void RefreshSelectedTopicHistory(bool keepCurrentSelection)
    {
        var previousSelectedMessage = keepCurrentSelection ? SelectedHistoryItem?.Message : null;
        SelectedTopicHistory.Clear();

        if (SelectedTopic is null)
        {
            ValuePayloadText = string.Empty;
            SelectedHistoryItem = null;
            SelectedPayloadText = string.Empty;
            SelectedTopicAveragePeriodText = "Avg -";
            return;
        }

        MqttMessageSnapshot? previousNewer = null;
        var history = SelectedTopic.HistoryNewestFirst(HistoryDisplayLimit);
        foreach (var message in history)
        {
            SelectedTopicHistory.Add(new HistoryItemViewModel(message, previousNewer));
            previousNewer = message;
        }

        SelectedTopicAveragePeriodText = FormatAveragePeriod(history);

        if (previousSelectedMessage is not null)
        {
            var previousSelection = SelectedTopicHistory.FirstOrDefault(x => ReferenceEquals(x.Message, previousSelectedMessage));
            if (previousSelection is not null)
            {
                SelectedHistoryItem = previousSelection;
                return;
            }
        }

        if (!keepCurrentSelection)
        {
            SelectedHistoryItem = null;
            SelectedPayloadText = string.Empty;
        }
    }

    private void ShowValuePayload(MqttMessageSnapshot message)
    {
        ValuePayloadText = FormatPayloadForDetail(message);
    }

    private void RefreshSelectedTopicValue()
    {
        if (SelectedTopic?.LastMessage is { } message)
        {
            ShowValuePayload(message);
            return;
        }

        ValuePayloadText = string.Empty;
    }

    private void QueueSelectedTopicValueRefresh()
    {
        if (SelectedTopic?.LastMessage is not { } message)
        {
            ValuePayloadText = string.Empty;
            return;
        }

        Interlocked.Exchange(ref _pendingValueFormatMessage, message);
        StartValueFormatWorker();
    }

    private void StartValueFormatWorker()
    {
        if (_isDisposed || Interlocked.CompareExchange(ref _valueFormatWorkerRunning, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(ProcessPendingValueFormatsAsync);
    }

    private async Task ProcessPendingValueFormatsAsync()
    {
        try
        {
            while (!_isDisposed
                   && Interlocked.Exchange(ref _pendingValueFormatMessage, null) is { } message)
            {
                var formatted = FormatPayloadForDetail(message);
                if (_isDisposed)
                {
                    return;
                }

                await _dispatcher.InvokeAsync(() =>
                {
                    if (!_isDisposed
                        && !FreezeDetail
                        && !HistoryPaused
                        && ReferenceEquals(SelectedTopic?.LastMessage, message))
                    {
                        ValuePayloadText = formatted;
                    }
                }, DispatcherPriority.DataBind);
            }
        }
        catch (TaskCanceledException) when (_isDisposed || _dispatcher.HasShutdownStarted)
        {
            // Normal shutdown while a formatted Value is waiting for the UI thread.
        }
        catch (InvalidOperationException) when (_isDisposed || _dispatcher.HasShutdownStarted)
        {
            // The dispatcher stopped between formatting and posting the result.
        }
        finally
        {
            Interlocked.Exchange(ref _valueFormatWorkerRunning, 0);
            if (!_isDisposed && Volatile.Read(ref _pendingValueFormatMessage) is not null)
            {
                StartValueFormatWorker();
            }
        }
    }

    private void ClearPendingMessages()
    {
        var removed = 0;
        long removedPayloadCharacters = 0;
        while (_pendingMessages.TryDequeue(out var message))
        {
            removed++;
            removedPayloadCharacters += message.PayloadLength;
        }

        if (removed > 0)
        {
            Interlocked.Add(ref _pendingQueueCount, -removed);
            Interlocked.Add(ref _pendingPayloadCharacters, -removedPayloadCharacters);
        }

        PendingCount = Math.Max(0, Volatile.Read(ref _pendingQueueCount));
    }

    private void TrimPendingMessages()
    {
        while (Volatile.Read(ref _pendingQueueCount) > MaxPendingMessages
               || Volatile.Read(ref _pendingPayloadCharacters) > MaxPendingPayloadCharacters)
        {
            if (!_pendingMessages.TryDequeue(out var dropped))
            {
                return;
            }

            Interlocked.Decrement(ref _pendingQueueCount);
            Interlocked.Add(ref _pendingPayloadCharacters, -dropped.PayloadLength);
        }
    }

    private void ClearPeriodCheckSamples()
    {
        while (_periodCheckSamples.TryDequeue(out _))
        {
        }
    }

    private static string CreateAppVersionText()
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version;
        return version is null
            ? "version unavailable"
            : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string FormatPayloadForDetail(MqttMessageSnapshot message) =>
        PayloadFormatter.Format(message.PayloadText, previewLimit: 200).DisplayText;

    private static string FormatAveragePeriod(IReadOnlyList<MqttMessageSnapshot> history)
    {
        var statistics = MessagePeriodStatistics.Calculate(history.Select(x => x.ReceivedAt));
        return statistics.HasIntervals
            ? $"Avg {FormatMilliseconds(statistics.AverageMilliseconds)}"
            : "Avg -";
    }

    private static string FormatMilliseconds(double milliseconds)
    {
        var format = milliseconds < 10 ? "0.##" : milliseconds < 100 ? "0.#" : "0";
        return $"{milliseconds.ToString(format, CultureInfo.InvariantCulture)} ms";
    }

    private bool TryGetPeriodCheckDuration(out int seconds)
    {
        var text = PeriodCheckDurationText.Trim();
        var multiplier = 1;

        if (text.EndsWith("분", StringComparison.Ordinal))
        {
            text = text[..^1].Trim();
            multiplier = 60;
        }
        else if (text.EndsWith("초", StringComparison.Ordinal))
        {
            text = text[..^1].Trim();
        }
        else if (text.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^1].Trim();
            multiplier = 60;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value <= 0
            || value > MaxPeriodCheckSeconds / multiplier)
        {
            seconds = 0;
            return false;
        }

        seconds = value * multiplier;
        return seconds is >= MinPeriodCheckSeconds and <= MaxPeriodCheckSeconds;
    }

    private static string FormatDurationSeconds(int seconds)
    {
        return seconds >= 60 && seconds % 60 == 0
            ? $"{seconds / 60}분"
            : $"{seconds}초";
    }

    private static string FormatElapsedTime(double milliseconds)
    {
        if (milliseconds < 1_000)
        {
            return FormatMilliseconds(milliseconds);
        }

        return $"{(milliseconds / 1_000).ToString("0.##", CultureInfo.InvariantCulture)}초";
    }

    private static string BuildPeriodCheckSummary(MessagePeriodStatisticsResult statistics, bool wasStopped)
    {
        var state = wasStopped ? "중단" : "완료";
        if (!statistics.HasIntervals)
        {
            return $"{state} · 표본 {statistics.SampleCount:N0}개 · 주기 계산 불가";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{state} · 평균 {FormatMilliseconds(statistics.AverageMilliseconds)} · 최소 {FormatMilliseconds(statistics.MinimumMilliseconds)} · 최대 {FormatMilliseconds(statistics.MaximumMilliseconds)} · 표본 {statistics.SampleCount:N0}개");
    }

    private static string BuildPeriodCheckResult(
        string topic,
        MessagePeriodStatisticsResult statistics,
        int requestedSeconds,
        double elapsedMilliseconds,
        bool wasStopped)
    {
        var lines = new List<string>
        {
            $"상태: {(wasStopped ? "사용자 중단" : "측정 완료")}",
            $"토픽: {topic}",
            $"설정 시간: {FormatDurationSeconds(requestedSeconds)}",
            $"실제 측정 시간: {FormatElapsedTime(elapsedMilliseconds)}",
            $"수신 표본: {statistics.SampleCount:N0}개",
            $"주기 구간: {statistics.IntervalCount:N0}개"
        };

        if (!statistics.HasIntervals)
        {
            lines.Add("결과: 메시지가 2개 이상 수신되지 않아 주기를 계산할 수 없습니다.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add($"평균 주기: {FormatMilliseconds(statistics.AverageMilliseconds)}");
        lines.Add($"최소 주기: {FormatMilliseconds(statistics.MinimumMilliseconds)}");
        lines.Add($"최대 주기: {FormatMilliseconds(statistics.MaximumMilliseconds)}");
        lines.Add($"메시지 구간: {FormatElapsedTime(statistics.DurationMilliseconds)}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string AppendResultLine(string text, string line)
    {
        return string.IsNullOrWhiteSpace(text)
            ? line
            : text + Environment.NewLine + line;
    }

    private void RaiseCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        ConnectSelectedProfileCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        ToggleConnectionCommand.RaiseCanExecuteChanged();
        OpenConnectionManagerCommand.RaiseCanExecuteChanged();
        PublishCommand.RaiseCanExecuteChanged();
        OpenPeriodCheckCommand.RaiseCanExecuteChanged();
        ClosePeriodCheckCommand.RaiseCanExecuteChanged();
        StartPeriodCheckCommand.RaiseCanExecuteChanged();
        StopPeriodCheckCommand.RaiseCanExecuteChanged();
        DeleteProfileCommand.RaiseCanExecuteChanged();
        RenameFolderCommand.RaiseCanExecuteChanged();
        DeleteFolderCommand.RaiseCanExecuteChanged();
    }

}
