namespace DMS.Core.Mes;

public enum MesMachineRuntimeState
{
    Unknown,
    WaitingForPulse,
    Running,
    Stopped,
    Offline
}

public sealed class MesMachineRuntimeResult
{
    public MesMachineRuntimeState State { get; init; }

    public TimeSpan? ObservedCycleTime { get; init; }

    public ulong CounterDelta { get; init; }

    public bool CounterResetDetected { get; init; }

    public bool CounterWrapDetected { get; init; }
}

/// <summary>
/// Evaluates the machine state from changes of the configured GOOD_PIECES counter.
/// The observed cycle time is based on polling timestamps and is therefore diagnostic,
/// not a replacement for a hardware timestamp or a dedicated gate-time value.
/// </summary>
public sealed class MesMachineRuntimeEvaluator
{
    private string _stationCode = string.Empty;
    private ulong? _lastCounterValue;
    private ulong? _counterMaximum;
    private DateTimeOffset? _observationStartedAt;
    private DateTimeOffset? _lastPulseAt;
    private DateTimeOffset? _previousPulseAt;
    private ulong _lastCounterDelta;

    public void Reset(string? stationCode = null)
    {
        _stationCode = stationCode?.Trim() ?? string.Empty;
        _lastCounterValue = null;
        _counterMaximum = null;
        _observationStartedAt = null;
        _lastPulseAt = null;
        _previousPulseAt = null;
        _lastCounterDelta = 0;
    }

    public MesMachineRuntimeResult Update(
        string stationCode,
        bool isOnline,
        object? counterValue,
        DateTimeOffset readAt,
        TimeSpan stopTimeout)
    {
        var normalizedStation = stationCode?.Trim() ?? string.Empty;

        if (!string.Equals(
                _stationCode,
                normalizedStation,
                StringComparison.OrdinalIgnoreCase))
        {
            Reset(normalizedStation);
        }

        if (!isOnline)
        {
            return new MesMachineRuntimeResult
            {
                State = MesMachineRuntimeState.Offline
            };
        }

        if (!TryConvertCounter(
                counterValue,
                out var currentValue,
                out var maximumValue))
        {
            return new MesMachineRuntimeResult
            {
                State = MesMachineRuntimeState.Unknown
            };
        }

        if (!_lastCounterValue.HasValue)
        {
            _lastCounterValue = currentValue;
            _counterMaximum = maximumValue;
            _observationStartedAt = readAt;

            return new MesMachineRuntimeResult
            {
                State = MesMachineRuntimeState.WaitingForPulse
            };
        }

        var resetDetected = false;
        var wrapDetected = false;

        if (currentValue > _lastCounterValue.Value)
        {
            RegisterPulse(
                currentValue - _lastCounterValue.Value,
                readAt);
        }
        else if (currentValue < _lastCounterValue.Value)
        {
            if (IsProbableWrap(
                    _lastCounterValue.Value,
                    currentValue,
                    _counterMaximum))
            {
                var maximum = _counterMaximum!.Value;
                var delta = maximum - _lastCounterValue.Value + 1UL + currentValue;

                RegisterPulse(delta, readAt);
                wrapDetected = true;
            }
            else
            {
                _observationStartedAt = readAt;
                _lastPulseAt = null;
                _previousPulseAt = null;
                _lastCounterDelta = 0;
                resetDetected = true;
            }
        }

        _lastCounterValue = currentValue;
        _counterMaximum = maximumValue ?? _counterMaximum;

        var effectiveTimeout = stopTimeout > TimeSpan.Zero
            ? stopTimeout
            : TimeSpan.FromSeconds(30);

        if (!_lastPulseAt.HasValue)
        {
            var waitingState = _observationStartedAt.HasValue &&
                               readAt - _observationStartedAt.Value > effectiveTimeout
                ? MesMachineRuntimeState.Stopped
                : MesMachineRuntimeState.WaitingForPulse;

            return new MesMachineRuntimeResult
            {
                State = waitingState,
                CounterResetDetected = resetDetected,
                CounterWrapDetected = wrapDetected
            };
        }

        var state = readAt - _lastPulseAt.Value <= effectiveTimeout
            ? MesMachineRuntimeState.Running
            : MesMachineRuntimeState.Stopped;

        TimeSpan? observedCycleTime = null;

        if (_previousPulseAt.HasValue && _lastCounterDelta > 0)
        {
            var observedInterval = _lastPulseAt.Value - _previousPulseAt.Value;
            observedCycleTime = TimeSpan.FromTicks(
                observedInterval.Ticks / checked((long)_lastCounterDelta));
        }

        return new MesMachineRuntimeResult
        {
            State = state,
            ObservedCycleTime = observedCycleTime,
            CounterDelta = _lastCounterDelta,
            CounterResetDetected = resetDetected,
            CounterWrapDetected = wrapDetected
        };
    }

    private void RegisterPulse(ulong delta, DateTimeOffset readAt)
    {
        if (delta == 0)
        {
            return;
        }

        _previousPulseAt = _lastPulseAt;
        _lastPulseAt = readAt;
        _lastCounterDelta = delta;
    }

    private static bool IsProbableWrap(
        ulong previousValue,
        ulong currentValue,
        ulong? maximumValue)
    {
        if (!maximumValue.HasValue || maximumValue.Value == ulong.MaxValue)
        {
            return false;
        }

        var maximum = maximumValue.Value;
        var highThreshold = maximum - maximum / 10UL;
        var lowThreshold = maximum / 10UL;

        return previousValue >= highThreshold && currentValue <= lowThreshold;
    }

    private static bool TryConvertCounter(
        object? value,
        out ulong result,
        out ulong? maximumValue)
    {
        switch (value)
        {
            case byte byteValue:
                result = byteValue;
                maximumValue = byte.MaxValue;
                return true;

            case ushort ushortValue:
                result = ushortValue;
                maximumValue = ushort.MaxValue;
                return true;

            case uint uintValue:
                result = uintValue;
                maximumValue = uint.MaxValue;
                return true;

            case ulong ulongValue:
                result = ulongValue;
                maximumValue = ulong.MaxValue;
                return true;

            case sbyte sbyteValue when sbyteValue >= 0:
                result = (ulong)sbyteValue;
                maximumValue = (ulong)sbyte.MaxValue;
                return true;

            case short shortValue when shortValue >= 0:
                result = (ulong)shortValue;
                maximumValue = (ulong)short.MaxValue;
                return true;

            case int intValue when intValue >= 0:
                result = (ulong)intValue;
                maximumValue = int.MaxValue;
                return true;

            case long longValue when longValue >= 0:
                result = (ulong)longValue;
                maximumValue = long.MaxValue;
                return true;

            default:
                result = 0;
                maximumValue = null;
                return false;
        }
    }
}
