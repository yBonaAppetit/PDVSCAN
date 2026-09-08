# Código-fonte do Filtro PDV

Esta pasta contém uma cópia independente do projeto PdvBarcodeFilter, versão
1.4.0, preparada em 8 de setembro de 2026 para leitura e edição no VS Code.
As alterações feitas aqui não são sincronizadas automaticamente com a pasta
original `PdvBarcodeFilter`.

## Abrir no VS Code

No VS Code, escolha **Arquivo > Abrir Pasta** e selecione esta pasta:

```text
PdvBarcodeFilter-CodigoFonte
```

Você também pode usar **Arquivo > Abrir Workspace do Arquivo** e selecionar
`PdvBarcodeFilter.code-workspace`.

Não é necessário executar o programa nem instalar o .NET para apenas ler os
arquivos. O código principal está em C# (`.cs`); os scripts de compilação estão
em PowerShell (`.ps1`).

## Por onde começar

| Arquivo | O que você encontra |
| --- | --- |
| `src/PdvBarcodeFilter/Program.cs` | Inicialização, instância única e encerramento. |
| `src/PdvBarcodeFilter/ScanProcessor.cs` | Separação do QR, escolha do código padrão ou auxiliar e regra de repetição. |
| `src/PdvBarcodeFilter/KeyboardHook.cs` | Captura das teclas e reconhecimento da leitura concluída com Enter. |
| `src/PdvBarcodeFilter/ScanCoordinator.cs` | Fila de leituras, processamento e coordenação do envio. |
| `src/PdvBarcodeFilter/KeyboardSender.cs` | Envio de Ctrl+A, código útil e Enter ao campo ativo. |
| `src/PdvBarcodeFilter/FilterSettings.cs` | Opções disponíveis e valores padrão. |
| `src/PdvBarcodeFilter/TrayApplicationContext.cs` | Ícone, menus, pausa, diagnóstico e controles da aplicação. |
| `src/PdvBarcodeFilter/RealtimeDiagnosticHub.cs` | Registro dos eventos no log em tempo real. |
| `src/PdvBarcodeFilter/ScannerSimulatorProtocol.cs` | Comunicação local entre o simulador e o filtro. |
| `tools/PdvQrScannerSimulator/SimulatorForm.cs` | Tela e envio das leituras de teste. |
| `tests/PdvBarcodeFilter.Tests/Program.cs` | Casos de teste automatizados. |

Para entender a transformação do cupom, comece por `ScanProcessor.cs`.
Depois acompanhe `KeyboardHook.cs`, `ScanCoordinator.cs` e `KeyboardSender.cs`
para entender como a entrada chega ao processamento e volta ao campo do PDV.

## Organização

```text
src/PdvBarcodeFilter/          Aplicativo principal
tools/PdvQrScannerSimulator/   Simulador de leitor
tests/PdvBarcodeFilter.Tests/  Testes automatizados
docs/                         Guia resumido de uso
.github/workflows/            Configuração de integração contínua
filtersettings.example.json   Exemplo de configuração
build-release.ps1             Publicação dos executáveis
build-installer.ps1           Geração do instalador
installer.iss                 Definição do instalador
README.md                     Documentação geral
```

A pasta não inclui executáveis publicados, caches de compilação, logs
operacionais nem o histórico Git. Os arquivos-fonte foram copiados sem
alterações; este guia e o workspace foram acrescentados para facilitar a leitura.

## Compilar ou testar quando necessário

Para compilar, use Windows com o SDK .NET 8 instalado. Execute os comandos no
terminal, dentro desta pasta:

```powershell
dotnet build .\src\PdvBarcodeFilter\PdvBarcodeFilter.csproj -c Release
dotnet build .\tools\PdvQrScannerSimulator\PdvQrScannerSimulator.csproj -c Release
dotnet run --project .\tests\PdvBarcodeFilter.Tests\PdvBarcodeFilter.Tests.csproj -c Release
```

O projeto de testes usa um executor próprio, por isso o comando é `dotnet run`.
A criação do instalador também depende do Inno Setup. Consulte `README.md` e
os scripts de compilação para os detalhes de publicação.
