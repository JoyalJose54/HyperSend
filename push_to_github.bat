@echo off
echo ========================================================
echo   Pushing HyperSend to GitHub
echo   Remote: https://github.com/JoyalJose54/HyperSend.git
echo ========================================================
echo.
git push -u origin master
if %errorlevel% equ 0 (
    echo.
    echo ========================================================
    echo   [SUCCESS] HyperSend pushed to GitHub successfully!
    echo   View repository: https://github.com/JoyalJose54/HyperSend
    echo ========================================================
) else (
    echo.
    echo [NOTE] If you haven't created the repository on GitHub yet:
    echo 1. Go to https://github.com/new
    echo 2. Repository name: HyperSend
    echo 3. Click "Create repository" (leave README/license unchecked)
    echo 4. Run this script again!
)
pause
