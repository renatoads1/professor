export async function copiarTexto(texto) {
    await navigator.clipboard.writeText(texto);
}