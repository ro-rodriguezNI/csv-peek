# Arquitectura de CSV Peek

CSV Peek usa cuatro capas con dependencias unidireccionales:

```text
CsvPeek.App ───────────────┐
    │                      v
    ├─ interfaz       CsvPeek.Application ──> CsvPeek.Core
    └─ composición          ^
                           │
                 CsvPeek.Infrastructure
```

- **Core** contiene modelos, límites e índice disperso. No accede al sistema de archivos.
- **Application** define contratos y coordina sesiones, escaneo, búsqueda, páginas, caché e indexado.
- **Infrastructure** implementa lectura física, detección de dialecto, huellas e índices persistidos.
- **App** compone las implementaciones y muestra WinForms; `MainForm` delega documento, cuadrícula, búsqueda y tema a controladores específicos.

## Flujo principal

1. `DocumentWorkspaceController` solicita una sesión a `ICsvDocumentSessionFactory`.
2. La sesión detecta el dialecto, carga la primera página y restaura un índice compatible si existe.
3. `CsvIndexCoordinator` completa el índice en segundo plano y emite progreso.
4. `VirtualCsvGridController` solicita páginas bajo demanda y repinta solo las filas visibles afectadas.
5. `CsvSearchService` consume el mismo recorrido que construye el índice, evitando un segundo escaneo.

El formato persistido continúa en la versión 1 para conservar los índices creados por versiones anteriores.
