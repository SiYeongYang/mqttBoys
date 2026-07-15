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
    private const int HistoryDisplayLimit = 1_000;
    private static readonly long DetailRefreshMinTicks = Stopwatch.Frequency / 4;
    private const int SearchResultLimit = 500;
    private const int PeriodCheckSeconds = 10;
    private const int PeriodCheckHistoryLimit = 100;
    private readonly ConcurrentQueue<MqttMessageSnapshot> _pendingMessages = new();
    private readonly ConcurrentQueue<long> _periodCheckSamples = new();
    private readonly Dictionary<string, TopicViewModel> _rootTopicsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TopicViewModel> _leafTopicsByFullName = new(StringComparer.Ordinal);
    private readonly HashSet<string> _profileFolderPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProfileStore _profileStore;
    private readonly MqttClientService _mqttClient = new();
    private readonly DispatcherTimer _flushTimer;
    private ProfileTreeNodeViewModel? _selectedProfileNode;
    private TopicViewModel? _brokerTopicRoot;
    private BrokerProfile? _selectedProfile;
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
    private bool _historyPaused;
    private bool _freezeDetail;
    private bool _followLatest = true;
    private string _statusMessage = "Ready";
    private string _periodCheckTopicText = string.Empty;
    private string _periodCheckStatus = "토픽을 입력하거나 기존 토픽에서 선택하세요.";
    private string _periodCheckResultText = "아직 측정 결과가 없습니다.";
    private string _periodCheckTargetTopic = string.Empty;
    private long _receivedMessages;
    private int _pendingQueueCount;
    private int _pendingCount;
    private long _lastDetailRefreshTimestamp;

    public MainViewModel(ProfileStore? profileStore = null)
    {
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
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => !IsBusy && IsConnected);
        PublishCommand = new AsyncRelayCommand(PublishAsync, () => IsConnected && !string.IsNullOrWhiteSpace(PublishTopic));
        FormatPublishJsonCommand = new RelayCommand(FormatPublishJson);
        CopyValueCommand = new RelayCommand(CopyValueToClipboard, () => !string.IsNullOrEmpty(ValuePayloadText));
        CopySelectedCommand = new RelayCommand(CopySelectedToClipboard, () => !string.IsNullOrEmpty(SelectedPayloadText));
        ToggleHistoryPauseCommand = new RelayCommand(ToggleHistoryPause);
        OpenConnectionManagerCommand = new AsyncRelayCommand(OpenConnectionManagerAsync, () => !IsBusy && !IsPeriodCheckRunning);
        CloseConnectionManagerCommand = new RelayCommand(CloseConnectionManager);
        OpenPeriodCheckCommand = new RelayCommand(OpenPeriodCheck, () => IsConnected && !IsPeriodCheckRunning);
        ClosePeriodCheckCommand = new RelayCommand(ClosePeriodCheck, () => !IsPeriodCheckRunning);
        StartPeriodCheckCommand = new RelayCommand(StartPeriodCheck, () => IsConnected && !IsPeriodCheckRunning && !string.IsNullOrWhiteSpace(PeriodCheckTopicText));
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

        _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _flushTimer.Tick += (_, _) => DrainPendingMessages();
        _flushTimer.Start();
    }

    public ObservableCollection<BrokerProfile> Profiles { get; }

    public ObservableCollection<ProfileTreeNodeViewModel> ProfileTree { get; } = new();

    public ObservableCollection<string> FolderOptions { get; } = new();

    public ObservableCollection<TopicViewModel> RootTopics { get; } = new();

    public ObservableCollection<TopicViewModel> SearchResults { get; } = new();

    public ObservableCollection<HistoryItemViewModel> SelectedTopicHistory { get; } = new();

    public ObservableCollection<string> PeriodCheckTopicSuggestions { get; } = new();

    public ObservableCollection<PeriodCheckHistoryItemViewModel> PeriodCheckHistory { get; } = new();

    public string AppVersionText { get; } = CreateAppVersionText();

    public IReadOnlyList<int> QosOptions { get; } = new[] { 0, 1, 2 };

    public IReadOnlyList<string> TransportOptions { get; } = new[] { "mqtt", "ws", "wss" };

    public AsyncRelayCommand ConnectCommand { get; }

    public AsyncRelayCommand DisconnectCommand { get; }

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

    public string SelectedProfileCaption => SelectedProfile is null
        ? "No broker selected"
        : $"{SelectedProfile.Name} ({SelectedProfile.Transport}://{SelectedProfile.Host}:{SelectedProfile.Port})";

    public TopicViewModel? SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            if (SetProperty(ref _selectedTopic, value))
            {
                SelectedHistoryItem = null;
                PublishTopic = value?.FullTopic ?? string.Empty;
                _lastDetailRefreshTimestamp = 0;
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
                OnPropertyChanged(nameof(SearchResultsVisibility));
                UpdateSearchResults();
            }
        }
    }

    public Visibility SearchResultsVisibility => string.IsNullOrWhiteSpace(SearchText)
        ? Visibility.Collapsed
        : Visibility.Visible;

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
                RaiseCommandStates();
            }
        }
    }

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
            IsConnected = true;
            EnsureBrokerRoot();
            IsConnectionManagerOpen = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connect failed: {ex.Message}";
            IsConnected = false;
        }
        finally
        {
            IsBusy = false;
        }
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
            IsConnected = false;
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

    private async Task OpenConnectionManagerAsync()
    {
        if (IsConnected)
        {
            await DisconnectAsync();
            ClearPendingMessages();
        }

        RebuildProfileTree();
        RestoreProfileTreeSelection(SelectedProfile?.Id, null);
        IsConnectionManagerOpen = true;
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
        if (topic.Length == 0)
        {
            return;
        }

        PeriodCheckTopicText = topic;
        _ = StartPeriodCheckAsync(topic);
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
        PeriodCheckTopicSuggestions.Clear();
        PeriodCheckTopicText = string.Empty;
        _brokerTopicRoot = null;
        RootTopics.Clear();
        SearchResults.Clear();
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
            return;
        }

        var selectedTopicWasTouched = false;
        var searchNeedsRefresh = false;
        var processed = 0;

        // Batch UI updates so fast brokers do not cause one layout pass per MQTT packet.
        while (processed < MaxMessagesPerUiTick && _pendingMessages.TryDequeue(out var message))
        {
            Interlocked.Decrement(ref _pendingQueueCount);
            var leafTopic = IngestIntoTree(message, out var leafTopicWasNew);
            selectedTopicWasTouched |= ReferenceEquals(leafTopic, SelectedTopic);
            searchNeedsRefresh |= leafTopicWasNew;
            processed++;
        }

        ReceivedMessages += processed;
        PendingCount = Math.Max(0, Volatile.Read(ref _pendingQueueCount));
        OnPropertyChanged(nameof(TopicCount));

        if (!FreezeDetail && !HistoryPaused && selectedTopicWasTouched && ShouldRefreshSelectedTopicDetail())
        {
            RefreshSelectedTopic(keepCurrentSelection: true);
        }

        if (searchNeedsRefresh && !string.IsNullOrWhiteSpace(SearchText))
        {
            UpdateSearchResults();
        }
    }

    private bool ShouldRefreshSelectedTopicDetail()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _lastDetailRefreshTimestamp < DetailRefreshMinTicks)
        {
            return false;
        }

        _lastDetailRefreshTimestamp = now;
        return true;
    }

    private TopicViewModel IngestIntoTree(MqttMessageSnapshot message, out bool leafTopicWasNew)
    {
        var segments = message.Topic.Split('/', StringSplitOptions.None);
        var current = EnsureBrokerRoot();
        var path = string.Empty;
        leafTopicWasNew = !_leafTopicsByFullName.ContainsKey(message.Topic);

        current.Record(message, isLeaf: false, leafTopicWasNew);

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            path = path.Length == 0 ? segment : $"{path}/{segment}";
            var isLeaf = i == segments.Length - 1;

            current = current.GetOrCreateChild(segment, path, SelectedProfile?.MaxHistoryPerTopic ?? 300);
            current.Record(message, isLeaf, leafTopicWasNew);

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

        var rootName = SelectedProfile is null
            ? "broker"
            : SelectedProfile.Host.Trim();
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = SelectedProfile?.Name ?? "broker";
        }

        _brokerTopicRoot = new TopicViewModel(rootName, string.Empty, SelectedProfile?.MaxHistoryPerTopic ?? 300);
        RootTopics.Add(_brokerTopicRoot);
        return _brokerTopicRoot;
    }

    private void UpdateSearchResults()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        foreach (var match in _leafTopicsByFullName.Values
                     .Where(x => x.FullTopic.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.FullTopic, StringComparer.OrdinalIgnoreCase)
                     .Take(SearchResultLimit))
        {
            SearchResults.Add(match);
        }
    }

    private async Task StartPeriodCheckAsync(string topic)
    {
        if (SelectedProfile is null || !IsConnected)
        {
            PeriodCheckResultText = "브로커에 연결된 상태에서만 주기 체크를 실행할 수 있습니다.";
            return;
        }

        var profile = SelectedProfile;
        _periodCheckTargetTopic = topic;
        IsPeriodCheckRunning = true;
        PeriodCheckResultText = string.Empty;
        PeriodCheckStatus = $"준비 중: {topic}";
        ClearPendingMessages();
        ClearPeriodCheckSamples();
        _flushTimer.Stop();
        var historySummary = "측정 실패";

        try
        {
            await _mqttClient.ReplaceSubscriptionAsync(topic, profile.SubscribeQos, CancellationToken.None);
            ClearPeriodCheckSamples();

            for (var remaining = PeriodCheckSeconds; remaining > 0; remaining--)
            {
                PeriodCheckStatus = $"측정 중: {topic} ({remaining}초 남음)";
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            var samples = _periodCheckSamples.ToArray();
            var statistics = MessagePeriodStatistics.CalculateStopwatchTicks(samples, Stopwatch.Frequency);
            PeriodCheckResultText = BuildPeriodCheckResult(topic, statistics);
            historySummary = BuildPeriodCheckSummary(statistics);
            PeriodCheckStatus = "측정 완료. 원래 구독으로 복원 중...";
        }
        catch (Exception ex)
        {
            PeriodCheckResultText = $"측정 실패: {ex.Message}";
            PeriodCheckStatus = "측정 실패";
        }
        finally
        {
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

            _periodCheckTargetTopic = string.Empty;
            IsPeriodCheckRunning = false;
            _flushTimer.Start();
            PendingCount = Math.Max(0, Volatile.Read(ref _pendingQueueCount));
            PeriodCheckStatus = PeriodCheckResultText.StartsWith("측정 실패:", StringComparison.Ordinal)
                ? "측정 실패"
                : "측정 완료";
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

        if (SelectedTopic.LastMessage is not null)
        {
            ShowValuePayload(SelectedTopic.LastMessage);
        }
        else
        {
            ValuePayloadText = string.Empty;
        }

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

    private void ClearPendingMessages()
    {
        var removed = 0;
        while (_pendingMessages.TryDequeue(out _))
        {
            removed++;
        }

        if (removed > 0)
        {
            Interlocked.Add(ref _pendingQueueCount, -removed);
        }

        PendingCount = Math.Max(0, Volatile.Read(ref _pendingQueueCount));
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

    private static string BuildPeriodCheckSummary(MessagePeriodStatisticsResult statistics)
    {
        if (!statistics.HasIntervals)
        {
            return $"Samples {statistics.SampleCount:N0}, 주기 계산 불가";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Avg {FormatMilliseconds(statistics.AverageMilliseconds)} · Min {FormatMilliseconds(statistics.MinimumMilliseconds)} · Max {FormatMilliseconds(statistics.MaximumMilliseconds)} · Samples {statistics.SampleCount:N0}");
    }
    private static string BuildPeriodCheckResult(string topic, MessagePeriodStatisticsResult statistics)
    {
        if (!statistics.HasIntervals)
        {
            return string.Join(Environment.NewLine,
                $"Topic: {topic}",
                $"Samples: {statistics.SampleCount:N0}",
                "Intervals: 0",
                "Result: 메시지가 2개 이상 수신되지 않아 주기를 계산할 수 없습니다.");
        }

        return string.Join(Environment.NewLine,
            $"Topic: {topic}",
            $"Samples: {statistics.SampleCount:N0}",
            $"Intervals: {statistics.IntervalCount:N0}",
            $"Average: {FormatMilliseconds(statistics.AverageMilliseconds)}",
            $"Min: {FormatMilliseconds(statistics.MinimumMilliseconds)}",
            $"Max: {FormatMilliseconds(statistics.MaximumMilliseconds)}",
            $"Captured: {FormatMilliseconds(statistics.DurationMilliseconds)}");
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
        DisconnectCommand.RaiseCanExecuteChanged();
        OpenConnectionManagerCommand.RaiseCanExecuteChanged();
        PublishCommand.RaiseCanExecuteChanged();
        OpenPeriodCheckCommand.RaiseCanExecuteChanged();
        ClosePeriodCheckCommand.RaiseCanExecuteChanged();
        StartPeriodCheckCommand.RaiseCanExecuteChanged();
        DeleteProfileCommand.RaiseCanExecuteChanged();
        RenameFolderCommand.RaiseCanExecuteChanged();
        DeleteFolderCommand.RaiseCanExecuteChanged();
    }
}
