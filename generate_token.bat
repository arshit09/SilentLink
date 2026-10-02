@echo off
rem ============================================================
rem  SilentLink - Strong Auth Token Generator
rem
rem  Double-click this file. It prints a cryptographically strong
rem  random token and then waits:
rem    [C] copy it to the clipboard
rem    [U] write it into config.json as auth_token
rem    [L] change the token length
rem    [Q] quit
rem    any other key - generate a fresh token
rem
rem  Randomness comes from the Windows crypto RNG
rem  (RNGCryptoServiceProvider), never from the weak RANDOM
rem  variable. Characters used: A-Z a-z 0-9 - _  - 64 symbols,
rem  6 bits each, mapped with no modulo bias. All are safe in an
rem  HTTP header, a URL query string and JSON. The first and last
rem  character are always letters, so no tool mistakes the token
rem  for a command line switch.
rem ============================================================

setlocal
title SilentLink - Auth Token Generator
cd /d "%~dp0"

set "PS=powershell -NoProfile -ExecutionPolicy Bypass -Command"
set "CONFIG=%~dp0config.json"
set "CHARSET=ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
set "TOKEN_LEN=64"
set "TMPFILE=%TEMP%\slk_token_%RANDOM%%RANDOM%.tmp"
set "TOKEN="
set "STATUS="

where powershell >nul 2>&1 || goto :no_powershell

:new_token
call :generate
if not defined TOKEN goto :generate_failed

:show
cls
echo.
echo  ==============================================================
echo    SilentLink  -  Strong Auth Token Generator
echo  ==============================================================
echo.
echo    New token  -  %TOKEN_LEN% characters, about %BITS% bits of entropy:
echo.
echo      %TOKEN%
echo.
echo  --------------------------------------------------------------
if defined STATUS echo    ^>^> %STATUS%
if defined STATUS echo.
echo    [C]  Copy to clipboard
echo    [U]  Use as auth_token in config.json
echo    [L]  Change length  -  currently %TOKEN_LEN%
echo    [Q]  Quit
echo.
echo    Press any other key to generate a new token.
echo  --------------------------------------------------------------
echo.
set "STATUS="

%PS% "try{$c=([Console]::ReadKey($true)).KeyChar}catch{$s=Read-Host; if($s.Length -gt 0){$c=$s[0]}else{$c=[char]13}}; $u=([string]$c).ToUpper(); if($u -eq 'C'){exit 1}; if($u -eq 'U'){exit 2}; if($u -eq 'L'){exit 3}; if($u -eq 'Q' -or [int]$c -eq 27){exit 4}; exit 0"
if errorlevel 4 goto :quit
if errorlevel 3 goto :change_length
if errorlevel 2 goto :use_token
if errorlevel 1 goto :copy_token
goto :new_token

rem ------------------------------------------------------------
rem  Generate a token into TOKEN using the crypto RNG.
rem  PowerShell writes it to a temp file, which is read back and
rem  deleted at once so the secret does not linger on disk.
rem ------------------------------------------------------------
:generate
set "TOKEN="
set /a BITS=TOKEN_LEN*6
if exist "%TMPFILE%" del /f /q "%TMPFILE%" >nul 2>&1
%PS% "$n=[int]$env:TOKEN_LEN; $cs=$env:CHARSET; $b=New-Object byte[] $n; $r=New-Object System.Security.Cryptography.RNGCryptoServiceProvider; $r.GetBytes($b); $r.Dispose(); $sb=New-Object System.Text.StringBuilder; for($i=0; $i -lt $n; $i++){ $m=63; if($i -eq 0 -or $i -eq ($n-1)){$m=31}; [void]$sb.Append($cs[$b[$i] -band $m]) }; [IO.File]::WriteAllText($env:TMPFILE,$sb.ToString())" >nul 2>&1
if exist "%TMPFILE%" for /f "usebackq delims=" %%A in ("%TMPFILE%") do set "TOKEN=%%A"
if exist "%TMPFILE%" del /f /q "%TMPFILE%" >nul 2>&1
goto :eof

rem ------------------------------------------------------------
:copy_token
set /p "=%TOKEN%" <nul | clip
if errorlevel 1 goto :copy_failed
set "STATUS=Copied to the clipboard - paste it wherever you need it."
goto :show

:copy_failed
set "STATUS=Could not reach clip.exe - select the token above and copy it manually."
goto :show

rem ------------------------------------------------------------
rem  Write the token into config.json as auth_token, keeping the
rem  other settings and backing the old file up to config.json.bak.
rem  SilentLink re-reads config.json whenever it changes, so a
rem  running server picks the new token up without a restart.
rem ------------------------------------------------------------
:use_token
%PS% "$ErrorActionPreference='Stop'; $p=$env:CONFIG; $t=$env:TOKEN; try{ if(Test-Path -LiteralPath $p){ Copy-Item -LiteralPath $p -Destination ($p+'.bak') -Force; $o=Get-Content -LiteralPath $p -Raw | ConvertFrom-Json; if($o.PSObject.Properties.Name -contains 'auth_token'){$o.auth_token=$t}else{$o | Add-Member -NotePropertyName auth_token -NotePropertyValue $t}; $made=0 } else { $o=[pscustomobject][ordered]@{auth_token=$t; port=5000; restrict_to_local_network=$true}; $made=1 }; $j=($o | ConvertTo-Json) -replace '(?m)^    ','  ' -replace '(?m)^(\s*\S+):  +','$1: '; [IO.File]::WriteAllText($p,$j+[Environment]::NewLine); exit (3*$made) }catch{ exit 1 }"
if errorlevel 3 goto :config_created
if errorlevel 1 goto :config_failed
set "STATUS=Saved as auth_token in config.json - old file kept as config.json.bak."
goto :show

:config_created
set "STATUS=Created config.json with this token, port 5000, local network only."
goto :show

:config_failed
set "STATUS=Could not write config.json - check it is valid JSON and not read-only."
goto :show

rem ------------------------------------------------------------
:change_length
echo.
set "NEWLEN="
set /p "NEWLEN=   New length in characters, 16 to 256, Enter to keep %TOKEN_LEN%: "
if not defined NEWLEN goto :show
set "BADLEN="
for /f "delims=0123456789" %%A in ("%NEWLEN%") do set "BADLEN=1"
if not "%NEWLEN:~3%"=="" set "BADLEN=1"
if "%NEWLEN:~0,1%"=="0" set "BADLEN=1"
if defined BADLEN goto :bad_length
if %NEWLEN% LSS 16 goto :bad_length
if %NEWLEN% GTR 256 goto :bad_length
set "TOKEN_LEN=%NEWLEN%"
goto :new_token

:bad_length
set "STATUS=Length must be a whole number from 16 to 256 - keeping %TOKEN_LEN%."
goto :show

rem ------------------------------------------------------------
:generate_failed
echo.
echo    Token generation failed - PowerShell could not write to
echo    "%TMPFILE%".
echo.
pause
goto :quit

:no_powershell
echo.
echo    PowerShell was not found on this PC, so a cryptographically
echo    strong token cannot be generated. Install or restore
echo    Windows PowerShell and run this file again.
echo.
pause
goto :quit

rem ------------------------------------------------------------
:quit
if exist "%TMPFILE%" del /f /q "%TMPFILE%" >nul 2>&1
echo.
echo    Remember: every client - MacroDroid, curl, Tasker - has to
echo    send the new token, and the old one stops working.
echo.
endlocal
exit /b 0
