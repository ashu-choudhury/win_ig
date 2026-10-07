using System.Text.Json;
using WinInstagram.Models;

namespace WinInstagram.Services;

public static class InstagramParser
{
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

            // Check Stories Tray (reels_tray)
            if (element.TryGetProperty("tray", out var tray) && tray.ValueKind == JsonValueKind.Array)
            {
                foreach (var userTray in tray.EnumerateArray())
                {
                    if (userTray.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var storyItem in items.EnumerateArray())
                        {
                            var storyPost = ParseSinglePost(storyItem);
                            if (storyPost != null && !string.IsNullOrWhiteSpace(storyPost.Id) && !posts.Any(p => p.Id == storyPost.Id))
                            {
                                storyPost.Caption = string.IsNullOrWhiteSpace(storyPost.Caption) 
                                    ? $"[Story by @{storyPost.Username}]" 
                                    : $"[Story @{storyPost.Username}] {storyPost.Caption}";
                                posts.Add(storyPost);
                            }
                        }
                    }
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
        return el.TryGetProperty("image_versions2", out _) ||
               el.TryGetProperty("carousel_media", out _) ||
               el.TryGetProperty("display_url", out _) ||
               (el.TryGetProperty("media", out var m) && m.ValueKind == JsonValueKind.Object &&
                (m.TryGetProperty("image_versions2", out _) || m.TryGetProperty("carousel_media", out _) || m.TryGetProperty("display_url", out _)));
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

            return post;
        }
        catch
        {
            return null;
        }
    }
}
