using UvOptimierer.Core.Bewertung;
using UvOptimierer.Core.Modell;
using Xunit;

namespace UvOptimierer.Tests;

public class ScoreTests
{
    [Fact]
    public void Berechne_klassenleitung_plus_freie_stunden()
    {
        var l = new Lehrer { Name = "KL", SollWst = 25, KlassenleitungKlasse = "5a" };
        var e = new Eintrag { Klasse = "5a", Fach = "D", Wst = 4, WertUv = 4 };
        var k = Ktx(new[] { l }, new[] { e });

        // KL(100) + freieStd(25)*FreiF(3)=75 → 175; nachZuweisung=21 (außerhalb Toleranzfenster) → 0.
        Assert.Equal(175, Score.Berechne(k, 0, 0), 3);
    }

    [Fact]
    public void Berechne_positiver_wunsch_prio2()
    {
        var l = new Lehrer { Name = "W", SollWst = 20 };
        var e = new Eintrag { Klasse = "7c", Fach = "M", Wst = 3, WertUv = 3 };
        var w = new Wunsch { LehrerName = "W", WunschKlasse = "7c", WunschPrio = 2 };
        var k = Ktx(new[] { l }, new[] { e }, new[] { w });

        // W2(40) + freieStd(20)*FreiF(3)=60 → 100.
        Assert.Equal(100, Score.Berechne(k, 0, 0), 3);
    }

    [Fact]
    public void Berechne_anti_wunsch_prio1_zieht_ab()
    {
        var l = new Lehrer { Name = "A", SollWst = 10 };
        var e = new Eintrag { Klasse = "9b", Fach = "E", Wst = 2, WertUv = 2 };
        var w = new Wunsch { LehrerName = "A", AntiKlasse = "9b", AntiPrio = 1 };
        var k = Ktx(new[] { l }, new[] { e }, new[] { w });

        // -A1(15) + freieStd(10)*FreiF(3)=30 → 15.
        Assert.Equal(15, Score.Berechne(k, 0, 0), 3);
    }

    [Fact]
    public void HatAntiPflicht_nur_bei_prio3()
    {
        var l = new Lehrer { Name = "X" };
        var e = new Eintrag { Klasse = "8a", Fach = "D" };
        var w3 = new Wunsch { LehrerName = "X", AntiKlasse = "8a", AntiPrio = 3 };
        var k = Ktx(new[] { l }, new[] { e }, new[] { w3 });

        Assert.True(Score.HatAntiPflicht(k, "X", "8a", "D"));
        Assert.False(Score.HatAntiPflicht(k, "X", "8b", "D"));
        Assert.False(Score.HatAntiPflicht(k, "Y", "8a", "D"));
    }

    [Fact]
    public void GesamtScore_bestraft_unbesetzte_eintraege()
    {
        var l = new Lehrer { Name = "L", SollWst = 20 };
        var e1 = new Eintrag { Klasse = "5a", Fach = "D", Wst = 2, WertUv = 2 };
        var e2 = new Eintrag { Klasse = "5a", Fach = "M", Wst = 2, WertUv = 2 };
        var k = Ktx(new[] { l }, new[] { e1, e2 });

        // e1 an L, e2 unbesetzt → -500-Strafe muss enthalten sein.
        double mitLuecke = Score.GesamtScore(k, new[] { "L", "?" });
        double voll = Score.GesamtScore(k, new[] { "L", "L" });
        Assert.True(voll > mitLuecke);
        Assert.True(mitLuecke <= voll - 500);
    }

    private static Kontext Ktx(Lehrer[] lehrer, Eintrag[] eintraege, Wunsch[]? wuensche = null)
        => new(lehrer, eintraege, wuensche ?? Array.Empty<Wunsch>(), new ScoreParameter());
}
