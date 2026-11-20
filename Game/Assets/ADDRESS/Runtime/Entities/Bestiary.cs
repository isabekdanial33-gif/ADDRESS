using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Address.Entities
{
    /// <summary>
    /// The explorer's index: every creature that was actually seen and every world that was entered, kept between
    /// runs (PlayerPrefs). Unseen entries stay "???" in the menu.
    /// </summary>
    public static class Bestiary
    {
        public struct Entry { public string key, name, about; }

        public static readonly Entry[] Creatures =
        {
            E("Verity", "Verity", "Жёлтый смайлик-помощник. Летает по коридорам, подлетает к путникам и рассказывает, где выход. Подойди и нажми E."),
            E("FakeVerity", "Фальшивый Verity", "Тусклее настоящего и говорит так же уверенно. Только ведёт он не к выходу, а к тому, кто там ждёт."),
            E("Echo", "Отголосок", "Растянутая тёмная копия исследователя. Идёт за тобой, только пока ты не смотришь."),
            E("Salesman", "Продавец", "Высокая фигура из шоурумов. Под взглядом замирает, но быстро догоняет, если отвернуться."),
            E("Crawler", "Ползун", "Живёт в тёмных зонах. Взгляд его не останавливает."),
            E("Mannequin", "Манекен", "Стоит неподвижно. Отвернёшься, и он уже ближе. Очень быстрый."),
            E("Limper", "Хромой", "Медленный, но никогда не останавливается."),
            E("Smiler", "Смайлер", "Улыбка в темноте. Пока ты на него смотришь, он не двигается."),
            E("Faceling", "Безликий", "Человек без лица. Мирный, бродит по своим делам."),
            E("Partygoer", "Тусовщик", "Хозяин Уровня Веселья =). Бежит быстро и под взглядом не замирает."),
            E("SkinStealer", "Кожекрад", "Носит чужую кожу. Подбирается на корточках, потом бросается."),
            E("Clark", "Капитан Кларк", "Искажённый капитан из фильма Kane Pixels. Хромает на правую ногу, но догонит, если не смотреть."),
            E("Growler", "Рычун", "Длинное существо с парковки. Очень быстрое."),
            E("Needlelimbs", "Иглоног", "Тонкие длинные конечности. Под взглядом замирает."),
            E("Howler", "Бактерия", "Чёрная тварь из тонких отростков. Не замирает."),
            E("Lifeform", "Форма жизни", "Обитатель склада и труб. Бегает быстро."),
            E("Hazmat", "Пропавший исследователь", "Человек в защитном костюме. Когда-то он тоже искал выход."),
            E("Hound", "Гончая", "Бегает на четырёх лапах, но лицо у неё человеческое. Очень быстрая."),
            E("Rake", "Грабля", "Бледная, с длинными когтями. Не замирает, держись от неё подальше."),
            E("Librarian", "Библиотекарь", "Стоит между полками. Двигается, только когда на него не смотрят."),
            E("Deathmoth", "Смертельный мотылёк", "Огромный мотылёк. Летит на свет и на тебя."),
            E("Spider", "Паук", "Паук размером с диван. Живёт в подвалах и пещерах и бегает быстрее тебя."),
            E("Thinman", "Тощий", "Три метра роста, кожа да кости. Видит издалека, под взглядом замирает."),
            E("Grinner", "Хохотун", "Зелёный, на четвереньках, с оскаленной пастью. Подкрадывается и не замирает."),
            E("Shy", "Скромник", "Сидит на полу и плачет. Не смотри ему в лицо: за тем, кто посмотрел, он бежит, пока не догонит."),
            E("Ember", "Угольная гончая", "Зверь из тлеющих углей. Живёт у машин и в пещерах."),
            E("Upir", "Упырь", "Сгорбленный, с длинными руками и пастью до ушей. Прыгает и не замирает."),
            E("Wendigo", "Вендиго", "Белый, с рогами, выше двери. Под взглядом стоит, но стоит отвернуться, и он бежит очень быстро."),
            E("Hellhound", "Адская собака", "Тощая собака без шерсти. Самая быстрая тварь в этих местах."),
            E("Decayed", "Гниющий", "Медленный, разваливается на ходу. Чует тебя издалека и идёт, не останавливаясь."),
            E("Medusa", "Медуза", "Клубок шевелящихся отростков на двух ногах. Хватает с расстояния больше вытянутой руки."),
        };

        public static readonly Entry[] Worlds =
        {
            E("BR", "Жёлтые комнаты", "Уровень 0. Жужжащие лампы, мокрый ковёр, бесконечные жёлтые обои."),
            E("PR", "Бассейны", "Белая плитка, тёплая вода, эхо. Сюда чаще ведут люки."),
            E("FA", "Frutiger Aero", "Светлый парк. Арки в павильонах ведут в Бассейны."),
            E("PG", "Подземная парковка", "Бетон, разметка и гул. Сюда спускаются лифты."),
            E("HT", "Бесконечный отель", "Коридоры с одинаковыми дверями. Половина ламп выключена."),
            E("OF", "Заброшенный офис", "Ковролин, столы и мёртвые лампы. Здесь ходит капитан Кларк."),
            E("LF", "Уровень Веселья =)", "Праздник, который никогда не кончается. Не ешь торт."),
            E("PD", "Трубы", "Уровень 2. Тесные бетонные коридоры, жар и трубы под потолком."),
            E("WH", "Склад", "Уровень 1. Огромные бетонные залы со стеллажами."),
            E("LO", "Без света", "Почти все лампы мертвы. Здесь живут Смайлеры."),
            E("EL", "Электростанция", "Уровень 3. Металл, гул машин и красные аварийные лампы."),
            E("CV", "Пещеры", "Уровень 8. Сырой камень и почти полная тьма."),
            E("RN", "Беги!", "Уровень «!». Красный свет, длинные коридоры. Кто-то уже бежит за тобой."),
            E("FL", "Затопленные комнаты", "Жёлтые комнаты под водой по щиколотку. Тихий плеск."),
            E("LB", "Библиотека", "Бесконечные стеллажи с книгами. Тишина и Библиотекарь."),
            E("LA", "Лаборатория Async", "Белые коридоры исследовательского института из фильма Kane Pixels."),
        };

        static Entry E(string k, string n, string a) => new Entry { key = k, name = n, about = a };

        /// <summary>Raised the first time something new is seen: (section title, name).</summary>
        public static System.Action<string, string> onNew;

        static HashSet<string> seen;
        static HashSet<string> Seen
        {
            get
            {
                if (seen != null) return seen;
                seen = new HashSet<string>();
                try { foreach (var s in PlayerPrefs.GetString("index_v1", "").Split(',')) if (s.Length > 0) seen.Add(s); } catch { }
                return seen;
            }
        }

        public static bool Known(string key) => Seen.Contains(key);
        public static int Count(Entry[] list) { int n = 0; foreach (var e in list) if (Known(e.key)) n++; return n; }

        /// <summary>Built-in worlds plus the data worlds (Resources/Worlds and mods).</summary>
        public static Entry[] AllWorlds() => Worlds.Concat(Address.Mods.ModSystem.Worlds.Select(w => E(w.id, w.title + (string.IsNullOrEmpty(w.level) ? "" : "  (" + w.level + ")"), w.about ?? ""))).ToArray();
        public static Entry[] AllCreatures() => Creatures.Concat(Address.Mods.ModSystem.Entities.Select(e => E(e.id, e.name ?? e.id, e.about ?? ""))).ToArray();

        static void Mark(string key, Entry[] list, string section)
        {
            if (string.IsNullOrEmpty(key) || Seen.Contains(key)) return;
            Seen.Add(key);
            try { PlayerPrefs.SetString("index_v1", string.Join(",", Seen)); PlayerPrefs.Save(); } catch { }
            foreach (var e in list) if (e.key == key) { onNew?.Invoke(section, e.name); break; }
            Debug.Log($"[ADDRESS] index: new {section} {key}");
        }

        public static void SeeCreature(string key) => Mark(key, AllCreatures(), "существо");
        public static void VisitWorld(string key) => Mark(key, AllWorlds(), "мир");

        public static string Name(EntityKind k) { foreach (var e in Creatures) if (e.key == k.ToString()) return e.name; return k.ToString(); }
        public static string WorldName(string key) { foreach (var e in Worlds) if (e.key == key) return e.name; return key; }
    }
}
