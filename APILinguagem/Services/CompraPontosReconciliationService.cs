using Microsoft.Extensions.Options;

namespace APILinguagem.Services;

public sealed class CompraPontosReconciliationService(
    IServiceScopeFactory scopeFactory,
    IOptions<InterPixOptions> options,
    IHostEnvironment environment,
    ILogger<CompraPontosReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured(environment.ContentRootPath))
        {
            logger.LogWarning("Reconciliação de cobranças Pix do Inter não iniciada: configure a integração na API.");
            return;
        }

        var intervalo = TimeSpan.FromSeconds(Math.Clamp(options.Value.ReconciliationIntervalSeconds, 15, 3600));
        using var timer = new PeriodicTimer(intervalo);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<CompraPontosService>();
                await service.ReconciliarPendentesAsync(stoppingToken);
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(exception, "Falha ao reconciliar cobranças Pix pendentes com o Inter.");
            }
            catch (InvalidDataException exception)
            {
                logger.LogError(exception, "Resposta inválida do Inter durante a reconciliação de cobranças Pix.");
            }
            catch (InvalidOperationException exception)
            {
                logger.LogError(exception, "Falha de configuração ou consistência durante a reconciliação Pix.");
            }
        }
    }
}