using System.Security.Claims;
using APILinguagem.Data;
using APILinguagem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace APILinguagem.Hubs;

[Authorize]
public sealed class AulaHub(AlunosDbContext dbContext) : Hub
{
    private const string AulaConexaoKey = "aulaId";
    private const int MinutosAntesDaAula = 15;
    private const int MinutosAposAula = 15;
    private const int TamanhoMaximoSdp = 65_536;
    private const int TamanhoMaximoCandidato = 4_096;

    public async Task<AulaHubEntradaResponse> EntrarNaAulaAsync(long aulaId)
    {
        if (aulaId <= 0 || Context.Items.ContainsKey(AulaConexaoKey))
        {
            throw new HubException("A conexão já está em uma sala ou o identificador da aula é inválido.");
        }

        if (!int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var contaAcessoId))
        {
            throw new HubException("A identidade da conexão é inválida.");
        }

        var aula = await dbContext.AulasAgendadas
            .AsNoTracking()
            .Where(item => item.Id == aulaId
                && (item.ContaAcessoAlunoId == contaAcessoId || item.ContaAcessoProfessorId == contaAcessoId))
            .Select(item => new
            {
                item.ContaAcessoProfessorId,
                item.Status,
                item.InicioUtc,
                item.FimUtc
            })
            .SingleOrDefaultAsync(Context.ConnectionAborted);

        var agora = DateTimeOffset.UtcNow;
        if (aula is null
            || aula.Status != StatusAula.Agendada
            || agora < aula.InicioUtc.AddMinutes(-MinutosAntesDaAula)
            || agora > aula.FimUtc.AddMinutes(MinutosAposAula))
        {
            throw new HubException("A aula não existe, não permite entrada neste momento ou não pertence a esta conta.");
        }

        var grupo = NomeGrupo(aulaId);
        await Groups.AddToGroupAsync(Context.ConnectionId, grupo, Context.ConnectionAborted);
        Context.Items[AulaConexaoKey] = aulaId;
        await Clients.OthersInGroup(grupo).SendAsync("ParticipanteEntrou", Context.ConnectionAborted);

        return new AulaHubEntradaResponse(aula.ContaAcessoProfessorId == contaAcessoId);
    }

    public Task EnviarOfertaAsync(long aulaId, string sdp) =>
        EnviarSinalAsync(aulaId, "OfertaRecebida", sdp, TamanhoMaximoSdp);

    public Task EnviarRespostaAsync(long aulaId, string sdp) =>
        EnviarSinalAsync(aulaId, "RespostaRecebida", sdp, TamanhoMaximoSdp);

    public async Task EnviarCandidatoAsync(long aulaId, CandidatoIceRequest candidato)
    {
        ValidarSala(aulaId);
        if (candidato is null
            || string.IsNullOrWhiteSpace(candidato.Candidato)
            || candidato.Candidato.Length > TamanhoMaximoCandidato
            || candidato.SdpMLineIndex is < 0
            || candidato.SdpMid?.Length is > 256)
        {
            throw new HubException("O candidato ICE é inválido.");
        }

        await Clients.OthersInGroup(NomeGrupo(aulaId))
            .SendAsync("CandidatoRecebido", candidato, Context.ConnectionAborted);
    }

    public async Task SairDaAulaAsync(long aulaId)
    {
        ValidarSala(aulaId);
        var grupo = NomeGrupo(aulaId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, grupo, Context.ConnectionAborted);
        Context.Items.Remove(AulaConexaoKey);
        await Clients.OthersInGroup(grupo).SendAsync("ParticipanteSaiu", Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(AulaConexaoKey, out var value) && value is long aulaId)
        {
            await Clients.OthersInGroup(NomeGrupo(aulaId)).SendAsync("ParticipanteSaiu", CancellationToken.None);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task EnviarSinalAsync(long aulaId, string evento, string sdp, int tamanhoMaximo)
    {
        ValidarSala(aulaId);
        if (string.IsNullOrWhiteSpace(sdp) || sdp.Length > tamanhoMaximo)
        {
            throw new HubException("A mensagem de sinalização é inválida.");
        }

        await Clients.OthersInGroup(NomeGrupo(aulaId))
            .SendAsync(evento, sdp, Context.ConnectionAborted);
    }

    private void ValidarSala(long aulaId)
    {
        if (aulaId <= 0
            || !Context.Items.TryGetValue(AulaConexaoKey, out var value)
            || value is not long salaAtual
            || salaAtual != aulaId)
        {
            throw new HubException("Entre na sala autorizada antes de enviar sinalização.");
        }
    }

    private static string NomeGrupo(long aulaId) => $"aula:{aulaId}";
}

public sealed record AulaHubEntradaResponse(bool Professor);

public sealed record CandidatoIceRequest(string Candidato, string? SdpMid, int? SdpMLineIndex);
