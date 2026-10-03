using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace WinInstagram.Services;

/// <summary>
/// Provides standard Windows UI Automation (UIA) announcements for screen readers (NVDA, JAWS, Narrator)
/// without using any custom text-to-speech engine.
/// </summary>
public static class AccessibilityHelper
{
    private static TextBlock? _announcementTarget;

    public static void RegisterAnnouncementTarget(TextBlock target)
    {
        _announcementTarget = target;
    }

    public static void Announce(string message)
    {
        if (_announcementTarget == null || string.IsNullOrWhiteSpace(message))
            return;

        _announcementTarget.Dispatcher.Invoke(() =>
        {
            _announcementTarget.Text = message;
            var peer = UIElementAutomationPeer.FromElement(_announcementTarget);
            if (peer == null)
            {
                peer = UIElementAutomationPeer.CreatePeerForElement(_announcementTarget);
            }
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        });
    }
}
