using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities;

/// <summary>
/// Fires an output on a repeating interval, for as long as it is enabled.
/// </summary>
// Enable restarts the interval, it does not resume a partly elapsed one.
// The interval is floored, not refused: zero would be a think that is always
// due, and the keyvalue binder must not throw during a level load.
[SpectraEntity("logic_timer", Group = "Logic", Placement = EntityPlacement.Abstract)]
public sealed partial class LogicTimer : Entity
{
    /// <summary>The shortest interval a timer will run at, in seconds.</summary>
    public const float MinimumInterval = 0.01f;

    /// <summary>Fired at the end of every interval.</summary>
    [EntityOutput]
    public const string OnTimer = nameof(OnTimer);

    private float _refireInterval = 1f;

    /// <summary>Whether the timer starts switched off.</summary>
    [Keyvalue(
        "startdisabled",
        Display = "Start disabled",
        Tooltip = "The timer does not run until something sends it Enable.",
        Default = "0")]
    public bool StartDisabled { get; set; }

    /// <summary>Seconds between fires, floored at <see cref="MinimumInterval"/>.</summary>
    [Keyvalue(
        "refiretime",
        Display = "Refire time",
        Tooltip = "Seconds between fires.",
        Default = "1",
        Min = MinimumInterval)]
    public float RefireInterval
    {
        get => _refireInterval;
        // NaN fails the comparison and lands on the floor too.
        set => _refireInterval = value >= MinimumInterval ? value : MinimumInterval;
    }

    /// <summary>Whether the timer is currently running.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>How many times this timer has fired since it spawned.</summary>
    public int FireCount { get; private set; }

    /// <inheritdoc/>
    // Not OnSpawn: a short interval could fire at a target that does not exist yet.
    protected override void OnActivate()
    {
        if (!StartDisabled)
            Start();
    }

    /// <inheritdoc/>
    protected override void Think()
    {
        FireCount++;
        FireOnTimer();

        if (IsEnabled)
            SetNextThinkIn(RefireInterval);
    }

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        if (IsEnabled)
            state.Headline("fired", HeadlineText.Times(FireCount));
        else
            state.Headline("state", "off");

        state.Add("enabled", IsEnabled);
        state.Add("fires", FireCount);
        state.Add("refire time", RefireInterval);
    }

    [EntityInput("Enable")]
    private void Enable(ref EntityInputContext context) => Start();

    [EntityInput("Disable")]
    private void Disable(ref EntityInputContext context) => Stop();

    [EntityInput("Toggle")]
    private void Toggle(ref EntityInputContext context)
    {
        if (IsEnabled)
            Stop();
        else
            Start();
    }

    // Restarts the interval without firing. Does nothing on a stopped timer.
    [EntityInput("ResetTimer")]
    private void ResetTimer(ref EntityInputContext context)
    {
        if (IsEnabled)
            Start();
    }

    // Fires now and leaves the schedule alone.
    [EntityInput("FireTimer")]
    private void FireTimer(ref EntityInputContext context)
    {
        FireCount++;
        FireOnTimer(context.Activator);
    }

    // Takes effect at the next interval. Rescheduling here would mean a level
    // that adjusts the interval every tick never fires.
    [EntityInput("RefireTime")]
    private void RefireTime(ref EntityInputContext context)
    {
        if (KeyvalueWire.TryParseFloat(context.Parameter, out float seconds))
            RefireInterval = seconds;
    }

    private void Start()
    {
        IsEnabled = true;
        SetNextThinkIn(RefireInterval);
    }

    private void Stop()
    {
        IsEnabled = false;
        CancelThink();
    }
}
