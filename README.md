# Filtro de código de barras para PDV

Aplicativo Windows 10/11 que reconhece o conteúdo bruto de cupons, extrai o
código padrão ou auxiliar e substitui o conteúdo do campo ativo antes do Enter.

## Como funciona

- O teclado físico funciona normalmente enquanto o filtro está ativo.
- Se o Enter concluir uma estrutura `IDENTIFICADOR|PADRAO|AUXILIAR` ou
  `IDENTIFICADOR}PADRAO}AUXILIAR`, somente esse Enter original é bloqueado.
- O filtro envia `Ctrl+A`, o código selecionado e um novo Enter. Assim, o
  conteúdo bruto que chegou ao campo é substituído pelo resultado tratado.
- Na primeira leitura de um cupom é enviado o código padrão. Da segunda leitura
  consecutiva do mesmo cupom em diante, é enviado sempre o código auxiliar.
- Um cupom diferente reinicia a sequência. Se o cupom anterior voltar depois
  de outro, sua leitura também começa novamente pelo código padrão.
- `Ctrl+Shift+F12` pausa ou reativa globalmente o filtro por padrão.
- O ícone da bandeja também permite pausar, abrir o diagnóstico e sair.

Antes de ler o QR Code, mantenha o cursor no campo do cupom. Como o modo de
teclado livre utiliza `Ctrl+A` para substituir o bruto, esse campo deve estar
selecionado e pronto para receber a leitura.

## Atalho configurável

Copie `filtersettings.example.json` para a pasta do executável com o nome
`filtersettings.json`. Altere, por exemplo:

```json
{
  "ToggleHotkey": "Ctrl+Shift+F12"
}
```

Também são aceitos formatos como `F11`, `Ctrl+F12` ou `Ctrl+Alt+P`. O atalho
alterna entre:

- **ATIVO**: cupons válidos são tratados; o teclado comum continua liberado;
- **PAUSADO**: teclado e scanner passam sem qualquer tratamento.

Se o atalho já pertencer a outro programa, a falha aparecerá no log em tempo
real e o menu da bandeja continuará disponível.

## Regra para repetição do mesmo cupom

Com `"UseAuxiliaryFromSecondScan": true`, que é o padrão, o mesmo conteúdo
bruto produz `PADRÃO, AUXILIAR, AUXILIAR...`. A comparação considera o QR
completo, incluindo o UID. Um cupom diferente reinicia a sequência.

## Simulador de leitor QR

O executável `PdvQrScannerSimulator.exe` testa o fluxo sem um leitor físico.
Ele não imita teclas injetadas pelo Windows: envia a leitura por um canal local
de teste, e o filtro executa a mesma separação e envio usados pelo scanner.

1. Inicie o filtro e confirme o estado **ATIVO**.
2. No ícone da bandeja, habilite **Permitir simulador (somente teste)**.
3. Abra o simulador, escolha um exemplo e clique em **Enviar leitura**.
4. Selecione o campo do PDV durante a contagem regressiva.
5. Repita o exemplo para conferir o envio do código auxiliar e desative o
   simulador ao terminar.

Os exemplos usam UIDs sintéticos e os códigos mostrados nas imagens:
`X96UG / 2W7LX1` e `8H57E / 2W7LZ5`. O canal aceita conexões somente do mesmo
usuário do Windows e permanece bloqueado por padrão. Para habilitá-lo já na
inicialização de um equipamento de teste, use `"SimulatorInputEnabled": true`.

## Diagnóstico e som

Por padrão, entradas inválidas e leituras que não forem reconhecidas não emitem
som. Para habilitar esse aviso deliberadamente, use `"BeepOnError": true` no
`filtersettings.json`.

O log em tempo real registra inicialização, mudanças de estado, cupons
concluídos, resultado do tratamento, envio e falhas:

`%PROGRAMDATA%\PdvBarcodeFilter\diagnostics\realtime-*.log`

O menu **Abrir pasta de logs** abre esse diretório. O menu **Diagnóstico em
tempo real** mostra os mesmos eventos numa janela que não toma o foco do PDV.

O fluxo útil esperado é `SCAN_COMPLETE -> PARSE_OK -> SEND_BEGIN -> SEND_OK`.
O heartbeat é gravado somente quando o estado muda ou após cinco minutos sem
alterações. Com `DiagnosticLogRawData: true`, o log inclui o conteúdo do QR
Code; trate o arquivo como dado administrativo sensível.

Eventos detalhados de cada tecla ficam desativados por padrão. Para uma
investigação temporária, use `"DetailedInputLogging": true` e retorne a opção
para `false` após coletar o erro.

## Compilar e testar

Requer .NET SDK 8:

```powershell
dotnet build .\src\PdvBarcodeFilter\PdvBarcodeFilter.csproj -c Release
dotnet build .\tools\PdvQrScannerSimulator\PdvQrScannerSimulator.csproj -c Release
dotnet run --project .\tests\PdvBarcodeFilter.Tests\PdvBarcodeFilter.Tests.csproj -c Release
.\build-release.ps1
.\build-installer.ps1
```

O executável autocontido será criado em
`dist-1.4.0\PdvBarcodeFilter.exe`. O simulador ficará em
`simulator-dist-1.4.0\PdvQrScannerSimulator.exe` e o instalador em
`installer\PdvBarcodeFilter-Setup-1.4.0-x64.exe`.

Autoteste do artefato gerado:

```powershell
Start-Process .\dist-1.4.0\PdvBarcodeFilter.exe -ArgumentList '--self-test' -Wait -PassThru
```

Código de saída `0` significa aprovação.

O workflow `.github/workflows/ci.yml` compila o projeto e executa os testes no
Windows a cada push ou pull request.

## Inicialização e assinatura

O instalador oferece inicialização elevada no logon. O menu da bandeja também
permite criar/remover essa tarefa agendada.

Os artefatos são gerados sem assinatura quando nenhum certificado é fornecido.
Para assinar:

```powershell
.\build-installer.ps1 -PfxPath 'C:\certificados\code-signing.pfx'
```

Arquivos `.pfx` e outros certificados são ignorados pelo Git.
