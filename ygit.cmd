CD /D %~dp0

git add -A

git commit -a -m ok

IF %ERRORLEVEL% NEQ 0 PAUSE
