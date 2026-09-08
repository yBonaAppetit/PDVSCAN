# Filtro de código de barras para PDV

Aplicativo Windows 10/11 que reconhece o conteúdo bruto de cupons, extrai o
código padrão ou auxiliar e substitui o conteúdo do campo ativo antes do Enter.

## Comportamento da versão 1.4.0

- O teclado físico funciona normalmente enquanto o filtro está ativo.
- O hook apenas observa e acumula os caracteres; teclas comuns são liberadas
  imediatamente para o Windows.
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

A opção padrão é:

```json
{
  "UseAuxiliaryFromSecondScan": true
}
```

Com esse valor, o mesmo conteúdo bruto produz `PADRÃO, AUXILIAR, AUXILIAR...`.
A comparação considera o QR completo, incluindo o UID. Para recuperar a
alternância antiga (`PADRÃO, AUXILIAR, PADRÃO...`), configure também:

```json
{
  "UseAuxiliaryFromSecondScan": false,
  "AlternateRepeatedScans": true
}
```

## Simulador de leitor QR

O executável `PdvQrScannerSimulator.exe` testa o fluxo sem um leitor físico.
Ele não imita teclas injetadas pelo Windows: envia a leitura por um canal local
de teste, e o filtro executa a mesma separação e envio usados pelo scanner.

1. Inicie o filtro e confirme o estado **ATIVO**.
2. No ícone da bandeja, habilite **Permitir simulador (somente teste)**.
3. Abra o simulador e escolha um dos cupons de exemplo.
4. Clique em **Enviar leitura** e selecione o campo do PDV durante os três
   segundos de contagem regressiva.
5. Repita o mesmo exemplo: a primeira leitura envia o padrão e as seguintes
   enviam o auxiliar.
6. Ao terminar, desative **Permitir simulador**.

Os exemplos usam UIDs sintéticos e os códigos mostrados nas imagens:
`X96UG / 2W7LX1` e `8H57E / 2W7LZ5`. O canal aceita conexões somente do mesmo
usuário do Windows e permanece bloqueado por padrão. Para habilitá-lo já na
inicialização de um equipamento de teste, use `"SimulatorInputEnabled": true`.

## Som e nível de diagnóstico

Por padrão, entradas inválidas e leituras que não forem reconhecidas não emitem
som. Para habilitar esse aviso deliberadamente, use `"BeepOnError": true` no
`filtersettings.json`.

O log padrão registra inicialização, mudanças de estado, cupons concluídos,
resultado do tratamento, envio e falhas. Eventos repetitivos de cada tecla e
pacotes Raw Input ficam desativados. Para uma investigação temporária, use
`"DetailedInputLogging": true`; volte para `false` após coletar o erro.

## Log único em tempo real

Esta versão possui somente um tipo de log:

`%PROGRAMDATA%\PdvBarcodeFilter\diagnostics\realtime-*.log`

O menu **Abrir pasta de logs** abre esse diretório. O menu **Diagnóstico em
tempo real** mostra os mesmos eventos numa janela que não toma o foco do PDV.

O fluxo útil esperado é `SCAN_COMPLETE -> PARSE_OK -> SEND_BEGIN -> SEND_OK`.
O heartbeat é gravado somente quando o estado muda ou após cinco minutos sem
alterações. Com `DiagnosticLogRawData: true`, o log inclui o conteúdo do QR
Code; trate o arquivo como dado administrativo sensível.

Os antigos `filter-*.log` e `history-*.jsonl` não são mais criados. A aplicação
não remove automaticamente arquivos antigos já existentes.

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

Autoteste do artefato publicado:

```powershell
Start-Process .\dist-1.4.0\PdvBarcodeFilter.exe -ArgumentList '--self-test' -Wait -PassThru
```

Código de saída `0` significa aprovação.

## Repositório privado no GitHub

O projeto foi preparado para um repositório privado. `.gitignore` exclui
binários publicados, `bin/`, `obj/`, logs, históricos, configurações locais e
certificados. O workflow em `.github/workflows/ci.yml` compila e executa os
testes em Windows a cada push ou pull request.

Depois de revisar `git status`, a publicação pode ser feita com GitHub CLI:

```powershell
git add .
git commit -m "Versão inicial privada"
gh repo create pdv-barcode-filter --private --source . --remote origin --push
```

O parâmetro `--private` é indispensável. Não inclua a pasta externa `Erros`,
logs reais ou imagens do ambiente do cliente.

## Inicialização e assinatura

O instalador oferece inicialização elevada no logon. O menu da bandeja também
permite criar/remover essa tarefa agendada.

Os artefatos são gerados sem assinatura quando nenhum certificado é fornecido.
Para assinar:

```powershell
.\build-installer.ps1 -PfxPath 'C:\certificados\code-signing.pfx'
```

Arquivos `.pfx` e outros certificados são ignorados pelo Git.

## Atualizações

Não existe sistema de atualização automática nesta versão. Novas versões devem
ser compiladas e instaladas manualmente.
