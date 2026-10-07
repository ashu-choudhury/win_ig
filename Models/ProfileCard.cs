using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinInstagram.Models;

/// <summary>
/// A public profile, read from Instagram's own web_profile_info payload. Everything the web page
/// shows is mirrored here as text so a profile can be understood without seeing the page.
/// </summary>
public class ProfileCard : INotifyPropertyChanged
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Biography { get; set; } = string.Empty;
    public string ExternalUrl { get; set; } = string.Empty;
    public string ProfilePicUrl { get; set; } = string.Empty;
    public long FollowerCount { get; set; }
    public long FollowingCount { get; set; }
    public long PostCount { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsVerified { get; set; }
    public bool IsBusiness { get; set; }
    public string BusinessCategory { get; set; } = string.Empty;

    public string Handle => string.IsNullOrWhiteSpace(Username) ? "unknown account" : $"@{Username}";

    public string Title => string.IsNullOrWhiteSpace(FullName) ? Handle : $"{FullName} ({Handle})";

    public string FollowerText => $"{(FollowerCount > 0 ? FollowerCount.ToString("N0") : "0")} followers";
    public string FollowingText => $"{(FollowingCount > 0 ? FollowingCount.ToString("N0") : "0")} following";
    public string PostCountText => $"{(PostCount > 0 ? PostCount.ToString("N0") : "0")} posts";

    public string BioText => string.IsNullOrWhiteSpace(Biography)
        ? "No biography."
        : Biography.Replace("\n", " ");

    public string AccountTypeText
    {
        get
        {
            var parts = new List<string>();
            if (IsVerified) parts.Add("Verified account");
            if (IsPrivate) parts.Add("Private account");
            else parts.Add("Public account");
            if (IsBusiness) parts.Add(string.IsNullOrWhiteSpace(BusinessCategory) ? "Business account" : $"Business account, {BusinessCategory}");
            return string.Join(". ", parts) + ".";
        }
    }

    public string AccessibleDescription =>
        $"{Title}. {AccountTypeText} {FollowerText}, {FollowingText}, {PostCountText}. Biography: {BioText}";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One hit from Instagram's own search endpoint: either an account to open or content to read.
/// </summary>
public class SearchResult
{
    /// <summary>Either "account", "hashtag" or "post".</summary>
    public string Kind { get; set; } = "account";

    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string ProfilePicUrl { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public bool IsPrivate { get; set; }

    /// <summary>Set for content hits, so the result can be opened in the engine.</summary>
    public string MediaCode { get; set; } = string.Empty;

    public string OpenUrl => Kind switch
    {
        "hashtag" when !string.IsNullOrWhiteSpace(Tag) => $"https://www.instagram.com/explore/tags/{Tag}/",
        "post" when !string.IsNullOrWhiteSpace(MediaCode) => $"https://www.instagram.com/p/{MediaCode}/",
        _ when !string.IsNullOrWhiteSpace(Username) => $"https://www.instagram.com/{Username}/",
        _ => string.Empty
    };

    public string KindText => Kind switch
    {
        "hashtag" => "Hashtag",
        "post" => "Post",
        _ => "Account"
    };

    public string Title => Kind switch
    {
        "hashtag" => $"#{Tag}",
        "post" => string.IsNullOrWhiteSpace(Username) ? "Post" : $"Post by @{Username}",
        _ => string.IsNullOrWhiteSpace(FullName) ? $"@{Username}" : $"{FullName} (@{Username})"
    };

    public string AccessibleDescription
    {
        get
        {
            var parts = new List<string> { $"{KindText}: {Title}" };
            if (Kind == "account")
            {
                if (IsVerified) parts.Add("verified");
                if (IsPrivate) parts.Add("private account");
                if (!string.IsNullOrWhiteSpace(Subtitle)) parts.Add(Subtitle);
            }
            else if (!string.IsNullOrWhiteSpace(Subtitle))
            {
                parts.Add(Subtitle);
            }
            return string.Join(". ", parts) + ". Press Enter to open.";
        }
    }
}
