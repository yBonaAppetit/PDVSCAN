# Changelog

## 1.4.0 — 2026-09-06

- adiciona um simulador de leitor QR separado, com dois cupons de exemplo;
- usa um canal local restrito ao usuário atual, habilitado somente em modo de teste;
- adiciona a opção de bandeja `Permitir simulador (somente teste)`;
- na repetição consecutiva do mesmo cupom, usa o padrão na primeira leitura e
  somente o auxiliar da segunda leitura em diante;
- reinicia a sequência ao receber qualquer cupom diferente;
- mantém a alternância antiga disponível por configuração;
- adiciona teste integrado do simulador, parser e coordenador de envio.

## 1.3.1 — 2026-09-01

- reduz o log padrão aos eventos úteis e às mudanças reais de estado;
- deixa eventos por tecla e pacotes Raw Input atrás de opção de diagnóstico;
- limita o heartbeat a mudanças ou a uma confirmação periódica de cinco minutos;
- desativa o som de entrada inválida por padrão, mantendo-o configurável;
- adiciona testes para os novos padrões de configuração.

## 1.3.0 — 2026-09-01

- libera o teclado físico durante a operação normal;
- intercepta somente o Enter de uma estrutura válida de cupom;
- substitui o conteúdo bruto do campo pelo código tratado;
- adiciona atalho global configurável, com `Ctrl+Shift+F12` como padrão;
- remove o logger legado e o histórico administrativo;
- mantém somente o diagnóstico `realtime-*.log` e seu botão de pasta;
- adiciona preparação para repositório GitHub privado e CI em Windows;
- não adiciona atualização automática.

## 1.2.1 — 2026-08-25

- corrige o layout x64 da estrutura nativa `INPUT` usada por `SendInput`.

## 1.2.0 — 2026-08-25

- adiciona diagnóstico em tempo real, Raw Input e inventário PnP.
