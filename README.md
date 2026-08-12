# CSV Peek

CSV Peek es un visualizador de solo lectura para abrir archivos CSV grandes rápidamente en Windows 10/11 x64. Muestra una primera página de 256 filas y construye un índice disperso en segundo plano, sin cargar el archivo completo en memoria.

## Descargar

Descarga el instalador de Windows x64 desde la [versión más reciente](https://github.com/ro-rodriguezNI/csv-peek/releases/latest/download/CSV-Peek-Setup-x64.msi). No es necesario instalar .NET ni descargar el código fuente.

> Windows puede mostrar una advertencia de editor desconocido porque el instalador todavía no tiene firma digital.

## Funciones principales

- Apertura mediante selector, arrastrar y soltar, argumentos de consola o **Abrir con**.
- CSV separados por coma, punto y coma o tabulador, incluidos campos entre comillas y multilínea.
- UTF-8, UTF-16 LE/BE y Windows-1252.
- Tabla virtual con caché limitada y un índice persistente en `%LocalAppData%\CSV Peek\Indexes`.
- Búsqueda global con `Ctrl+Shift+F`, resultados progresivos, cancelación y salto a la fila.
- Detección de cambios externos y recarga explícita.
- Tema de interfaz **Sistema**, **Claro** u **Oscuro**; la preferencia se conserva entre aperturas.

## Arquitectura

Desde la versión 1.1.2, CSV Peek está organizado en cuatro capas con dependencias unidireccionales:

```text
CsvPeek.App ───────────┬──> CsvPeek.Application ──> CsvPeek.Core
                      └──> CsvPeek.Infrastructure ─┬──> CsvPeek.Application
                                                  └──> CsvPeek.Core
```

- **`CsvPeek.Core`** contiene los modelos, límites, resultados e índice disperso. No conoce WinForms ni accede al sistema de archivos.
- **`CsvPeek.Application`** define los contratos y coordina los casos de uso mediante `CsvDocumentSession`, `CsvScanEngine`, `CsvIndexCoordinator`, `CsvPageProvider` y `CsvSearchService`.
- **`CsvPeek.Infrastructure`** implementa la lectura física de registros, detección de formato y codificación, huellas de archivos y persistencia del índice.
- **`CsvPeek.App`** contiene WinForms y compone las implementaciones. `MainForm` delega el ciclo de vida del documento, la cuadrícula virtual, la búsqueda y el tema a controladores separados.

Al abrir un archivo, la aplicación detecta su formato, carga la primera página y recupera un índice persistido compatible cuando existe. Después completa el índice en segundo plano. Las páginas adicionales se solicitan bajo demanda y la búsqueda puede aprovechar el mismo recorrido activo sin duplicar el escaneo.

La cuadrícula mantiene páginas de 256 filas en una caché limitada a 128 MB. Las actualizaciones del número de filas se agrupan durante el indexado, solo se invalidan las filas visibles de una página recién cargada y el doble búfer reduce el parpadeo durante el desplazamiento.

```text
src/
├── CsvPeek.Core
├── CsvPeek.Application
├── CsvPeek.Infrastructure
└── CsvPeek.App
tests/
├── CsvPeek.Tests
└── CsvPeek.App.Tests
tools/
└── CsvPeek.Benchmark
installer/
└── CsvPeek.Installer.wixproj
```

Los contratos principales entre capas son `ICsvDocumentSession`, `ICsvDocumentSessionFactory`, `ICsvRecordSourceFactory` e `ICsvIndexStore`. El formato de índice persistido continúa en la versión 1 para reutilizar índices válidos creados por versiones anteriores. Consulta [ARCHITECTURE.md](ARCHITECTURE.md) para ver el flujo interno resumido.

## Desarrollo

Requisitos: .NET 10 SDK en Windows x64. La aplicación no utiliza paquetes de terceros en tiempo de ejecución; xUnit se utiliza para pruebas y WiX para producir el MSI.

```powershell
dotnet test .\tests\CsvPeek.Tests\CsvPeek.Tests.csproj
dotnet test .\tests\CsvPeek.App.Tests\CsvPeek.App.Tests.csproj
dotnet run --project .\src\CsvPeek.App\CsvPeek.App.csproj -- "C:\datos\archivo.csv"
```

Para producir la publicación autocontenida y el MSI por usuario:

```powershell
.\scripts\build-release.ps1
```

La versión se centraliza en `Directory.Build.props`. El script ejecuta ambos proyectos de pruebas, publica la aplicación una sola vez, construye el MSI y genera su archivo SHA-256. El MSI registra CSV Peek como alternativa para `.csv`, pero deja que Windows y el usuario decidan cuál aplicación es la predeterminada.

## Prueba de volumen

El generador crea datos de forma incremental y no conserva todas las filas en memoria:

```powershell
.\scripts\generate-benchmark.ps1 -Path "$env:TEMP\csv-peek-million.csv" -Rows 1000000
```
