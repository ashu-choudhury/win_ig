using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using WinInstagram.Models;

namespace WinInstagram.Services;

public class InstagramBridgeService
{
    private static InstagramBridgeService? _instance;
    public static InstagramBridgeService Instance => _instance ??= new InstagramBridgeService();

    private CoreWebView2? _coreWebView2;
    private Dispatcher? _dispatcher;
    private DispatcherTimer? _authPollTimer;

    public bool IsInitialized => _coreWebView2 != null;
    public bool IsLoggedIn { get; private set; }

    /// <summary>The logged-in user's numeric id (ds_user_id cookie), used to attribute DM direction.</summary>
    public string? CurrentUserId { get; private set; }

    public event Action<ReelItem, bool, bool, double>? ActiveReelChanged;
    public event Action<List<FeedPost>>? FeedReceived;
    public event Action<List<InstagramComment>>? CommentsReceived;
    public event Action<List<DirectConversation>>? ConversationsReceived;
    public event Action<string, List<DirectMessage>>? DirectMessagesReceived;
    public event Action<bool>? LoginStatusChanged;
    public event Action<string>? StatusMessageUpdated;

    // Native discovery surfaces. Each one is filled from Instagram's own API response body, so the
    // native panels never guess at page content.
    public event Action<List<StoryItem>>? StoriesReceived;
    public event Action<ProfileCard, List<FeedPost>>? ProfileReceived;
    public event Action<List<FeedPost>>? SavedPostsReceived;
    public event Action<List<SavedCollection>>? CollectionsReceived;
    public event Action<List<ActivityItem>>? ActivityReceived;
    public event Action<List<SearchResult>>? SearchReceived;

    private const string WinInstagramScript = @"
    (function() {
        window.__winInstagram = window.__winInstagram || {};
        window.__winInstagram_unmuted = true;
        window.__winInstagram_volume = 1.0;

        function dispatchKey(keyName, codeName, keyCode) {
            try {
                const opts = { key: keyName, code: codeName, keyCode: keyCode, which: keyCode, bubbles: true, cancelable: true };
                document.dispatchEvent(new KeyboardEvent('keydown', opts));
                window.dispatchEvent(new KeyboardEvent('keydown', opts));
                if (document.activeElement && document.activeElement !== document.body) {
                    document.activeElement.dispatchEvent(new KeyboardEvent('keydown', opts));
                }
            } catch(e) {}
        }

        function triggerClick(el) {
            if (!el) return;
            try {
                el.focus();
                const opts = { bubbles: true, cancelable: true, view: window };
                el.dispatchEvent(new PointerEvent('pointerdown', opts));
                el.dispatchEvent(new MouseEvent('mousedown', opts));
                el.dispatchEvent(new PointerEvent('pointerup', opts));
                el.dispatchEvent(new MouseEvent('mouseup', opts));
                el.click();
            } catch(e) {
                try { el.click(); } catch(e2) {}
            }
        }

        function triggerDoubleTap(el) {
            if (!el) return;
            try {
                const rect = el.getBoundingClientRect();
                const clientX = rect.left + rect.width / 2;
                const clientY = rect.top + rect.height / 2;
                const opts = { bubbles: true, cancelable: true, view: window, clientX: clientX, clientY: clientY };
                el.dispatchEvent(new MouseEvent('dblclick', opts));
            } catch(e) {}
        }

        function findReelLikeButton(root) {
            if (!root) root = document;
            const btns = Array.from(root.querySelectorAll('button, div[role=""button""], [role=""button""]'));
            for (const b of btns) {
                const aria = (b.getAttribute('aria-label') || '').trim().toLowerCase();
                if (aria === 'like' || aria === 'unlike' || aria.startsWith('like') || aria.startsWith('unlike')) {
                    return b;
                }
            }

            const svgs = Array.from(root.querySelectorAll('svg'));
            for (const s of svgs) {
                const aria = (s.getAttribute('aria-label') || '').trim().toLowerCase();
                const titleEl = s.querySelector('title');
                const title = (titleEl ? titleEl.textContent || '' : '').trim().toLowerCase();
                if (aria === 'like' || aria === 'unlike' || title === 'like' || title === 'unlike' ||
                    aria.startsWith('like') || aria.startsWith('unlike')) {
                    return s.closest('button, div[role=""button""], [role=""button""]') || s.parentElement;
                }
            }

            for (const s of svgs) {
                const paths = Array.from(s.querySelectorAll('path'));
                for (const p of paths) {
                    const d = p.getAttribute('d') || '';
                    if (d.includes('34.6 3.1') || d.includes('16.792 3.904') || d.includes('1.246 7.414') || d.includes('20.84 4.61') || d.includes('21.35')) {
                        return s.closest('button, div[role=""button""], [role=""button""]') || s.parentElement;
                    }
                }
            }

            const commentBtn = root.querySelector('button[aria-label*=""Comment"" i], div[role=""button""][aria-label*=""Comment"" i], svg[aria-label*=""Comment"" i]');
            if (commentBtn) {
                const realCommentBtn = commentBtn.closest('button, div[role=""button""], [role=""button""]') || commentBtn;
                let bar = realCommentBtn.parentElement;
                for (let k = 0; k < 3 && bar && bar !== document.body; k++) {
                    const barBtns = Array.from(bar.querySelectorAll('button, div[role=""button""]'));
                    const idx = barBtns.indexOf(realCommentBtn);
                    if (idx > 0) return barBtns[idx - 1];
                    bar = bar.parentElement;
                }
            }
            return null;
        }

        function findReelCommentButton(root) {
            if (!root) root = document;
            const btns = Array.from(root.querySelectorAll('button, div[role=""button""], [role=""button""]'));
            for (const b of btns) {
                const aria = (b.getAttribute('aria-label') || '').trim().toLowerCase();
                if (aria === 'comment' || aria === 'comments' || aria.startsWith('comment')) return b;
            }
            const svgs = Array.from(root.querySelectorAll('svg'));
            for (const s of svgs) {
                const aria = (s.getAttribute('aria-label') || '').trim().toLowerCase();
                const titleEl = s.querySelector('title');
                const title = (titleEl ? titleEl.textContent || '' : '').trim().toLowerCase();
                if (aria.includes('comment') || title.includes('comment')) {
                    return s.closest('button, div[role=""button""], [role=""button""]') || s.parentElement;
                }
            }
            return null;
        }

        function checkIsLiked(btn) {
            if (!btn) return false;
            const btnAria = (btn.getAttribute('aria-label') || '').toLowerCase();
            if (btnAria.includes('unlike') || btnAria.includes('liked')) return true;

            const svg = btn.querySelector('svg') || (btn.tagName && btn.tagName.toLowerCase() === 'svg' ? btn : null);
            if (svg) {
                const svgAria = (svg.getAttribute('aria-label') || '').toLowerCase();
                if (svgAria.includes('unlike') || svgAria.includes('liked')) return true;

                const fill = (svg.getAttribute('fill') || '').toLowerCase();
                if (fill.includes('red') || fill.includes('rgb(255, 48, 64)') || fill.includes('ed4956') || fill.includes('ff3040')) return true;

                const color = (svg.getAttribute('color') || svg.style.color || svg.style.fill || '').toLowerCase();
                if (color.includes('red') || color.includes('rgb(255, 48, 64)') || color.includes('ed4956') || color.includes('ff3040')) return true;

                const paths = Array.from(svg.querySelectorAll('path'));
                for (const p of paths) {
                    const pFill = (p.getAttribute('fill') || '').toLowerCase();
                    if (pFill.includes('red') || pFill.includes('rgb(255, 48, 64)') || pFill.includes('ed4956') || pFill.includes('ff3040')) return true;
                }
            }
            return false;
        }

        function getActiveReel() {
            const videos = Array.from(document.querySelectorAll('video'));
            if (videos.length === 0) return null;

            const midY = window.innerHeight / 2;
            let activeVideo = null;
            let minDiff = Infinity;
            for (const v of videos) {
                const r = v.getBoundingClientRect();
                if (r.height === 0) continue;
                if (r.top <= midY && r.bottom >= midY) {
                    activeVideo = v;
                    break;
                }
                const diff = Math.abs((r.top + r.bottom) / 2 - midY);
                if (diff < minDiff) {
                    minDiff = diff;
                    activeVideo = v;
                }
            }
            if (!activeVideo) activeVideo = videos[0];

            let container = activeVideo.closest('article') ||
                            activeVideo.closest('div[role=""dialog""]');
            if (!container) {
                let p = activeVideo.parentElement;
                let candidate = null;
                while (p && p !== document.body && p !== document.documentElement) {
                    const hasAction = p.querySelector('button[aria-label*=""Like"" i], div[role=""button""][aria-label*=""Like"" i], svg[aria-label*=""Like"" i], [aria-label*=""Unlike"" i]');
                    if (hasAction) {
                        candidate = p;
                        const r = p.getBoundingClientRect();
                        if (r.height > 200 && r.height <= window.innerHeight * 1.5) {
                            container = p;
                            break;
                        }
                    }
                    p = p.parentElement;
                }
                if (!container && candidate) container = candidate;
            }
            if (!container) container = activeVideo.parentElement || document.body;
            return { video: activeVideo, container: container };
        }

        function syncState() {
            try {
                const info = getActiveReel();
                if (!info || !info.video) return;
                const v = info.video;
                const container = info.container || document;

                if (window.__winInstagram_unmuted && v.muted) {
                    v.muted = false;
                }

                // 1. Exact Creator Username
                let username = '';
                const links = Array.from(container.querySelectorAll('header a[href^=""/""], a[role=""link""][href^=""/""], a[href^=""/""]'));
                for (const a of links) {
                    const href = a.getAttribute('href') || '';
                    const m = href.match(/^\/([a-zA-Z0-9._]+)\/?$/);
                    if (m && m[1]) {
                        const cand = m[1];
                        const low = cand.toLowerCase();
                        if (!['explore', 'reels', 'direct', 'stories', 'accounts', 'p', 'about', 'legal', 'privacy', 'reel', 'tv'].includes(low)) {
                            const t = (a.innerText || '').trim();
                            if (t && t.toLowerCase() === low) {
                                username = cand;
                                break;
                            }
                            if (!username) username = cand;
                        }
                    }
                }

                if (!username) {
                    const imgs = Array.from(container.querySelectorAll('img[alt*=""profile picture"" i]'));
                    for (const img of imgs) {
                        const alt = img.getAttribute('alt') || '';
                        const m = alt.match(/^([^']+)'s profile picture/i) || alt.match(/profile picture of (.*)/i);
                        if (m && m[1]) {
                            username = m[1].trim();
                            break;
                        }
                    }
                }

                if (!username) {
                    const docLinks = Array.from(document.querySelectorAll('article header a[href^=""/""], a[role=""link""][href^=""/""]'));
                    for (const a of docLinks) {
                        const href = a.getAttribute('href') || '';
                        const m = href.match(/^\/([a-zA-Z0-9._]+)\/?$/);
                        if (m && m[1] && !['explore', 'reels', 'direct', 'stories'].includes(m[1].toLowerCase())) {
                            username = m[1];
                            break;
                        }
                    }
                }
                if (!username) username = 'Instagram User';

                // 2. Real Likes Count & Liked State
                let likes = '';
                let isLiked = false;
                const likeBtn = findReelLikeButton(container) || findReelLikeButton(document);
                if (likeBtn) {
                    isLiked = checkIsLiked(likeBtn);

                    // A. Check aria-label on the button itself e.g. 'Like, 12.4K people' or '12.4K likes'
                    const btnAria = likeBtn.getAttribute('aria-label') || '';
                    const matchAria = btnAria.match(/([0-9.,]+(\s*[KkMmBb])?)\s*(?:likes?|people|others)/i) ||
                                      btnAria.match(/([0-9.,]+(\s*[KkMmBb]))/i);
                    if (matchAria && matchAria[1]) {
                        likes = matchAria[1].trim();
                    }

                    // B. Check elements inside likeBtn
                    if (!likes) {
                        const inSpans = Array.from(likeBtn.querySelectorAll('span, div'));
                        for (const s of inSpans) {
                            const txt = (s.innerText || '').trim();
                            if (txt && /^[0-9.,]+[KkMmBb]?$/.test(txt)) {
                                likes = txt;
                                break;
                            }
                        }
                    }

                    // C. Check wrapper/parent around likeBtn (action item column cell)
                    if (!likes) {
                        const wrappers = [likeBtn.parentElement, likeBtn.parentElement?.parentElement];
                        for (const wrap of wrappers) {
                            if (!wrap) continue;
                            const wrapItems = Array.from(wrap.querySelectorAll('span, div, button'));
                            for (const item of wrapItems) {
                                if (item === likeBtn || likeBtn.contains(item)) continue;
                                const txt = (item.innerText || '').trim();
                                if (txt && /^[0-9.,]+[KkMmBb]?$/.test(txt)) {
                                    likes = txt;
                                    break;
                                }
                                const m = txt.match(/([0-9.,]+[KkMmBb]?)\s*(?:likes?|others)/i);
                                if (m && m[1]) {
                                    likes = m[1].trim();
                                    break;
                                }
                            }
                            if (likes) break;
                        }
                    }
                }

                // D. Check entire container for text matching likes
                if (!likes) {
                    const allSpans = Array.from(container.querySelectorAll('span, div, button'));
                    for (const s of allSpans) {
                        const txt = (s.innerText || '').trim();
                        const m = txt.match(/([0-9.,]+[KkMmBb]?)\s*(?:likes?|others)/i) ||
                                  txt.match(/liked by\s+.*?([0-9.,]+[KkMmBb]?)/i);
                        if (m && m[1]) {
                            likes = m[1].trim();
                            break;
                        }
                    }
                }

                // 3. Comments Count
                let commentsCount = '';
                const commentBtn = findReelCommentButton(container) || findReelCommentButton(document);
                if (commentBtn) {
                    const cAria = commentBtn.getAttribute('aria-label') || '';
                    const m = cAria.match(/([0-9.,]+(\s*[KkMmBb])?)\s*comments?/i);
                    if (m && m[1]) commentsCount = m[1].trim();

                    if (!commentsCount) {
                        const wrappers = [commentBtn.parentElement, commentBtn.parentElement?.parentElement];
                        for (const wrap of wrappers) {
                            if (!wrap) continue;
                            const wrapItems = Array.from(wrap.querySelectorAll('span, div'));
                            for (const item of wrapItems) {
                                if (item === commentBtn || commentBtn.contains(item)) continue;
                                const txt = (item.innerText || '').trim();
                                if (txt && /^[0-9.,]+[KkMmBb]?$/.test(txt)) {
                                    commentsCount = txt;
                                    break;
                                }
                            }
                            if (commentsCount) break;
                        }
                    }
                }

                // E. If likes or commentsCount still missing, extract numeric badges from action bar
                if (!likes || !commentsCount) {
                    const allBadges = [];
                    const allEls = Array.from(container.querySelectorAll('span, div'));
                    for (const el of allEls) {
                        if (el.children.length > 0) continue;
                        const txt = (el.innerText || '').trim();
                        if (/^[0-9.,]+[KkMmBb]?$/.test(txt) && txt.length <= 10 && !txt.includes(':') && !txt.startsWith('#')) {
                            allBadges.push(txt);
                        }
                    }
                    if (!likes && allBadges.length > 0) {
                        likes = allBadges[0];
                    }
                    if (!commentsCount && allBadges.length > 1) {
                        commentsCount = allBadges[1];
                    }
                }

                // Calculate numeric likesCount
                let numericLikes = 0;
                if (likes) {
                    const clean = likes.replace(/,/g, '').trim().toUpperCase();
                    if (clean.endsWith('K')) numericLikes = Math.round(parseFloat(clean) * 1000);
                    else if (clean.endsWith('M')) numericLikes = Math.round(parseFloat(clean) * 1000000);
                    else if (clean.endsWith('B')) numericLikes = Math.round(parseFloat(clean) * 1000000000);
                    else numericLikes = parseInt(clean, 10) || 0;
                }

                // 4. Caption
                let caption = '';
                const capEl = container.querySelector('h1') || container.querySelector('div[dir=""auto""] span');
                if (capEl) {
                    const t = (capEl.innerText || '').trim();
                    if (t.length > 2 && t !== username && !t.startsWith('#')) {
                        caption = t.length > 120 ? t.substring(0, 120) + '...' : t;
                    }
                }

                // 5. Audio Title
                let audio = 'Original Audio';
                const audioLink = container.querySelector('a[href*=""/audio/""]');
                if (audioLink) audio = (audioLink.innerText || '').trim();

                const payload = {
                    type: 'REEL_CHANGED',
                    data: {
                        id: window.location.href,
                        username: username,
                        caption: caption,
                        audioTitle: audio,
                        altText: (function() { try { const imgs = Array.from(container.querySelectorAll('img[alt]')); for (const img of imgs) { const a = (img.getAttribute('alt') || '').trim(); if (a && !/profile picture/i.test(a)) return a; } } catch(e) {} return ''; })(),
                        hasCaptions: !!(v.querySelectorAll && v.querySelectorAll('track').length > 0),
                        formattedLikes: likes,
                        likesCount: numericLikes,
                        commentsCount: commentsCount,
                        isLiked: isLiked,
                        isPlaying: !v.paused,
                        isMuted: v.muted,
                        volume: v.volume
                    }
                };
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage(JSON.stringify(payload));
                }
            } catch(e) {}
        }

        window.__winInstagram.scrapeComments = function() {
            try {
                const commentSection = document.querySelector('div[role=""dialog""]') ||
                                       document.querySelector('article') ||
                                       document;
                const found = [];
                const seen = new Set();
                const spans = Array.from(commentSection.querySelectorAll('span[dir=""auto""], div[dir=""auto""]'));
                for (const el of spans) {
                    const text = (el.innerText || '').trim();
                    if (!text || text.length < 1 || seen.has(text)) continue;

                    let p = el.parentElement;
                    let user = '';
                    let itemBox = null;
                    for (let step = 0; step < 6 && p && p !== document.body; step++) {
                        const a = p.querySelector('h3 a, a[role=""link""][href^=""/""], a[href^=""/""]');
                        if (a) {
                            const u = (a.innerText || a.getAttribute('href') || '').replace(/^\//, '').replace(/\/$/, '').trim();
                            if (u && !['explore', 'reels', 'direct', 'stories'].includes(u.toLowerCase()) && u !== text) {
                                user = u;
                                itemBox = p;
                                break;
                            }
                        }
                        p = p.parentElement;
                    }
                    if (user && itemBox) {
                        seen.add(text);
                        let isLiked = false;
                        let likesCount = 0;
                        let timeStr = '';

                        const likeSvg = itemBox.querySelector('svg[aria-label=""Like""], svg[aria-label=""Unlike""]');
                        if (likeSvg) isLiked = likeSvg.getAttribute('aria-label') === 'Unlike';

                        const timeEl = itemBox.querySelector('time');
                        if (timeEl) timeStr = (timeEl.innerText || timeEl.getAttribute('datetime') || '').trim();

                        const countSpans = Array.from(itemBox.querySelectorAll('span, button'));
                        for (const s of countSpans) {
                            const m = (s.innerText || '').match(/([0-9.,]+)\s*likes?/i);
                            if (m && m[1]) {
                                likesCount = parseInt(m[1].replace(/,/g, ''), 10) || 0;
                                break;
                            }
                        }

                        found.push({
                            id: 'dom_' + found.length,
                            index: found.length,
                            username: user,
                            text: text,
                            createdAt: timeStr,
                            likesCount: likesCount,
                            isLiked: isLiked
                        });
                        if (found.length >= 60) break;
                    }
                }
                if (found.length > 0) {
                    if (window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage(JSON.stringify({ type: 'COMMENTS_LOADED', data: found }));
                    }
                }
                return found.length;
            } catch(e) { return 0; }
        };

        window.__winInstagram.likeComment = function(commentIndex, username, text) {
            try {
                const commentSection = document.querySelector('div[role=""dialog""]') ||
                                       document.querySelector('article') ||
                                       document;
                const candidates = Array.from(commentSection.querySelectorAll('svg[aria-label=""Like""], svg[aria-label=""Unlike""], button[aria-label*=""Like"" i], div[role=""button""][aria-label*=""Like"" i]'));
                for (const item of candidates) {
                    let p = item.parentElement;
                    for (let step = 0; step < 6 && p && p !== document.body; step++) {
                        const t = p.innerText || '';
                        if ((username && t.includes(username)) || (text && t.includes(text))) {
                            const btn = item.closest('button, div[role=""button""]') || item;
                            triggerClick(btn);
                            setTimeout(() => window.__winInstagram.scrapeComments(), 300);
                            return true;
                        }
                        p = p.parentElement;
                    }
                }
                if (typeof commentIndex === 'number' && commentIndex >= 0 && commentIndex < candidates.length) {
                    const btn = candidates[commentIndex].closest('button, div[role=""button""]') || candidates[commentIndex];
                    triggerClick(btn);
                    setTimeout(() => window.__winInstagram.scrapeComments(), 300);
                    return true;
                }
                return false;
            } catch(e) { return false; }
        };

        window.__winInstagram.nextReel = function() {
            try {
                const isStories = window.location.href.includes('/stories/');
                if (isStories) {
                    const nextBtn = document.querySelector('button[aria-label=""Next""], div[role=""button""][aria-label=""Next""]');
                    if (nextBtn) {
                        nextBtn.click();
                    } else {
                        const el = document.elementFromPoint(window.innerWidth * 0.85, window.innerHeight * 0.5) || document.body;
                        el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: window.innerWidth * 0.85, clientY: window.innerHeight * 0.5 }));
                    }
                    dispatchKey('ArrowRight', 'ArrowRight', 39);
                    dispatchKey('PageDown', 'PageDown', 34);
                } else {
                    const btns = Array.from(document.querySelectorAll('button, div[role=""button""]'));
                    const nextBtn = btns.find(b => (b.getAttribute('aria-label') || '').toLowerCase().includes('next'));
                    if (nextBtn) nextBtn.click();

                    dispatchKey('ArrowDown', 'ArrowDown', 40);
                    dispatchKey('PageDown', 'PageDown', 34);

                    window.scrollBy({ top: window.innerHeight, behavior: 'smooth' });
                    const main = document.querySelector('main');
                    if (main) main.scrollBy({ top: window.innerHeight, behavior: 'smooth' });
                }
                setTimeout(syncState, 350);
                return true;
            } catch(e) { return false; }
        };

        window.__winInstagram.prevReel = function() {
            try {
                const isStories = window.location.href.includes('/stories/');
                if (isStories) {
                    const prevBtn = document.querySelector('button[aria-label=""Previous""], div[role=""button""][aria-label=""Previous""]');
                    if (prevBtn) {
                        prevBtn.click();
                    } else {
                        const el = document.elementFromPoint(window.innerWidth * 0.15, window.innerHeight * 0.5) || document.body;
                        el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: window.innerWidth * 0.15, clientY: window.innerHeight * 0.5 }));
                    }
                    dispatchKey('ArrowLeft', 'ArrowLeft', 37);
                    dispatchKey('PageUp', 'PageUp', 33);
                } else {
                    const btns = Array.from(document.querySelectorAll('button, div[role=""button""]'));
                    const prevBtn = btns.find(b => (b.getAttribute('aria-label') || '').toLowerCase().includes('previous'));
                    if (prevBtn) prevBtn.click();

                    dispatchKey('ArrowUp', 'ArrowUp', 38);
                    dispatchKey('PageUp', 'PageUp', 33);

                    window.scrollBy({ top: -window.innerHeight, behavior: 'smooth' });
                    const main = document.querySelector('main');
                    if (main) main.scrollBy({ top: -window.innerHeight, behavior: 'smooth' });
                }
                setTimeout(syncState, 350);
                return true;
            } catch(e) { return false; }
        };

        window.__winInstagram.togglePlay = function() {
            try {
                const info = getActiveReel();
                const v = (info && info.video) ? info.video : document.querySelector('video');
                if (v) {
                    if (v.paused) {
                        const p = v.play();
                        if (p && typeof p.catch === 'function') {
                            p.catch(() => {
                                dispatchKey(' ', 'Space', 32);
                            });
                        }
                    } else {
                        document.querySelectorAll('video').forEach(vid => {
                            try { vid.pause(); } catch(e) {}
                        });
                    }
                    syncState();
                    return !v.paused;
                }
                dispatchKey(' ', 'Space', 32);
                setTimeout(syncState, 150);
                return true;
            } catch(e) { return false; }
        };

        window.__winInstagram.toggleMute = function() {
            try {
                window.__winInstagram_unmuted = !window.__winInstagram_unmuted;
                const isMuted = !window.__winInstagram_unmuted;
                document.querySelectorAll('video').forEach(v => { v.muted = isMuted; });
                const btns = Array.from(document.querySelectorAll('button, div[role=""button""]'));
                const muteBtn = btns.find(b => (b.getAttribute('aria-label') || '').toLowerCase().includes('audio'));
                if (muteBtn) muteBtn.click();
                setTimeout(syncState, 150);
                return isMuted;
            } catch(e) { return false; }
        };

        window.__winInstagram.setVolume = function(vol) {
            try {
                window.__winInstagram_volume = vol;
                document.querySelectorAll('video').forEach(v => { v.volume = vol; });
            } catch(e) {}
        };

        window.__winInstagram.toggleLike = function() {
            try {
                const info = getActiveReel();
                const root = info?.container || document;
                const btn = findReelLikeButton(root) || findReelLikeButton(document);
                if (btn) {
                    triggerClick(btn);
                    setTimeout(syncState, 150);
                    setTimeout(syncState, 500);
                    return true;
                }
                if (info && info.video) {
                    triggerDoubleTap(info.video);
                }
                dispatchKey('l', 'KeyL', 76);
                setTimeout(syncState, 150);
                setTimeout(syncState, 500);
                return true;
            } catch(e) { return false; }
        };

        window.__winInstagram.toggleComments = function() {
            try {
                const info = getActiveReel();
                const root = info?.container || document;
                const btn = findReelCommentButton(root) || findReelCommentButton(document);
                if (btn) {
                    triggerClick(btn);
                    setTimeout(() => {
                        window.__winInstagram.scrapeComments();
                    }, 500);
                    setTimeout(() => {
                        window.__winInstagram.scrapeComments();
                    }, 1200);
                    return true;
                }
                dispatchKey('c', 'KeyC', 67);
                return true;
            } catch(e) { return false; }
        };

        window.__winInstagram.closeComments = function() {
            try {
                const closeBtn = document.querySelector('div[role=""dialog""] svg[aria-label=""Close""], svg[aria-label=""Close""]')?.closest('button, div[role=""button""]');
                if (closeBtn) {
                    closeBtn.click();
                    return true;
                }
                dispatchKey('Escape', 'Escape', 27);
                return true;
            } catch(e) { return false; }
        };

        window.__winInstagram.postComment = function(text) {
            try {
                if (!text) return false;
                const input = document.querySelector('form textarea, textarea[aria-label*=""comment"" i], textarea[placeholder*=""comment"" i], div[contenteditable=""true""][aria-label*=""comment"" i], textarea');
                if (!input) {
                    window.__winInstagram.toggleComments();
                    setTimeout(() => window.__winInstagram.postComment(text), 450);
                    return true;
                }

                input.focus();
                if (input.tagName === 'TEXTAREA') {
                    input.value = text;
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                } else {
                    input.innerText = text;
                    input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: text }));
                }

                setTimeout(() => {
                    const form = input.closest('form');
                    const formBtn = form ? form.querySelector('button[type=""submit""], div[role=""button""]') : null;
                    if (formBtn) {
                        formBtn.click();
                    } else {
                        const buttons = Array.from(document.querySelectorAll('button, div[role=""button""]'));
                        const postBtn = buttons.find(b => {
                            const t = (b.innerText || '').trim().toLowerCase();
                            return t === 'post' || t === 'send';
                        });
                        if (postBtn) postBtn.click();
                    }
                    setTimeout(window.__winInstagram.scrapeComments, 600);
                }, 200);
                return true;
            } catch(e) { return false; }
        };

        function findDirectComposer() {
            const selectors = [
                'textarea[placeholder*=""Message"" i]',
                'textarea[aria-label*=""Message"" i]',
                'div[contenteditable=""true""][role=""textbox""]',
                'div[contenteditable=""true""][aria-label*=""Message"" i]',
                'form textarea',
                'textarea'
            ];
            for (const sel of selectors) {
                const el = document.querySelector(sel);
                if (el) return el;
            }
            return null;
        }

        window.__winInstagram.getCsrfToken = function() {
            try {
                const match = document.cookie.match(/(?:^|;\s*)csrftoken=([^;]+)/);
                if (match && match[1]) return decodeURIComponent(match[1]);
                const meta = document.querySelector('meta[name=""csrf-token""]');
                return meta ? (meta.getAttribute('content') || '') : '';
            } catch(e) { return ''; }
        };

        // Likes a feed post through Instagram's own authenticated web endpoint, addressed
        // by media id. This is the same call the real web client makes and it lets the
        // native feed report the true outcome instead of assuming success.
        window.__winInstagram.likeMediaById = async function(mediaId, shouldLike) {
            try {
                if (!mediaId) return false;
                const action = shouldLike ? 'like' : 'unlike';
                const res = await fetch('/api/v1/web/likes/' + mediaId + '/' + action + '/', {
                    method: 'POST',
                    credentials: 'include',
                    headers: {
                        'x-csrftoken': window.__winInstagram.getCsrfToken(),
                        'x-ig-app-id': '936619743392459',
                        'x-requested-with': 'XMLHttpRequest'
                    }
                });
                return res.ok;
            } catch(e) { return false; }
        };

        // Types a reply into the engine's DM composer and submits it, so the native
        // messages panel can send without the user ever touching the web view.
        window.__winInstagram.sendDirectMessage = function(text) {
            try {
                if (!text) return false;
                const box = findDirectComposer();
                if (!box) return false;
                box.focus();
                if (box.tagName === 'TEXTAREA') {
                    box.value = text;
                    box.dispatchEvent(new Event('input', { bubbles: true }));
                    box.dispatchEvent(new Event('change', { bubbles: true }));
                } else {
                    box.innerText = text;
                    box.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: text }));
                }
                box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true }));
                setTimeout(() => {
                    try {
                        const btns = Array.from(document.querySelectorAll('button, div[role=""button""]'));
                        const send = btns.find(b => (b.innerText || '').trim().toLowerCase() === 'send');
                        if (send) send.click();
                    } catch(e) {}
                }, 250);
                return true;
            } catch(e) { return false; }
        };

        // The page area holding the media currently on screen (reel, story or open post).
        function activeArticle() {
            try {
                const info = getActiveReel();
                if (info && info.container) return info.container;
                return document.querySelector('article') || document.body;
            } catch(e) { return document.body; }
        }

        // Alt text of the media currently on screen, so a blind user learns what an image shows.
        window.__winInstagram.readActiveAltText = function() {
            try {
                const root = activeArticle();
                const imgs = Array.from(root.querySelectorAll('img[alt]'));
                for (const img of imgs) {
                    const alt = (img.getAttribute('alt') || '').trim();
                    if (alt && !/profile picture/i.test(alt)) return alt;
                }
                return '';
            } catch(e) { return ''; }
        };

        // Reads the WebVTT caption track attached to the playing video, when Instagram exposes one.
        // Returns the joined transcript, or an empty string when the reel has no captions.
        window.__winInstagram.readActiveCaptions = async function() {
            try {
                const info = getActiveReel();
                const video = (info && info.video) ? info.video : document.querySelector('video');
                if (!video) return '';
                const tracks = Array.from(video.querySelectorAll('track'));
                if (tracks.length === 0) return '';
                const track = tracks.find(t => {
                    const kind = (t.getAttribute('kind') || '').toLowerCase();
                    return kind === 'captions' || kind === 'subtitles' || t.hasAttribute('default');
                }) || tracks[0];
                const src = track.getAttribute('src') || track.src || '';
                if (!src) return '';
                const res = await fetch(src, { credentials: 'include' });
                if (!res.ok) return '';
                const text = await res.text();
                const cues = [];
                for (const block of text.split(/\r?\n\r?\n/)) {
                    const lines = block.split(/\r?\n/).filter(l => {
                        const t = l.trim();
                        return t && !/^WEBVTT/.test(t) && !/^\d+$/.test(t) && !/-->/.test(t);
                    });
                    const joined = lines.join(' ').trim();
                    if (joined) cues.push(joined);
                }
                return cues.join(' ');
            } catch(e) { return ''; }
        };

        // Reads the 2 of 7 style slide indicator Instagram shows on a carousel.
        function carouselPosition(root) {
            try {
                const candidates = Array.from(root.querySelectorAll('span, div'));
                for (const c of candidates) {
                    const t = (c.innerText || '').trim();
                    const m = t.match(/^(\d+)\s*\/\s*(\d+)$/);
                    if (m) return { index: parseInt(m[1], 10), count: parseInt(m[2], 10) };
                }
            } catch(e) {}
            return null;
        }

        // Steps a carousel one slide and reports the resulting slide, so the caller can speak the
        // truth instead of assuming the click worked.
        window.__winInstagram.stepCarousel = function(direction) {
            return new Promise(resolve => {
                try {
                    const root = activeArticle();
                    const wanted = direction >= 0 ? 'next' : 'previous';
                    const btns = Array.from(root.querySelectorAll('button, div[role=""button""]'));
                    const btn = btns.find(b => {
                        const aria = (b.getAttribute('aria-label') || '').trim().toLowerCase();
                        return aria === wanted || aria.startsWith(wanted);
                    });
                    if (!btn) { resolve(null); return; }
                    triggerClick(btn);
                    setTimeout(() => {
                        const pos = carouselPosition(root);
                        let alt = '';
                        try { alt = window.__winInstagram.readActiveAltText(); } catch(e) {}
                        resolve({ index: pos ? pos.index : 0, count: pos ? pos.count : 0, altText: alt });
                    }, 400);
                } catch(e) { resolve(null); }
            });
        };

        // Fetches an Instagram endpoint from inside the authenticated page and hands the raw body
        // back to the native app, so native panels can be filled from Instagram's own API rather
        // than by scraping the page. Failures are reported with status 0.
        window.__winInstagram.apiGet = async function(url, tag) {
            let status = 0;
            let body = '';
            try {
                const res = await fetch(url, {
                    method: 'GET',
                    credentials: 'include',
                    headers: {
                        'x-ig-app-id': '936619743392459',
                        'x-requested-with': 'XMLHttpRequest'
                    }
                });
                status = res.status;
                body = await res.text();
            } catch(e) { status = 0; body = ''; }
            try {
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage(JSON.stringify({
                        type: 'API_JSON', tag: tag, url: url, status: status, body: body
                    }));
                }
            } catch(e) {}
            return status;
        };

        // The stories viewer lives at /stories/ and auto-advances on its own timer. These three
        // helpers let the native navigator step it deliberately and hold it still, and they report
        // honestly when there is no story on screen to control.
        function storyRoot() {
            try {
                if (!window.location.href.includes('/stories/')) return null;
                return document.querySelector('div[role=""dialog""]') || document.querySelector('section') || document.body;
            } catch(e) { return null; }
        }

        window.__winInstagram.storyState = function() {
            try {
                const root = storyRoot();
                if (!root) return { isStory: false, hasVideo: false, isPaused: false, username: '', altText: '' };

                let paused = null;
                const videos = Array.from(document.querySelectorAll('video'));
                for (const v of videos) {
                    const r = v.getBoundingClientRect();
                    if (r.height > 0) { paused = v.paused; break; }
                }

                let username = '';
                const links = Array.from(root.querySelectorAll('a[href^=""/""]'));
                for (const a of links) {
                    const m = (a.getAttribute('href') || '').match(/^\/([a-zA-Z0-9._]+)\/?$/);
                    if (m && m[1]) {
                        const cand = m[1].toLowerCase();
                        if (!['explore', 'reels', 'direct', 'stories', 'accounts'].includes(cand)) { username = m[1]; break; }
                    }
                }

                let altText = '';
                const imgs = Array.from(root.querySelectorAll('img[alt]'));
                for (const img of imgs) {
                    const alt = (img.getAttribute('alt') || '').trim();
                    if (alt && !/profile picture/i.test(alt)) { altText = alt; break; }
                }

                return {
                    isStory: true,
                    hasVideo: paused !== null,
                    isPaused: paused === true,
                    username: username,
                    altText: altText
                };
            } catch(e) { return null; }
        };

        window.__winInstagram.storyPause = function() {
            try {
                const videos = Array.from(document.querySelectorAll('video'));
                if (videos.length === 0) return false;
                window.__winInstagram_storyPaused = true;
                videos.forEach(v => { try { v.pause(); } catch(e) {} });
                return true;
            } catch(e) { return false; }
        };

        window.__winInstagram.storyResume = function() {
            try {
                const videos = Array.from(document.querySelectorAll('video'));
                if (videos.length === 0) return false;
                window.__winInstagram_storyPaused = false;
                videos.forEach(v => {
                    try {
                        const p = v.play();
                        if (p && typeof p.catch === 'function') p.catch(() => {});
                    } catch(e) {}
                });
                return true;
            } catch(e) { return false; }
        };

        // Steps to the next or previous story item and reports where it landed. Unlike the reel
        // handlers this never falls back to scrolling, because a story has no scroll.
        window.__winInstagram.storyNext = function() {
            try {
                const root = storyRoot();
                if (!root) return false;
                const btn = root.querySelector('button[aria-label=""Next""], div[role=""button""][aria-label=""Next""]');
                if (btn) { triggerClick(btn); return true; }
                const el = document.elementFromPoint(window.innerWidth * 0.85, window.innerHeight * 0.5);
                if (el) {
                    el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: window.innerWidth * 0.85, clientY: window.innerHeight * 0.5 }));
                    return true;
                }
                return false;
            } catch(e) { return false; }
        };

        window.__winInstagram.storyPrev = function() {
            try {
                const root = storyRoot();
                if (!root) return false;
                const btn = root.querySelector('button[aria-label=""Previous""], div[role=""button""][aria-label=""Previous""]');
                if (btn) { triggerClick(btn); return true; }
                const el = document.elementFromPoint(window.innerWidth * 0.15, window.innerHeight * 0.5);
                if (el) {
                    el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, clientX: window.innerWidth * 0.15, clientY: window.innerHeight * 0.5 }));
                    return true;
                }
                return false;
            } catch(e) { return false; }
        };

        // Names of the controls the native panels drive, checked against the live page so the app
        // can warn honestly when Instagram changes its markup and a control stops working.
        window.__winInstagram.probeSelectors = function() {
            try {
                const info = getActiveReel();
                const root = (info && info.container) ? info.container : (document.querySelector('article') || document.body);
                const buttons = Array.from(root.querySelectorAll('button, div[role=""button""]'));
                const hasLabelled = (wanted) => buttons.some(b => {
                    const a = (b.getAttribute('aria-label') || '').trim().toLowerCase();
                    return a === wanted || a.startsWith(wanted);
                });

                return {
                    likeButton: !!findReelLikeButton(root),
                    commentButton: !!findReelCommentButton(root),
                    videoElement: document.querySelectorAll('video').length > 0,
                    carouselNext: hasLabelled('next'),
                    carouselPrevious: hasLabelled('previous'),
                    messageComposer: !!findDirectComposer(),
                    storyNext: !!(document.querySelector('button[aria-label=""Next""], div[role=""button""][aria-label=""Next""]')),
                    csrfToken: !!window.__winInstagram.getCsrfToken()
                };
            } catch(e) { return null; }
        };

        // The direct URLs of whatever is on screen plus its readable text, so the app can save a
        // real copy of the media the user is looking at.
        window.__winInstagram.activeMediaUrls = function() {
            try {
                const info = getActiveReel();
                const videos = Array.from(document.querySelectorAll('video'));
                let videoUrl = '';
                if (videos.length > 0) videoUrl = videos[0].currentSrc || videos[0].src || '';

                const root = (info && info.container) ? info.container : (document.querySelector('article') || document.body);
                let imageUrl = '';
                const imgs = Array.from(root.querySelectorAll('img[src]'));
                for (const img of imgs) {
                    const src = img.getAttribute('src') || '';
                    const alt = img.getAttribute('alt') || '';
                    if (src && !/profile picture/i.test(alt) && !src.startsWith('data:')) { imageUrl = src; break; }
                }

                let altText = '';
                try { altText = window.__winInstagram.readActiveAltText(); } catch(e) {}

                let caption = '';
                const capEl = root.querySelector('h1') || root.querySelector('div[dir=""auto""] span');
                if (capEl) caption = (capEl.innerText || '').trim();

                let username = '';
                const link = root.querySelector('header a[href^=""/""]');
                if (link) {
                    const m = (link.getAttribute('href') || '').match(/^\/([a-zA-Z0-9._]+)\/?$/);
                    if (m && m[1]) username = m[1];
                }

                return {
                    videoUrl: videoUrl,
                    imageUrl: imageUrl,
                    altText: altText,
                    caption: caption,
                    username: username,
                    pageUrl: window.location.href
                };
            } catch(e) { return null; }
        };

        window.__winInstagram.sync = syncState;

        document.addEventListener('play', (e) => {
            if (e.target && e.target.tagName === 'VIDEO') {
                setTimeout(syncState, 200);
            }
        }, true);

        document.addEventListener('pause', (e) => {
            if (e.target && e.target.tagName === 'VIDEO') {
                setTimeout(syncState, 200);
            }
        }, true);
    })();
    ";

    private InstagramBridgeService() { }

    public async Task InitializeAsync(CoreWebView2 coreWebView2, Dispatcher dispatcher)
    {
        _coreWebView2 = coreWebView2;
        _dispatcher = dispatcher;

        AppLogger.Info("INIT", "Initializing CoreWebView2 listeners and network interceptors...");

        _coreWebView2.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;
        _coreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        _coreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        _coreWebView2.SourceChanged += CoreWebView2_SourceChanged;
        _coreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;

        try
        {
            await _coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(WinInstagramScript);
        }
        catch (Exception ex)
        {
            AppLogger.Error("INIT", "Failed to register document created script", ex);
        }

        // Periodic polling timer to detect login in initial transitions
        _authPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.0)
        };
        _authPollTimer.Tick += async (s, e) =>
        {
            if (!IsLoggedIn)
            {
                await CheckLoginStateAsync();
            }
        };
        _authPollTimer.Start();

        await CheckLoginStateAsync();
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
            {
                json = e.WebMessageAsJson;
            }

            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var typeProp))
            {
                var msgType = typeProp.GetString();
                if (msgType == "REEL_CHANGED")
                {
                    if (root.TryGetProperty("data", out var data))
                    {
                        long cCount = 0;
                        if (data.TryGetProperty("commentsCount", out var ccProp))
                        {
                            if (ccProp.ValueKind == JsonValueKind.Number) cCount = ccProp.GetInt64();
                            else if (ccProp.ValueKind == JsonValueKind.String && long.TryParse(ccProp.GetString(), out var parsedC)) cCount = parsedC;
                        }

                        long lCount = 0;
                        if (data.TryGetProperty("likesCount", out var lkcProp))
                        {
                            if (lkcProp.ValueKind == JsonValueKind.Number) lCount = lkcProp.GetInt64();
                            else if (lkcProp.ValueKind == JsonValueKind.String && long.TryParse(lkcProp.GetString(), out var parsedL)) lCount = parsedL;
                        }

                        var reel = new ReelItem
                        {
                            Id = data.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                            Username = data.TryGetProperty("username", out var uProp) ? uProp.GetString() ?? "" : "Instagram User",
                            Caption = data.TryGetProperty("caption", out var cProp) ? cProp.GetString() ?? "" : "",
                            AudioTitle = data.TryGetProperty("audioTitle", out var aProp) ? aProp.GetString() ?? "" : "Original Audio",
                            LikesCount = lCount,
                            FormattedLikes = data.TryGetProperty("formattedLikes", out var lProp) ? lProp.GetString() ?? "" : "",
                            CommentsCount = cCount,
                            IsLiked = data.TryGetProperty("isLiked", out var likedProp) && likedProp.GetBoolean(),
                            AltText = data.TryGetProperty("altText", out var altProp) ? altProp.GetString() ?? "" : "",
                            HasCaptions = data.TryGetProperty("hasCaptions", out var capProp) && capProp.ValueKind == JsonValueKind.True,
                        };

                        bool isPlaying = data.TryGetProperty("isPlaying", out var pProp) && pProp.GetBoolean();
                        bool isMuted = data.TryGetProperty("isMuted", out var mProp) && mProp.GetBoolean();
                        double vol = data.TryGetProperty("volume", out var vProp) ? vProp.GetDouble() : 1.0;

                        _dispatcher?.Invoke(() =>
                        {
                            ActiveReelChanged?.Invoke(reel, isPlaying, isMuted, vol);
                        });
                    }
                }
                else if (msgType == "API_JSON")
                {
                    HandleApiJson(root);
                }
                else if (msgType == "COMMENTS_LOADED")
                {
                    if (root.TryGetProperty("data", out var commentsArray) && commentsArray.ValueKind == JsonValueKind.Array)
                    {
                        var list = new List<InstagramComment>();
                        int idx = 0;
                        foreach (var item in commentsArray.EnumerateArray())
                        {
                            var comment = new InstagramComment
                            {
                                Id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : $"c_{idx}",
                                Index = item.TryGetProperty("index", out var ixProp) ? ixProp.GetInt32() : idx,
                                Username = item.TryGetProperty("username", out var uProp) ? uProp.GetString() ?? "user" : "user",
                                Text = item.TryGetProperty("text", out var tProp) ? tProp.GetString() ?? "" : "",
                                CreatedAt = item.TryGetProperty("createdAt", out var caProp) ? caProp.GetString() ?? "" : "",
                                LikesCount = item.TryGetProperty("likesCount", out var lcProp) ? lcProp.GetInt64() : 0,
                                IsLiked = item.TryGetProperty("isLiked", out var lkProp) && lkProp.GetBoolean()
                            };
                            if (!string.IsNullOrWhiteSpace(comment.Text))
                            {
                                list.Add(comment);
                            }
                            idx++;
                        }

                        if (list.Count > 0)
                        {
                            AppLogger.Success("DOM", $"Extracted {list.Count} comments from DOM!");
                            _dispatcher?.Invoke(() =>
                            {
                                CommentsReceived?.Invoke(list);
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("BRIDGE", "Error processing web message", ex);
        }
    }

    /// <summary>
    /// Routes a raw API body fetched from inside the page to the parser for its tag, so every
    /// native panel is filled from Instagram's own data rather than from page scraping.
    /// </summary>
    private void HandleApiJson(JsonElement root)
    {
        var tag = root.TryGetProperty("tag", out var tagProp) ? tagProp.GetString() ?? string.Empty : string.Empty;
        var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;
        var url = root.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;
        var status = root.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.Number
            ? statusProp.GetInt32()
            : 0;

        if (status < 200 || status >= 300 || string.IsNullOrWhiteSpace(body))
        {
            AppLogger.Warn("API", $"Instagram returned status {status} for {tag} ({url}).");
            if (tag == "activity") _dispatcher?.Invoke(() => ActivityReceived?.Invoke(new List<ActivityItem>()));
            else if (tag == "saved") _dispatcher?.Invoke(() => SavedPostsReceived?.Invoke(new List<FeedPost>()));
            else if (tag == "collections") _dispatcher?.Invoke(() => CollectionsReceived?.Invoke(new List<SavedCollection>()));
            else if (tag == "search") _dispatcher?.Invoke(() => SearchReceived?.Invoke(new List<SearchResult>()));
            return;
        }

        switch (tag)
        {
            case "stories":
            {
                var stories = InstagramParser.ParseStories(body);
                if (stories.Count > 0)
                {
                    AppLogger.Success("PARSER", $"Scan found {stories.Count} story tray entries!");
                    _dispatcher?.Invoke(() => StoriesReceived?.Invoke(stories));
                }
                break;
            }

            case "profile":
            {
                var profile = InstagramParser.ParseProfile(body);
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Username))
                {
                    // Recent posts travel in the same payload, so the profile view can list them.
                    var profilePosts = InstagramParser.ParseSavedPosts(body);
                    AppLogger.Success("PARSER", $"Scan found profile for @{profile.Username} with {profilePosts.Count} posts.");
                    _dispatcher?.Invoke(() => ProfileReceived?.Invoke(profile, profilePosts));
                }
                break;
            }

            case "saved":
            {
                var posts = InstagramParser.ParseSavedPosts(body);
                AppLogger.Success("PARSER", $"Scan found {posts.Count} saved posts.");
                _dispatcher?.Invoke(() => SavedPostsReceived?.Invoke(posts));
                break;
            }

            case "collections":
            {
                var collections = InstagramParser.ParseCollections(body);
                _dispatcher?.Invoke(() => CollectionsReceived?.Invoke(collections));
                break;
            }

            case "activity":
            {
                var items = InstagramParser.ParseActivity(body);
                AppLogger.Success("PARSER", $"Scan found {items.Count} activity entries.");
                _dispatcher?.Invoke(() => ActivityReceived?.Invoke(items));
                break;
            }

            case "search":
            {
                var results = InstagramParser.ParseSearch(body);
                _dispatcher?.Invoke(() => SearchReceived?.Invoke(results));
                break;
            }
        }
    }

    private async void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (_coreWebView2 == null) return;
        AppLogger.Info("NAV", $"Source URL changed to: {_coreWebView2.Source}");
        if (!IsLoggedIn)
        {
            await CheckLoginStateAsync();
        }
    }

    private async void CoreWebView2_HistoryChanged(object? sender, object e)
    {
        if (_coreWebView2 == null) return;
        if (!IsLoggedIn)
        {
            await CheckLoginStateAsync();
        }
    }

    private async void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var status = e.IsSuccess ? "Success" : $"Failed ({e.WebErrorStatus})";
        AppLogger.Info("NAV", $"Navigation completed: {status}");
        if (!IsLoggedIn)
        {
            await CheckLoginStateAsync();
        }
        else if (e.IsSuccess && (_coreWebView2?.Source ?? "").Contains("/reels/"))
        {
            await Task.Delay(500);
            await SyncActiveReelAsync();
        }
    }

    public async Task CheckLoginStateAsync()
    {
        if (_coreWebView2 == null) return;

        try
        {
            var cookieManager = _coreWebView2.CookieManager;
            var cookies = await cookieManager.GetCookiesAsync("https://www.instagram.com");
            var altCookies = await cookieManager.GetCookiesAsync("https://instagram.com");
            var allCookies = cookies.Concat(altCookies).DistinctBy(c => c.Name).ToList();

            var sessionCookie = allCookies.FirstOrDefault(c => c.Name.Equals("sessionid", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
            var userCookie = allCookies.FirstOrDefault(c => c.Name.Equals("ds_user_id", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));

            if (!string.IsNullOrWhiteSpace(userCookie?.Value))
            {
                CurrentUserId = userCookie.Value;
            }

            bool hasSessionCookie = sessionCookie != null;
            bool urlIsNotLogin = !string.IsNullOrEmpty(_coreWebView2.Source) &&
                                 _coreWebView2.Source.Contains("instagram.com") &&
                                 !_coreWebView2.Source.Contains("/accounts/login");

            bool domLoggedIn = false;
            try
            {
                var domCheckResult = await _coreWebView2.ExecuteScriptAsync(
                    "Boolean(document.querySelector('svg[aria-label=\"Home\"], svg[aria-label=\"Direct\"], a[href*=\"/reels/\"]'))"
                );
                domLoggedIn = domCheckResult != null && domCheckResult.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            bool authenticated = hasSessionCookie || (urlIsNotLogin && domLoggedIn);

            if (authenticated)
            {
                if (!IsLoggedIn)
                {
                    IsLoggedIn = true;
                    AppLogger.Success("AUTH", $"Login confirmed! (sessionid: {(hasSessionCookie ? "Found" : "None")}, ds_user_id: {userCookie?.Value ?? "N/A"}, DOM verified: {domLoggedIn})");
                    _dispatcher?.Invoke(() =>
                    {
                        LoginStatusChanged?.Invoke(true);
                        StatusMessageUpdated?.Invoke("Instagram session active.");
                    });
                }
            }
            else
            {
                if (IsLoggedIn)
                {
                    AppLogger.Warn("AUTH", "Session expired or user logged out.");
                    IsLoggedIn = false;
                    _dispatcher?.Invoke(() => LoginStatusChanged?.Invoke(false));
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("AUTH", "Error while checking cookies/login state", ex);
        }
    }

    private void CoreWebView2_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        var uri = e.Request.Uri;
        if (!uri.Contains("instagram.com")) return;

        // Only inspect the payload shapes we actually know how to parse.
        bool isDirect = uri.Contains("direct_v2");
        // The stories tray is its own surface: it feeds the stories navigator, not the timeline.
        bool isStories = !isDirect && uri.Contains("reels_tray");
        bool isFeed = !isDirect && !isStories && (uri.Contains("/api/v1/feed/timeline/") ||
                                    uri.Contains("/api/v1/feed/") || uri.Contains("/graphql/query") || uri.Contains("clips/home"));
        bool isComments = !isDirect && (uri.Contains("/api/v1/comments/") || uri.Contains("edge_media_to_parent_comment") || (uri.Contains("graphql") && uri.Contains("comment")));
        if (!isFeed && !isComments && !isDirect && !isStories) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var stream = await e.Response.GetContentAsync();
                if (stream == null) return;

                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(json)) return;

                if (isDirect)
                {
                    var conversations = InstagramParser.ParseDirectConversations(json, CurrentUserId);
                    var messages = InstagramParser.ParseDirectMessages(json, CurrentUserId);

                    if (conversations.Count > 0)
                    {
                        AppLogger.Success("PARSER", $"Scan found {conversations.Count} DM conversations!");
                        _dispatcher?.Invoke(() => ConversationsReceived?.Invoke(conversations));
                    }

                    if (messages.Count > 0)
                    {
                        var threadId = conversations.FirstOrDefault()?.ThreadId ?? string.Empty;
                        AppLogger.Success("PARSER", $"Scan found {messages.Count} direct messages!");
                        _dispatcher?.Invoke(() => DirectMessagesReceived?.Invoke(threadId, messages));
                    }
                }
                else if (isStories)
                {
                    var stories = InstagramParser.ParseStories(json);
                    if (stories.Count > 0)
                    {
                        AppLogger.Success("PARSER", $"Scan found {stories.Count} story tray entries!");
                        _dispatcher?.Invoke(() => StoriesReceived?.Invoke(stories));
                    }
                }
                else if (isFeed)
                {
                    // graphql responses are shared by many features, so only treat the body
                    // as a timeline when it carries timeline-specific markers.
                    bool looksLikeTimeline = uri.Contains("/api/v1/feed/timeline/") ||
                                             uri.Contains("/api/v1/feed/") ||
                                             json.Contains("xdt_api__v1__feed__timeline") ||
                                             json.Contains("media_or_ad") || json.Contains("timeline_feed");
                    if (!looksLikeTimeline) return;

                    var posts = InstagramParser.ParseTimelineFeed(json);
                    if (posts.Count > 0)
                    {
                        AppLogger.Success("PARSER", $"Scan found {posts.Count} Feed Posts!");
                        _dispatcher?.Invoke(() =>
                        {
                            StatusMessageUpdated?.Invoke($"Received {posts.Count} feed posts.");
                            FeedReceived?.Invoke(posts);
                        });
                    }
                }
                else if (isComments)
                {
                    var comments = InstagramParser.ParseComments(json);
                    if (comments.Count > 0)
                    {
                        AppLogger.Success("PARSER", $"Scan found {comments.Count} comments!");
                        _dispatcher?.Invoke(() => CommentsReceived?.Invoke(comments));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("NET", $"Background parse error for {uri}", ex);
            }
        });
    }

    public void Navigate(string url)
    {
        if (_coreWebView2 != null)
        {
            AppLogger.Info("NAV", $"Instructing WebView2 to navigate to: {url}");
            _coreWebView2.Navigate(url);
        }
    }

    public void NavigateToReels()
    {
        if (_coreWebView2 == null) return;
        var current = _coreWebView2.Source ?? "";
        if (!current.Contains("instagram.com/reels"))
        {
            AppLogger.Info("NAV", "Navigating WebView2 directly to https://www.instagram.com/reels/");
            _coreWebView2.Navigate("https://www.instagram.com/reels/");
        }
    }

    private async Task ExecuteBridgeMethodAsync(string methodName, string args = "")
    {
        if (_coreWebView2 == null) return;
        try
        {
            var callScript = string.IsNullOrEmpty(args)
                ? $"if (window.__winInstagram && typeof window.__winInstagram.{methodName} === 'function') {{ window.__winInstagram.{methodName}(); }} else {{ if (!window.__winInstagram) {{ {WinInstagramScript} }} window.__winInstagram && window.__winInstagram.{methodName} && window.__winInstagram.{methodName}(); }}"
                : $"if (window.__winInstagram && typeof window.__winInstagram.{methodName} === 'function') {{ window.__winInstagram.{methodName}({args}); }} else {{ if (!window.__winInstagram) {{ {WinInstagramScript} }} window.__winInstagram && window.__winInstagram.{methodName} && window.__winInstagram.{methodName}({args}); }}";

            await _coreWebView2.ExecuteScriptAsync(callScript);
        }
        catch (Exception ex)
        {
            AppLogger.Error("BRIDGE", $"Failed to execute {methodName}", ex);
        }
    }

    public async Task NextReelAsync()
    {
        if (_coreWebView2 == null) return;
        await ExecuteBridgeMethodAsync("nextReel");
    }

    public async Task PreviousReelAsync()
    {
        if (_coreWebView2 == null) return;
        await ExecuteBridgeMethodAsync("prevReel");
    }

    public async Task TogglePlayAsync()
    {
        if (_coreWebView2 == null) return;
        await ExecuteBridgeMethodAsync("togglePlay");
    }

    public async Task ToggleMuteAsync()
    {
        if (_coreWebView2 == null) return;
        await ExecuteBridgeMethodAsync("toggleMute");
    }

    public async Task SetVolumeAsync(double volume)
    {
        if (_coreWebView2 == null) return;
        var vStr = volume.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        await ExecuteBridgeMethodAsync("setVolume", vStr);
    }

    public async Task ToggleLikeAsync()
    {
        if (_coreWebView2 == null) return;
        await ExecuteBridgeMethodAsync("toggleLike");
    }

    public async Task ToggleCommentsAsync()
    {
        if (_coreWebView2 == null) return;
        await ExecuteBridgeMethodAsync("toggleComments");
    }

    public async Task PostCommentAsync(string commentText)
    {
        if (_coreWebView2 == null || string.IsNullOrWhiteSpace(commentText)) return;
        var escaped = JsonSerializer.Serialize(commentText);
        await ExecuteBridgeMethodAsync("postComment", escaped);
    }

    public async Task SyncActiveReelAsync()
    {
        if (_coreWebView2 == null) return;
        await ExecuteBridgeMethodAsync("sync");
    }

    public async Task LikeCommentAsync(int index, string username = "", string commentText = "")
    {
        if (_coreWebView2 == null) return;
        var uEsc = JsonSerializer.Serialize(username ?? "");
        var tEsc = JsonSerializer.Serialize(commentText ?? "");
        await ExecuteBridgeMethodAsync("likeComment", $"{index}, {uEsc}, {tEsc}");
    }

    public async Task CloseCommentsAsync()
    {
        if (_coreWebView2 == null) return;
        await ExecuteBridgeMethodAsync("closeComments");
    }

    public async Task ScrapeCommentsAsync()
    {
        if (_coreWebView2 == null) return;
        await ExecuteBridgeMethodAsync("scrapeComments");
    }

    /// <summary>
    /// Likes or unlikes a specific media item (feed post or reel) by id through Instagram's
    /// authenticated web endpoint. Returns the real outcome so callers can announce the truth
    /// instead of optimistically claiming success.
    /// </summary>
    public async Task<bool> SetMediaLikedAsync(string mediaId, bool shouldLike)
    {
        if (_coreWebView2 == null || string.IsNullOrWhiteSpace(mediaId)) return false;
        try
        {
            var idJson = JsonSerializer.Serialize(mediaId);
            var flag = shouldLike ? "true" : "false";
            var callScript = $"(async () => {{ if (!window.__winInstagram || typeof window.__winInstagram.likeMediaById !== 'function') {{ {WinInstagramScript} }} return await window.__winInstagram.likeMediaById({idJson}, {flag}); }})()";
            var result = await _coreWebView2.ExecuteScriptAsync(callScript);
            var ok = !string.IsNullOrWhiteSpace(result) && result.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            if (!ok) AppLogger.Warn("LIKE", $"Media {mediaId} {(shouldLike ? "like" : "unlike")} reported failure from Instagram.");
            return ok;
        }
        catch (Exception ex)
        {
            AppLogger.Error("LIKE", $"Failed to set liked state for media {mediaId}", ex);
            return false;
        }
    }

    /// <summary>
    /// Sends a direct message by typing into the engine's composer. Returns false when the
    /// composer is not present (for example the thread has not finished loading).
    /// </summary>
    public async Task<bool> SendDirectMessageAsync(string text)
    {
        if (_coreWebView2 == null || string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var escaped = JsonSerializer.Serialize(text);
            var callScript = $"(function() {{ if (!window.__winInstagram || typeof window.__winInstagram.sendDirectMessage !== 'function') {{ {WinInstagramScript} }} return window.__winInstagram.sendDirectMessage({escaped}); }})()";
            var result = await _coreWebView2.ExecuteScriptAsync(callScript);
            return !string.IsNullOrWhiteSpace(result) && result.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLogger.Error("DM", "Failed to send direct message", ex);
            return false;
        }
    }

    /// <summary>Where a carousel landed after a step, so the caller can announce the real slide.</summary>
    public record CarouselStepResult(bool Ok, int Index, int Count, string AltText);

    /// <summary>Steps a carousel in the open post by one slide and reports where it landed.</summary>
    public async Task<CarouselStepResult> StepCarouselAsync(int direction)
    {
        var el = await ExecuteJsonAsync($"window.__winInstagram.stepCarousel({(direction >= 0 ? 1 : -1)})");
        if (el == null) return new CarouselStepResult(false, 0, 0, string.Empty);

        var index = el.Value.TryGetProperty("index", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : 0;
        var count = el.Value.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
        var alt = el.Value.TryGetProperty("altText", out var a) && a.ValueKind == JsonValueKind.String
            ? a.GetString() ?? string.Empty
            : string.Empty;
        return new CarouselStepResult(true, index, count, alt);
    }

    /// <summary>Alt text of whatever media is on screen, or empty when there is none.</summary>
    public async Task<string> ReadActiveAltTextAsync()
    {
        var el = await ExecuteJsonAsync("window.__winInstagram.readActiveAltText()");
        if (el == null || el.Value.ValueKind != JsonValueKind.String) return string.Empty;
        return el.Value.GetString() ?? string.Empty;
    }

    /// <summary>Transcript of the playing video's caption track, or empty when it has none.</summary>
    public async Task<string> ReadActiveCaptionsAsync()
    {
        var el = await ExecuteJsonAsync("window.__winInstagram.readActiveCaptions()");
        if (el == null || el.Value.ValueKind != JsonValueKind.String) return string.Empty;
        return el.Value.GetString() ?? string.Empty;
    }

    /// <summary>
    /// Runs a page script that returns a value or a promise and parses the JSON WebView2 hands
    /// back, so callers can report what actually happened rather than assuming success.
    /// </summary>
    private async Task<JsonElement?> ExecuteJsonAsync(string expression)
    {
        if (_coreWebView2 == null) return null;
        try
        {
            var guarded = $"(async () => {{ if (!window.__winInstagram) {{ {WinInstagramScript} }} return await ({expression}); }})()";
            var raw = await _coreWebView2.ExecuteScriptAsync(guarded);
            if (string.IsNullOrWhiteSpace(raw) || raw == "null" || raw == "undefined") return null;

            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            AppLogger.Error("BRIDGE", $"Script failed: {expression}", ex);
            return null;
        }
    }

    // ---- Stories navigator -------------------------------------------------------------------

    /// <summary>What the story viewer is doing right now, as reported by the page itself.</summary>
    public record StoryState(bool IsStory, bool HasVideo, bool IsPaused, string Username, string AltText);

    /// <summary>The direct media URLs and readable text of whatever is on screen.</summary>
    public record ActiveMediaInfo(string VideoUrl, string ImageUrl, string AltText, string Caption, string Username, string PageUrl);

    /// <summary>
    /// The result of checking the page for every control the native panels drive. This exists so
    /// the app can warn a user out loud when Instagram changes its markup, instead of failing
    /// silently when a key press stops doing anything.
    /// </summary>
    public record SelectorHealthReport(Dictionary<string, bool> Probes)
    {
        private static readonly Dictionary<string, string> Labels = new()
        {
            ["likeButton"] = "like button",
            ["commentButton"] = "comments button",
            ["videoElement"] = "video player",
            ["carouselNext"] = "carousel next arrow",
            ["carouselPrevious"] = "carousel previous arrow",
            ["messageComposer"] = "message box",
            ["storyNext"] = "story next arrow",
            ["csrfToken"] = "session token"
        };

        public List<string> Missing =>
            Probes.Where(kv => !kv.Value).Select(kv => Labels.TryGetValue(kv.Key, out var l) ? l : kv.Key).ToList();

        public int FoundCount => Probes.Count(kv => kv.Value);
        public int TotalCount => Probes.Count;

        public string Describe()
        {
            if (TotalCount == 0) return "The page could not be checked. Open an Instagram page and try again.";
            if (Missing.Count == 0) return $"All {TotalCount} controls are present and working.";
            return $"{FoundCount} of {TotalCount} controls are present. Not found on this page: {string.Join(", ", Missing)}.";
        }
    }

    private async Task<bool> ExecuteJsonBoolAsync(string expression)
    {
        var el = await ExecuteJsonAsync(expression);
        return el != null && el.Value.ValueKind == JsonValueKind.True;
    }

    /// <summary>Reads what the story viewer is showing, so the navigator can report the truth.</summary>
    public async Task<StoryState> ReadStoryStateAsync()
    {
        var el = await ExecuteJsonAsync("window.__winInstagram.storyState()");
        if (el == null || el.Value.ValueKind != JsonValueKind.Object)
            return new StoryState(false, false, false, string.Empty, string.Empty);

        var o = el.Value;
        string ReadString(string name) =>
            o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;
        bool ReadBool(string name) =>
            o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

        return new StoryState(ReadBool("isStory"), ReadBool("hasVideo"), ReadBool("isPaused"), ReadString("username"), ReadString("altText"));
    }

    /// <summary>Holds the story still. Returns false when there is no story video to pause.</summary>
    public Task<bool> PauseStoryAsync() => ExecuteJsonBoolAsync("window.__winInstagram.storyPause()");

    /// <summary>Lets a paused story continue.</summary>
    public Task<bool> ResumeStoryAsync() => ExecuteJsonBoolAsync("window.__winInstagram.storyResume()");

    /// <summary>Steps to the next story item. Returns false when no story is open.</summary>
    public Task<bool> NextStoryAsync() => ExecuteJsonBoolAsync("window.__winInstagram.storyNext()");

    /// <summary>Steps back to the previous story item. Returns false when no story is open.</summary>
    public Task<bool> PreviousStoryAsync() => ExecuteJsonBoolAsync("window.__winInstagram.storyPrev()");

    /// <summary>Checks every control the native panels drive and reports which ones are missing.</summary>
    public async Task<SelectorHealthReport> ProbeSelectorsAsync()
    {
        var probes = new Dictionary<string, bool>();
        var el = await ExecuteJsonAsync("window.__winInstagram.probeSelectors()");
        if (el != null && el.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.Value.EnumerateObject())
            {
                probes[prop.Name] = prop.Value.ValueKind == JsonValueKind.True;
            }
        }
        return new SelectorHealthReport(probes);
    }

    /// <summary>Direct URLs and readable text of the media on screen, or null when there is none.</summary>
    public async Task<ActiveMediaInfo?> ReadActiveMediaAsync()
    {
        var el = await ExecuteJsonAsync("window.__winInstagram.activeMediaUrls()");
        if (el == null || el.Value.ValueKind != JsonValueKind.Object) return null;

        var o = el.Value;
        string ReadString(string name) =>
            o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? string.Empty : string.Empty;

        return new ActiveMediaInfo(
            ReadString("videoUrl"), ReadString("imageUrl"), ReadString("altText"),
            ReadString("caption"), ReadString("username"), ReadString("pageUrl"));
    }

    // ---- Native discovery surfaces ------------------------------------------------------------

    /// <summary>Fetches an Instagram endpoint from inside the page and hands the body to the parser.</summary>
    public async Task RequestApiAsync(string url, string tag)
    {
        if (_coreWebView2 == null || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var urlJson = JsonSerializer.Serialize(url);
            var tagJson = JsonSerializer.Serialize(tag);
            var callScript = $"(async () => {{ if (!window.__winInstagram || typeof window.__winInstagram.apiGet !== 'function') {{ {WinInstagramScript} }} return await window.__winInstagram.apiGet({urlJson}, {tagJson}); }})()";
            await _coreWebView2.ExecuteScriptAsync(callScript);
        }
        catch (Exception ex)
        {
            AppLogger.Error("API", $"Failed to fetch {url}", ex);
        }
    }

    /// <summary>Loads the stories tray into the stories navigator.</summary>
    public Task RequestStoriesAsync() =>
        RequestApiAsync("https://www.instagram.com/api/v1/feed/reels_tray/", "stories");

    /// <summary>Loads a profile by handle into the native profile viewer.</summary>
    public Task RequestProfileAsync(string username) =>
        string.IsNullOrWhiteSpace(username)
            ? Task.CompletedTask
            : RequestApiAsync($"https://www.instagram.com/api/v1/users/web_profile_info/?username={Uri.EscapeDataString(username)}", "profile");

    /// <summary>Loads the account's saved posts.</summary>
    public Task RequestSavedPostsAsync() =>
        RequestApiAsync("https://www.instagram.com/api/v1/feed/saved/posts/", "saved");

    /// <summary>Loads the account's saved collections.</summary>
    public Task RequestCollectionsAsync() =>
        RequestApiAsync("https://www.instagram.com/api/v1/collections/list/", "collections");

    /// <summary>Loads the activity feed, which is what drives new-activity announcements.</summary>
    public Task RequestActivityAsync() =>
        RequestApiAsync("https://www.instagram.com/api/v1/news/inbox/", "activity");

    /// <summary>Searches accounts, hashtags and posts through Instagram's own search endpoint.</summary>
    public Task RequestSearchAsync(string query) =>
        string.IsNullOrWhiteSpace(query)
            ? Task.CompletedTask
            : RequestApiAsync($"https://www.instagram.com/api/v1/web/search/topsearch/?context=blended&query={Uri.EscapeDataString(query)}", "search");

    /// <summary>Kept for compatibility; delegate likes must be addressed by media id.</summary>
    public Task<bool> LikeMediaAsync(string mediaId) => SetMediaLikedAsync(mediaId, true);
}
