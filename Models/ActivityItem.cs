using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinInstagram.Models;

/// <summary>
/// One entry from Instagram's own activity feed (likes, comments, follows, mentions). The web page
/// groups these into a scrolling list of avatars, which is unreadable without sight; this model
/// turns every entry into a sentence.
/// </summary>
public class ActivityItem : INotifyPropertyChanged
{
    private bool _isNew;

    public string Id { get; set; } = string.Empty;

    /// <summary>Instagram's raw story type, for example "like", "comment" or "follow".</summary>
    public string Type { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string MediaCode { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;

    /// <summary>True for entries that arrived after the panel last loaded, so they can be announced once.</summary>
    public bool IsNew
    {
        get => _isNew;
        set
        {
            if (_isNew == value) return;
            _isNew = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NewTag));
            OnPropertyChanged(nameof(AccessibleDescription));
        }
    }

    public string NewTag => IsNew ? "New" : string.Empty;

    public string TypeText => Type switch
    {
        "like" or "like_comment" => "Liked",
        "comment" => "Commented",
        "follow" => "Started following you",
        "mention" => "Mentioned you",
        "comment_mention" => "Mentioned you in a comment",
        "tag" => "Tagged you",
        "story_mention" => "Mentioned you in a story",
        "requested_to_follow" => "Requested to follow you",
        "friend_request" => "Sent a follow request",
        "share" => "Shared a post with you",
        "direct_share" => "Sent you a message",
        _ => string.IsNullOrWhiteSpace(Type) ? "Activity" : Type.Replace('_', ' ')
    };

    public string OpenUrl => string.IsNullOrWhiteSpace(MediaCode)
        ? (string.IsNullOrWhiteSpace(Username) ? string.Empty : $"https://www.instagram.com/{Username}/")
        : $"https://www.instagram.com/p/{MediaCode}/";

    public string AccessibleDescription
    {
        get
        {
            var who = string.IsNullOrWhiteSpace(Username) ? "Someone" : $"@{Username}";
            var verb = TypeText.ToLowerInvariant();
            var body = Text.Trim();

            string sentence;
            if (string.IsNullOrWhiteSpace(body))
            {
                // Nothing but the action, for example "follow".
                sentence = $"{who} {verb}";
            }
            else if (VerbAlreadySpoken(body, verb))
            {
                // Instagram phrased the action itself, for example "liked your photo".
                sentence = $"{who} {body}";
            }
            else
            {
                // The text is the object of the action: a comment body, a mention, a shared post.
                sentence = $"{who} {verb}: {body}";
            }

            if (!string.IsNullOrWhiteSpace(Timestamp)) sentence += $", {Timestamp}";
            var newPart = IsNew ? " New. " : ". ";
            return sentence + newPart + (string.IsNullOrWhiteSpace(OpenUrl) ? string.Empty : "Press Enter to open.");
        }
    }

    /// <summary>
    /// True when the payload text already begins with the action, so the sentence is not read twice
    /// as "liked: liked your photo".
    /// </summary>
    private static bool VerbAlreadySpoken(string text, string verb)
    {
        var firstWord = verb.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return !string.IsNullOrWhiteSpace(firstWord) &&
               text.StartsWith(firstWord, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
