#!/usr/bin/env bash
# Roda CSharpier antes de qualquer "git commit" e re-adiciona os arquivos
# reformatados, para que o commit ja saia formatado.
# Registrado como hook PreToolUse no matcher "Bash" em .claude/settings.json
# Só faz sentido no repositorio backend (petadoption-api).

input=$(cat)
command=$(echo "$input" | jq -r '.tool_input.command // empty')

if [[ "$command" != git\ commit* ]]; then
  exit 0
fi

echo "Rodando CSharpier antes do commit..." >&2

if ! dotnet csharpier format . ; then
  echo "CSharpier falhou ao formatar. Corrija o erro antes de commitar." >&2
  exit 2
fi

# csharpier pode ter alterado arquivos que ja estavam staged - re-adiciona
git add -A

exit 0
