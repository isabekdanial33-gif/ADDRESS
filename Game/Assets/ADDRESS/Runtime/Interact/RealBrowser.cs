using System;
using UnityEngine;
using UnityEngine.UI;
using VoltstroStudios.UnityWebBrowser;
using VoltstroStudios.UnityWebBrowser.Communication;
using VoltstroStudios.UnityWebBrowser.Core.Engines;
using VoltstroStudios.UnityWebBrowser.Input;

namespace Address.Interact
{
    /// <summary>
    /// A real web browser (Chromium through UnityWebBrowser, MIT) inside the game computer. In "2007" mode every
    /// address is opened as it looked in 2007 through the Internet Archive (web.archive.org, no toolbar), so the
    /// web really is the web of 2007. The start page is our own 2007-style portal. Search goes to a plain HTML search.
    /// </summary>
    public class RealBrowser : MonoBehaviour
    {
        public static bool Available => Resources.Load<Engine>("Cef Engine Configuration") != null;
        public bool era2007 = true;
        public Action<string> onUrl, onTitle, onStatus;
        WebBrowserUIBasic ui;

        public static RealBrowser Create(RectTransform view, bool mouse, bool keyboard)
        {
            var go = new GameObject("web", typeof(RectTransform)); go.transform.SetParent(view, false);
            var r = (RectTransform)go.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            go.AddComponent<RawImage>().color = Color.white;
            go.SetActive(false);                         // configure before the component's Start
            var ui = go.AddComponent<WebBrowserUIBasic>();
            var c = ui.browserClient;
            c.engine = Resources.Load<Engine>("Cef Engine Configuration");
            c.communicationLayer = Resources.Load<CommunicationLayer>("TCP Communication Layer");
            ui.inputHandler = Resources.Load<WebBrowserInputHandler>("Input System Handler");
            ui.disableMouseInputs = !mouse; ui.disableKeyboardInputs = !keyboard;
            c.initialUrl = "about:blank";
            c.backgroundColor = new Color32(255, 255, 255, 255);
            c.popupAction = default;
            SetResolution(c, 1180, 760);
            var rb = go.AddComponent<RealBrowser>(); rb.ui = ui;
            // engine events arrive on a network thread: queue them for the main thread (Unity UI is not thread-safe)
            c.OnUrlChanged += u => rb.q.Enqueue(() => rb.onUrl?.Invoke(u));
            c.OnTitleChange += t => rb.q.Enqueue(() => rb.onTitle?.Invoke(t));
            c.OnLoadStart += u => rb.q.Enqueue(() => rb.onStatus?.Invoke("Загрузка " + Pretty(u) + "..."));
            c.OnLoadFinish += u => rb.q.Enqueue(() => rb.onStatus?.Invoke("Готово       Интернет"));
            c.OnClientConnected += () => rb.q.Enqueue(() => { rb.ready = true; rb.Home(); });
            go.SetActive(true);
            return rb;
        }
        bool ready; public bool Ready => ready;
        readonly System.Collections.Concurrent.ConcurrentQueue<Action> q = new();
        void LateUpdate() { while (q.TryDequeue(out var a)) { try { a(); } catch (Exception e) { Debug.LogWarning("[ADDRESS] browser event: " + e.Message); } } }

        static void SetResolution(object client, int w, int h)
        {
            var f = client.GetType().GetField("resolution", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f == null) return;
            try { f.SetValue(client, Activator.CreateInstance(f.FieldType, (uint)w, (uint)h)); } catch { }
        }

        public void Back() { if (ready) ui.GoBack(); }
        public void Forward() { if (ready) ui.GoForward(); }
        public void Refresh() { if (ready) ui.Refresh(); }
        public void Home() { if (ready) ui.LoadHtml(StartPage()); onUrl?.Invoke("Стартовая страница"); }

        /// <summary>Address bar: a URL (opened as of 2007 in 2007 mode) or words (searched).</summary>
        public void Go(string text)
        {
            if (!ready || string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            bool looksUrl = text.Contains(".") && !text.Contains(" ");
            string url;
            if (!looksUrl) url = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(text);
            else
            {
                url = text.StartsWith("http") ? text : "http://" + text;
                if (era2007 && !url.Contains("web.archive.org")) url = "https://web.archive.org/web/2007if_/" + url;
            }
            ui.NavigateUrl(url);
        }

        /// <summary>Hide the archive prefix in the address bar: show the page's own address.</summary>
        public static string Pretty(string u)
        {
            if (string.IsNullOrEmpty(u)) return "";
            int i = u.IndexOf("/http", StringComparison.Ordinal);
            if (u.Contains("web.archive.org/web/") && i > 0) return u.Substring(i + 1);
            if (u.StartsWith("data:")) return "Стартовая страница";
            return u;
        }

        static string StartPage() => @"<!DOCTYPE html><html><head><meta charset='utf-8'><title>Искатель — стартовая страница</title>
<style>
body{margin:0;font-family:Verdana,Tahoma,Arial,sans-serif;font-size:12px;background:#eef2f8 url() repeat;color:#222}
#top{background:linear-gradient(#5b8fd6,#2a5aa8);color:#fff;padding:10px 18px;border-bottom:3px solid #f2a20c}
#top b{font-size:30px;font-family:Georgia,serif;letter-spacing:1px;text-shadow:1px 1px 0 #123}
#top span{background:#f2a20c;color:#fff;font-size:10px;padding:1px 4px;border-radius:3px;margin-left:6px;vertical-align:top}
#search{margin:22px auto;width:620px;text-align:center}
#search input[type=text]{width:420px;font-size:16px;padding:4px;border:1px solid #7f9db9}
#search input[type=submit]{font-size:14px;padding:3px 12px;background:linear-gradient(#fff,#dde5f0);border:1px solid #7f9db9}
table{margin:0 auto;border-collapse:collapse;width:880px}
td{vertical-align:top;padding:8px}
.box{background:#fff;border:1px solid #b8c8e0}
.box h3{margin:0;padding:4px 8px;font-size:12px;color:#fff;background:linear-gradient(#7fa7e0,#4a78c0)}
.box ul{margin:6px 0 8px 18px;padding:0;line-height:20px}
a{color:#0033cc}a:visited{color:#660099}
#foot{text-align:center;color:#888;font-size:10px;margin:18px}
marquee{color:#c00;font-weight:bold}
</style></head><body>
<div id='top'><b>Искатель</b><span>beta 2.0</span><br>интернет, каким он был</div>
<marquee scrollamount=4>*** Добро пожаловать! Все сайты открываются такими, какими они были в 2007 году ***</marquee>
<div id='search'><form onsubmit=""var q=this.q.value;location.href=(q.indexOf('.')>0&&q.indexOf(' ')<0)?'https://web.archive.org/web/2007if_/http://'+q.replace(/^https?:\/\//,''):'https://html.duckduckgo.com/html/?q='+encodeURIComponent(q);return false;"">
<input type=text name=q autofocus> <input type=submit value='Найти!'></form></div>
<table><tr>
<td width=33%><div class=box><h3>Почта и общение</h3><ul>
<li><a href='https://web.archive.org/web/2007if_/http://www.livejournal.com/'>Дневники</a>
<li><a href='https://web.archive.org/web/2007if_/http://www.myspace.com/'>Страницы друзей</a>
<li><a href='https://web.archive.org/web/2007if_/http://www.icq.com/'>Аська</a>
</ul></div></td>
<td width=33%><div class=box><h3>Знания</h3><ul>
<li><a href='https://web.archive.org/web/2007if_/http://ru.wikipedia.org/'>Энциклопедия</a>
<li><a href='https://web.archive.org/web/2007if_/http://www.nasa.gov/'>Космос</a>
<li><a href='https://web.archive.org/web/2007if_/http://www.bbc.co.uk/'>Новости мира</a>
</ul></div></td>
<td width=33%><div class=box><h3>Развлечения</h3><ul>
<li><a href='https://web.archive.org/web/2007if_/http://www.youtube.com/'>Видео</a>
<li><a href='https://web.archive.org/web/2007if_/http://www.newgrounds.com/'>Флеш-игры</a>
<li><a href='https://web.archive.org/web/2007if_/http://www.geocities.com/'>Домашние страницы</a>
</ul></div></td></tr></table>
<div id='foot'>Лучше всего смотреть в 1024x768 · Оптимизировано для Обозревателя · Вы посетитель №&nbsp;<b>000417</b></div>
</body></html>";
    }
}
