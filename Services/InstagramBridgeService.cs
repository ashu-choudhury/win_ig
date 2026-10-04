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

    public event Action<ReelItem, bool, bool, double>? ActiveReelChanged;
    public event Action<List<FeedPost>>? FeedReceived;
    public event Action<List<InstagramComment>>? CommentsReceived;
    public event Action<bool>? LoginStatusChanged;
    public event Action<string>? StatusMessageUpdated;

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

        // Strictly parse only Timeline feed & comments payloads
        bool isFeed = uri.Contains("/api/v1/feed/timeline/") || uri.Contains("feed/reels_tray");
        bool isComments = uri.Contains("/api/v1/comments/") || uri.Contains("edge_media_to_parent_comment") || (uri.Contains("graphql") && uri.Contains("comment"));
        if (!isFeed && !isComments) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var stream = await e.Response.GetContentAsync();
                if (stream == null) return;

                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(json)) return;

                if (isFeed)
                {
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

    public async Task LikeMediaAsync(string mediaId)
    {
        await ToggleLikeAsync();
    }
}
