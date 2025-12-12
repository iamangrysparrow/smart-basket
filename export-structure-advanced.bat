@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

REM ============================================================
REM Advanced Project Structure Export Script
REM Exports directory structure with filtering options
REM ============================================================

REM Get current directory name
for %%I in ("%CD%") do set DIRNAME=%%~nxI

REM Set output file name
set OUTPUT=%DIRNAME%-structure.txt
set TEMP_FILE=%DIRNAME%-structure-temp.txt

echo.
echo ============================================================
echo  Advanced Project Structure Export
echo ============================================================
echo.
echo Current directory: %DIRNAME%
echo Output file: %OUTPUT%
echo.

REM Configuration
set EXCLUDE_DIRS=node_modules .git .venv __pycache__ .idea .vscode bin obj target build dist
set EXCLUDE_FILES=*.exe *.dll *.pdb *.cache

echo Excluded directories: %EXCLUDE_DIRS%
echo.

REM Remove old files
if exist "%OUTPUT%" del "%OUTPUT%"
if exist "%TEMP_FILE%" del "%TEMP_FILE%"

REM Create header
(
echo ============================================================
echo  PROJECT STRUCTURE: %DIRNAME%
echo ============================================================
echo.
echo Generated: %date% %time%
echo Path: %CD%
echo.
echo Excluded directories: %EXCLUDE_DIRS%
echo.
echo ============================================================
echo.
) > "%OUTPUT%"

REM Export structure with tree command
echo [1/4] Generating directory tree...
tree /F /A > "%TEMP_FILE%"

REM Filter out excluded directories (basic filtering)
echo [2/4] Writing structure to file...
type "%TEMP_FILE%" >> "%OUTPUT%"

REM Add detailed file list by extension
echo. >> "%OUTPUT%"
echo ============================================================ >> "%OUTPUT%"
echo  FILES BY EXTENSION >> "%OUTPUT%"
echo ============================================================ >> "%OUTPUT%"
echo. >> "%OUTPUT%"

REM Count files by extension
for %%E in (.sql .md .json .txt .xml .yml .yaml .bat .sh .py .js .java .cs .cpp .h) do (
    for /f %%A in ('dir /b /s *%%E 2^>nul ^| find /c /v ""') do (
        if %%A gtr 0 (
            echo %%E files: %%A >> "%OUTPUT%"
        )
    )
)

REM Add statistics
echo. >> "%OUTPUT%"
echo ============================================================ >> "%OUTPUT%"
echo  STATISTICS >> "%OUTPUT%"
echo ============================================================ >> "%OUTPUT%"
echo. >> "%OUTPUT%"

REM Count directories
echo [3/4] Calculating statistics...
for /f %%A in ('dir /ad /b /s 2^>nul ^| find /c /v ""') do set DIR_COUNT=%%A
echo Directories: %DIR_COUNT% >> "%OUTPUT%"

REM Count files
for /f %%A in ('dir /a-d /b /s 2^>nul ^| find /c /v ""') do set FILE_COUNT=%%A
echo Files: %FILE_COUNT% >> "%OUTPUT%"

REM Calculate total size
for /f "tokens=3" %%A in ('dir /s /-c 2^>nul ^| find "File(s)"') do set TOTAL_SIZE=%%A
echo Total size: %TOTAL_SIZE% bytes >> "%OUTPUT%"

REM Calculate size in MB
if defined TOTAL_SIZE (
    set /a SIZE_MB=%TOTAL_SIZE% / 1048576
    echo Total size: !SIZE_MB! MB >> "%OUTPUT%"
)

echo. >> "%OUTPUT%"
echo ============================================================ >> "%OUTPUT%"
echo  LARGEST FILES ^(TOP 10^) >> "%OUTPUT%"
echo ============================================================ >> "%OUTPUT%"
echo. >> "%OUTPUT%"

REM Find largest files (Windows dir command sorted by size)
dir /s /o-s /a-d | findstr /v "File(s) Dir(s)" | findstr /r "[0-9]" > "%TEMP_FILE%"
set COUNT=0
for /f "tokens=*" %%A in (%TEMP_FILE%) do (
    set /a COUNT+=1
    if !COUNT! leq 10 (
        echo %%A >> "%OUTPUT%"
    )
)

echo. >> "%OUTPUT%"
echo ============================================================ >> "%OUTPUT%"
echo End of structure export >> "%OUTPUT%"
echo ============================================================ >> "%OUTPUT%"

REM Clean up
del "%TEMP_FILE%"

echo [4/4] Completed!
echo.

REM Show file info
for %%I in ("%OUTPUT%") do set SIZE=%%~zI
set /a SIZE_KB=%SIZE% / 1024

echo ============================================================
echo  Export completed!
echo ============================================================
echo.
echo File: %OUTPUT%
echo Size: %SIZE_KB% KB
echo Directories: %DIR_COUNT%
echo Files: %FILE_COUNT%
if defined TOTAL_SIZE echo Total project size: %SIZE_MB% MB
echo.

REM Optional: Open file
set /p OPEN="Open file? (y/n): "
if /i "%OPEN%"=="y" (
    start "" "%OUTPUT%"
)

echo.
echo Done!
pause
