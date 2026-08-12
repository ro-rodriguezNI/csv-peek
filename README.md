# CSV Peek

CSV Peek es un visualizador de solo lectura para abrir archivos CSV grandes rápidamente en Windows 10/11 x64. Muestra una primera página de 256 filas y construye un índice disperso en segundo plano, sin cargar el archivo completo en memoria.

## Descargar

Descarga el instalador de Windows x64 desde la [versión más reciente](https://github.com/ro-rodriguezNI/csv-peek/releases/latest/download/CSV-Peek-Setup-x64.msi). No es necesario instalar .NET ni descargar el código fuente.

> Windows puede mostrar una advertencia de editor desconocido porque el instalador todavía no tiene firma digital.

## Funciones de la versión 1

- Apertura mediante selector, arrastrar y soltar, argumentos de consola o **Abrir con**.
- CSV separados por coma, punto y coma o tabulador, incluidos campos entre comillas y multilínea.
- UTF-8, UTF-16 LE/BE y Windows-1252.
- Tabla virtual con caché limitada y un índice persistente en `%LocalAppData%\CSV Peek\Indexes`.
- Búsqueda global con `Ctrl+Shift+F`, resultados progresivos, cancelación y salto a la fila.
- Detección de cambios externos y recarga explícita.
- Tema de interfaz **Sistema**, **Claro** u **Oscuro**; la preferencia se conserva entre aperturas.

## Desarrollo

Requisitos: .NET 10 SDK en Windows x64. La aplicación no utiliza paquetes de terceros en tiempo de ejecución; xUnit se utiliza para pruebas y WiX para producir el MSI.

```powershell
dotnet test .\tests\CsvPeek.Tests\CsvPeek.Tests.csproj
dotnet run --project .\src\CsvPeek.App\CsvPeek.App.csproj -- "C:\datos\archivo.csv"
```

Para producir la publicación autocontenida y el MSI por usuario:

```powershell
.\scripts\build-release.ps1
```

El MSI registra CSV Peek como alternativa para `.csv`, pero deja que Windows y el usuario decidan cuál aplicación es la predeterminada.

La organización interna y las dependencias entre capas se describen en [ARCHITECTURE.md](ARCHITECTURE.md).

## Prueba de volumen

El generador crea datos de forma incremental y no conserva todas las filas en memoria:

```powershell
.\scripts\generate-benchmark.ps1 -Path "$env:TEMP\csv-peek-million.csv" -Rows 1000000
```
