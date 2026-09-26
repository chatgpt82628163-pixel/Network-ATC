using System.Text;
using System.Text.RegularExpressions;
using NetworkAtc.Core.Sectors;
using NetworkAtc.Core.Weather;

namespace NetworkAtc.Core.Atis;

/// <summary>
/// The spoken ATIS in Russian, as Russian air traffic services say it: wind in metres per second, cloud base in
/// metres, the aerodrome pressure (QFE) when the METAR remarks give it. Numbers are written in digits with the
/// right word forms (5 метров, 2 метра, 1013 гектопаскалей); the Windows voice reads them.
/// </summary>
public static partial class AtisText
{
    private static readonly string[] PhoneticRu =
    [
        "Альфа", "Браво", "Чарли", "Дельта", "Эхо", "Фокстрот", "Гольф", "Хотел", "Индия", "Джульетт", "Кило", "Лима", "Майк",
        "Новембер", "Оскар", "Папа", "Квебек", "Ромео", "Сьерра", "Танго", "Юниформ", "Виктор", "Виски", "Икс-рей", "Янки", "Зулу",
    ];

    /// <summary>The letter as a Russian controller says it: "B" → "Браво".</summary>
    public static string PhoneticRussian(string letter) =>
        letter is { Length: 1 } && char.ToUpperInvariant(letter[0]) is >= 'A' and <= 'Z' and var c
            ? PhoneticRu[c - 'A'] : letter;

    /// <summary>"24R" → "24 правая".</summary>
    public static string RunwayRussian(string id)
    {
        id = id.Trim().ToUpperInvariant();
        if (id.Length == 0) return "";
        string side = id[^1] switch
        {
            'L' => " левая",
            'R' => " правая",
            'C' => " центральная",
            _ => "",
        };
        string number = side.Length > 0 ? id[..^1] : id;
        return number.TrimStart('0').PadLeft(1, '0') + side;
    }

    /// <summary>The Russian word form for a number: 1 метр, 2 метра, 5 метров, 11 метров, 21 метр.</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        n = Math.Abs(n);
        int last = n % 10, lastTwo = n % 100;
        return lastTwo is >= 11 and <= 14 ? many : last == 1 ? one : last is >= 2 and <= 4 ? few : many;
    }

    [GeneratedRegex(@"^(?<dir>\d{3}|VRB)(?<spd>\d{2,3})(G(?<gust>\d{2,3}))?(?<unit>KT|MPS)$")]
    private static partial Regex WindGroupRu();

    [GeneratedRegex(@"\bQFE(?<mm>\d{3})(/(?<hpa>\d{3,4}))?\b")]
    private static partial Regex QfeRemark();

    /// <summary>
    /// The spoken ATIS in Russian: airport, letter, time, runways, wind, visibility, weather, clouds, temperature,
    /// QNH, the aerodrome pressure, the remark and the request to report the letter on first contact.
    /// </summary>
    public static string SpeechRu(string airportName, string letter, DateTime utc, RunwayUse? runways, Metar? metar, string remark)
    {
        var sb = new StringBuilder();
        void Say(string s)
        {
            s = s.Trim().TrimEnd('.', ' ');
            if (s.Length == 0) return;
            sb.Append(char.ToUpper(s[0])).Append(s, 1, s.Length - 1).Append(". ");
        }
        string phonetic = PhoneticRussian(letter);
        Say($"{airportName}, информация {phonetic}, время {utc.ToString("HH", Inv)} {utc.ToString("mm", Inv)}");

        if (runways != null)
        {
            string dep = string.Join(" и ", runways.Departure.Select(RunwayRussian));
            string arr = string.Join(" и ", runways.Arrival.Select(RunwayRussian));
            if (dep.Length > 0 && dep == arr) Say($"Посадка и взлёт, полоса {dep}");
            else
            {
                if (arr.Length > 0) Say($"Посадочная полоса {arr}");
                if (dep.Length > 0) Say($"Взлётная полоса {dep}");
            }
        }

        if (metar != null)
        {
            Say(WindRussian(metar));
            DescribeBodyRussian(metar.Raw, Say);
            if (metar.Qnh is { } q) Say($"Давление кью-эн-эйч {q} {Plural(q, "гектопаскаль", "гектопаскаля", "гектопаскалей")}");
            if (QfeRemark().Match(metar.Raw) is { Success: true } qfe)
            {
                int mm = int.Parse(qfe.Groups["mm"].Value, Inv);
                string text = $"Давление аэродрома {mm} {Plural(mm, "миллиметр", "миллиметра", "миллиметров")} ртутного столба";
                if (qfe.Groups["hpa"].Success)
                {
                    int hpa = int.Parse(qfe.Groups["hpa"].Value, Inv);
                    text += $", {hpa} {Plural(hpa, "гектопаскаль", "гектопаскаля", "гектопаскалей")}";
                }
                Say(text);
            }
        }

        Say(remark);
        Say($"При первом выходе на связь сообщите о получении информации {phonetic}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>The wind in metres per second, as the METAR gives it or converted from knots.</summary>
    private static string WindRussian(Metar metar)
    {
        var tokens = metar.Raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var w = tokens.Skip(1).TakeWhile(t => t != "RMK").Select(t => WindGroupRu().Match(t)).FirstOrDefault(m => m.Success);
        if (w == null) return "";
        bool mps = w.Groups["unit"].Value == "MPS";
        int Mps(string value) => mps ? int.Parse(value, Inv) : (int)Math.Round(int.Parse(value, Inv) / 1.94384);
        string Speed(int v) => $"{v} {Plural(v, "метр", "метра", "метров")} в секунду";
        int speed = Mps(w.Groups["spd"].Value);
        if (speed == 0) return "Штиль";
        string gust = w.Groups["gust"].Success ? $", порывы {Mps(w.Groups["gust"].Value)}" : "";
        if (w.Groups["dir"].Value == "VRB") return $"Ветер переменный, {Speed(speed)}{gust}";
        int dir = int.Parse(w.Groups["dir"].Value, Inv);
        return $"Ветер {dir} {Plural(dir, "градус", "градуса", "градусов")}, {Speed(speed)}{gust}";
    }

    /// <summary>Visibility, weather, clouds (in metres) and temperature of the METAR, in Russian.</summary>
    private static void DescribeBodyRussian(string raw, Action<string> say)
    {
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var clouds = new List<string>();
        void SayClouds()
        {
            if (clouds.Count == 0) return;
            say("Облачность " + string.Join(", ", clouds));
            clouds.Clear();
        }
        static int Metres(int feet) => (int)Math.Round(feet * 0.3048 / 10.0) * 10;

        foreach (var t in tokens.Skip(1))
        {
            if (t is "RMK" or "TEMPO" or "BECMG" or "NOSIG") break;
            if (t == "CAVOK")
            {
                say("Кавок");
                continue;
            }
            if (t.Length == 4 && t.All(char.IsDigit))
            {
                int m = int.Parse(t, Inv);
                int km = m / 1000;
                say(m >= 9999 ? "Видимость 10 километров и более"
                    : m >= 5000 ? $"Видимость {km} {Plural(km, "километр", "километра", "километров")}"
                    : $"Видимость {m} {Plural(m, "метр", "метра", "метров")}");
                continue;
            }
            if (t.EndsWith("SM", StringComparison.Ordinal))
            {
                say($"Видимость {t[..^2]} миль");
                continue;
            }
            if (t is "NSC" or "NCD")
            {
                say("Без существенной облачности");
                continue;
            }
            if (t is "SKC" or "CLR")
            {
                say("Ясно");
                continue;
            }
            if (CloudGroup().Match(t) is { Success: true } c)
            {
                int metres = Metres(int.Parse(c.Groups["h"].Value, Inv) * 100);
                string unit = Plural(metres, "метр", "метра", "метров");
                if (c.Groups["cover"].Value == "VV")
                {
                    SayClouds();
                    say($"Вертикальная видимость {metres} {unit}");
                    continue;
                }
                string cover = c.Groups["cover"].Value switch
                {
                    "FEW" => "незначительная",
                    "SCT" => "рассеянная",
                    "BKN" => "значительная",
                    _ => "сплошная",
                };
                string type = c.Groups["type"].Value switch
                {
                    "CB" => " кучево-дождевая",
                    "TCU" => " мощно-кучевая",
                    _ => "",
                };
                clouds.Add($"{cover} {metres} {unit}{type}");
                continue;
            }
            if (TemperatureGroup().Match(t) is { Success: true } temp)
            {
                SayClouds();
                static string T(string v)
                {
                    int n = int.Parse(v.TrimStart('M'), Inv);
                    return n == 0 ? "ноль" : (v.StartsWith('M') ? "минус " : "плюс ") + n.ToString(Inv);
                }
                string dew = temp.Groups["d"].Success ? temp.Groups["d"].Value : "";
                say($"Температура {T(temp.Groups["t"].Value)}" + (dew.Length > 0 ? $", точка росы {T(dew)}" : ""));
                continue;
            }
            if (t.Length >= 2 && t != "AUTO" && !t.Contains('/') && WeatherGroup().Match(t) is { Success: true } w && w.Groups["ph"].Success)
                say(WeatherRussian(w));
        }
        SayClouds();
    }

    /// <summary>A weather group in Russian: "-SHRA" → "слабый ливневый дождь", "+TSRA" → "гроза, сильный дождь".</summary>
    private static string WeatherRussian(Match w)
    {
        string ph = w.Groups["ph"].Value, desc = w.Groups["desc"].Value, intensity = w.Groups["int"].Value;
        if (desc == "BL" && ph == "SN") return "Низовая метель";
        if (desc == "DR" && ph == "SN") return "Позёмок";

        // Phenomena with their grammatical gender: m, f or p (plural), so the adjectives agree.
        var nouns = new List<(string Word, char Gender)>();
        for (int i = 0; i + 1 < ph.Length; i += 2)
            nouns.Add(ph.Substring(i, 2) switch
            {
                "RA" => ("дождь", 'm'),
                "SN" => ("снег", 'm'),
                "DZ" => ("морось", 'f'),
                "BR" => ("дымка", 'f'),
                "FG" => ("туман", 'm'),
                "HZ" => ("мгла", 'f'),
                "GR" => ("град", 'm'),
                "GS" => ("мелкий град", 'm'),
                "PL" => ("ледяная крупа", 'f'),
                "SG" => ("снежные зёрна", 'p'),
                "IC" => ("ледяные иглы", 'p'),
                "FU" => ("дым", 'm'),
                "SQ" => ("шквал", 'm'),
                "DU" => ("пыль", 'f'),
                "SA" => ("песок", 'm'),
                "VA" => ("вулканический пепел", 'm'),
                "PO" => ("пыльные вихри", 'p'),
                "FC" => ("смерч", 'm'),
                "SS" => ("песчаная буря", 'f'),
                "DS" => ("пыльная буря", 'f'),
                "UP" => ("осадки", 'p'),
                var other => (other, 'm'),
            });
        char gender = nouns.Count > 0 ? nouns[0].Gender : 'm';
        string Adj(string stem) => stem + gender switch
        {
            'f' => "ая",
            'p' => "ые",
            _ => "ый",
        };

        var words = new List<string>();
        if (intensity == "-") words.Add(Adj("слаб"));
        else if (intensity == "+") words.Add(Adj("сильн"));
        string? descriptor = desc switch
        {
            "SH" => Adj("ливнев"),
            "FZ" => Adj("переохлаждённ"),
            "MI" => Adj("поземн"),
            "PR" => Adj("частичн"),
            _ => null,
        };
        if (descriptor != null) words.Add(descriptor);
        words.Add(string.Join(" и ", nouns.Select(n => n.Word)));
        string text = string.Join(' ', words.Where(x => x.Length > 0));
        if (desc == "BC") text += " клочьями";
        if (desc == "TS") text = nouns.Count > 0 ? "Гроза, " + text : "Гроза";
        if (intensity == "VC") text += " в окрестностях аэродрома";
        return text;
    }
}
