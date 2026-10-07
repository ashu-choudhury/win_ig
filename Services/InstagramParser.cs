using System.Text.Json;
using WinInstagram.Models;

namespace WinInstagram.Services;

public static class InstagramParser
{
    /// <summary>Field names Instagram has used for an image's alt text across API versions.</summary>
    private static readonly string[] AltTextFields = { "accessibility_caption", "alt_text", "alt" };

    public static List<FeedPost> ParseTimelineFeed(string json)
    {
        var posts = new List<FeedPost>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanForPosts(doc.RootElement, posts);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing timeline feed JSON", ex);
        }
        return posts;
    }

    public static List<InstagramComment> ParseComments(string json)
    {
        var list = new List<InstagramComment>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanForComments(doc.RootElement, list);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing comments JSON", ex);
        }
        return list;
    }

    /// <summary>
    /// Parses Instagram direct_v2 inbox payloads into accessible conversation summaries.
    /// A "thread object" is any JSON object carrying a thread_id, so the same scanner
    /// handles both inbox listings and individual thread responses.
    /// </summary>
    public static List<DirectConversation> ParseDirectConversations(string json, string? viewerId = null)
    {
        var list = new List<DirectConversation>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanForThreads(doc.RootElement, list, viewerId);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing direct inbox JSON", ex);
        }
        return list;
    }

    /// <summary>
    /// Parses the messages of a direct_v2 thread response. viewerId (the ds_user_id cookie)
    /// is used to mark outgoing messages so they can be announced as "You said" .
    /// </summary>
    public static List<DirectMessage> ParseDirectMessages(string json, string? viewerId = null)
    {
        var list = new List<DirectMessage>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanForDirectItems(doc.RootElement, list, viewerId);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing direct message JSON", ex);
        }
        return list;
    }

    private static void ScanForThreads(JsonElement element, List<DirectConversation> threads, string? viewerId)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("thread_id", out var threadIdProp))
            {
                var convo = ParseSingleThread(element, threadIdProp, viewerId);
                if (convo != null && !string.IsNullOrWhiteSpace(convo.ThreadId) &&
                    !threads.Any(t => t.ThreadId == convo.ThreadId))
                {
                    threads.Add(convo);
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForThreads(prop.Value, threads, viewerId);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForThreads(item, threads, viewerId);
                }
            }
        }
    }

    private static DirectConversation? ParseSingleThread(JsonElement thread, JsonElement threadIdProp, string? viewerId)
    {
        try
        {
            var convo = new DirectConversation
            {
                ThreadId = threadIdProp.ValueKind == JsonValueKind.String
                    ? threadIdProp.GetString() ?? string.Empty
                    : threadIdProp.ToString()
            };
            if (string.IsNullOrWhiteSpace(convo.ThreadId)) return null;

            if (thread.TryGetProperty("is_group", out var isGroup) && isGroup.ValueKind == JsonValueKind.True)
                convo.IsGroup = true;

            // Participants: skip ourselves so 1:1 threads are named after the other person.
            if (thread.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array)
            {
                foreach (var user in users.EnumerateArray())
                {
                    if (user.ValueKind != JsonValueKind.Object) continue;
                    var username = user.TryGetProperty("username", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                    var fullName = user.TryGetProperty("full_name", out var f) ? f.GetString() ?? string.Empty : string.Empty;
                    var avatar = user.TryGetProperty("profile_pic_url", out var p) ? p.GetString() ?? string.Empty : string.Empty;
                    var pk = user.TryGetProperty("pk", out var pkProp) ? pkProp.ToString() : string.Empty;
                    var isMe = !string.IsNullOrWhiteSpace(viewerId) && pk == viewerId;

                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        convo.Participants.Add(username);
                    }

                    if (!isMe && string.IsNullOrWhiteSpace(convo.RecipientUsername))
                    {
                        convo.RecipientUsername = username;
                        convo.RecipientFullName = fullName;
                        convo.RecipientAvatarUrl = avatar;
                    }
                }
            }

            if (thread.TryGetProperty("thread_title", out var title) && thread.TryGetProperty("is_group", out var g2) && g2.ValueKind == JsonValueKind.True)
            {
                var t = title.GetString();
                if (!string.IsNullOrWhiteSpace(t)) convo.RecipientFullName = t!;
            }

            // Unread state: 0 means unread in Instagram's own payloads.
            int unread = 0;
            if (thread.TryGetProperty("read_state", out var readState) && readState.ValueKind == JsonValueKind.Number && readState.GetInt32() == 0)
                unread = 1;
            if (thread.TryGetProperty("unread_count", out var unreadProp) && unreadProp.ValueKind == JsonValueKind.Number)
                unread = Math.Max(unread, unreadProp.GetInt32());
            convo.UnreadCount = unread;

            // Preview text may live on last_permanent_item, items[last], or last_activity_at.
            JsonElement previewItem = default;
            var hasPreview = false;
            if (thread.TryGetProperty("last_permanent_item", out var lpi) && lpi.ValueKind == JsonValueKind.Object)
            {
                previewItem = lpi;
                hasPreview = true;
            }
            else if (thread.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
            {
                previewItem = items[items.GetArrayLength() - 1];
                hasPreview = true;
            }

            if (hasPreview)
            {
                convo.LastMessage = DescribeItem(previewItem, viewerId);
                if (previewItem.ValueKind == JsonValueKind.Object && previewItem.TryGetProperty("timestamp", out var ts))
                    convo.LastTimestamp = FormatTimestamp(ts);
            }

            if (string.IsNullOrWhiteSpace(convo.LastTimestamp) &&
                thread.TryGetProperty("last_activity_at", out var laa))
            {
                convo.LastTimestamp = FormatTimestamp(laa);
            }

            return convo;
        }
        catch
        {
            return null;
        }
    }

    private static void ScanForDirectItems(JsonElement element, List<DirectMessage> messages, string? viewerId)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            // Only descend into message arrays that belong to a thread object.
            if (element.TryGetProperty("thread_id", out _) &&
                element.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                var participants = BuildParticipantMap(element);
                foreach (var item in items.EnumerateArray())
                {
                    var msg = ParseSingleDirectItem(item, viewerId, messages.Count, participants);
                    if (msg != null && (!string.IsNullOrWhiteSpace(msg.Text) || msg.ItemType != "text") &&
                        !messages.Any(m => !string.IsNullOrWhiteSpace(m.Id) && m.Id == msg.Id))
                    {
                        messages.Add(msg);
                    }
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForDirectItems(prop.Value, messages, viewerId);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForDirectItems(item, messages, viewerId);
                }
            }
        }
    }

    /// <summary>Maps participant pk to username so each message can name its real sender.</summary>
    private static Dictionary<string, string> BuildParticipantMap(JsonElement thread)
    {
        var map = new Dictionary<string, string>();
        try
        {
            if (!thread.TryGetProperty("users", out var users) || users.ValueKind != JsonValueKind.Array) return map;

            foreach (var user in users.EnumerateArray())
            {
                if (user.ValueKind != JsonValueKind.Object) continue;
                if (!user.TryGetProperty("pk", out var pk)) continue;
                if (!user.TryGetProperty("username", out var un)) continue;

                var username = un.GetString();
                if (!string.IsNullOrWhiteSpace(username))
                {
                    map[pk.ToString()] = username!;
                }
            }
        }
        catch { }
        return map;
    }

    private static DirectMessage? ParseSingleDirectItem(JsonElement item, string? viewerId, int index, Dictionary<string, string>? participants)
    {
        try
        {
            if (item.ValueKind != JsonValueKind.Object) return null;

            var itemType = item.TryGetProperty("item_type", out var it) ? it.GetString() ?? "text" : "text";
            var userId = item.TryGetProperty("user_id", out var uid) ? uid.ToString() : string.Empty;

            var msg = new DirectMessage
            {
                Id = item.TryGetProperty("item_id", out var iid)
                    ? iid.GetString() ?? $"dm_{index}"
                    : $"dm_{index}",
                ItemType = itemType ?? "text",
                IsFromMe = !string.IsNullOrWhiteSpace(viewerId) && userId == viewerId
            };

            if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                msg.Text = text.GetString() ?? string.Empty;

            if (item.TryGetProperty("timestamp", out var ts))
                msg.Timestamp = FormatTimestamp(ts);

            if (msg.IsFromMe)
            {
                msg.SenderUsername = "You";
            }
            else if (participants != null && !string.IsNullOrWhiteSpace(userId) &&
                     participants.TryGetValue(userId, out var senderName) && !string.IsNullOrWhiteSpace(senderName))
            {
                msg.SenderUsername = senderName;
            }
            else
            {
                msg.SenderUsername = "Someone";
            }

            return msg;
        }
        catch
        {
            return null;
        }
    }

    private static string DescribeItem(JsonElement item, string? viewerId)
    {
        var type = item.TryGetProperty("item_type", out var it) ? it.GetString() ?? "text" : "text";
        var body = type switch
        {
            "text" => item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
            "media" => "[photo]",
            "clip" => "[reel]",
            "media_share" => "[post]",
            "raven_media" => "[disappearing media]",
            "voice_media" => "[voice message]",
            "animated_media" or "sticker" => "[sticker]",
            "like" => "[liked a message]",
            "action_log" => "[conversation event]",
            _ => "[message]"
        };

        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        var fromMe = !string.IsNullOrWhiteSpace(viewerId) &&
                     item.TryGetProperty("user_id", out var uid) && uid.ToString() == viewerId;
        return fromMe ? $"You: {body}" : body;
    }

    private static string FormatTimestamp(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n)) return FormatUnix(n);
        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            if (long.TryParse(s, out var parsed)) return FormatUnix(parsed);
            return s!;
        }
        return string.Empty;
    }

    private static string FormatUnix(long value)
    {
        try
        {
            // Instagram mixes seconds, milliseconds and microseconds across payloads.
            if (value > 1_000_000_000_000_000) value /= 1_000_000;
            else if (value > 1_000_000_000_000) value /= 1_000;
            if (value <= 0) return string.Empty;

            var dt = DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime();
            var delta = DateTimeOffset.Now - dt;
            if (delta.TotalMinutes < 1) return "just now";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} minutes ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} hours ago";
            if (delta.TotalDays < 7) return $"{(int)delta.TotalDays} days ago";
            return dt.ToString("g");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void ScanForPosts(JsonElement element, List<FeedPost> posts)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            // Check direct post object
            if (IsPostObject(element))
            {
                var post = ParseSinglePost(element);
                if (post != null && !string.IsNullOrWhiteSpace(post.Id) && !posts.Any(p => p.Id == post.Id))
                {
                    posts.Add(post);
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForPosts(prop.Value, posts);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForPosts(item, posts);
                }
            }
        }
    }

    private static bool IsPostObject(JsonElement el)
    {
        // Stories carry expiring_at and live in their own navigator; letting them through here
        // would fill the timeline with posts that vanish, so they are excluded.
        if (el.TryGetProperty("expiring_at", out _)) return false;

        return el.TryGetProperty("image_versions2", out _) ||
               el.TryGetProperty("carousel_media", out _) ||
               el.TryGetProperty("display_url", out _) ||
               (el.TryGetProperty("media", out var m) && m.ValueKind == JsonValueKind.Object &&
                (m.TryGetProperty("image_versions2", out _) || m.TryGetProperty("carousel_media", out _) || m.TryGetProperty("display_url", out _)));
    }

    /// <summary>
    /// Parses the stories tray (reels_tray) into navigable story entries. Tray payloads appear
    /// both as a flat tray array and as a graphql connection with edges, so both shapes are read.
    /// </summary>
    public static List<StoryItem> ParseStories(string json)
    {
        var stories = new List<StoryItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanForStories(doc.RootElement, stories);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing stories tray JSON", ex);
        }
        return stories;
    }

    private static void ScanForStories(JsonElement element, List<StoryItem> stories)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var story = ParseSingleStory(element);
            if (story != null && !string.IsNullOrWhiteSpace(story.Username) &&
                !stories.Any(s => s.Username == story.Username))
            {
                stories.Add(story);
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForStories(prop.Value, stories);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForStories(item, stories);
                }
            }
        }
    }

    private static StoryItem? ParseSingleStory(JsonElement element)
    {
        try
        {
            // Only a tray entry has both a user and a story item array; a plain post does not.
            if (!element.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;

            var username = user.TryGetProperty("username", out var u) ? u.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(username)) return null;

            var story = new StoryItem
            {
                Id = element.TryGetProperty("id", out var idProp)
                    ? (idProp.ValueKind == JsonValueKind.String ? idProp.GetString() ?? string.Empty : idProp.ToString())
                    : string.Empty,
                Username = username,
                UserFullName = user.TryGetProperty("full_name", out var f) ? f.GetString() ?? string.Empty : string.Empty,
                AvatarUrl = user.TryGetProperty("profile_pic_url", out var p) ? p.GetString() ?? string.Empty : string.Empty,
                ItemCount = items.GetArrayLength()
            };

            if (element.TryGetProperty("media_count", out var mc) && mc.ValueKind == JsonValueKind.Number)
                story.ItemCount = Math.Max(story.ItemCount, mc.GetInt32());
            if (element.TryGetProperty("seen", out var seen) && seen.ValueKind == JsonValueKind.Number)
                story.IsSeen = seen.GetInt64() > 0;
            else if (element.TryGetProperty("seen", out var seenBool) && seenBool.ValueKind == JsonValueKind.True)
                story.IsSeen = true;

            if (story.ItemCount > 0 && items[0].ValueKind == JsonValueKind.Object)
            {
                var first = items[0];
                if (first.TryGetProperty("image_versions2", out var iv) && iv.ValueKind == JsonValueKind.Object &&
                    iv.TryGetProperty("candidates", out var cands) && cands.ValueKind == JsonValueKind.Array && cands.GetArrayLength() > 0 &&
                    cands[0].TryGetProperty("url", out var url))
                {
                    story.ThumbnailUrl = url.GetString() ?? string.Empty;
                }
            }

            return story;
        }
        catch
        {
            return null;
        }
    }

    private static void ScanForComments(JsonElement element, List<InstagramComment> comments)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                JsonElement userObj = default;
                bool hasUser = (element.TryGetProperty("user", out userObj) && userObj.ValueKind == JsonValueKind.Object) ||
                               (element.TryGetProperty("owner", out userObj) && userObj.ValueKind == JsonValueKind.Object);

                if (hasUser)
                {
                    string idStr = "";
                    if (element.TryGetProperty("id", out var idProp))
                        idStr = idProp.ValueKind == JsonValueKind.String ? idProp.GetString() ?? "" : idProp.ToString();
                    else if (element.TryGetProperty("pk", out var pkProp))
                        idStr = pkProp.ToString();

                    string username = "user";
                    if (userObj.TryGetProperty("username", out var u))
                        username = u.GetString() ?? "user";

                    long likesCount = 0;
                    if (element.TryGetProperty("comment_like_count", out var lk) && lk.ValueKind == JsonValueKind.Number)
                        likesCount = lk.GetInt64();
                    else if (element.TryGetProperty("edge_liked_by", out var elb) && elb.ValueKind == JsonValueKind.Object &&
                             elb.TryGetProperty("count", out var elbCount) && elbCount.ValueKind == JsonValueKind.Number)
                        likesCount = elbCount.GetInt64();

                    bool isLiked = false;
                    if (element.TryGetProperty("has_liked_comment", out var hlc) && hlc.ValueKind == JsonValueKind.True)
                        isLiked = true;
                    else if (element.TryGetProperty("viewer_has_liked", out var vhl) && vhl.ValueKind == JsonValueKind.True)
                        isLiked = true;

                    string createdAtStr = "";
                    if (element.TryGetProperty("created_at", out var caProp))
                    {
                        if (caProp.ValueKind == JsonValueKind.Number)
                        {
                            var dt = DateTimeOffset.FromUnixTimeSeconds(caProp.GetInt64()).ToLocalTime();
                            createdAtStr = dt.ToString("g");
                        }
                        else if (caProp.ValueKind == JsonValueKind.String)
                        {
                            createdAtStr = caProp.GetString() ?? "";
                        }
                    }

                    var c = new InstagramComment
                    {
                        Id = string.IsNullOrWhiteSpace(idStr) ? Guid.NewGuid().ToString("N") : idStr,
                        Index = comments.Count,
                        Text = textProp.GetString() ?? "",
                        Username = username,
                        LikesCount = likesCount,
                        IsLiked = isLiked,
                        CreatedAt = createdAtStr
                    };

                    if (!string.IsNullOrWhiteSpace(c.Text) && !comments.Any(x => x.Id == c.Id))
                    {
                        comments.Add(c);
                    }
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForComments(prop.Value, comments);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForComments(item, comments);
                }
            }
        }
    }

    private static FeedPost? ParseSinglePost(JsonElement element)
    {
        try
        {
            if (element.TryGetProperty("media", out var m) && m.ValueKind == JsonValueKind.Object)
            {
                element = m;
            }

            var post = new FeedPost();

            if (element.TryGetProperty("id", out var id))
                post.Id = id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : id.ToString();
            else if (element.TryGetProperty("pk", out var pk))
                post.Id = pk.ToString();

            if (string.IsNullOrWhiteSpace(post.Id))
                post.Id = Guid.NewGuid().ToString("N");

            // Permalink shortcode, used to open the post natively in the engine.
            if (element.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.String)
                post.MediaCode = codeProp.GetString() ?? string.Empty;

            // Numeric pk for Instagram's like endpoint (id may be a base64-ish graphql id).
            if (element.TryGetProperty("pk", out var pkProp2))
                post.MediaPk = pkProp2.ToString();
            if (string.IsNullOrWhiteSpace(post.MediaPk) && long.TryParse(post.Id, out _))
                post.MediaPk = post.Id;

            // Relative posting time so screen readers hear "3 hours ago" rather than nothing.
            if (element.TryGetProperty("taken_at", out var takenAt))
                post.Timestamp = FormatTimestamp(takenAt);
            else if (element.TryGetProperty("taken_at_timestamp", out var takenAtTs))
                post.Timestamp = FormatTimestamp(takenAtTs);

            if ((element.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object && user.TryGetProperty("username", out var uName)) ||
                (element.TryGetProperty("owner", out var owner) && owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty("username", out uName)))
            {
                post.Username = uName.GetString() ?? "instagram_user";
            }

            if (element.TryGetProperty("caption", out var cap) && cap.ValueKind == JsonValueKind.Object &&
                cap.TryGetProperty("text", out var capText))
            {
                post.Caption = capText.GetString() ?? "";
            }
            else if (element.TryGetProperty("edge_media_to_caption", out var emtc) && emtc.ValueKind == JsonValueKind.Object &&
                     emtc.TryGetProperty("edges", out var edges) && edges.ValueKind == JsonValueKind.Array && edges.GetArrayLength() > 0)
            {
                var first = edges[0];
                if (first.TryGetProperty("node", out var n) && n.TryGetProperty("text", out var t))
                {
                    post.Caption = t.GetString() ?? "";
                }
            }

            // Media URL (single candidate, carousel first item, or display_url)
            if (element.TryGetProperty("image_versions2", out var imgVers) && imgVers.ValueKind == JsonValueKind.Object &&
                imgVers.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0 &&
                candidates[0].ValueKind == JsonValueKind.Object &&
                candidates[0].TryGetProperty("url", out var imgUrl))
            {
                post.MediaUrl = imgUrl.GetString() ?? "";
            }
            else if (element.TryGetProperty("carousel_media", out var carMedia) && carMedia.ValueKind == JsonValueKind.Array && carMedia.GetArrayLength() > 0)
            {
                var firstCar = carMedia[0];
                if (firstCar.TryGetProperty("image_versions2", out var carImg) && carImg.TryGetProperty("candidates", out var carCand) &&
                    carCand.ValueKind == JsonValueKind.Array && carCand.GetArrayLength() > 0 &&
                    carCand[0].TryGetProperty("url", out var carUrl))
                {
                    post.MediaUrl = carUrl.GetString() ?? "";
                }
            }
            else if (element.TryGetProperty("display_url", out var dispUrl))
            {
                post.MediaUrl = dispUrl.GetString() ?? "";
            }

            post.IsVideo = element.TryGetProperty("video_versions", out _) ||
                           (element.TryGetProperty("is_video", out var iv) && iv.ValueKind == JsonValueKind.True);

            if (element.TryGetProperty("like_count", out var lc))
                post.LikesCount = lc.ValueKind == JsonValueKind.Number ? lc.GetInt64() : 0;
            else if (element.TryGetProperty("edge_media_preview_like", out var edgeLikes) && edgeLikes.TryGetProperty("count", out var elc))
                post.LikesCount = elc.ValueKind == JsonValueKind.Number ? elc.GetInt64() : 0;

            if (element.TryGetProperty("comment_count", out var cc))
                post.CommentsCount = cc.ValueKind == JsonValueKind.Number ? cc.GetInt64() : 0;
            else if (element.TryGetProperty("edge_media_to_comment", out var edgeComments) && edgeComments.TryGetProperty("count", out var ecc))
                post.CommentsCount = ecc.ValueKind == JsonValueKind.Number ? ecc.GetInt64() : 0;

            // Whether the viewer already liked this post. Without this the native feed would
            // show "Like" on an already liked post and then try to like it a second time.
            if (element.TryGetProperty("has_liked", out var postLiked) && postLiked.ValueKind == JsonValueKind.True)
                post.IsLiked = true;
            else if (element.TryGetProperty("viewer_has_liked", out var viewerLiked) && viewerLiked.ValueKind == JsonValueKind.True)
                post.IsLiked = true;

            // Alt text is the only description of what the image actually shows, so a screen
            // reader user has no other way to learn it.
            foreach (var altName in AltTextFields)
            {
                if (element.TryGetProperty(altName, out var altProp) && altProp.ValueKind == JsonValueKind.String)
                {
                    var alt = altProp.GetString();
                    if (!string.IsNullOrWhiteSpace(alt))
                    {
                        post.AltText = alt!.Trim();
                        break;
                    }
                }
            }

            // Carousel slide count, so the list can say "carousel of five" and the panel can step it.
            if (element.TryGetProperty("carousel_media", out var carousel) && carousel.ValueKind == JsonValueKind.Array)
            {
                post.CarouselCount = carousel.GetArrayLength();
            }
            else if (element.TryGetProperty("carousel_media_count", out var carouselCountProp) && carouselCountProp.ValueKind == JsonValueKind.Number)
            {
                post.CarouselCount = carouselCountProp.GetInt32();
            }

            return post;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a web_profile_info payload into a readable profile card. The same shape is nested
    /// under data.user on graphql responses, so the scan looks for the innermost object that has a
    /// username and any of the profile-only counters.
    /// </summary>
    public static ProfileCard? ParseProfile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var card = ScanForProfile(doc.RootElement);
            if (card != null && string.IsNullOrWhiteSpace(card.Username))
            {
                // Older payloads keep the username inside data.user rather than at the top level.
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("user", out var user))
                {
                    card.Username = user.TryGetProperty("username", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                }
            }
            return card;
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing profile JSON", ex);
            return null;
        }
    }

    private static ProfileCard? ScanForProfile(JsonElement element)
    {
        ProfileCard? best = null;

        if (element.ValueKind == JsonValueKind.Object)
        {
            var hasUsername = element.TryGetProperty("username", out _);
            var looksLikeProfile = hasUsername &&
                (element.TryGetProperty("edge_followed_by", out _) ||
                 element.TryGetProperty("follower_count", out _) ||
                 element.TryGetProperty("edge_owner_to_timeline_media", out _) ||
                 element.TryGetProperty("biography", out _) ||
                 element.TryGetProperty("is_private", out _) ||
                 element.TryGetProperty("full_name", out _) ||
                 element.TryGetProperty("media_count", out _));

            if (looksLikeProfile)
            {
                var candidate = BuildProfileCard(element);
                // Prefer the richest object, which is the one the web page itself renders from.
                if (candidate != null && (best == null || ScoreProfile(candidate) > ScoreProfile(best)))
                {
                    best = candidate;
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    var nested = ScanForProfile(prop.Value);
                    if (nested != null && (best == null || ScoreProfile(nested) > ScoreProfile(best)))
                    {
                        best = nested;
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    var nested = ScanForProfile(item);
                    if (nested != null && (best == null || ScoreProfile(nested) > ScoreProfile(best)))
                    {
                        best = nested;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>How much real information a candidate profile object carries.</summary>
    private static int ScoreProfile(ProfileCard card)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(card.FullName)) score++;
        if (!string.IsNullOrWhiteSpace(card.Biography)) score++;
        if (card.FollowerCount > 0) score += 2;
        if (card.PostCount > 0) score += 2;
        return score;
    }

    private static ProfileCard BuildProfileCard(JsonElement element)
    {
        var card = new ProfileCard
        {
            Username = element.TryGetProperty("username", out var u) ? u.GetString() ?? string.Empty : string.Empty,
            FullName = element.TryGetProperty("full_name", out var f) ? f.GetString() ?? string.Empty : string.Empty,
            Biography = element.TryGetProperty("biography", out var b) ? b.GetString() ?? string.Empty : string.Empty,
            ExternalUrl = element.TryGetProperty("external_url", out var e) ? e.GetString() ?? string.Empty : string.Empty,
            ProfilePicUrl = element.TryGetProperty("profile_pic_url_hd", out var ph) ? ph.GetString() ?? string.Empty
                : element.TryGetProperty("profile_pic_url", out var p) ? p.GetString() ?? string.Empty : string.Empty,
            IsPrivate = element.TryGetProperty("is_private", out var ip) && ip.ValueKind == JsonValueKind.True,
            IsVerified = element.TryGetProperty("is_verified", out var iv) && iv.ValueKind == JsonValueKind.True,
            IsBusiness = element.TryGetProperty("is_business_account", out var ib) && ib.ValueKind == JsonValueKind.True,
            BusinessCategory = element.TryGetProperty("business_category_name", out var bc) ? bc.GetString() ?? string.Empty
                : element.TryGetProperty("category_name", out var cn) ? cn.GetString() ?? string.Empty : string.Empty
        };

        card.FollowerCount = ReadCount(element, "edge_followed_by") ?? ReadCount(element, "follower_count") ?? 0;
        card.FollowingCount = ReadCount(element, "edge_follow") ?? ReadCount(element, "following_count") ?? 0;
        card.PostCount = ReadCount(element, "edge_owner_to_timeline_media") ?? ReadCount(element, "media_count") ?? 0;

        return card;
    }

    /// <summary>Reads the count of either a {"count": n} edge object or a plain number property.</summary>
    private static long? ReadCount(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)) return n;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number)
            return c.GetInt64();
        return null;
    }

    /// <summary>Parses a saved-posts payload. Saved items are ordinary post shapes, so the post scanner is reused.</summary>
    public static List<FeedPost> ParseSavedPosts(string json)
    {
        var posts = new List<FeedPost>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanForPosts(doc.RootElement, posts);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing saved posts JSON", ex);
        }
        return posts;
    }

    /// <summary>Parses the account's saved collections into named, countable entries.</summary>
    public static List<SavedCollection> ParseCollections(string json)
    {
        var collections = new List<SavedCollection>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanForCollections(doc.RootElement, collections);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing collections JSON", ex);
        }
        return collections;
    }

    private static void ScanForCollections(JsonElement element, List<SavedCollection> collections)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var hasId = element.TryGetProperty("collection_id", out var collectionId) ||
                        element.TryGetProperty("collection_pk", out collectionId);
            var hasName = element.TryGetProperty("collection_name", out var collectionName) ||
                          element.TryGetProperty("name", out collectionName);

            if (hasName && collectionName.ValueKind == JsonValueKind.String)
            {
                var name = collectionName.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name) && !collections.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    var collection = new SavedCollection
                    {
                        Name = name,
                        Id = hasId
                            ? (collectionId.ValueKind == JsonValueKind.String ? collectionId.GetString() ?? string.Empty : collectionId.ToString())
                            : string.Empty
                    };

                    if (element.TryGetProperty("collection_media_count", out var cmc) && cmc.ValueKind == JsonValueKind.Number)
                        collection.ItemCount = cmc.GetInt32();
                    else if (element.TryGetProperty("media_count", out var mc) && mc.ValueKind == JsonValueKind.Number)
                        collection.ItemCount = mc.GetInt32();

                    collections.Add(collection);
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForCollections(prop.Value, collections);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForCollections(item, collections);
                }
            }
        }
    }

    /// <summary>
    /// Parses Instagram's activity feed (news/inbox) into sentences. Every "story" is one person
    /// or group of people acting on you, with optional args describing the target post.
    /// </summary>
    public static List<ActivityItem> ParseActivity(string json)
    {
        var items = new List<ActivityItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanForActivity(doc.RootElement, items);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing activity JSON", ex);
        }
        return items;
    }

    private static void ScanForActivity(JsonElement element, List<ActivityItem> items)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("story_type", out var storyType) && storyType.ValueKind == JsonValueKind.String)
            {
                var item = BuildActivityItem(element, storyType.GetString() ?? string.Empty);
                if (item != null && !items.Any(i => i.Id == item.Id))
                {
                    items.Add(item);
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForActivity(prop.Value, items);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in element.EnumerateArray())
            {
                if (entry.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForActivity(entry, items);
                }
            }
        }
    }

    private static ActivityItem? BuildActivityItem(JsonElement element, string storyType)
    {
        try
        {
            var item = new ActivityItem
            {
                Type = storyType,
                Id = element.TryGetProperty("pk", out var pk)
                    ? pk.ToString()
                    : element.TryGetProperty("id", out var id)
                        ? (id.ValueKind == JsonValueKind.String ? id.GetString() ?? string.Empty : id.ToString())
                        : Guid.NewGuid().ToString("N")
            };

            if (element.TryGetProperty("timestamp", out var ts)) item.Timestamp = FormatTimestamp(ts);

            // The headline text is sometimes a plain string and sometimes a styled text object.
            if (element.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
            {
                if (args.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    item.Text = t.GetString() ?? string.Empty;
                else if (args.TryGetProperty("comment", out var comment) && comment.ValueKind == JsonValueKind.String)
                    item.Text = comment.GetString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(item.Text) &&
                    args.TryGetProperty("rich_text", out var rich) && rich.ValueKind == JsonValueKind.String)
                    item.Text = rich.GetString() ?? string.Empty;

                if (args.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array && media.GetArrayLength() > 0)
                {
                    var first = media[0];
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                        item.MediaCode = code.GetString() ?? string.Empty;
                }

                if (args.TryGetProperty("profile_id", out var profileId))
                    item.Username = profileId.ToString();
            }

            // Resolve the acting username through the nested user object, falling back to the id.
            if (element.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            {
                if (user.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(un.GetString()))
                    item.Username = un.GetString() ?? string.Empty;
                if (user.TryGetProperty("profile_pic_url", out var pic) && pic.ValueKind == JsonValueKind.String)
                    item.AvatarUrl = pic.GetString() ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(item.Username) && item.Username.All(char.IsDigit))
            {
                // A numeric id is not something to read out; say someone instead.
                item.Username = string.Empty;
            }

            return item;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a web search response. Accounts come from the topsearch shape and posts are picked
    /// up by the ordinary post scanner, so "search" covers both people and content.
    /// </summary>
    public static List<SearchResult> ParseSearch(string json)
    {
        var results = new List<SearchResult>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanForSearchResults(doc.RootElement, results);

            var posts = new List<FeedPost>();
            ScanForPosts(doc.RootElement, posts);
            foreach (var post in posts.Take(15))
            {
                if (results.Any(r => r.Kind == "post" && r.MediaCode == post.MediaCode && !string.IsNullOrWhiteSpace(post.MediaCode))) continue;
                results.Add(new SearchResult
                {
                    Kind = "post",
                    Username = post.Username,
                    MediaCode = post.MediaCode,
                    Subtitle = string.IsNullOrWhiteSpace(post.Caption) ? string.Empty : post.Caption
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing search JSON", ex);
        }
        return results;
    }

    private static void ScanForSearchResults(JsonElement element, List<SearchResult> results)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            // topsearch wraps each account in a {user:{...}} entry.
            if (element.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object &&
                user.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String)
            {
                var username = un.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(username) && !results.Any(r => r.Kind == "account" && r.Username == username))
                {
                    results.Add(new SearchResult
                    {
                        Kind = "account",
                        Username = username,
                        FullName = user.TryGetProperty("full_name", out var fn) ? fn.GetString() ?? string.Empty : string.Empty,
                        ProfilePicUrl = user.TryGetProperty("profile_pic_url", out var pic) ? pic.GetString() ?? string.Empty : string.Empty,
                        IsVerified = user.TryGetProperty("is_verified", out var iv) && iv.ValueKind == JsonValueKind.True,
                        IsPrivate = user.TryGetProperty("is_private", out var ip) && ip.ValueKind == JsonValueKind.True
                    });
                }
            }

            // Hashtag hits appear as {hashtag:{name:...}} or as {name:...} with a media_count.
            if (element.TryGetProperty("hashtag", out var hashtag) && hashtag.ValueKind == JsonValueKind.Object &&
                hashtag.TryGetProperty("name", out var tagName) && tagName.ValueKind == JsonValueKind.String)
            {
                var tag = tagName.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(tag) && !results.Any(r => r.Kind == "hashtag" && r.Tag == tag))
                {
                    var mediaCount = ReadCount(hashtag, "media_count");
                    results.Add(new SearchResult
                    {
                        Kind = "hashtag",
                        Tag = tag,
                        Subtitle = mediaCount.HasValue ? $"{mediaCount.Value:N0} posts" : string.Empty
                    });
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForSearchResults(prop.Value, results);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForSearchResults(item, results);
                }
            }
        }
    }
}
