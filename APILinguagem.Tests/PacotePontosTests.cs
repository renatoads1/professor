using APILinguagem.Models;
using Xunit;

namespace APILinguagem.Tests;

public sealed class PacotePontosTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(100_000_000)]
    public void AceitaPrecoNosLimites(int precoCentavos)
    {
        var pacote = new PacotePontos("Pacote", 1, precoCentavos);

        Assert.Equal(precoCentavos, pacote.PrecoCentavos);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100_000_001)]
    public void RejeitaPrecoForaDosLimites(int precoCentavos)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PacotePontos("Pacote", 1, precoCentavos));
    }
}
