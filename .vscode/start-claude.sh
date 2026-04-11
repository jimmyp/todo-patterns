#!/usr/bin/env bash
set -euo pipefail
jq 'del(.tasks[] | select(.label == "Start Claude") | .runOptions)' \
    .vscode/tasks.json > .vscode/tasks.json.tmp \
    && mv .vscode/tasks.json.tmp .vscode/tasks.json
exec claude --dangerously-skip-permissions --model claude-sonnet-4-6
