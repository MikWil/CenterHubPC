using System;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace CenterHubNew.MVVM.Services
{
    public enum WindowsNotificationPriority
    {
        /// <summary>Normal toast. Suppressed by Focus Assist / Do Not Disturb / Game Mode.</summary>
        Normal,

        /// <summary>
        /// Reminder scenario — toast stays on screen until the user dismisses
        /// it, and is classified as a "Priority" notification. Focus Assist
        /// (in "Priority Only" mode) and Game Mode let these through by default,
        /// which is exactly what we want for a standing timer poke. The closest
        /// thing to "urgent" in the Toolkit's v7.x API surface.
        /// </summary>
        Reminder,

        /// <summary>
        /// Alarm scenario — adds Snooze and Dismiss buttons; plays a looping
        /// alarm sound until dismissed. Reserved for actual alarms, not regular
        /// reminders. Always bypasses Focus Assist.
        /// </summary>
        Alarm,
    }

    /// <summary>
    /// Real Windows Action Center toast notifications (not in-app toasts).
    /// Survives the CenterHub window being minimized or hidden; with the
    /// Reminder / Urgent priorities, breaks through Focus Assist, Do Not
    /// Disturb, and Game Mode so the user actually sees them while gaming.
    /// </summary>
    public sealed class WindowsNotificationService
    {
        private readonly ILogger<WindowsNotificationService>? _logger;

        public WindowsNotificationService(ILogger<WindowsNotificationService>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Show a Windows toast. Priority controls Focus Assist / Game Mode
        /// bypass behavior. Returns true if the toast was dispatched.
        /// </summary>
        public bool Show(
            string title,
            string body,
            WindowsNotificationPriority priority = WindowsNotificationPriority.Reminder,
            string? subtitleTag = null,
            string? group = null)
        {
            try
            {
                var builder = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(body);

                if (!string.IsNullOrWhiteSpace(subtitleTag))
                {
                    builder = builder.AddAttributionText(subtitleTag);
                }

                switch (priority)
                {
                    case WindowsNotificationPriority.Reminder:
                        // Reminder = sticky toast classified as Priority —
                        // Focus Assist (Priority-Only) and Game Mode allow it.
                        builder.SetToastScenario(ToastScenario.Reminder);
                        builder.AddAudio(new Uri("ms-winsoundevent:Notification.Reminder"));
                        break;

                    case WindowsNotificationPriority.Alarm:
                        // Alarm = always-on-top, looping sound, snooze UI.
                        // Reserved for actual alarms.
                        builder.SetToastScenario(ToastScenario.Alarm);
                        break;

                    default:
                        // Normal toast — default sound, no scenario.
                        break;
                }

                // Group/tag allow replacing in-flight toasts (e.g. swapping a
                // "Stand up" reminder for "Sit down" without two stacking)
                builder.Show(t =>
                {
                    if (!string.IsNullOrEmpty(group)) t.Group = group;
                    if (!string.IsNullOrEmpty(subtitleTag)) t.Tag = subtitleTag;
                    // Reminders should expire after 30 minutes if the user
                    // never touches them — they're hints, not blockers.
                    t.ExpirationTime = DateTimeOffset.Now.AddMinutes(30);
                });

                _logger?.LogDebug("Windows toast dispatched: {Title} ({Priority})", title, priority);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to dispatch Windows toast '{Title}'", title);
                return false;
            }
        }

        /// <summary>Clear all active toasts dispatched by this app.</summary>
        public void ClearAll()
        {
            try { ToastNotificationManagerCompat.History.Clear(); }
            catch (Exception ex) { _logger?.LogDebug(ex, "Could not clear toast history"); }
        }
    }
}
