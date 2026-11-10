# Compras de pontos por Pix (Inter)

A compra é criada somente a partir de um pacote ativo. A API grava o preço e a quantidade do pacote, cria uma cobrança Pix do Inter e devolve o QR Code e o código copia e cola ao aluno autenticado. O navegador nunca informa o valor nem confirma o pagamento.

## Configuração local

Configure a seção `InterPix` somente na configuração da API (`APILinguagem/appsettings.json`, que é ignorado pelo Git) ou em um provedor de configuração privado. Não coloque credenciais, certificados ou chaves no projeto Blazor WebAssembly.

```json
{
  "InterPix": {
	"Enabled": true,
	"BaseUrl": "https://cdpj-sandbox.partners.uatinter.co",
	"ClientId": "",
	"ClientSecret": "",
	"PixKey": "",
	"CertificatePath": "certificados/inter-certificado.crt",
	"PrivateKeyPath": "certificados/inter-chave.key",
	"Scope": "cob.write cob.read pix.read webhook.write",
	"WebhookUrl": "https://api.exemplo.com/api/compras-pontos/webhook/inter",
	"ExpirationMinutes": 30,
	"ReconciliationIntervalSeconds": 60
  }
}
```

O certificado pode ser PEM com a chave privada em arquivo separado (`CertificatePath` e `PrivateKeyPath`) ou um arquivo `.pfx`/`.p12` (`CertificatePath`). `CertificatePassword` é opcional e só deve ser informado se o arquivo PFX/P12 tiver senha; para arquivos PEM, CRT e KEY, omita essa propriedade. Caminhos relativos são resolvidos a partir da pasta da API. Comece sempre pelo sandbox e use a URL HTTPS pública da API em `WebhookUrl`.

Depois de iniciar a API, autentique-se como gerente e execute `PUT /api/compras-pontos/webhook/configurar` uma vez para registrar a URL de notificação no Inter. O endpoint público de webhook não credita pontos com base no conteúdo recebido: ele consulta a cobrança no Inter e valida txid, status e valor. Uma rotina da API também reconcilia cobranças pendentes.

## Endpoints usados pelo cliente

- `GET /api/pacotes-pontos`: lista os pacotes ativos.
- `POST /api/compras-pontos` com `{ "pacoteId": 1 }`: cria a compra e a cobrança Pix para o aluno autenticado.
- `POST /api/compras-pontos/{id}/pix`: tenta gerar novamente a cobrança pendente após uma falha temporária.
- `GET /api/compras-pontos/{id}`: consulta somente uma compra pertencente ao usuário autenticado.

Em ambientes de desenvolvimento, a API aplica as migrations ao iniciar. Para atualizar outro banco, use `dotnet ef database update --project .\APILinguagem\APILinguagem.csproj --startup-project .\APILinguagem\APILinguagem.csproj`.

Documentação oficial: [Inter Developers — Pix Cobrança](https://developers.inter.co/references/pix).