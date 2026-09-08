# Segurança e privacidade

Este projeto deve permanecer em um repositório privado.

- Não envie logs reais, históricos, imagens do totem, códigos de cupom,
  certificados ou `filtersettings.json` para o GitHub.
- O diagnóstico em tempo real pode conter o QR Code completo quando
  `DiagnosticLogRawData` estiver habilitado.
- Use somente dados sintéticos em testes e documentação.
- Armazene certificados de assinatura fora da árvore do projeto.
- Antes de cada push, confira `git status` e `git diff --cached`.

Não há atualização automática. Binários devem ser distribuídos manualmente e,
em produção, preferencialmente assinados digitalmente.
