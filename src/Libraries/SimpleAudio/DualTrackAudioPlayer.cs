using System.Diagnostics;

namespace SimpleAudio;

public sealed class DualTrackAudioPlayer : IDisposable
{
    private readonly object _crossfadeGate = new();
    private readonly SimpleAudioPlayer _deckA = new();
    private readonly SimpleAudioPlayer _deckB = new();
    private readonly Action _deckAEndedHandler;
    private readonly Action _deckBEndedHandler;
    private CancellationTokenSource? _crossfadeCancellation;

    private SimpleAudioPlayer _activeDeck;
    private SimpleAudioPlayer? _fadingDeck;
    private SimpleAudioPlayer _standbyDeck;

    private float[] _currentEq = new float[10];
    private Task? _crossfadeTask;
    private float _crossfadeProgress;
    private float _currentReverbAmount;
    private float _currentReverbTimeMs = 1500f;
    private float _currentStereoWidth;
    private bool _surroundEnabled;
    private float _userVolume = 1.0f;
    private string? _activeSource;
    private string? _preparedSource;

    public DualTrackAudioPlayer()
    {
        _activeDeck = _deckA;
        _standbyDeck = _deckB;

        _deckAEndedHandler = () => HandleDeckPlaybackEnded(_deckA);
        _deckBEndedHandler = () => HandleDeckPlaybackEnded(_deckB);

        _deckA.PlaybackEnded += _deckAEndedHandler;
        _deckB.PlaybackEnded += _deckBEndedHandler;
    }

    public event Action? PlaybackEnded;

    public bool IsPlaying => _activeDeck.IsPlaying;

    public bool IsPaused => _activeDeck.IsPaused;

    public bool IsStopped => _activeDeck.IsStopped;

    public bool IsStalled => _activeDeck.IsStalled;

    public bool IsCrossfading { get; private set; }

    public float CrossfadeProgress => _crossfadeProgress;

    public bool HasPreparedTrack => !string.IsNullOrWhiteSpace(_preparedSource);

    public string? ActiveSource => _activeSource;

    public string? PreparedSource => _preparedSource;

    public bool Load(string url)
    {
        AbortCrossfade();
        if (!PrepareNext(url))
        {
            return false;
        }

        _activeDeck.Stop();
        return SwitchToPrepared();
    }

    public bool PrepareNext(string url)
    {
        _standbyDeck.Stop();
        ApplyDeckSettings(_standbyDeck);

        if (!_standbyDeck.Load(url))
        {
            _preparedSource = null;
            return false;
        }

        _preparedSource = url;
        return true;
    }

    public bool SwitchToPrepared()
    {
        if (!HasPreparedTrack)
        {
            return false;
        }

        SwapDecks();
        ApplyDeckSettings(_activeDeck);
        _activeSource = _preparedSource;
        _preparedSource = null;
        return true;
    }

    public void CancelPrepared()
    {
        _standbyDeck.Stop();
        _preparedSource = null;
    }

    public void Play()
    {
        _activeDeck.Play();
        _fadingDeck?.Play();
    }

    public void Pause()
    {
        _activeDeck.Pause();
        _fadingDeck?.Pause();
    }

    public void Stop()
    {
        AbortCrossfade();
        _activeDeck.Stop();
        _standbyDeck.Stop();
        _activeSource = null;
        _preparedSource = null;
    }

    public void SetVolume(float volume)
    {
        _userVolume = volume;
        _activeDeck.SetVolume(volume);
        _standbyDeck.SetVolume(volume);
    }

    public float GetVolume()
    {
        return _activeDeck.GetVolume();
    }

    public void SetPosition(TimeSpan time)
    {
        _activeDeck.SetPosition(time);
    }

    public void SetPosition(double percentage)
    {
        _activeDeck.SetPosition(percentage);
    }

    public TimeSpan GetDuration()
    {
        return _activeDeck.GetDuration();
    }

    public TimeSpan GetPosition()
    {
        return _activeDeck.GetPosition();
    }

    public AudioAnalysisSnapshot GetActiveAnalysisSnapshot()
    {
        return _activeDeck.GetRealtimeAnalysisSnapshot();
    }

    public void SetEQ(float[]? gains)
    {
        if (gains == null || gains.Length != 10)
        {
            return;
        }

        _currentEq = gains.ToArray();
        _activeDeck.SetEQ(_currentEq);
        _standbyDeck.SetEQ(_currentEq);
    }

    public void SetSurround(bool enable)
    {
        _surroundEnabled = enable;
        if (enable)
        {
            _currentStereoWidth = 0.20f;
            _currentReverbAmount = 0.15f;
            _currentReverbTimeMs = 1500f;
        }
        else
        {
            _currentStereoWidth = 0f;
            _currentReverbAmount = 0f;
            _currentReverbTimeMs = 1500f;
        }

        _activeDeck.SetSurround(enable);
        _standbyDeck.SetSurround(enable);
        _fadingDeck?.SetSurround(enable);
    }

    public void SetStereoWidth(float width)
    {
        _currentStereoWidth = Math.Clamp(width, 0f, 1f);
        _activeDeck.SetStereoWidth(_currentStereoWidth);
        _standbyDeck.SetStereoWidth(_currentStereoWidth);
        _fadingDeck?.SetStereoWidth(_currentStereoWidth);
    }

    public void SetReverbAmount(float amount)
    {
        _currentReverbAmount = Math.Clamp(amount, 0f, 1f);
        _activeDeck.SetReverbAmount(_currentReverbAmount);
        _standbyDeck.SetReverbAmount(_currentReverbAmount);
        _fadingDeck?.SetReverbAmount(_currentReverbAmount);
    }

    public void SetReverbTime(float milliseconds)
    {
        _currentReverbTimeMs = Math.Clamp(milliseconds, 100f, 4000f);
        _activeDeck.SetReverbTime(_currentReverbTimeMs);
        _standbyDeck.SetReverbTime(_currentReverbTimeMs);
        _fadingDeck?.SetReverbTime(_currentReverbTimeMs);
    }

    public bool StartCrossfade(TransitionProfile profile, double? availableOverlapSec = null)
    {
        lock (_crossfadeGate)
        {
            if (IsCrossfading || !HasPreparedTrack)
            {
                return false;
            }

            var incomingDeck = _standbyDeck;
            var outgoingDeck = _activeDeck;
            var incomingSource = _preparedSource;
            if (incomingSource == null)
            {
                return false;
            }

            ApplyDeckSettings(incomingDeck);
            incomingDeck.SetTransitionGain(0f);
            incomingDeck.SetTransitionTone(profile.IncomingToneDepth);
            incomingDeck.SetReverbAmount(profile.IncomingReverbAmount);
            incomingDeck.SetStereoWidth(profile.StereoWidth * 0.85f);
            incomingDeck.Play();

            _activeDeck = incomingDeck;
            _standbyDeck = outgoingDeck;
            _fadingDeck = outgoingDeck;
            _activeSource = incomingSource;
            _preparedSource = null;
            _crossfadeProgress = 0f;
            IsCrossfading = true;
            _crossfadeCancellation = new CancellationTokenSource();
            _crossfadeTask = RunCrossfadeAsync(outgoingDeck, incomingDeck, profile, availableOverlapSec, _crossfadeCancellation.Token);
            return true;
        }
    }

    public void AbortCrossfade()
    {
        CancellationTokenSource? cancellation;
        SimpleAudioPlayer? fadingDeck;
        lock (_crossfadeGate)
        {
            if (!IsCrossfading)
            {
                return;
            }

            cancellation = _crossfadeCancellation;
            _crossfadeCancellation = null;
            fadingDeck = _fadingDeck;
            IsCrossfading = false;
            _crossfadeProgress = 0f;
            _fadingDeck = null;
        }

        cancellation?.Cancel();
        if (fadingDeck != null)
        {
            fadingDeck.Stop();
            ApplyDeckSettings(fadingDeck);
        }

        ApplyDeckSettings(_activeDeck);
    }

    public void Dispose()
    {
        AbortCrossfade();
        _deckA.PlaybackEnded -= _deckAEndedHandler;
        _deckB.PlaybackEnded -= _deckBEndedHandler;
        _deckA.Dispose();
        _deckB.Dispose();
    }

    private void HandleDeckPlaybackEnded(SimpleAudioPlayer deck)
    {
        if (IsCrossfading)
        {
            return;
        }

        if (ReferenceEquals(deck, _activeDeck))
        {
            PlaybackEnded?.Invoke();
        }
    }

    private void ApplyDeckSettings(SimpleAudioPlayer deck)
    {
        deck.SetEQ(_currentEq);
        deck.SetSurround(_surroundEnabled);
        deck.SetStereoWidth(_currentStereoWidth);
        deck.SetReverbAmount(_currentReverbAmount);
        deck.SetReverbTime(_currentReverbTimeMs);
        deck.SetVolume(_userVolume);
        deck.SetTransitionGain(1f);
        deck.SetTransitionTone(0f);
    }

    private void SwapDecks()
    {
        (_activeDeck, _standbyDeck) = (_standbyDeck, _activeDeck);
    }

    private async Task RunCrossfadeAsync(
        SimpleAudioPlayer outgoingDeck,
        SimpleAudioPlayer incomingDeck,
        TransitionProfile profile,
        double? availableOverlapSec,
        CancellationToken cancellationToken)
    {
        var plannedOverlapSec = profile.OverlapSec > 0
            ? profile.OverlapSec
            : Math.Min(profile.MixEntrySec, profile.MixDurationSec);
        plannedOverlapSec = Math.Max(0.35, plannedOverlapSec);
        var overlapSec = Math.Min(plannedOverlapSec, Math.Max(0.35, availableOverlapSec ?? plannedOverlapSec));
        var releaseSec = profile.ReleaseSec > 0
            ? profile.ReleaseSec
            : Math.Max(0.7, profile.MixDurationSec - overlapSec);
        releaseSec = Math.Clamp(releaseSec, 0.55, 4.5);
        var durationSec = Math.Max(0.5, overlapSec + releaseSec);
        var breathSec = Math.Clamp(profile.MixBreathSec, overlapSec * 0.12, Math.Max(0.12, overlapSec * 0.78));
        var stopwatch = Stopwatch.StartNew();
        double? outgoingEndedAtSec = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var elapsedSec = stopwatch.Elapsed.TotalSeconds;
                if (outgoingEndedAtSec == null && outgoingDeck.IsStopped)
                {
                    outgoingEndedAtSec = elapsedSec;
                }

                var overlapRatio = outgoingEndedAtSec.HasValue
                    ? 1.0
                    : Math.Clamp(elapsedSec / overlapSec, 0.0, 1.0);
                var releaseElapsedSec = outgoingEndedAtSec.HasValue
                    ? elapsedSec - outgoingEndedAtSec.Value
                    : elapsedSec - overlapSec;
                var releaseRatio = Math.Clamp(releaseElapsedSec / releaseSec, 0.0, 1.0);
                var ratio = Math.Clamp(elapsedSec / durationSec, 0.0, 1.0);
                _crossfadeProgress = (float)ratio;
                ApplyCrossfadeState(
                    outgoingDeck,
                    incomingDeck,
                    profile,
                    elapsedSec,
                    overlapSec,
                    overlapRatio,
                    releaseRatio,
                    breathSec,
                    outgoingEndedAtSec.HasValue);
                if (ratio >= 1.0 || outgoingEndedAtSec.HasValue && releaseRatio >= 1.0)
                {
                    break;
                }

                await Task.Delay(45, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            stopwatch.Stop();
        }

        lock (_crossfadeGate)
        {
            if (!IsCrossfading || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            IsCrossfading = false;
            _crossfadeProgress = 0f;
            _crossfadeCancellation = null;
            _fadingDeck = null;
        }

        outgoingDeck.Stop();
        ApplyDeckSettings(outgoingDeck);
        ApplyDeckSettings(incomingDeck);
    }

    private void ApplyCrossfadeState(
        SimpleAudioPlayer outgoingDeck,
        SimpleAudioPlayer incomingDeck,
        TransitionProfile profile,
        double elapsedSec,
        double overlapSec,
        double overlapRatio,
        double releaseRatio,
        double breathSec,
        bool outgoingReleased)
    {
        var safeOverlapRatio = Math.Clamp(overlapRatio, 0.0, 1.0);
        var safeReleaseRatio = Math.Clamp(releaseRatio, 0.0, 1.0);
        var outgoingCore = Math.Cos(safeOverlapRatio * Math.PI * 0.5);
        var outgoingDuckCurve = 1.0 - (0.14 * safeOverlapRatio) - (0.12 * Math.Pow(safeOverlapRatio, 2.2));
        var outgoingShape = 1.02 + profile.OutgoingDuckStrength * 0.38;
        var outgoingCurve = Math.Pow(Math.Max(0.0, outgoingCore * Math.Max(0.0, outgoingDuckCurve)), outgoingShape);
        var incomingRatio = elapsedSec <= breathSec
            ? 0
            : (Math.Min(elapsedSec, overlapSec) - breathSec) / Math.Max(0.001, overlapSec - breathSec);
        var incomingCurve = EaseOutSine(incomingRatio);
        var outgoingGain = outgoingReleased ? 0f : (float)Math.Clamp(outgoingCurve, 0.0, 1.0);
        var incomingBase = 0.82f - profile.IncomingToneDepth * 0.12f;
        var incomingTarget = 0.93f - Math.Max(0f, profile.IncomingToneDepth - 0.16f) * 0.08f;
        var incomingGain = (float)Math.Clamp((incomingBase + incomingCurve * (incomingTarget - incomingBase)) * profile.IncomingGainCap, 0.0, 1.0);
        var combinedCap = 1.02f + profile.IncomingGainCap * 0.03f;
        if (!outgoingReleased && outgoingGain + incomingGain > combinedCap)
        {
            incomingGain = Math.Max(0f, combinedCap - outgoingGain);
        }

        var settleRatio = EaseOutSine(safeReleaseRatio);
        incomingGain = Lerp(incomingGain, 1f, (float)settleRatio);

        outgoingDeck.SetTransitionGain(outgoingGain);
        incomingDeck.SetTransitionGain(incomingGain);

        var entryEnhance = EaseInOutCubic(Math.Min(1.0, safeOverlapRatio / 0.36));
        var outgoingTone = profile.OutgoingToneDepth * (0.82 + entryEnhance * 0.18);
        var incomingTone = profile.IncomingToneDepth * (0.84 - incomingRatio * 0.72);
        outgoingDeck.SetTransitionTone(outgoingReleased ? 0f : (float)Math.Clamp(outgoingTone * entryEnhance, 0.0, 1.0));
        var incomingToneDepth = (float)Math.Clamp(incomingTone * (0.88 + entryEnhance * 0.12), 0.0, 1.0);
        incomingDeck.SetTransitionTone(Lerp(incomingToneDepth, 0f, (float)settleRatio));

        outgoingDeck.SetReverbAmount(outgoingReleased ? 0f : (float)Math.Clamp(profile.OutgoingReverbAmount * (0.55 + entryEnhance * 0.45), 0.0, 1.0));
        var incomingReverb = (float)Math.Clamp(profile.IncomingReverbAmount * (0.95 - incomingRatio * 0.45), 0.0, 1.0);
        incomingDeck.SetReverbAmount(Lerp(incomingReverb, _currentReverbAmount, (float)settleRatio));

        outgoingDeck.SetStereoWidth(outgoingReleased ? 0f : (float)Math.Clamp(profile.StereoWidth * (0.65 + entryEnhance * 0.35), 0.0, 1.0));
        var incomingStereoWidth = (float)Math.Clamp(profile.StereoWidth * (0.70 + incomingRatio * 0.20), 0.0, 1.0);
        incomingDeck.SetStereoWidth(Lerp(incomingStereoWidth, _currentStereoWidth, (float)settleRatio));
    }

    private static double EaseOutSine(double value)
    {
        var safe = Math.Clamp(value, 0.0, 1.0);
        return Math.Sin(safe * Math.PI * 0.5);
    }

    private static double EaseInOutCubic(double value)
    {
        var safe = Math.Clamp(value, 0.0, 1.0);
        return safe < 0.5
            ? 4 * safe * safe * safe
            : 1 - Math.Pow(-2 * safe + 2, 3) / 2;
    }

    private static float Lerp(float from, float to, float amount)
    {
        var safe = Math.Clamp(amount, 0f, 1f);
        return from + (to - from) * safe;
    }
}
