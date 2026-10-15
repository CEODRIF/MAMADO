namespace AusbildungsWegInterview.Core.Audio;

/// <summary>
/// What the capture layer should do after an OS device-change notification.
/// </summary>
public enum DeviceChangeAction
{
    /// <summary>Nothing relevant changed.</summary>
    None,

    /// <summary>
    /// The user was tracking the system default and it moved to another device:
    /// re-open the capture on <c>NewDeviceId</c>.
    /// </summary>
    RestartOnNewDefault,

    /// <summary>
    /// The device being captured from disappeared (disconnected/unplugged):
    /// surface a fatal error (the pipeline's bounded restart applies).
    /// </summary>
    DeviceLost
}

/// <summary>
/// Pure, unit-testable decision logic for reacting to audio device-change
/// notifications. The platform glue (WASAPI on Windows) provides the fresh
/// device state; this type only decides what to do with it.
/// </summary>
public sealed class DeviceChangeMonitor
{
    /// <param name="requestedDeviceId">
    /// The device the user explicitly selected, or null when "follow the system
    /// default" is active.
    /// </param>
    /// <param name="currentDeviceId">The device actually being captured right now.</param>
    /// <param name="activeDeviceIds">All currently active output devices (fresh probe).</param>
    /// <param name="newDefaultId">The system default output device now (null if none).</param>
    public (DeviceChangeAction Action, string? NewDeviceId, string? LostDeviceId) Evaluate(
        string? requestedDeviceId, string currentDeviceId, IReadOnlyList<string> activeDeviceIds, string? newDefaultId)
    {
        if (!activeDeviceIds.Contains(currentDeviceId))
        {
            // Whatever the selection mode, the device we are capturing from is gone.
            return (DeviceChangeAction.DeviceLost, newDefaultId, currentDeviceId);
        }

        if (requestedDeviceId is null)
        {
            // "Follow default" mode: restart only when the default actually moved
            // to a different device.
            if (newDefaultId is not null && !newDefaultId.Equals(currentDeviceId, StringComparison.OrdinalIgnoreCase))
                return (DeviceChangeAction.RestartOnNewDefault, newDefaultId, null);
            return (DeviceChangeAction.None, null, null);
        }

        // Explicit selection that still exists: keep capturing.
        return (DeviceChangeAction.None, null, null);
    }
}
