using UvOptimierer.Core.Fachlogik;
using UvOptimierer.Core.Modell;
using Xunit;

namespace UvOptimierer.Tests;

public class FachLogikTests
{
    [Theory]
    [InlineData("D L1", "D L")]
    [InlineData("SB2", "SB")]
    [InlineData("D", "")]          // keine Endziffer → kein Gruppenmitglied
    [InlineData("M G1", "M G")]
    [InlineData("", "")]
    [InlineData("D LRS 5.1", "D LRS 5.")]
    public void FachStamm_entfernt_endziffern(string fach, string erwartet)
        => Assert.Equal(erwartet, FachLogik.FachStamm(fach));

    [Fact]
    public void KannFach_exakter_treffer_immer_erlaubt()
    {
        var l = Lehrer("Müller", "D");
        Assert.True(FachLogik.KannFach(l, "D", oberstufe: false));
        // Exakter Treffer gilt auch in der Oberstufe, unabhängig vom Oberstufen-Flag.
        Assert.True(FachLogik.KannFach(l, "D", oberstufe: true));
    }

    [Fact]
    public void KannFach_stammtreffer_in_sekundarstufe_erlaubt()
    {
        var l = Lehrer("Meier", "D L1");
        Assert.True(FachLogik.KannFach(l, "D L2", oberstufe: false));
    }

    [Fact]
    public void KannFach_stammtreffer_in_oberstufe_nur_mit_flag()
    {
        var mitFlag = Lehrer("A", "D L1");
        mitFlag.OberstufeOk[0] = true;
        Assert.True(FachLogik.KannFach(mitFlag, "D L2", oberstufe: true));

        var ohneFlag = Lehrer("B", "D L1");
        ohneFlag.OberstufeOk[0] = false;
        Assert.False(FachLogik.KannFach(ohneFlag, "D L2", oberstufe: true));
    }

    [Fact]
    public void KannFach_fremdes_fach_abgelehnt()
    {
        var l = Lehrer("C", "D");
        Assert.False(FachLogik.KannFach(l, "M", oberstufe: false));
    }

    [Theory]
    [InlineData("D EF", true)]
    [InlineData("M Q1", true)]
    [InlineData("D L1", true)]   // Level-Präfix
    [InlineData("M G1", true)]   // Gruppen-Präfix
    [InlineData("D", false)]
    [InlineData("", false)]
    public void IstOberstufenFach_erkennt_oberstufe(string fach, bool erwartet)
        => Assert.Equal(erwartet, FachLogik.IstOberstufenFach(fach));

    private static Lehrer Lehrer(string name, params string[] faecher)
    {
        var l = new Lehrer { Name = name };
        for (int i = 0; i < faecher.Length && i < l.Faecher.Length; i++)
            l.Faecher[i] = faecher[i];
        return l;
    }
}
