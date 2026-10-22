# Instrucciones del proyecto

## Commits

- Todos los mensajes de commit deben estar escritos en inglés, obligatoriamente. Esto incluye el título y cualquier descripción o cuerpo del commit.

## Reinicio después de cada cambio

- Al terminar cada cambio en el proyecto, detén la instancia en ejecución de BetterWinTab, compila la aplicación y, si la compilación termina correctamente, inicia la aplicación compilada.
- Ejecuta desde la raíz del repositorio, en PowerShell, en este orden:

  ```powershell
  Stop-Process -Name BetterWinTab -Force -ErrorAction SilentlyContinue
  dotnet build BetterWinTab.csproj -p:Platform=x64
  if ($LASTEXITCODE -eq 0) {
      Start-Process `
          -FilePath "bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\BetterWinTab.exe" `
          -WorkingDirectory (Get-Location).Path
  }
  ```

- No inicies la aplicación si la compilación falla; informa del error y deja la aplicación detenida.
