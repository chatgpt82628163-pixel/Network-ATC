using NetworkAtc.Core.Atis;
using NetworkAtc.Core.Customization;
using NetworkAtc.Core.Fsd;
using NetworkAtc.Core.Sectors;
using NetworkAtc.Core.Weather;
using SkyNetwork.Voice;

namespace NetworkAtc.Core.Tests;

public class AtisTests
{
    private static readonly Metar Uuee = MetarParser.Parse("UUEE 241230Z 27008MPS 9999 -SHRA BKN020CB OVC100 15/10 Q1013 NOSIG")!;

    [Fact]
    public void LettersCallsignsAndRunways()
    {
        Assert.Equal("B", AtisText.Next("A"));
        Assert.Equal("A", AtisText.Next("Z"));
        Assert.Equal("A", AtisText.Next(""));
        Assert.Equal("UUEE_ATIS", AtisText.Callsign("uuee", ""));
        Assert.Equal("UUEE_A_ATIS", AtisText.Callsign("UUEE", "a"));
        Assert.Equal("Bravo", AtisText.Phonetic("b"));
        Assert.Equal("X-ray", AtisText.Phonetic("X"));
        Assert.Equal("24 right", AtisText.Runway("24R"));
        Assert.Equal("6 left", AtisText.Runway("06L"));
    }

    [Fact]
    public void SpeechFromTheMetar()
    {
        var rwy = new RunwayUse { Departure = ["24R"], Arrival = ["24L"] };
        var en = AtisText.Speech("Sheremetyevo", "B", new DateTime(2026, 9, 24, 12, 30, 0), rwy, Uuee, "Taxiway B closed");
        Assert.StartsWith("Sheremetyevo information Bravo, time 1230.", en);
        Assert.Contains("Landing runway 24 left. Departure runway 24 right.", en);
        Assert.Contains("Wind 270 degrees 16 knots.", en);
        Assert.Contains("Visibility 10 kilometers or more.", en);
        Assert.Contains("light showers of rain.", en);
        Assert.Contains("Clouds broken 2000 feet cumulonimbus, overcast 10000 feet.", en);
        Assert.Contains("Temperature 15, dew point 10.", en);
        Assert.Contains("QNH 1013.", en);
        Assert.Contains("Taxiway B closed.", en);
        Assert.EndsWith("Advise on initial contact you have information Bravo.", en);

        var calm = AtisText.Speech("UUEE", "C", new DateTime(2026, 9, 24, 12, 30, 0), new RunwayUse { Departure = ["24"], Arrival = ["24"] },
            MetarParser.Parse("UUEE 241230Z 00000KT CAVOK M02/M05 Q1030")!, "");
        Assert.Contains("UUEE information Charlie, time 1230.", calm);
        Assert.Contains("Runway in use 24.", calm);
        Assert.Contains("Wind calm.", calm);
        Assert.Contains("CAVOK.", calm);
        Assert.Contains("Temperature minus 2, dew point minus 5.", calm);
        Assert.Contains("QNH 1030.", calm);
    }

    [Fact]
    public void RussianSpeechFromTheMetar()
    {
        var at = new DateTime(2026, 9, 24, 12, 30, 0);
        var rwy = new RunwayUse { Departure = ["24R"], Arrival = ["24L"] };
        var metar = MetarParser.Parse("UUEE 241230Z 27008G13MPS 9999 -SHRA BKN020CB OVC100 15/10 Q1013 NOSIG RMK QFE745/0993")!;
        var ru = AtisText.SpeechRu("Шереметьево", "B", at, rwy, metar, "Рулёжная дорожка B закрыта");
        Assert.StartsWith("Шереметьево, информация Браво, время 12 30.", ru);
        Assert.Contains("Посадочная полоса 24 левая. Взлётная полоса 24 правая.", ru);
        Assert.Contains("Ветер 270 градусов, 8 метров в секунду, порывы 13.", ru);
        Assert.Contains("Видимость 10 километров и более.", ru);
        Assert.Contains("Слабый ливневый дождь.", ru);
        Assert.Contains("Облачность значительная 610 метров кучево-дождевая, сплошная 3050 метров.", ru);
        Assert.Contains("Температура плюс 15, точка росы плюс 10.", ru);
        Assert.Contains("Давление кью-эн-эйч 1013 гектопаскалей.", ru);
        Assert.Contains("Давление аэродрома 745 миллиметров ртутного столба, 993 гектопаскаля.", ru);
        Assert.Contains("Рулёжная дорожка B закрыта.", ru);
        Assert.EndsWith("При первом выходе на связь сообщите о получении информации Браво.", ru);

        var calm = AtisText.SpeechRu("UUEE", "C", at, new RunwayUse { Departure = ["24"], Arrival = ["24"] },
            MetarParser.Parse("UUEE 241230Z 00000KT CAVOK M02/M05 Q1030")!, "");
        Assert.Contains("Посадка и взлёт, полоса 24.", calm);
        Assert.Contains("Штиль.", calm);
        Assert.Contains("Кавок.", calm);
        Assert.Contains("Температура минус 2, точка росы минус 5.", calm);
        Assert.Contains("Давление кью-эн-эйч 1030 гектопаскалей.", calm);

        // Knots are spoken in metres per second; cloud base in metres.
        var knots = AtisText.SpeechRu("UUEE", "D", at, null, MetarParser.Parse("UUEE 241230Z 18010KT 4000 BR FEW005 08/07 Q0998")!, "");
        Assert.Contains("Ветер 180 градусов, 5 метров в секунду.", knots);
        Assert.Contains("Видимость 4000 метров.", knots);
        Assert.Contains("Дымка.", knots);
        Assert.Contains("Облачность незначительная 150 метров.", knots);
        Assert.Contains("Давление кью-эн-эйч 998 гектопаскалей.", knots);
        Assert.Contains("Гроза, сильный дождь.", AtisText.SpeechRu("UUEE", "E", at, null, MetarParser.Parse("UUEE 241230Z 21005MPS 3000 +TSRA BKN010CB 20/18 Q1005")!, ""));
    }

    [Fact]
    public void RussianWordForms()
    {
        Assert.Equal("метр", AtisText.Plural(1, "метр", "метра", "метров"));
        Assert.Equal("метра", AtisText.Plural(3, "метр", "метра", "метров"));
        Assert.Equal("метров", AtisText.Plural(5, "метр", "метра", "метров"));
        Assert.Equal("метров", AtisText.Plural(12, "метр", "метра", "метров"));
        Assert.Equal("метр", AtisText.Plural(21, "метр", "метра", "метров"));
        Assert.Equal("Браво", AtisText.PhoneticRussian("b"));
        Assert.Equal("24 правая", AtisText.RunwayRussian("24R"));
        Assert.Equal("6 левая", AtisText.RunwayRussian("06L"));
    }

    [Fact]
    public void Service_SpeaksInTheStationLanguage()
    {
        var profile = new Profile();
        var station = new AtisSettings { Airport = "UUEE", SpokenName = "Шереметьево", Language = "ru" };
        profile.Atis.Add(station);
        var service = new AtisService(() => profile, line => line, _ => Uuee, () => new DateTime(2026, 9, 24, 12, 30, 0));
        service.SetLetter("UUEE", "A");
        Assert.StartsWith("Шереметьево, информация Альфа", service.Speech(station));
        station.Language = "en";
        station.SpokenName = "Sheremetyevo";
        Assert.StartsWith("Sheremetyevo information Alfa", service.Speech(station));
        Assert.Same(Uuee, service.CurrentMetar("uuee"));
    }

    [Fact]
    public void Service_ExpandsTheTextPerAirport_AndMovesTheLetterOnANewMetar()
    {
        var profile = new Profile();
        profile.Atis.Add(new AtisSettings { Airport = "UUEE", Frequency = "128.050", Remark = "BIRD ACTIVITY" });
        var metars = new Dictionary<string, Metar> { ["UUEE"] = Uuee };
        var service = new AtisService(() => profile,
            line => line.Replace("$atiscode(UUEE)", profile.AtisLetters.GetValueOrDefault("UUEE", "")).Replace("$metar(UUEE)", metars["UUEE"].Raw)
                        .Replace("$time", "1230").Replace("$deprwy(UUEE)", "24R").Replace("$arrrwy(UUEE)", "24L"),
            s => metars.GetValueOrDefault(s));
        var changed = new List<string>();
        service.LetterChanged += (_, a) => changed.Add(a);

        service.SetLetter("uuee", "A");
        var text = service.Text(profile.Atis[0]);
        Assert.Equal("UUEE ATIS INFORMATION A 1230", text[0]);
        Assert.Equal("DEPARTURE RWY 24R ARRIVAL RWY 24L", text[1]);
        Assert.Equal(Uuee.Raw, text[2]);
        Assert.Equal("BIRD ACTIVITY", text[^1]);

        service.OnMetar(Uuee);                  // the first METAR seen: no change
        service.OnMetar(Uuee);                  // the same again: no change
        Assert.Equal("A", service.Letter("UUEE"));
        service.OnMetar(MetarParser.Parse("UUEE 241300Z 27010MPS 9999 BKN020 15/10 Q1012")!);
        Assert.Equal("B", service.Letter("UUEE"));
        profile.Atis[0].AutoLetter = false;
        service.OnMetar(MetarParser.Parse("UUEE 241330Z 27010MPS 9999 BKN020 15/10 Q1011")!);
        Assert.Equal("B", service.Letter("UUEE"));
        Assert.Equal(["UUEE", "UUEE"], changed);
        Assert.Contains("information Bravo", service.Speech(profile.Atis[0]));
    }

    [Fact]
    public void Station_AnswersAtisRequestsAddressedToIt()
    {
        var station = new AtisStation("UUEE_ATIS", () => ["UUEE ATIS INFORMATION A", "QNH 1013"]);
        var reply = station.Reply(FsdPacket.Parse("$CQAFL123:UUEE_ATIS:ATIS")!).ToList();
        Assert.Equal(["$CRUUEE_ATIS:AFL123:ATIS:T:UUEE ATIS INFORMATION A", "$CRUUEE_ATIS:AFL123:ATIS:T:QNH 1013", "$CRUUEE_ATIS:AFL123:ATIS:E:3"], reply);
        Assert.Equal(["$POUUEE_ATIS:AFL123:42"], station.Reply(FsdPacket.Parse("$PIAFL123:UUEE_ATIS:42")!));
        Assert.Empty(station.Reply(FsdPacket.Parse("#TMAFL123:UUEE_ATIS:hello")!));
    }

    [Fact]
    public void ProfileKeepsAtisStations()
    {
        var path = Path.Combine(Path.GetTempPath(), $"natc-{Guid.NewGuid():N}.json");
        try
        {
            var p = new Profile();
            p.Atis.Add(new AtisSettings { Airport = "UUEE", Frequency = "128.050", Voice = AtisVoiceMode.Recording, Language = "en" });
            p.Save(path);
            var q = Profile.Load(path);
            var a = Assert.Single(q.Atis);
            Assert.Equal(("UUEE_ATIS", "128.050", AtisVoiceMode.Recording, "en"), (a.Callsign, a.Frequency, a.Voice, a.Language));
            Assert.Equal(AtisSettings.DefaultText, a.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FakeSender : IAudioSender
    {
        public List<(uint Seq, bool Last)> Frames { get; } = [];
        public bool IsConnected => true;
        public void SendAudio(uint sequence, bool last, ReadOnlySpan<byte> transmitterIds, ReadOnlySpan<byte> opus) => Frames.Add((sequence, last));
    }

    [Fact]
    public void Broadcaster_PlaysTheRecordingInALoopInRealTime()
    {
        var sender = new FakeSender();
        var b = new AtisBroadcaster(sender, [0]) { Pause = TimeSpan.FromMilliseconds(100) };
        b.SetRecording(new float[AudioFormat.FrameSamples * 5]); // 100 ms
        b.Pump(TimeSpan.FromMilliseconds(80));                     // frames due at 0, 20, 40, 60, 80
        Assert.Equal(5, sender.Frames.Count(f => !f.Last));
        Assert.Single(sender.Frames, f => f.Last);                  // the recording ended: closing frame
        b.Pump(TimeSpan.FromMilliseconds(180));                    // the pause: nothing on the air
        Assert.Equal(6, sender.Frames.Count);
        b.Pump(TimeSpan.FromMilliseconds(200));                    // the next repeat starts
        Assert.Equal(7, sender.Frames.Count);
        Assert.True(b.OnAir);
        b.Stop();
        Assert.False(b.OnAir);
        Assert.True(sender.Frames[^1].Last);
    }

    [Fact]
    public void VatisProfileBecomesStationsWithPresets()
    {
        const string json = """
        {
          "name": "Moscow",
          "stations": [
            {
              "identifier": "uuee",
              "name": "Sheremetyevo",
              "atisType": "Arrival",
              "frequency": 128050000,
              "atisVoice": { "useTextToSpeech": true },
              "contractions": [ { "variableName": "ILS", "text": "ILS APPROACH", "voice": "I L S approach" } ],
              "airportConditionDefinitions": [ { "text": "BIRD ACTIVITY", "enabled": true }, { "text": "OFF", "enabled": false } ],
              "presets": [
                {
                  "name": "West",
                  "template": "[FACILITY] ATIS INFORMATION [ATIS_CODE] [OBS_TIME]. EXPECT @ILS. [FULL_WX_STRING]. [ARPT_COND] [NOTAMS] ADVISE YOU HAVE [ATIS_CODE]. [VOICE:spoken only][TEXT:TL 60]",
                  "airportConditions": "RWY 24L CLOSED",
                  "notams": "TWY B CLOSED"
                },
                { "name": "East", "template": "[FACILITY] INFO [ATIS_CODE] QNH [PRESSURE]" }
              ]
            },
            { "Identifier": "UUDD", "AtisType": "Combined", "Frequency": 128400, "AtisVoice": { "UseTextToSpeech": false } }
          ]
        }
        """;
        var stations = VatisImport.Parse(json);
        Assert.Equal(2, stations.Count);
        var s = stations[0];
        Assert.Equal(("UUEE", "A", "128.050", "Sheremetyevo", "UUEE_A_ATIS"), (s.Airport, s.Suffix, s.Frequency, s.SpokenName, s.Callsign));
        Assert.Equal(AtisVoiceMode.Speech, s.Voice);
        Assert.Equal(["West", "East"], s.Presets.Select(p => p.Name));
        Assert.Equal("West", s.Preset);
        Assert.Equal(["$airport ATIS INFORMATION $atiscode($airport) $time. EXPECT ILS APPROACH. $metar($airport). ADVISE YOU HAVE $atiscode($airport). TL 60"], s.Text);
        Assert.Equal("RWY 24L CLOSED BIRD ACTIVITY TWY B CLOSED", s.Remark);
        s.ApplyPreset(s.Presets[1]);
        Assert.Equal(["$airport INFO $atiscode($airport) QNH $qnh($airport)"], s.Text);
        Assert.Equal(("East", "BIRD ACTIVITY"), (s.Preset, s.Remark)); // conditions switched on for the station apply to every preset

        var d = stations[1];
        Assert.Equal(("UUDD", "", "128.400", AtisVoiceMode.None), (d.Airport, d.Suffix, d.Frequency, d.Voice));
        Assert.Equal(AtisSettings.DefaultText, d.Text);
        Assert.Throws<FormatException>(() => VatisImport.Parse("""{ "name": "x" }"""));
    }
}
