using System.Collections.Generic;
using System.Text;
using Address.Core;
using UnityEngine;

namespace Address.Interact
{
    /// <summary>
    /// The web inside the Library: every URL already exists and is generated from its own text (like addresses).
    /// Styles of ~2007: portal with a search box, search results, a forum, personal homepages with tiled backgrounds,
    /// marquees, counters and guestbooks, a local news site. All names are invented.
    /// </summary>
    public static class WebGen
    {
        public enum Kind { Title, Text, Link, Search, Marquee, Banner, Counter, Rule, Post, Image, Small, Construction }

        public class Block
        {
            public Kind kind; public string text, url, extra; public Color color = Color.black; public int size = 15; public bool bold, center;
        }

        public class Page
        {
            public string url, title; public Color bg = Color.white; public int tile = -1; public Color tileA, tileB;
            public List<Block> blocks = new();
            public Block Add(Kind k, string text, string url = null) { var b = new Block { kind = k, text = text, url = url }; blocks.Add(b); return b; }
        }

        public const string Home = "http://iskatel.lib/";

        static readonly string[] Places = { "жёлтый коридор", "пустой бассейн", "офис без окон", "комната с гулом", "лестница вниз", "склад за дверью", "холл с аквариумом", "торговый центр ночью", "школьный спортзал", "отель на третьем этаже", "парковка под землёй", "комната с ковролином" };
        static readonly string[] Nicks = { "Тень_77", "kotik2005", "НочнойСторож", "user4412", "Лампа", "dimon_rus", "ВасилисаК", "xXx_Bro_xXx", "Сосед", "гость", "Мираж", "Eho", "Silent_Hall", "Ковролин" };
        static readonly string[] Topics = { "Кто-нибудь был на этом этаже?", "Странный гул за стеной", "Нашёл дверь без ручки", "Помогите опознать место по фото", "Бассейн, в котором никого нет", "Почему здесь всегда горит свет", "Снова оказался в той же комнате", "Лифт, который никуда не едет", "Ищу людей, кто помнит этот адрес", "Мой компьютер включился сам" };
        static readonly string[] Sentences = {
            "Был там вчера, ничего не изменилось.", "Свет гудит ровно, как будто дышит.", "Если долго идти прямо, начинаешь узнавать углы.",
            "Дверь была на месте, а потом её не стало.", "Сфоткал стену, на фото там проход.", "Не оборачивайтесь, когда слышите шаги за спиной.",
            "Ковёр влажный, но воды нигде нет.", "Там стоит компьютер, и он подключён.", "Я нашёл кабель в шкафу и подключил монитор.",
            "Лучше всего смотреть в 800x600.", "Кто-то оставил кружку с ещё тёплым кофе.", "Вентиляция ведёт куда-то дальше, я туда не полез.",
            "Сайт обновлён! Добавил новые фотки.", "Пишите в гостевую, отвечу всем.", "Этот адрес я записал на бумажке, проверьте:" };
        static readonly string[] News = { "В районе снова отключали свет", "Жители жалуются на гул в подвалах", "Открылся новый торговый центр — посетителей пока нет", "Бассейн «Волна» закрыт на неопределённый срок", "Найдена дверь, которой нет на плане", "Учёные: коридоры бесконечны, но это не страшно", "Погода: туман, без изменений", "Интернет-кафе на углу работает круглосуточно" };

        static Rng R(string url) => new Rng(Hashing.Fnv64(url));
        static string Addr(Rng r)
        {
            const string hex = "0123456789ABCDEF"; var a = new[] { "BR", "PR", "FA" }; var sb = new StringBuilder(a[r.Range(0, 3)]);
            for (int g = 0; g < 4; g++) { sb.Append('-'); for (int i = 0; i < 4; i++) sb.Append(hex[r.Range(0, 16)]); }
            return sb.ToString();
        }

        public static Page Get(string url)
        {
            url = Normalize(url);
            if (url == Home || url == "http://iskatel.lib") return Portal();
            if (url.Contains("iskatel.lib/search?q=")) return Search(System.Uri.UnescapeDataString(url.Substring(url.IndexOf("q=") + 2)));
            if (url.Contains("forum.podsobka.lib")) return Forum(url);
            if (url.Contains("narod.lib/~")) return Homepage(url);
            if (url.Contains("vesti.lib")) return NewsPage(url);
            if (url.EndsWith(".lib/") || url.EndsWith(".lib")) return Homepage(url);
            return NotFound(url);
        }

        public static string Normalize(string u)
        {
            u = (u ?? "").Trim(); if (u.Length == 0) return Home;
            if (!u.StartsWith("http://")) u = u.Contains(".") ? "http://" + u : Home + "search?q=" + System.Uri.EscapeDataString(u);
            return u;
        }

        static Page Portal()
        {
            var p = new Page { url = Home, title = "Искатель — найдётся всё, что уже существует" };
            var t = p.Add(Kind.Title, "Искатель"); t.size = 46; t.center = true; t.color = new Color(0.85f, 0.2f, 0.1f); t.bold = true;
            p.Add(Kind.Small, "бета-версия 2.0 · найдено 9 999 999 999 страниц").center = true;
            p.Add(Kind.Search, "");
            p.Add(Kind.Rule, "");
            p.Add(Kind.Text, "Популярное сегодня:").bold = true;
            p.Add(Kind.Link, "Форум «Подсобка» — разговоры о странных местах", "http://forum.podsobka.lib/");
            p.Add(Kind.Link, "Вести района — новости, которые никто не пишет", "http://vesti.lib/");
            p.Add(Kind.Link, "Народ.либ — бесплатные домашние страницы", "http://narod.lib/~katalog");
            p.Add(Kind.Link, "Домашняя страница Лампы", "http://narod.lib/~lampa");
            p.Add(Kind.Rule, "");
            var b = p.Add(Kind.Banner, "ВЫИГРАЙ ТИШИНУ! Нажми сюда!!!", "http://narod.lib/~priz"); b.color = new Color(1f, 0.85f, 0.1f);
            p.Add(Kind.Small, "© 2007 Искатель. Все права защищены. Лучше всего смотреть в 800x600.").center = true;
            return p;
        }

        static Page Search(string q)
        {
            var p = new Page { url = Home + "search?q=" + System.Uri.EscapeDataString(q), title = "Искатель: " + q };
            var t = p.Add(Kind.Title, "Искатель"); t.size = 28; t.color = new Color(0.85f, 0.2f, 0.1f); t.bold = true;
            p.Add(Kind.Search, q);
            var r = R("search:" + q.ToLowerInvariant());
            int n = r.Range(4, 9);
            p.Add(Kind.Small, $"Результаты 1—{n} из примерно {r.Range(12, 99999)} по запросу «{q}» (0,{r.Range(10, 99)} сек.)");
            for (int i = 0; i < n; i++)
            {
                string kind = r.Pick(new[] { "forum", "narod", "vesti" });
                string url = kind == "forum" ? $"http://forum.podsobka.lib/thread/{r.Range(100, 99999)}" : kind == "narod" ? $"http://narod.lib/~{r.Pick(Nicks).ToLowerInvariant().Replace(' ', '_')}" : $"http://vesti.lib/{r.Range(1000, 9999)}";
                string title = kind == "forum" ? r.Pick(Topics) : kind == "narod" ? $"Мой сайт — {q}" : r.Pick(News);
                p.Add(Kind.Link, title, url);
                p.Add(Kind.Text, $"...{q} — {r.Pick(Sentences)} {r.Pick(Sentences)}...").size = 13;
                var u = p.Add(Kind.Small, url); u.color = new Color(0f, 0.5f, 0f);
            }
            return p;
        }

        static Page Forum(string url)
        {
            var r = R(url);
            var p = new Page { url = url, title = "Форум «Подсобка»", bg = new Color(0.9f, 0.92f, 0.95f) };
            var t = p.Add(Kind.Title, "Форум «Подсобка»"); t.color = new Color(0.1f, 0.2f, 0.45f); t.size = 30; t.bold = true;
            p.Add(Kind.Small, "Главная » Странные места » Обсуждения   |   Вход · Регистрация · Поиск");
            if (!url.Contains("/thread/"))
            {
                p.Add(Kind.Rule, "");
                for (int i = 0; i < 12; i++)
                {
                    var tr = r.Fork("t", i); int id = tr.Range(100, 99999);
                    p.Add(Kind.Link, $"{tr.Pick(Topics)}   ({tr.Range(1, 240)} ответов)", $"http://forum.podsobka.lib/thread/{id}");
                }
                return p;
            }
            var tt = p.Add(Kind.Title, r.Pick(Topics)); tt.size = 20; tt.color = new Color(0.1f, 0.1f, 0.3f);
            int posts = r.Range(3, 9);
            for (int i = 0; i < posts; i++)
            {
                var pr = r.Fork("p", i);
                var sb = new StringBuilder();
                int s = pr.Range(1, 4); for (int k = 0; k < s; k++) sb.Append(pr.Pick(Sentences)).Append(' ');
                if (pr.Chance(0.35f)) sb.Append(Addr(pr));
                var post = p.Add(Kind.Post, sb.ToString().Trim()); post.extra = $"{pr.Pick(Nicks)}|{2007}-{pr.Range(1, 13):00}-{pr.Range(1, 29):00} {pr.Range(0, 24):00}:{pr.Range(0, 60):00}|сообщений: {pr.Range(1, 3000)}";
            }
            p.Add(Kind.Link, "« Назад к списку тем", "http://forum.podsobka.lib/");
            return p;
        }

        static Page Homepage(string url)
        {
            var r = R(url);
            string who = url.Contains("~") ? url.Substring(url.IndexOf('~') + 1).Trim('/') : "гость";
            var p = new Page { url = url, title = $"Домашняя страница {who}", tile = r.Range(0, 4) };
            Color.RGBToHSV(new Color(r.Value, r.Value, r.Value), out _, out _, out _);
            p.tileA = Color.HSVToRGB(r.Value, 0.5f, 0.35f); p.tileB = Color.HSVToRGB(r.Value, 0.6f, 0.2f);
            var m = p.Add(Kind.Marquee, $"*** Добро пожаловать на мою страничку!!! *** {r.Pick(Sentences)} ***"); m.color = Color.yellow;
            var t = p.Add(Kind.Title, $"~ {who} ~"); t.color = Color.HSVToRGB(r.Value, 0.8f, 1f); t.size = 34; t.center = true; t.bold = true;
            p.Add(Kind.Image, "", null).extra = r.Range(0, 1000).ToString();
            for (int i = 0; i < r.Range(2, 5); i++) { var x = p.Add(Kind.Text, r.Pick(Sentences) + " " + r.Pick(Places) + "."); x.color = Color.white; }
            if (r.Chance(0.6f)) { var a = p.Add(Kind.Text, "Мои любимые места: " + Addr(r) + ", " + Addr(r)); a.color = new Color(0.6f, 1f, 0.6f); }
            p.Add(Kind.Construction, "СТРАНИЦА В РАЗРАБОТКЕ");
            p.Add(Kind.Link, "Гостевая книга", $"http://forum.podsobka.lib/thread/{r.Range(100, 99999)}").color = new Color(0.6f, 0.8f, 1f);
            p.Add(Kind.Link, "Мои друзья: " + r.Pick(Nicks), $"http://narod.lib/~{r.Pick(Nicks).ToLowerInvariant()}").color = new Color(0.6f, 0.8f, 1f);
            p.Add(Kind.Counter, $"{r.Range(1, 999999):000000}");
            var s = p.Add(Kind.Small, "Сайт сделан в Блокноте. Лучше всего смотреть в 800x600."); s.color = new Color(1, 1, 1, 0.7f); s.center = true;
            return p;
        }

        static Page NewsPage(string url)
        {
            var r = R(url);
            var p = new Page { url = url, title = "Вести района", bg = new Color(0.97f, 0.96f, 0.93f) };
            var t = p.Add(Kind.Title, "ВЕСТИ РАЙОНА"); t.color = new Color(0.55f, 0.05f, 0.05f); t.size = 32; t.bold = true;
            p.Add(Kind.Small, $"{r.Range(1, 29)} {r.Pick(new[] { "января", "марта", "июля", "октября", "ноября" })} 2007 · Погода: туман · Курс: без изменений");
            p.Add(Kind.Rule, "");
            for (int i = 0; i < 7; i++)
            {
                var nr = r.Fork("n", i);
                p.Add(Kind.Link, nr.Pick(News), $"http://vesti.lib/{nr.Range(1000, 9999)}");
                p.Add(Kind.Text, nr.Pick(Sentences) + " " + nr.Pick(Sentences)).size = 13;
            }
            return p;
        }

        static Page NotFound(string url)
        {
            var p = new Page { url = url, title = "Страница не найдена" };
            p.Add(Kind.Title, "Не удаётся отобразить страницу").size = 22;
            p.Add(Kind.Text, "Возможно, страница ещё не существует. Или уже не существует. Попробуйте позже.");
            p.Add(Kind.Link, "Вернуться на Искатель", Home);
            return p;
        }
    }
}
