@echo off
echo [CI] Running Automated Tcl Regression Test Suite...
set TEST_DIR=%~dp0..\..\test-scripts\vtc_cases
for %%f in ("%TEST_DIR%\*.tcl") do (
    echo Running test case: %%~nxf
    tclsh "%%f"
)
exit /b 0
