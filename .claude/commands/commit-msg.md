---
description: Gera a mensagem de commit (Conventional Commits) do que está staged, sem commitar
argument-hint: "[contexto extra opcional]"
allowed-tools: Bash(git diff:*), Bash(git status:*), Bash(git log:*)
---

Gere a mensagem de commit para o que está **staged** neste repositório.
Não rode `git commit`, `git add` nem qualquer comando que altere o repositório —
quem commita é o usuário.

## Passos

1. Rode `git status --short` e `git diff --cached --stat`.
   - Se **nada** estiver staged: diga isso em uma linha, mostre o que há de
     modificado e pare. Não invente uma mensagem a partir do working tree.
2. Rode `git diff --cached` para ler as mudanças de verdade. Se o diff for muito
   grande, use `git diff --cached --stat` mais os trechos dos arquivos principais.
3. Rode `git log --oneline -10` para conferir o estilo em uso no repositório.

## Formato

Conventional Commits, como o próprio GitHalls implementa em
`GitHalls.Core/Commits/ConventionalCommitSuggester.cs`:

```
tipo(escopo): resumo no imperativo

Corpo opcional explicando o porquê, não o quê.
```

- **tipo**: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`,
  `ci`, `chore`, `revert`.
- **escopo**: a pasta de topo, minúscula, **quando todos os arquivos staged
  compartilham uma** (`githalls.app`, `githalls.core`). Se estiverem espalhados,
  omita o escopo — é a mesma regra do `SuggestScope`.
- **resumo**: imperativo, minúscula, sem ponto final, até ~72 caracteres.
- **corpo**: só quando o porquê não for óbvio pelo resumo. Se a mudança corrige
  um bug, diga qual era o comportamento errado. Se foi uma decisão entre
  alternativas, diga por que essa. Bullets são bem-vindos. Sem corpo é melhor
  que corpo óbvio.
- **idioma**: português. (Para mudar, troque esta linha por "inglês".)

Escolha o tipo pelo que a mudança **é**, não pelo que ela toca: um commit que
mexe num teste e numa feature é `feat`, não `test`.

## Saída

Só isto, nesta ordem:

1. Uma linha dizendo o que foi lido (ex: "7 arquivos staged, +240/-58").
2. A mensagem num bloco de código simples, para copiar.
3. O comando pronto num bloco ```bash, com a mensagem já embutida via `-m`
   (e um segundo `-m` para o corpo, se houver).

Sem preâmbulo, sem explicar o formato, sem oferecer alternativas.

Se o que está staged claramente mistura duas mudanças sem relação, diga isso em
uma frase e sugira como separar — mas gere a mensagem mesmo assim.

$ARGUMENTS
