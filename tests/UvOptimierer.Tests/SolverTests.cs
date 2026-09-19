using UvOptimierer.Core.Fachlogik;
using UvOptimierer.Core.Modell;
using UvOptimierer.Core.Solver;
using Xunit;

namespace UvOptimierer.Tests;

public class SolverTests
{
    private static Kontext KleinesProblem()
    {
        var t1 = new Lehrer { Name = "T1", SollWst = 10 }; t1.Faecher[0] = "D";
        var t2 = new Lehrer { Name = "T2", SollWst = 10 }; t2.Faecher[0] = "M";

        var e1 = new Eintrag { Klasse = "5a", Fach = "D", Wst = 2, WertUv = 2, Zeile = 2 };
        var e2 = new Eintrag { Klasse = "5a", Fach = "M", Wst = 2, WertUv = 2, Zeile = 3 };

        return new Kontext(new[] { t1, t2 }, new[] { e1, e2 }, Array.Empty<Wunsch>(), new ScoreParameter());
    }

    [Fact]
    public void Backtracking_findet_vollstaendige_gueltige_loesung()
    {
        var k = KleinesProblem();
        var erg = Optimierer.FuerVerfahren(Verfahren.Backtracking).Loese(k);
        PruefeGueltig(k, erg);
    }

    [Fact]
    public void SimulatedAnnealing_findet_vollstaendige_gueltige_loesung()
    {
        var k = KleinesProblem();
        var erg = Optimierer.FuerVerfahren(Verfahren.SimulatedAnnealing, seed: 42).Loese(k);
        PruefeGueltig(k, erg);
    }

    private static void PruefeGueltig(Kontext k, OptimierungsErgebnis erg)
    {
        Assert.Equal(0, erg.Loesung1.Unbesetzt);
        // Jede Zuweisung muss fachlich zulässig sein.
        for (int e = 0; e < k.NE; e++)
        {
            int idx = k.LehrerIdx(erg.Loesung1.Zuweisung[e]);
            Assert.True(idx >= 0, "Zuweisung zeigt auf unbekannte Lehrkraft.");
            Assert.True(FachLogik.KannFach(k.Lehrer[idx], k.Eintraege[e].Fach, k.Eintraege[e].IstOberstufe));
        }
        // Eindeutige Zuordnung im Muster: D→T1, M→T2.
        Assert.Equal("T1", erg.Loesung1.Zuweisung[0]);
        Assert.Equal("T2", erg.Loesung1.Zuweisung[1]);
    }
}
