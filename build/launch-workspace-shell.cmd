@echo off
REM Manual validation helper for opening an already provisioned workspace shell
REM in Windows Terminal without repeating fragile nested quoting in ad-hoc commands.

if "%~1"=="" (
  echo Usage: launch-workspace-shell.cmd ^<container-name^>
  exit /b 1
)

docker exec -it %~1 bash -lc "export HOME=/home/opencode; cd /workspace; exec bash --rcfile /home/opencode/.bashrc -i"
