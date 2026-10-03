using System.Text.Json;
using WinInstagram.Models;

namespace WinInstagram.Services;

public static class InstagramParser
{
    public static List<ReelItem> ParseReels(string json)
    {
        var reels = new List<ReelItem>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            ScanForReels(doc.RootElement, reels);
        }
        catch (Exception ex)
        {
            AppLogger.Error("PARSER", "Error parsing reels JSON", ex);
        }
        return reels;
    }

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

    private static void ScanForReels(JsonElement element, List<ReelItem> reels)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("video_versions", out var vv) && vv.ValueKind == JsonValueKind.Array && vv.GetArrayLength() > 0)
            {
                var reel = ParseSingleReel(element);
                if (reel != null && !string.IsNullOrWhiteSpace(reel.VideoUrl) && !reels.Any(r => r.Id == reel.Id))
                {
                    reels.Add(reel);
                }
            }
            else if (element.TryGetProperty("media", out var m) && m.ValueKind == JsonValueKind.Object &&
                     m.TryGetProperty("video_versions", out var mvv) && mvv.ValueKind == JsonValueKind.Array && mvv.GetArrayLength() > 0)
            {
                var reel = ParseSingleReel(m);
                if (reel != null && !string.IsNullOrWhiteSpace(reel.VideoUrl) && !reels.Any(r => r.Id == reel.Id))
                {
                    reels.Add(reel);
                }
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForReels(prop.Value, reels);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    ScanForReels(item, reels);
                }
            }
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

    private static ReelItem? ParseSingleReel(JsonElement element)
    {
        try
        {
            var reel = new ReelItem();

            if (element.TryGetProperty("id", out var idProp))
                reel.Id = idProp.ValueKind == JsonValueKind.String ? idProp.GetString() ?? "" : idProp.ToString();
            else if (element.TryGetProperty("pk", out var pkProp))
                reel.Id = pkProp.ToString();

            if (element.TryGetProperty("code", out var codeProp))
                reel.MediaCode = codeProp.GetString() ?? "";

            if ((element.TryGetProperty("user", out var userObj) && userObj.ValueKind == JsonValueKind.Object) ||
                (element.TryGetProperty("owner", out userObj) && userObj.ValueKind == JsonValueKind.Object))
            {
                if (userObj.TryGetProperty("username", out var uName)) reel.Username = uName.GetString() ?? "instagram_user";
                if (userObj.TryGetProperty("full_name", out var fName)) reel.UserFullName = fName.GetString() ?? "";
                if (userObj.TryGetProperty("profile_pic_url", out var pPic)) reel.AvatarUrl = pPic.GetString() ?? "";
            }

            if (element.TryGetProperty("video_versions", out var vv) && vv.ValueKind == JsonValueKind.Array && vv.GetArrayLength() > 0)
            {
                var first = vv[0];
                if (first.TryGetProperty("url", out var vUrl)) reel.VideoUrl = vUrl.GetString() ?? "";
            }

            if (element.TryGetProperty("caption", out var capObj) && capObj.ValueKind == JsonValueKind.Object &&
                capObj.TryGetProperty("text", out var cText))
            {
                reel.Caption = cText.GetString() ?? "";
            }

            if (element.TryGetProperty("clips_metadata", out var clipsMeta) && clipsMeta.ValueKind == JsonValueKind.Object)
            {
                if (clipsMeta.TryGetProperty("audio_type", out var aType) &&
                    clipsMeta.TryGetProperty("music_info", out var mInfo) && mInfo.ValueKind == JsonValueKind.Object &&
                    mInfo.TryGetProperty("music_asset_info", out var mAsset) && mAsset.ValueKind == JsonValueKind.Object &&
                    mAsset.TryGetProperty("title", out var titleProp))
                {
                    var artist = mAsset.TryGetProperty("display_artist", out var artProp) ? artProp.GetString() : "";
                    reel.AudioTitle = string.IsNullOrWhiteSpace(artist) ? titleProp.GetString() ?? "Audio Track" : $"{titleProp.GetString()} - {artist}";
                }
                else if (clipsMeta.TryGetProperty("original_sound_info", out var origSound) && origSound.ValueKind == JsonValueKind.Object &&
                         origSound.TryGetProperty("original_audio_title", out var origTitle))
                {
                    reel.AudioTitle = origTitle.GetString() ?? "Original Audio";
                }
            }

            if (element.TryGetProperty("like_count", out var lkCount))
                reel.LikesCount = lkCount.ValueKind == JsonValueKind.Number ? lkCount.GetInt64() : 0;
            else if (element.TryGetProperty("edge_media_preview_like", out var edgeLikes) && edgeLikes.ValueKind == JsonValueKind.Object &&
                     edgeLikes.TryGetProperty("count", out var lkC))
                reel.LikesCount = lkC.ValueKind == JsonValueKind.Number ? lkC.GetInt64() : 0;

            if (element.TryGetProperty("comment_count", out var cmCount))
                reel.CommentsCount = cmCount.ValueKind == JsonValueKind.Number ? cmCount.GetInt64() : 0;
            else if (element.TryGetProperty("edge_media_to_comment", out var edgeComments) && edgeComments.ValueKind == JsonValueKind.Object &&
                     edgeComments.TryGetProperty("count", out var cmC))
                reel.CommentsCount = cmC.ValueKind == JsonValueKind.Number ? cmC.GetInt64() : 0;

            if (element.TryGetProperty("has_liked", out var hasLiked) && hasLiked.ValueKind == JsonValueKind.True)
                reel.IsLiked = true;

            return reel;
        }
        catch
        {
            return null;
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

            return post;
        }
        catch
        {
            return null;
        }
    }
}
