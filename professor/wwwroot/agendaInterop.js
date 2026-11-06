export function obterFusoHorarioId() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}
