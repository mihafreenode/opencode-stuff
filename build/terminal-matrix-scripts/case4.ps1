$docker = 'C:\Program Files\Docker\Docker\resources\bin\docker.exe'
& $docker 'exec' '-it' '--user' 'opencode' '-w' '/workspace' 'smoke-terminal-workspace-workspace' 'bash' '-lc' 'export TERM=xterm-256color; export LANG=C.UTF-8; export LC_ALL=C.UTF-8; opencode'
